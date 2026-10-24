//
// Copyright (C) 2026 DartPower Team
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DPTDOOM.Shared;

namespace DPTDOOM.Master
{
    /// <summary>
    /// Rejects a relay source that the operator's rules already refuse. Passed in
    /// as a delegate so the relay never reaches into the directory's policy.
    /// </summary>
    internal delegate bool RelaySourceGuard(byte[] host);

    /// <summary>
    /// The raised quotas an address is entitled to, or <see cref="RelayLimits.None"/>.
    ///
    /// Separate from the guard because the two answer different questions: the guard
    /// says whether a source may use the relay at all, this says how much of it. A
    /// source can be perfectly welcome and still be held to the ordinary limits.
    /// </summary>
    internal delegate RelayLimits RelayLiftLookup(IPAddress address);

    /// <summary>
    /// The relay: a bounded TCP proxy between one host and one guest.
    ///
    /// This is deliberately **not** a dedicated simulation server. The engine
    /// stays lockstep, the host still simulates the world, and the master has no
    /// idea what a tic is. It only moves bytes, so a host behind NAT becomes
    /// reachable without a port forward or UPnP, and the hairpin case (two
    /// players behind the same router, where the router refuses to loop the
    /// public address back inside) stops mattering: both ends connect *out* to
    /// the master and the master joins the two streams.
    ///
    /// The handshake is one request and then raw bytes:
    ///
    ///   host  -> DPTM header(operation 5) + 40-byte lease, then OK, then game bytes
    ///   guest -> DPTM header(operation 6) + 16-byte session id, then OK, then game bytes
    ///
    /// Only one guest is paired per session at a time. The game protocol itself
    /// is a single TCP stream per guest, so nothing more is needed.
    /// </summary>
    internal sealed class RelayService
    {
        private const int HandshakeDeadlineMs = 3000;

        private readonly MasterConfig config;
        private readonly MasterLog log;
        private readonly SessionDirectory directory;
        private readonly RelaySourceGuard guard;
        private readonly RelayLiftLookup lift;

        private readonly object sync = new object();
        private readonly Dictionary<string, WaitingHost> waiting = new Dictionary<string, WaitingHost>();
        private readonly List<RelayPair> pairs = new List<RelayPair>();

        private TcpListener listener;
        private Thread acceptThread;
        private Thread maintenanceThread;
        private volatile bool stopping;
        private int port;

        public RelayService(MasterConfig config, MasterLog log, SessionDirectory directory, RelaySourceGuard guard)
            : this(config, log, directory, guard, null)
        {
        }

        public RelayService(MasterConfig config, MasterLog log, SessionDirectory directory,
                            RelaySourceGuard guard, RelayLiftLookup lift)
        {
            this.config = config;
            this.log = log;
            this.directory = directory;
            this.guard = guard;
            this.lift = lift;
        }

        public int Port => port;

        /// <summary>
        /// The quotas this source is entitled to, or none.
        /// </summary>
        private RelayLimits LiftFor(IPEndPoint remote)
        {
            if (lift == null || remote == null)
            {
                return RelayLimits.None;
            }

            return lift(remote.Address) ?? RelayLimits.None;
        }

        /// <summary>
        /// A raised limit is a permission to exceed, never a reduction: a rule that
        /// says 1 must not lower the server's 3, because a rule in an allow list is
        /// about granting, and an operator who wants to tighten limits does it in the
        /// server's own configuration.
        /// </summary>
        private static int Raise(int configured, int granted)
        {
            return granted > configured ? granted : configured;
        }

        public void Start()
        {
            port = config.RelayPort > 0 ? config.RelayPort : config.Port + 1;

            if (port > 65535)
            {
                throw new Exception("relay port must be 1..65535 (control port " + config.Port +
                                    " plus one overflows)");
            }

            var bind = MasterConfig.ParseBind(config.Bind);

            listener = new TcpListener(bind, port);
            listener.Start(64);

            log.Info("relay listening on " + bind + ":" + port +
                     " sessions=" + config.RelaySessions +
                     " guests/session=" + config.RelayGuestsPerSession +
                     " sessions/source=" + config.RelaySessionsPerSource +
                     " per-source=" + config.RelayPerSource +
                     " rate=" + config.RelayRate + "B/s" +
                     " idle=" + config.RelayIdleSeconds + "s" +
                     " lifetime=" + config.RelayLifetimeSeconds + "s");

            acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Name = "DPTDOOM relay accept";
            acceptThread.Start();

            maintenanceThread = new Thread(MaintenanceLoop);
            maintenanceThread.IsBackground = true;
            maintenanceThread.Name = "DPTDOOM relay maintenance";
            maintenanceThread.Start();
        }

