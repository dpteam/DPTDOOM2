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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DPTDOOM.Shared;

namespace DPTDOOM.Net
{
    /// <summary>
    /// Settings that both ends of a session must agree on. They are fixed by
    /// the host and echoed back in the welcome message.
    /// </summary>
    public sealed class SessionSettings
    {
        public int Map = 1;
        public int Episode = 1;
        public GameSkill Skill = GameSkill.Medium;
        public GameMode GameMode = GameMode.Commercial;
        public MissionPack MissionPack = MissionPack.Doom2;
        public bool Deathmatch;
        public bool FastMonsters;
        public bool RespawnMonsters;
        public bool NoMonsters;
        public uint WadChecksum;

        /// <summary>
        /// Rules that change the simulation, so both ends must agree on them.
        /// They travel in the settings record, not in the command line: a host
        /// and a guest with different values would build different worlds and
        /// desync on the first tic.
        /// </summary>
        public bool TeammatesBlock = true;

        public bool CoopDeathmatchWeapons;
        public bool SharedKeys = true;

        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)Episode);
                writer.Write((byte)Map);
                writer.Write((byte)Skill);
                writer.Write((byte)GameMode);
                writer.Write((byte)MissionPack);
                writer.Write((byte)((Deathmatch ? 1 : 0) |
                                    (FastMonsters ? 2 : 0) |
                                    (RespawnMonsters ? 4 : 0) |
                                    (NoMonsters ? 8 : 0) |
                                    (TeammatesBlock ? 16 : 0) |
                                    (CoopDeathmatchWeapons ? 32 : 0) |
                                    (SharedKeys ? 64 : 0)));
                writer.Write(WadChecksum);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static SessionSettings Deserialize(BinaryReader reader)
        {
            var settings = new SessionSettings();
            settings.Episode = reader.ReadByte();
            settings.Map = reader.ReadByte();
            settings.Skill = (GameSkill)reader.ReadByte();
            settings.GameMode = (GameMode)reader.ReadByte();
            settings.MissionPack = (MissionPack)reader.ReadByte();
            var flags = reader.ReadByte();
            settings.Deathmatch = (flags & 1) != 0;
            settings.FastMonsters = (flags & 2) != 0;
            settings.RespawnMonsters = (flags & 4) != 0;
            settings.NoMonsters = (flags & 8) != 0;
            settings.TeammatesBlock = (flags & 16) != 0;
            settings.CoopDeathmatchWeapons = (flags & 32) != 0;
            settings.SharedKeys = (flags & 64) != 0;
            settings.WadChecksum = reader.ReadUInt32();
            return settings;
        }
    }

    public sealed class NetTic
    {
        public int Number;
        public byte Forward;
        public byte Side;
        public short AngleTurn;
        public byte Buttons;

        public void CopyTo(TicCmd cmd)
        {
            cmd.ForwardMove = (sbyte)Forward;
            cmd.SideMove = (sbyte)Side;
            cmd.AngleTurn = AngleTurn;
            cmd.Buttons = Buttons;
        }

        public static NetTic From(TicCmd cmd)
        {
            return new NetTic
            {
                Forward = (byte)cmd.ForwardMove,
                Side = (byte)cmd.SideMove,
                AngleTurn = cmd.AngleTurn,
                Buttons = cmd.Buttons
            };
        }
    }

    /// <summary>
    /// Transport for a small co-op session. One host accepts up to three
    /// guests; tics travel over TCP and the host relays them, so every client
    /// only needs a connection to the host.
    /// </summary>
    public sealed class NetSession : IDisposable
    {
        private readonly object sync = new object();

        private readonly bool isHost;
        private readonly int expectedPlayers;
        private readonly string localName;

        private readonly List<Peer> peers = new List<Peer>();
        private readonly Dictionary<int, Dictionary<int, NetTic>> incoming = new Dictionary<int, Dictionary<int, NetTic>>();
        private readonly List<string> names = new List<string>();

        private TcpListener listener;
        private TcpClient hostConnection;
        private Thread acceptThread;

        private readonly List<RelayLink> relayLinks = new List<RelayLink>();
        private RelayLink relayLink;
        private Thread relayHostThread;
        private Presence presence;

        private volatile bool disposed;
        private volatile bool faulted;
        private volatile bool finished;
        private string faultReason = "";

        private int localPlayer;
        private int connectedPlayers;
        private SessionSettings settings;

        private int delay;
        private readonly int delayOverride;

        private NetSession(bool isHost, int expectedPlayers, string localName)
            : this(isHost, expectedPlayers, localName, -1)
        {
        }

        private NetSession(bool isHost, int expectedPlayers, string localName, int delayOverride)
        {
            this.isHost = isHost;
            this.expectedPlayers = MathEx.Clamp(expectedPlayers, 2, NetProtocol.MaxPlayers);
            this.localName = localName;
            this.delayOverride = delayOverride;

            for (var i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                incoming[i] = new Dictionary<int, NetTic>();
            }

            names.Add(localName);
        }

        public static NetSession StartHost(int port, int players, string name, SessionSettings settings)
        {
            return StartHost(port, players, name, settings, -1);
        }

        /// <summary>
        /// <paramref name="delayOverride"/> forces the input delay in tics. -1
        /// measures the round trip and decides, which is right for players; a
        /// fixed value is right for tests that must be reproducible.
        /// </summary>
        public static NetSession StartHost(int port, int players, string name, SessionSettings settings, int delayOverride)
        {
            var session = new NetSession(true, players, name, delayOverride);
            session.settings = settings;
            session.localPlayer = 0;
            session.connectedPlayers = 1;
            session.OpenListener(port);
            return session;
        }

        public static NetSession Join(string address, int port, string name, int timeoutMs)
        {
            var session = new NetSession(false, 2, name, -1);

            try
            {
                session.Connect(address, port, timeoutMs);
            }
            catch
            {
                // A failed connect must not leave a half-open socket retrying in
                // the background - the caller may immediately retry through the
                // relay, and the leaked socket would still be trying the direct
                // address.
                session.Dispose();
                throw;
            }

            return session;
        }

        ////////////////////////////////////////////////////////////
        // Host side
        ////////////////////////////////////////////////////////////

        private void OpenListener(int port)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Name = "DPTDOOM net accept";
            acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (!disposed && !faulted)
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

                try
                {
                    AcceptGuest(client);
                }
                catch (Exception e)
                {
                    Fault("guest handshake failed: " + e.Message);
                    try
                    {
                        client.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void AcceptGuest(TcpClient client)
        {
            client.NoDelay = true;
            AcceptGuestStream(client.GetStream(), client);
        }

        /// <summary>
        /// A guest arriving through the relay is not a socket this process
        /// accepted - it is a stream the master paired with us. Everything after
        /// the hello is identical, which is deliberate: the game cannot tell a
        /// direct guest from a relayed one, so a relayed session cannot drift
        /// from a direct one.
        /// </summary>
        private void AcceptGuestStream(Stream stream, TcpClient client)
        {
            var reader = new BinaryReader(stream);

            var hello = ReadFrame(reader);
            if (hello == null || hello.Value.Type != NetProtocol.TypeHello)
            {
                SendFrame(stream, NetProtocol.TypeReject, EncodeReason("expected hello"), null);
                CloseGuest(client, stream);
                return;
            }

            string guestName;
            using (var payload = new MemoryStream(hello.Value.Payload))
            using (var payloadReader = new BinaryReader(payload))
            {
                guestName = NetProtocol.ReadString(payloadReader);
            }

            lock (sync)
            {
                if (connectedPlayers >= expectedPlayers)
                {
                    SendFrame(stream, NetProtocol.TypeReject, EncodeReason("session is full"), null);
                    CloseGuest(client, stream);
                    return;
                }

                var playerIndex = connectedPlayers;
                connectedPlayers++;
                names.Add(guestName);

                var peer = new Peer();
                peer.Client = client;
                peer.Stream = stream;
                peer.PlayerIndex = playerIndex;
                peer.Name = guestName;

                // Measure the round trip before the welcome goes out, and put
                // the result in it. Both ends then start with the same input
                // delay, which they must: a delay that differs between host and
                // guest shifts tic numbering on one side and desyncs the game.
                //
                // A fixed override skips the probe entirely, so a test can pin
                // the delay and stay reproducible.
                var rtt = -1;
                var spread = 0;

                if (delayOverride < 0)
                {
                    rtt = MeasureRoundTrip(stream, reader, out spread);
                }

                var measured = delayOverride >= 0
                    ? delayOverride
                    : DelayFromRoundTrip(rtt, spread, delayOverride);

                delay = Math.Max(delay, measured);

                SendWelcome(stream, playerIndex, guestName);

                peers.Add(peer);

                var thread = new Thread(() => ReadLoop(peer));
                thread.IsBackground = true;
                thread.Name = "DPTDOOM net peer " + playerIndex;
                peer.Thread = thread;
                thread.Start();

                Console.WriteLine("Net: '" + guestName + "' joined as player " + (playerIndex + 1) +
                                  (Delay > 0 ? ", input delay " + Delay + " tics" : "") + ".");
            }
        }

        /// <summary>
        /// Round-trip time in milliseconds, with the spikes trimmed.
        ///
        /// This runs before the welcome so the number can travel inside it. The
        /// guest answers pings while it is still waiting for the welcome, which
        /// is why it cannot simply be measured later: the game would already be
        /// running and the delay would have to change mid-session.
        ///
        /// Seven probes, then the fastest and slowest are dropped and the rest
        /// averaged.
        ///
        /// The first version took the *minimum* of three probes, which measures
        /// the best case the path can ever do and leaves no room for jitter. On a
        /// real international link that made the buffer too small: the session
        /// held 35 tics per second for a while and then stalled for a few tics
        /// whenever a packet took longer than the best case, which is most of the
        /// time. Trimming both ends instead of keeping the fastest gives the
        /// buffer something to breathe.
        ///
        /// The slowest sample is dropped because a single stall during setup
        /// (a DNS-free connect, a busy machine) would otherwise inflate the delay
        /// for the whole session.
        ///
        /// Returns -1 when the peer does not answer, and the caller then keeps
        /// the conservative default. <paramref name="spreadMs"/> reports how far
        /// the probes disagreed, which sizes the jitter margin.
        /// </summary>
        private static int MeasureRoundTrip(Stream stream, BinaryReader reader, out int spreadMs)
        {
            const int probes = 7;

            spreadMs = 0;

            var previousTimeout = stream.CanTimeout ? stream.ReadTimeout : 0;
            var watch = new Stopwatch();
            var samples = new List<long>();

            try
            {
                if (stream.CanTimeout)
                {
                    stream.ReadTimeout = 3000;
                }

                for (var attempt = 0; attempt < probes; attempt++)
                {
                    var payload = new byte[4];
                    NetProtocol.WriteUInt32(payload, 0, (uint)attempt);

                    watch.Restart();
                    SendFrame(stream, NetProtocol.TypePing, payload, null);

                    var reply = ReadFrame(reader);
                    if (reply == null || reply.Value.Type != NetProtocol.TypePong)
                    {
                        break;
                    }

                    watch.Stop();
                    samples.Add(watch.ElapsedMilliseconds);
                }
            }
            catch (Exception)
            {
                // A slow or silent peer just means no measurement.
            }
            finally
            {
                if (stream.CanTimeout)
                {
                    try
                    {
                        stream.ReadTimeout = previousTimeout;
                    }
                    catch
                    {
                    }
                }
            }

            if (samples.Count == 0)
            {
                return -1;
            }

            if (samples.Count > 2)
            {
                samples.Sort();
                samples.RemoveAt(samples.Count - 1);
                samples.RemoveAt(0);
            }

            long total = 0;
            var min = long.MaxValue;
            var max = long.MinValue;

            foreach (var sample in samples)
            {
                total += sample;
                if (sample < min) { min = sample; }
                if (sample > max) { max = sample; }
            }

            spreadMs = (int)(max - min);
            return (int)(total / samples.Count);
        }

        /// <summary>
        /// How many tics of input to keep in hand.
        ///
        /// Lockstep advances one tic per network round trip: the tic cannot be
        /// simulated until the peer's command for it has arrived, and the peer
        /// only generates that command when it gets to the same tic. At 100 ms of
        /// ping that caps the session at roughly 17 tics per second while the
        /// game asks for 35, so the world runs at half speed and every action
        /// feels late.
        ///
        /// Sending commands a few tics ahead breaks that coupling: by the time
        /// tic N is simulated, the peer's command for N was sent N-delay tics ago
        /// and has long arrived. The price is delay tics of input latency, which
        /// is the trade every lockstep game makes.
        ///
        /// One tic of margin is added only when the path actually jitters. A
        /// margin on a clean link is pure latency with nothing to show for it,
        /// and at 100 ms of ping a tic is 29 ms the player feels.
        /// </summary>
        private static int DelayFromRoundTrip(int roundTripMs, int spreadMs, int overrideTics)
        {
            if (overrideTics >= 0)
            {
                return MathEx.Clamp(overrideTics, 0, 10);
            }

            if (roundTripMs < 0)
            {
                // No measurement. Four tics covers about 115 ms, which is a
                // better guess for the internet than assuming a LAN.
                return 4;
            }

            var ticMs = 1000 / NetProtocol.TicsPerSecond;

            if (roundTripMs < ticMs / 2)
            {
                // Same machine or a fast LAN: no buffer, no added latency.
                return 0;
            }

            var tics = (roundTripMs + ticMs - 1) / ticMs;

            // Jitter breaks an exactly-sized buffer: the first chunk that takes
            // longer than the measurement stalls the tic. Only spend the extra
            // tic when the probes actually disagree.
            if (spreadMs > ticMs / 2)
            {
                tics++;
            }

            return MathEx.Clamp(tics, 1, 10);
        }

        private static void CloseGuest(TcpClient client, Stream stream)
        {
            try
            {
                stream.Close();
            }
            catch
            {
            }

            if (client != null)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }
        }

        private void SendWelcome(Stream stream, int playerIndex, string guestName)
        {
            using (var payload = new MemoryStream())
            using (var writer = new BinaryWriter(payload))
            {
                writer.Write((byte)playerIndex);
                writer.Write((byte)expectedPlayers);
                writer.Write(settings.Serialize());
                writer.Write((byte)Delay);

                lock (sync)
                {
                    writer.Write((byte)names.Count);
                    foreach (var name in names)
                    {
                        NetProtocol.WriteString(writer, name);
                    }
                }

                writer.Flush();
                SendFrame(stream, NetProtocol.TypeWelcome, payload.ToArray(), null);
            }
        }

        ////////////////////////////////////////////////////////////
        // Client side
        ////////////////////////////////////////////////////////////

        private void Connect(string address, int port, int timeoutMs)
        {
            hostConnection = new TcpClient();
            hostConnection.NoDelay = true;

            var result = hostConnection.BeginConnect(address, port, null, null);
            if (!result.AsyncWaitHandle.WaitOne(timeoutMs, false))
            {
                throw new Exception("Connection to " + address + ":" + port + " timed out.");
            }

            hostConnection.EndConnect(result);

            Handshake(hostConnection.GetStream());
        }

        /// <summary>
        /// Joins through the master's relay. The stream is already open, so there
        /// is no address to dial and no timeout of our own: the only thing left
        /// is the ordinary game handshake, byte for byte the same as direct.
        /// </summary>
        public static NetSession JoinRelay(RelayLink link, string name)
        {
            var session = new NetSession(false, 2, name);
            session.relayLink = link;
            session.Relayed = true;
            session.Handshake(link.Stream);
            return session;
        }

        /// <summary>
        /// Opens and keeps a relay channel for this host and accepts whatever the
        /// master pairs with it. The local listener keeps running, so a host is
        /// reachable both directly and through the relay with one command line.
        /// </summary>
        public void StartRelayHost(string masterAddress, int masterPort, byte[] auth)
        {
            relayHostThread = new Thread(() => RelayHostLoop(masterAddress, masterPort, auth));
            relayHostThread.IsBackground = true;
            relayHostThread.Name = "DPTDOOM relay host";
            relayHostThread.Start();
        }

        private void RelayHostLoop(string address, int port, byte[] auth)
        {
            while (!disposed && !faulted && !finished)
            {
                RelayLink link = null;

                try
                {
                    link = MasterClient.RelayHost(address, port, auth, 10000);

                    Console.WriteLine("Net: relay channel open, waiting for a guest.");
                    Relayed = true;

                    lock (sync)
                    {
                        relayLinks.Add(link);
                    }

                    AcceptGuestStream(link.Stream, null);

                    lock (sync)
                    {
                        relayLinks.Remove(link);
                    }

                    // Do not park a second waiting channel while the session is
                    // full: the master would hold a socket open for a guest that
                    // could never be admitted.
                    while (!disposed && !faulted && PlayerCount >= expectedPlayers)
                    {
                        Thread.Sleep(500);
                    }
                }
                catch (Exception e)
                {
                    if (disposed || faulted || finished)
                    {
                        break;
                    }

                    Console.WriteLine("Net: relay attach failed: " + e.Message);
                    Thread.Sleep(2000);
                }
            }
        }

        private void Handshake(Stream stream)
        {
            var reader = new BinaryReader(stream);

            using (var payload = new MemoryStream())
            using (var writer = new BinaryWriter(payload))
            {
                NetProtocol.WriteString(writer, localName);
                writer.Flush();
                SendFrame(stream, NetProtocol.TypeHello, payload.ToArray(), null);
            }

            var reply = awaitWelcome(reader, stream);

            using (var payload = new MemoryStream(reply.Payload))
            using (var payloadReader = new BinaryReader(payload))
            {
                localPlayer = payloadReader.ReadByte();
                var playerCount = payloadReader.ReadByte();
                settings = SessionSettings.Deserialize(payloadReader);

                // The host measured the round trip and decided how far ahead to
                // send commands. Both ends must use the same number or their tic
                // numbering diverges.
                delay = payloadReader.ReadByte();

                var count = payloadReader.ReadByte();
                lock (sync)
                {
                    names.Clear();
                    for (var i = 0; i < count; i++)
                    {
                        names.Add(NetProtocol.ReadString(payloadReader));
                    }

                    connectedPlayers = playerCount;
                }
            }

            var peer = new Peer();
            peer.Client = hostConnection;
            peer.Stream = stream;
            peer.PlayerIndex = 0;
            peer.Name = "host";

            peers.Add(peer);

            var thread = new Thread(() => ReadLoop(peer));
            thread.IsBackground = true;
            thread.Name = "DPTDOOM net host";
            peer.Thread = thread;
            thread.Start();
        }

        /// <summary>
        /// Waits for the welcome while answering the host's round-trip probes.
        ///
        /// The host measures the round trip before it sends the welcome, because
        /// the measurement has to travel inside it: both ends must start with the
        /// same input delay. That means the guest has to reply to pings from
        /// inside this wait - the read loop does not exist yet at this point.
        /// </summary>
        private Frame awaitWelcome(BinaryReader reader, Stream stream)
        {
            var previousTimeout = stream.CanTimeout ? stream.ReadTimeout : 0;

            try
            {
                if (stream.CanTimeout)
                {
                    stream.ReadTimeout = 15000;
                }

                while (true)
                {
                    var reply = ReadFrame(reader);

                    if (reply == null)
                    {
                        throw new Exception("The host closed the connection.");
                    }

                    var frame = reply.Value;

                    if (frame.Type == NetProtocol.TypePing)
                    {
                        SendFrame(stream, NetProtocol.TypePong, frame.Payload, null);
                        continue;
                    }

                    if (frame.Type == NetProtocol.TypeReject)
                    {
                        var reason = "";
                        using (var payload = new MemoryStream(frame.Payload))
                        using (var payloadReader = new BinaryReader(payload))
                        {
                            reason = NetProtocol.ReadString(payloadReader);
                        }

                        throw new Exception("Host refused the connection: " + reason);
                    }

                    if (frame.Type != NetProtocol.TypeWelcome)
                    {
                        throw new Exception("Unexpected reply from host.");
                    }

                    return frame;
                }
            }
            finally
            {
                if (stream.CanTimeout)
                {
                    try
                    {
                        stream.ReadTimeout = previousTimeout;
                    }
                    catch
                    {
                    }
                }
            }
        }

        ////////////////////////////////////////////////////////////
        // Common
        ////////////////////////////////////////////////////////////

        private void ReadLoop(Peer peer)
        {
            try
            {
                var reader = new BinaryReader(peer.Stream);

                while (!disposed)
                {
                    var frame = ReadFrame(reader);
                    if (frame == null)
                    {
                        break;
                    }

                    HandleFrame(peer, frame.Value.Type, frame.Value.Payload);
                }
            }
            catch (Exception)
            {
                // Falls through to the disconnect handling below.
            }

            if (!disposed && !finished)
            {
                Fault("connection to '" + peer.Name + "' was lost");
            }
        }

        private void HandleFrame(Peer peer, ushort type, byte[] payload)
        {
            switch (type)
            {
                case NetProtocol.TypeTic:
                    HandleTic(peer, payload);
                    break;

                case NetProtocol.TypeBye:
                    finished = true;
                    break;

                case NetProtocol.TypePing:
                    SendFrame(peer.Stream, NetProtocol.TypePong, new byte[0], peer);
                    break;
            }
        }

        private void HandleTic(Peer peer, byte[] payload)
        {
            if (payload.Length < 9)
            {
                return;
            }

            var tic = new NetTic();
            tic.Number = (int)NetProtocol.ReadUInt32(payload, 0);
            var player = payload[4];
            tic.Forward = payload[5];
            tic.Side = payload[6];
            tic.AngleTurn = (short)(payload[7] | (payload[8] << 8));
            tic.Buttons = payload.Length > 9 ? payload[9] : (byte)0;

            if (player >= NetProtocol.MaxPlayers)
            {
                return;
            }

            StoreTic(player, tic);

            // The host relays guests' tics to the other guests.
            if (isHost)
            {
                foreach (var other in peers)
                {
                    if (!ReferenceEquals(other, peer) && other.Stream != null)
                    {
                        SendFrame(other.Stream, NetProtocol.TypeTic, payload, other);
                    }
                }
            }
        }

        private void StoreTic(int player, NetTic tic)
        {
            lock (sync)
            {
                var queue = incoming[player];
                if (!queue.ContainsKey(tic.Number))
                {
                    queue.Add(tic.Number, tic);
                }
            }
        }

        public void SendTic(int player, NetTic tic)
        {
            var payload = new byte[10];
            NetProtocol.WriteUInt32(payload, 0, (uint)tic.Number);
            payload[4] = (byte)player;
            payload[5] = tic.Forward;
            payload[6] = tic.Side;
            payload[7] = (byte)(tic.AngleTurn & 0xFF);
            payload[8] = (byte)((tic.AngleTurn >> 8) & 0xFF);
            payload[9] = tic.Buttons;

            if (!isHost)
            {
                var peer = peers[0];
                SendFrame(peer.Stream, NetProtocol.TypeTic, payload, peer);
                return;
            }

            StoreTic(player, tic);

            foreach (var peer in peers)
            {
                SendFrame(peer.Stream, NetProtocol.TypeTic, payload, peer);
            }
        }

        public bool HasTic(int player, int number)
        {
            lock (sync)
            {
                return incoming[player].ContainsKey(number);
            }
        }

        public bool TryTakeTic(int player, int number, out NetTic tic)
        {
            lock (sync)
            {
                var queue = incoming[player];

                tic = null;
                if (!queue.TryGetValue(number, out tic))
                {
                    return false;
                }

                queue.Remove(number);

                // Anything older than the current tic can never be used again.
                if (queue.Count > 0)
                {
                    var stale = new List<int>();
                    foreach (var key in queue.Keys)
                    {
                        if (key < number)
                        {
                            stale.Add(key);
                        }
                    }

                    foreach (var key in stale)
                    {
                        queue.Remove(key);
                    }
                }

                return true;
            }
        }

        private void Fault(string reason)
        {
            if (disposed || faulted)
            {
                return;
            }

            faulted = true;
            faultReason = reason;
        }

        private static byte[] EncodeReason(string reason)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                NetProtocol.WriteString(writer, reason);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void SendFrame(Stream stream, ushort type, byte[] payload, Peer peer)
        {
            var frame = NetProtocol.BuildFrame(type, payload);

            try
            {
                if (peer != null)
                {
                    lock (peer.WriteLock)
                    {
                        stream.Write(frame, 0, frame.Length);
                        stream.Flush();
                    }
                }
                else
                {
                    stream.Write(frame, 0, frame.Length);
                    stream.Flush();
                }
            }
            catch (Exception)
            {
                // A failed write means the peer is gone; the read loop will
                // notice and report it.
            }
        }

        private static Frame? ReadFrame(BinaryReader reader)
        {
            var header = new byte[NetProtocol.HeaderSize];
            var read = 0;
            while (read < header.Length)
            {
                var count = reader.Read(header, read, header.Length - read);
                if (count <= 0)
                {
                    return null;
                }

                read += count;
            }

            if (header[0] != NetProtocol.Magic[0] ||
                header[1] != NetProtocol.Magic[1] ||
                header[2] != NetProtocol.Magic[2] ||
                header[3] != NetProtocol.Magic[3])
            {
                throw new InvalidDataException("Bad frame magic.");
            }

            var version = NetProtocol.ReadUInt16(header, 4);
            if (version != NetProtocol.Version)
            {
                throw new InvalidDataException("Protocol version mismatch.");
            }

            var type = (ushort)NetProtocol.ReadUInt16(header, 6);
            var length = (int)NetProtocol.ReadUInt32(header, 8);
            if (length < 0 || length > NetProtocol.MaxPayload)
            {
                throw new InvalidDataException("Bad frame length.");
            }

            var payload = new byte[length];
            read = 0;
            while (read < length)
            {
                var count = reader.Read(payload, read, length - read);
                if (count <= 0)
                {
                    return null;
                }

                read += count;
            }

            return new Frame { Type = type, Payload = payload };
        }

        public void AttachPresence(Presence value)
        {
            presence = value;
        }

        public void Finish()
        {
            finished = true;

            foreach (var peer in peers)
            {
                try
                {
                    SendFrame(peer.Stream, NetProtocol.TypeBye, new byte[0], peer);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (presence != null)
            {
                try
                {
                    presence.Dispose();
                }
                catch
                {
                }

                presence = null;
            }

            try
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
            catch
            {
            }

            foreach (var peer in peers)
            {
                try
                {
                    SendFrame(peer.Stream, NetProtocol.TypeBye, new byte[0], peer);
                    peer.Stream.Close();
                }
                catch
                {
                }
            }

            if (hostConnection != null)
            {
                try
                {
                    hostConnection.Close();
                }
                catch
                {
                }
            }

            if (relayLink != null)
            {
                try
                {
                    relayLink.Dispose();
                }
                catch
                {
                }
            }

            lock (sync)
            {
                foreach (var link in relayLinks)
                {
                    try
                    {
                        link.Dispose();
                    }
                    catch
                    {
                    }
                }

                relayLinks.Clear();
            }
        }

        private struct Frame
        {
            public ushort Type;
            public byte[] Payload;
        }

        private sealed class Peer
        {
            public TcpClient Client;
            public Stream Stream;
            public int PlayerIndex;
            public string Name;
            public Thread Thread;
            public readonly object WriteLock = new object();
        }

        public bool IsHost => isHost;
        public int LocalPlayer => localPlayer;
        public int PlayerCount
        {
            get
            {
                lock (sync)
                {
                    return connectedPlayers;
                }
            }
        }

        public int ExpectedPlayers => expectedPlayers;
        public SessionSettings Settings => settings;

        /// <summary>
        /// Input delay in tics, agreed during the handshake. Zero on a LAN.
        /// </summary>
        public int Delay => delay;
        public bool Faulted => faulted;
        public string FaultReason => faultReason;
        public bool Finished => finished;
        public bool Relayed { get; private set; }

        public string[] Names
        {
            get
            {
                lock (sync)
                {
                    return names.ToArray();
                }
            }
        }
    }
}