        private void AcceptLoop()
        {
            while (!stopping)
            {
                TcpClient client;

                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return;
                }

                var state = client;
                ThreadPool.QueueUserWorkItem(_ => HandleClient(state));
            }
        }

        private void HandleClient(TcpClient client)
        {
            var remote = client.Client.RemoteEndPoint as IPEndPoint;
            var host = remote == null ? new byte[4] : remote.Address.GetAddressBytes();
            var handedOff = false;

            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = HandshakeDeadlineMs;
                client.SendTimeout = Math.Max(config.RelayIdleSeconds, 10) * 1000;

                var stream = client.GetStream();
                var header = new byte[MasterProtocol.HeaderSize];

                if (!ReadExactly(stream, header, 0, header.Length))
                {
                    log.Event(Describe(host, remote) + " relay: closed before a request");
                    return;
                }

                var error = MasterProtocol.HeaderError(header);
                if (error != null)
                {
                    log.Event(Describe(host, remote) + " relay rejected: " + error);
                    Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusBadRequest);
                    return;
                }

                var operation = (ushort)MasterProtocol.Get16(header, 6);
                var length = MasterProtocol.RequestLength(header);

                // Drain the request before any reply, for the same reason as on
                // the control port: closing a socket with unread bytes makes
                // Windows reset the connection, and the reset takes the reply
                // with it.
                var body = new byte[length];

                if (length > 0 && !ReadExactly(stream, body, 0, length))
                {
                    log.Event(Describe(host, remote) + " relay: truncated request");
                    return;
                }

                if (guard != null && !guard(host))
                {
                    // The relay moves other people's traffic, so the ban and
                    // allow lists have to apply here too - not only on the
                    // control port. A banned source that can still open a relay
                    // channel has a way back in.
                    log.Event(Describe(host, remote) + " relay rejected: source not allowed");
                    Reply(stream, operation, new byte[0], MasterProtocol.StatusUnauthorized);
                    return;
                }

                if (operation != MasterProtocol.OpRelayHost && operation != MasterProtocol.OpRelayJoin)
                {
                    // Getting a directory request here means the client ignored
                    // the redirect, or a port was wired backwards. Say so instead
                    // of acting on it: a LIST answered from the relay port would
                    // make the redirect pointless and hide the mistake.
                    log.Event(Describe(host, remote) + " relay rejected: control operation on relay port");
                    Reply(stream, operation, new byte[0], MasterProtocol.StatusBadRequest);
                    return;
                }

                if (operation == MasterProtocol.OpRelayHost)
                {
                    handedOff = AttachHost(client, stream, host, remote, body);
                }
                else
                {
                    handedOff = AttachGuest(client, stream, host, remote, body);
                }
            }
            catch (Exception e)
            {
                log.Warn(Describe(host, remote) + " relay connection error: " + e.Message);
            }
            finally
            {
                // The socket belongs to the pair once a handshake succeeded, and
                // closing it here would cut a live game in half.
                if (!handedOff)
                {
                    Close(client);
                }
            }
        }

        private bool AttachHost(TcpClient client, NetworkStream stream, byte[] host, IPEndPoint remote, byte[] auth)
        {
            byte[] id;

            if (!directory.AuthorizeRelay(host, auth, out id))
            {
                log.Event(Describe(host, remote) + " relay host rejected: bad lease");
                Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusUnauthorized);
                return false;
            }

            var key = MasterProtocol.IdToHex(id);
            var now = Now();
            var lift = LiftFor(remote);

            lock (sync)
            {
                WaitingHost existing;
                if (waiting.TryGetValue(key, out existing))
                {
                    // A host that reconnects before maintenance noticed the old
                    // socket died should just take the slot. Answering Conflict
                    // would make the player wait a second for no reason.
                    if (IsDisconnected(existing.Client))
                    {
                        Close(existing.Client);
                        waiting.Remove(key);
                    }
                    else
                    {
                        log.Event(Describe(host, remote) + " relay host rejected: already waiting");
                        Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusConflict);
                        return false;
                    }
                }

                // The global cap, unless this source is trusted with more. The raise
                // is a ceiling of its own rather than a wider shared one, so an
                // address given room cannot create room for strangers.
                var total = waiting.Count + pairs.Count;
                var totalCap = Raise(config.RelaySessions, lift.SessionsTotal);

                if (total >= totalCap)
                {
                    log.Event(Describe(host, remote) + " relay host rejected: relay is full (" +
                              total + "/" + totalCap + ")");
                    Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusCapacity);
                    return false;
                }

                var linksCap = Raise(config.RelayPerSource, lift.LinksPerSource);

                if (CountForSource(remote) >= linksCap)
                {
                    log.Event(Describe(host, remote) + " relay host rejected: too many from this address (" +
                              linksCap + ")");
                    Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusCapacity);
                    return false;
                }

                var guestsCap = Raise(config.RelayGuestsPerSession, lift.GuestsPerSession);

                if (CountGuestsForKey(key) >= guestsCap)
                {
                    // The session already has all the guest tunnels it is allowed;
                    // parking another waiting channel would hold a socket open for
                    // a guest that could never be admitted.
                    log.Event("relay " + key + ": host channel not needed, " +
                              guestsCap + " guest tunnels already active");
                    Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusCapacity);
                    return false;
                }

                var sessionsCap = Raise(config.RelaySessionsPerSource, lift.SessionsPerSource);

                if (CountSessionsForSource(remote) >= sessionsCap)
                {
                    // The relay is a shared resource and costs the operator both
                    // bandwidth and CPU. Two sessions per address covers a
                    // household with two machines, which is the case NAT hairpin
                    // creates; more than that is not two people playing. An allow
                    // rule can raise it for an address the operator trusts.
                    log.Event(Describe(host, remote) + " relay host rejected: too many sessions from this address (" +
                              sessionsCap + ")");
                    Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusCapacity);
                    return false;
                }

                var slot = new WaitingHost();
                slot.Client = client;
                slot.Stream = stream;
                slot.Id = id;
                slot.Key = key;
                slot.Appended = now;
                slot.Source = remote;

                waiting.Add(key, slot);
            }

            // The reply has to go out before the pipe opens, otherwise the first
            // game bytes would arrive in front of the reply header and the client
            // would parse them as a response.
            Reply(stream, MasterProtocol.OpRelayHost, new byte[0], MasterProtocol.StatusOk);

            log.Event(Describe(host, remote) + " relay host waiting, session " + key);

            return true;
        }

        private bool AttachGuest(TcpClient client, NetworkStream stream, byte[] host, IPEndPoint remote, byte[] idBytes)
        {
            var key = MasterProtocol.IdToHex(idBytes);

            WaitingHost waitingHost;

            lock (sync)
            {
                if (!waiting.TryGetValue(key, out waitingHost))
                {
                    log.Event(Describe(host, remote) + " relay guest rejected: no such session");
                }
                else
                {
                    // Guests are limited per session, not per address: the relay
                    // is meant for a co-op group of up to four, and the host
                    // itself is one of them.
                    // The size of a group is the host's decision, so the raise comes
                    // from the host's own rule - not the guest's. A trusted guest
                    // joining an untrusted session does not enlarge that session,
                    // and that is deliberate: otherwise anybody could bring a
                    // bigger party into somebody else's room.
                    var guestsCap = Raise(config.RelayGuestsPerSession,
                                          LiftFor(waitingHost.Source).GuestsPerSession);

                    if (CountGuestsForKey(key) >= guestsCap)
                    {
                        log.Event("relay " + key + ": guest rejected from " + Describe(host, remote) +
                                  ": already " + guestsCap + " guest tunnels");
                        waitingHost = null;
                    }
                    else
                    {
                        waiting.Remove(key);
                    }
                }
            }

            if (waitingHost == null)
            {
                Reply(stream, MasterProtocol.OpRelayJoin, new byte[0], MasterProtocol.StatusConflict);
                return false;
            }

            // Same rule as the host: the reply precedes the pipe.
            Reply(stream, MasterProtocol.OpRelayJoin, new byte[0], MasterProtocol.StatusOk);

            var pair = new RelayPair();
            pair.Key = key;
            pair.HostClient = waitingHost.Client;
            pair.HostStream = waitingHost.Stream;
            pair.GuestClient = client;
            pair.GuestStream = stream;
            pair.HostSource = waitingHost.Source;
            pair.GuestSource = remote;
            pair.Started = Now();
            pair.RateWindow = pair.Started;

            lock (sync)
            {
                pairs.Add(pair);
            }

            log.Event("relay " + key + ": paired " + Describe(host, waitingHost.Source) +
                      " with " + Describe(host, remote));

            // Two threads, one per direction. Half-close is honoured: when one
            // side sends EOF its direction ends and the other side is told, so a
            // peer that finished writing still receives what was already in
            // flight. Both directions ending closes the pair.
            var hostToGuest = new Thread(() => Pump(pair, pair.HostStream, pair.GuestStream, pair.HostClient, pair.GuestClient, "host->guest"));
            hostToGuest.IsBackground = true;
            hostToGuest.Name = "DPTDOOM relay " + key + " h2g";

            var guestToHost = new Thread(() => Pump(pair, pair.GuestStream, pair.HostStream, pair.GuestClient, pair.HostClient, "guest->host"));
            guestToHost.IsBackground = true;
            guestToHost.Name = "DPTDOOM relay " + key + " g2h";

            hostToGuest.Start();
            guestToHost.Start();

            return true;
        }

        private void Pump(RelayPair pair, NetworkStream from, NetworkStream to, TcpClient fromClient, TcpClient toClient, string direction)
        {
            var buffer = new byte[config.RelayBuffer];
            var lifetimeMs = config.RelayLifetimeSeconds * 1000L;

            try
            {
                from.ReadTimeout = config.RelayIdleSeconds * 1000;

                while (!stopping)
                {
                    if (Now() - pair.Started > lifetimeMs)
                    {
                        log.Event("relay " + pair.Key + ": lifetime reached, closing");
                        break;
                    }

                    var read = from.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    AccountBytes(pair, read);
                    to.Write(buffer, 0, read);
                    to.Flush();

                    if (direction == "host->guest")
                    {
                        Interlocked.Add(ref pair.HostToGuest, read);
                    }
                    else
                    {
                        Interlocked.Add(ref pair.GuestToHost, read);
                    }
                }
            }
            catch (Exception)
            {
                // An idle timeout, a reset, or a closed socket all mean the same
                // thing here: this direction is finished.
            }
            finally
            {
                // Tell the other side that no more data is coming, but do not
                // close its socket: the opposite direction may still be draining.
                // NetworkStream.Socket is protected on .NET 4.0, so the shutdown
                // goes through the client that owns it.
                try
                {
                    toClient.Client.Shutdown(SocketShutdown.Send);
                }
                catch
                {
                }

                if (Interlocked.Decrement(ref pair.Directions) == 0)
                {
                    ClosePair(pair);
                }
            }
        }

        private void AccountBytes(RelayPair pair, int bytes)
        {
            lock (pair.RateLock)
            {
                var now = Now();

                if (now - pair.RateWindow >= 1000)
                {
                    pair.RateWindow = now;
                    pair.RateBytes = 0;
                }

                pair.RateBytes += bytes;

                if (pair.RateBytes > config.RelayRate)
                {
                    // Coarse shaping, not traffic policing: sleeping in the pump
                    // stops reading, which is the backpressure that makes the
                    // sender slow down. No packet is dropped and the game
                    // protocol is untouched.
                    var wait = (int)(1000 - (now - pair.RateWindow));
                    if (wait > 0 && wait <= 1000)
                    {
                        Thread.Sleep(wait);
                    }

                    pair.RateWindow = Now();
                    pair.RateBytes = 0;
                }
            }
        }

        private void ClosePair(RelayPair pair)
        {
            lock (sync)
            {
                pairs.Remove(pair);
            }

            Close(pair.HostClient);
            Close(pair.GuestClient);

            var seconds = (Now() - pair.Started) / 1000;

            log.Event("relay " + pair.Key + ": closed after " + seconds + "s, " +
                      pair.HostToGuest + " bytes to guest, " +
                      pair.GuestToHost + " bytes to host");
        }

        private int CountForSource(IPEndPoint source)
        {
            if (source == null)
            {
                return 0;
            }

            var count = 0;

            foreach (var slot in waiting.Values)
            {
                if (SameAddress(slot.Source, source))
                {
                    count++;
                }
            }

            foreach (var pair in pairs)
            {
                if (SameAddress(pair.HostSource, source))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// How many distinct sessions this address is currently relaying.
        ///
        /// A session counts once no matter how many guest tunnels it holds: three
        /// guests joining one host is one session, not three.
        /// </summary>
        private int CountSessionsForSource(IPEndPoint source)
        {
            if (source == null)
            {
                return 0;
            }

            var keys = new Dictionary<string, bool>();

            foreach (var slot in waiting.Values)
            {
                if (SameAddress(slot.Source, source))
                {
                    keys[slot.Key] = true;
                }
            }

            foreach (var pair in pairs)
            {
                if (SameAddress(pair.HostSource, source))
                {
                    keys[pair.Key] = true;
                }
            }

            return keys.Count;
        }

        private int CountGuestsForKey(string key)
        {
            var count = 0;

            foreach (var pair in pairs)
            {
                if (pair.Key == key)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool SameAddress(IPEndPoint left, IPEndPoint right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left.Address.Equals(right.Address);
        }

        private void MaintenanceLoop()
        {
            while (!stopping)
            {
                Thread.Sleep(1000);

                try
                {
                    var expired = new List<WaitingHost>();

                    lock (sync)
                    {
                        foreach (var slot in waiting.Values)
                        {
                            // A waiting channel lives exactly as long as the
                            // listing it belongs to. Tying it to the lease means
                            // there is no second timeout to keep in sync: when
                            // the host stops heartbeating, the listing and the
                            // waiting socket disappear together.
                            if (!directory.IsActive(slot.Id))
                            {
                                expired.Add(slot);
                                continue;
                            }

                            // A host that died while waiting must lose its slot
                            // at once, not when the lease runs out. Otherwise a
                            // crashed host keeps holding its per-address budget
                            // for up to a minute and its owner cannot re-host.
                            if (IsDisconnected(slot.Client))
                            {
                                expired.Add(slot);
                            }
                        }

                        foreach (var slot in expired)
                        {
                            waiting.Remove(slot.Key);
                        }
                    }

                    foreach (var slot in expired)
                    {
                        var reason = directory.IsActive(slot.Id)
                            ? "host disconnected while waiting"
                            : "waiting channel expired with its listing";

                        log.Event("relay " + slot.Key + ": " + reason);
                        Close(slot.Client);
                    }
                }
                catch (Exception e)
                {
                    log.Warn("relay maintenance: " + e.Message);
                }
            }
        }

        /// <summary>
        /// True when the peer has closed or reset the connection.
        ///
        /// A waiting channel is idle by design, so any readable state means the
        /// connection is finished rather than "data pending": the host sends
        /// nothing until it is paired. Poll(0) is the only way to notice without
        /// consuming bytes, and consuming them would corrupt the game stream once
        /// the channel is paired.
        /// </summary>
        private static bool IsDisconnected(TcpClient client)
        {
            try
            {
                var socket = client.Client;

                if (socket == null || !socket.Connected)
                {
                    return true;
                }

                return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
            }
            catch (Exception)
            {
                return true;
            }
        }

        public void Stop()
        {
            stopping = true;

            if (listener != null)
            {
                try
                {
                    listener.Stop();
                }
                catch
                {
                }
            }

            var closing = new List<TcpClient>();

            lock (sync)
            {
                foreach (var slot in waiting.Values)
                {
                    closing.Add(slot.Client);
                }

                waiting.Clear();

                foreach (var pair in pairs)
                {
                    closing.Add(pair.HostClient);
                    closing.Add(pair.GuestClient);
                }

                pairs.Clear();
            }

            foreach (var client in closing)
            {
                Close(client);
            }
        }

        private static void Reply(NetworkStream stream, ushort operation, byte[] body, ushort status)
        {
            var header = MasterProtocol.Header(operation, body == null ? 0 : body.Length, status);

            stream.Write(header, 0, header.Length);

            if (body != null && body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }

            stream.Flush();
        }

        private static bool ReadExactly(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            var read = 0;

            while (read < count)
            {
                var current = stream.Read(buffer, offset + read, count - read);
                if (current <= 0)
                {
                    return false;
                }

                read += current;
            }

            return true;
        }

        private static void Close(TcpClient client)
        {
            try
            {
                client.Close();
            }
            catch
            {
            }
        }

        private static string Describe(byte[] host, IPEndPoint remote)
        {
            var text = host[0] + "." + host[1] + "." + host[2] + "." + host[3];
            return remote == null ? text : text + ":" + remote.Port;
        }

        private static long Now()
        {
            return Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
        }

        private sealed class WaitingHost
        {
            public TcpClient Client;
            public NetworkStream Stream;
            public byte[] Id;
            public string Key;
            public IPEndPoint Source;
            public long Appended;
        }

        private sealed class RelayPair
        {
            public string Key;
            public TcpClient HostClient;
            public TcpClient GuestClient;
            public NetworkStream HostStream;
            public NetworkStream GuestStream;
            public IPEndPoint HostSource;
            public IPEndPoint GuestSource;
            public long Started;
            public int Directions = 2;
            public long HostToGuest;
            public long GuestToHost;

            public readonly object RateLock = new object();
            public long RateWindow;
            public int RateBytes;
        }
    }
}
