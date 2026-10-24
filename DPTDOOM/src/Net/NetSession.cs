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
using System.Text;
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
    /// Re-opens the link to the host after it dropped.
    ///
    /// A delegate rather than an address because a session can be reached two
    /// ways: directly, and through a relay channel that has to be requested from
    /// the master again. The session has no business knowing which one it is.
    /// Returns a stream on which the handshake has not been performed yet.
    /// </summary>
    public delegate Stream ReconnectDialer();

    /// <summary>
    /// A ring of the last commands this machine sent, in tic order.
    ///
    /// Needed only for reconnection: when the link drops, some of the commands
    /// already handed to the socket never arrive, and the peer cannot advance
    /// past the first one it is missing. Re-sending the window is enough, because
    /// a command is a plain record with a tic number - the receiver ignores
    /// duplicates and drops anything older than the tic it is on. Nothing has to
    /// be acknowledged, so no extra traffic is spent keeping the session alive.
    /// </summary>
    public sealed class TicHistory
    {
        public struct Entry
        {
            public int Player;
            public NetTic Tic;
        }

        private readonly Entry[] entries;
        private readonly int capacity;
        private int count;
        private int next;

        public TicHistory(int capacity)
        {
            this.capacity = capacity;
            entries = new Entry[capacity];
        }

        // Written by the game thread once per tic, read by the network thread
        // when a peer comes back. Both are cheap, and a torn read here would
        // replay a half-written command, so the lock is not optional.
        public void Add(int player, NetTic tic)
        {
            lock (this)
            {
                entries[next] = new Entry { Player = player, Tic = tic };
                next = (next + 1) % capacity;

                if (count < capacity)
                {
                    count++;
                }
            }
        }

        public Entry[] Snapshot()
        {
            lock (this)
            {
                var result = new Entry[count];

                var start = (next - count + capacity) % capacity;
                for (var i = 0; i < count; i++)
                {
                    result[i] = entries[(start + i) % capacity];
                }

                return result;
            }
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
        private int expectedPlayers;
        private readonly string localName;

        private readonly List<Peer> peers = new List<Peer>();
        private readonly Dictionary<int, Dictionary<int, NetTic>> incoming = new Dictionary<int, Dictionary<int, NetTic>>();
        private readonly List<string> names = new List<string>();
        private readonly Queue<string> chatIncoming = new Queue<string>();

        /// <summary>
        /// The last commands this machine originated, kept so a resumed peer can
        /// be given the tics that were lost with the connection.
        ///
        /// On the host this is the local player plus every bot; on a guest it is
        /// only the local player. Both are exactly what this side is responsible
        /// for sending, so replaying the whole buffer never sends anything the
        /// peer could not have expected from us.
        /// </summary>
        private readonly TicHistory localHistory = new TicHistory(256);

        /// <summary>
        /// Host only: the commands guests sent that were forwarded to the other
        /// guests. A resumed guest needs them as much as it needs the host's own,
        /// and they are not in <see cref="localHistory"/> because this machine did
        /// not originate them.
        /// </summary>
        private readonly TicHistory relayedHistory = new TicHistory(256);

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

        private int botCount;

        private int delay;
        private readonly int delayOverride;

        // Reconnection. See docs/NETWORK.md - the important property is that
        // lockstep makes this possible at all: neither side needs a world
        // snapshot, because both stalled at the same tic number and only the
        // commands in between were lost.
        private ReconnectDialer reconnectDialer;
        private int reconnectGraceSeconds = 30;
        private volatile bool reconnecting;
        private int reconnectDeadline;
        private int reconnectAttempts;
        private readonly Dictionary<int, int> awaitingResume = new Dictionary<int, int>();

        /// <summary>
        /// The token issued to each slot, kept for the life of the session.
        ///
        /// It cannot live on the peer object alone: the peer is removed from the
        /// list the moment its socket dies, and the token is exactly what the
        /// returning guest has to be checked against after that.
        /// </summary>
        private readonly Dictionary<int, ulong> slotTokens = new Dictionary<int, ulong>();
        private int resumeTokenSequence;
        private ulong resumeToken;

        private Thread pingThread;
        private readonly object pingLock = new object();
        private readonly Stopwatch pingWatch = new Stopwatch();
        private bool pingOutstanding;
        private int pingMs;
        private int pingSequence;

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

                var handedOff = false;

                try
                {
                    AcceptGuest(client);
                    handedOff = true;
                }
                catch (Exception e)
                {
                    // A failed guest handshake is the guest's problem, not the
                    // session's.
                    //
                    // This used to call Fault, which stopped the whole game: one
                    // stray connection that sent anything other than a valid
                    // handshake - a port scanner, a browser pointed at the port, a
                    // client built from a different version - killed the session for
                    // every player already in it. Verified by sending an HTTP
                    // request to a hosting game: the host exited with "guest
                    // handshake failed: Bad frame magic".
                    //
                    // Anything a remote peer can trigger must not be able to end a
                    // local game. The listener keeps accepting; the peer is closed.
                    Console.WriteLine("Net: rejected a connection (" + e.Message + ").");
                }
                finally
                {
                    // Only a peer that was *not* taken over gets closed here. On
                    // success the socket belongs to a Peer and to its read loop, and
                    // closing it would cut the guest off the moment it joined - which
                    // is exactly what the first version of this fix did.
                    if (!handedOff)
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
            var resume = false;
            var resumePlayer = -1;
            ulong resumeToken = 0;

            using (var payload = new MemoryStream(hello.Value.Payload))
            using (var payloadReader = new BinaryReader(payload))
            {
                guestName = NetProtocol.ReadString(payloadReader);

                // Trailing bytes are optional on purpose: a build that predates
                // reconnection sends only the name, and must still be able to
                // join. Reading them behind a "is there more" check is what keeps
                // the wire format compatible in both directions.
                if (payload.Position < payload.Length)
                {
                    var flags = payloadReader.ReadByte();

                    if ((flags & 1) != 0 && payload.Length - payload.Position >= 9)
                    {
                        resume = true;
                        resumeToken = payloadReader.ReadUInt64();
                        resumePlayer = payloadReader.ReadByte();
                    }
                }
            }

            if (resume)
            {
                AcceptResume(stream, client, reader, guestName, resumePlayer, resumeToken);
                return;
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
                peer.Token = NewResumeToken();

                slotTokens[playerIndex] = peer.Token;

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

                SendWelcome(stream, playerIndex, guestName, peer.Token);

                peers.Add(peer);

                var thread = new Thread(() => ReadLoop(peer));
                thread.IsBackground = true;
                thread.Name = "DPTDOOM net peer " + playerIndex;
                peer.Thread = thread;
                thread.Start();

                StartPingMonitor();

                // Always report the link, not only when a buffer was needed. A
                // player who says "it feels laggy" needs a number to look at, and
                // this is the one number that explains the whole feel of a
                // lockstep session: the tics of input delay it cost to keep the
                // world running at full speed.
                Console.WriteLine("Net: '" + guestName + "' joined as player " + (playerIndex + 1) + " (" +
                                  (rtt >= 0 ? "ping " + rtt + " ms" : "ping not measured") +
                                  ", input delay " + Delay + " tics = " + (Delay * 1000 / NetProtocol.TicsPerSecond) + " ms).");
            }
        }

        private ulong NewResumeToken()
        {
            // The token only has to tell this session's own guests apart, so a
            // counter mixed with the clock is enough; there is nothing to guess
            // and nothing to protect.
            var mixed = (ulong)unchecked(++resumeTokenSequence * 2654435761u) ^ (ulong)Environment.TickCount;
            var token = mixed | ((ulong)1 << 63);

            // Zero is the "no token" value on the wire.
            return token == 0 ? 1 : token;
        }

        /// <summary>
        /// A guest coming back on a new socket, claiming a slot it already had.
        ///
        /// Three things must not happen here, and all three would break the
        /// session rather than just fail it:
        ///  - the slot must not be re-issued, or the player's old commands and
        ///    their new ones would belong to different people;
        ///  - the input delay must not be re-measured, because the delay shifts
        ///    tic numbering and both sides already agreed on one;
        ///  - the old peer must stop being able to fault the session when its
        ///    dead read loop unwinds. It is removed from `peers` instead, and
        ///    ReadLoop treats "no longer in the list" as "superseded, stay quiet".
        /// </summary>
        private void AcceptResume(Stream stream, TcpClient client, BinaryReader reader,
                                  string guestName, int playerIndex, ulong token)
        {
            Peer old = null;
            string slotName;

            lock (sync)
            {
                // Checked against the session's own record of the slot, not
                // against the live peer list: by the time a guest comes back the
                // dead peer has already been dropped from that list.
                ulong expected;
                if (!slotTokens.TryGetValue(playerIndex, out expected) || expected != token)
                {
                    // Either the host has forgotten this player - the grace
                    // period ran out - or the token belongs to another session.
                    // Say so instead of silently starting a new game on a slot
                    // the guest thinks it already owns.
                    SendFrame(stream, NetProtocol.TypeReject, EncodeReason("unknown session"), null);
                    CloseGuest(client, stream);
                    return;
                }

                foreach (var candidate in peers)
                {
                    if (candidate.PlayerIndex == playerIndex)
                    {
                        old = candidate;
                        break;
                    }
                }

                slotName = playerIndex < names.Count ? names[playerIndex] : guestName;
            }

            var peer = new Peer();
            peer.Client = client;
            peer.Stream = stream;
            peer.PlayerIndex = playerIndex;
            peer.Name = slotName;
            peer.Token = token;

            lock (sync)
            {
                // The replacement goes in before the dead one comes out, so the
                // game thread never sees a moment with no peer for this slot.
                // Duplicate sends during the swap are harmless: a tic is keyed by
                // number and the receiver ignores a number it already has.
                SendWelcome(stream, playerIndex, peer.Name, token);
                peers.Add(peer);

                if (old != null)
                {
                    peers.Remove(old);
                }

                awaitingResume.Remove(playerIndex);

                var thread = new Thread(() => ReadLoop(peer));
                thread.IsBackground = true;
                thread.Name = "DPTDOOM net peer " + playerIndex + " (resumed)";
                peer.Thread = thread;
                thread.Start();

                if (awaitingResume.Count == 0)
                {
                    reconnecting = false;
                }
            }

            Console.WriteLine("Net: '" + guestName + "' reconnected as player " + (playerIndex + 1) + ".");

            if (old != null)
            {
                CloseGuest(old.Client, old.Stream);
            }

            // Everything this side originated while the peer was away: the local
            // player's commands and, on the host, the bots'. The guest cannot
            // have advanced past the first tic it lost.
            ReplayHistory(peer);
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

        private void SendWelcome(Stream stream, int playerIndex, string guestName, ulong token)
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

                // The resume token goes last so a guest that predates it reads
                // the payload it knows and ignores the tail.
                writer.Write(token);

                writer.Flush();
                SendFrame(stream, NetProtocol.TypeWelcome, payload.ToArray(), null);
            }
        }

        ////////////////////////////////////////////////////////////
        // Client side
        ////////////////////////////////////////////////////////////

        private void Connect(string address, int port, int timeoutMs)
        {
            var stream = DialDirect(address, port, timeoutMs, out hostConnection);
            Handshake(stream);
        }

        /// <summary>
        /// Opens a plain TCP link to a host, with a bounded wait.
        ///
        /// Shared with reconnection, which is why it is static and why it hands
        /// the client back: the first connection has to be closable from Dispose,
        /// and both paths must time out the same way or a reconnect would hang
        /// where the first join gave up.
        /// </summary>
        public static Stream DialDirect(string address, int port, int timeoutMs, out TcpClient client)
        {
            client = new TcpClient();
            client.NoDelay = true;

            try
            {
                var result = client.BeginConnect(address, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs, false))
                {
                    throw new Exception("Connection to " + address + ":" + port + " timed out.");
                }

                client.EndConnect(result);
                return client.GetStream();
            }
            catch
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }

                client = null;
                throw;
            }
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
                    //
                    // A lost guest is the exception. Its slot is still taken -
                    // that is the point of holding it - so "full" would keep the
                    // host from ever opening the channel the guest needs to come
                    // back through. Without this a relayed session could not
                    // reconnect at all, while a direct one could.
                    while (!disposed && !faulted && PlayerCount >= expectedPlayers)
                    {
                        lock (sync)
                        {
                            if (awaitingResume.Count > 0)
                            {
                                break;
                            }
                        }

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
            Handshake(stream, false);
            AttachHostPeer(stream);
        }

        /// <summary>
        /// The guest half of the handshake, used both for the first join and for
        /// a reconnect.
        ///
        /// On a reconnect nothing about the session may be renegotiated: the slot,
        /// the player count, the settings and above all the input delay are
        /// already fixed, and a delay that changed here would shift tic numbering
        /// on this side alone and desynchronise the world.
        /// </summary>
        private ulong Handshake(Stream stream, bool resume)
        {
            var reader = new BinaryReader(stream);

            using (var payload = new MemoryStream())
            using (var writer = new BinaryWriter(payload))
            {
                NetProtocol.WriteString(writer, localName);

                if (resume)
                {
                    writer.Write((byte)1);
                    writer.Write(resumeToken);
                    writer.Write((byte)localPlayer);
                }

                writer.Flush();
                SendFrame(stream, NetProtocol.TypeHello, payload.ToArray(), null);
            }

            var reply = awaitWelcome(reader, stream);

            var token = 0UL;

            using (var payload = new MemoryStream(reply.Payload))
            using (var payloadReader = new BinaryReader(payload))
            {
                var player = payloadReader.ReadByte();
                var playerCount = payloadReader.ReadByte();

                // The session size comes from the host, not from what the guest
                // assumed. A guest used to be created with two players hardcoded,
                // so in a three- or four-player game it silently simulated only
                // the first two: it waited for a tic that no one would ever send,
                // or advanced without the others. The welcome is the only place
                // that knows how many slots the session really has.
                var hostPlayers = MathEx.Clamp(playerCount, 2, NetProtocol.MaxPlayers);

                var hostSettings = SessionSettings.Deserialize(payloadReader);

                // The host measured the round trip and decided how far ahead to
                // send commands. Both ends must use the same number or their tic
                // numbering diverges.
                var hostDelay = payloadReader.ReadByte();

                var count = payloadReader.ReadByte();
                var receivedNames = new List<string>();
                for (var i = 0; i < count; i++)
                {
                    receivedNames.Add(NetProtocol.ReadString(payloadReader));
                }

                // Zero when the host predates reconnection.
                if (payload.Position + 8 <= payload.Length)
                {
                    token = payloadReader.ReadUInt64();
                }

                if (!resume)
                {
                    localPlayer = player;
                    expectedPlayers = hostPlayers;
                    settings = hostSettings;
                    delay = hostDelay;

                    lock (sync)
                    {
                        names.Clear();
                        names.AddRange(receivedNames);
                        connectedPlayers = playerCount;
                    }
                }
                else
                {
                    if (player != localPlayer || hostPlayers != expectedPlayers)
                    {
                        throw new Exception("The host no longer has this session.");
                    }

                    if (hostDelay != delay)
                    {
                        throw new Exception("The host changed the input delay.");
                    }
                }
            }

            resumeToken = token;

            return token;
        }

        private void AttachHostPeer(Stream stream)
        {
            var peer = new Peer();
            peer.Client = hostConnection;
            peer.Stream = stream;
            peer.PlayerIndex = 0;
            peer.Name = "host";
            peer.Token = resumeToken;

            lock (sync)
            {
                peers.Add(peer);
            }

            var thread = new Thread(() => ReadLoop(peer));
            thread.IsBackground = true;
            thread.Name = "DPTDOOM net host";
            peer.Thread = thread;
            thread.Start();

            StartPingMonitor();
        }

        /// <summary>
        /// Keeps a live round-trip figure for the on-screen overlay.
        ///
        /// The handshake measures the link once, which is enough to size the input
        /// buffer but not enough to answer "is it laggy right now". A player who
        /// suspects a bad connection should be able to look at a number instead of
        /// guessing, and one 16-byte exchange every two seconds costs nothing next
        /// to the 1.5 KB/s the session already uses.
        ///
        /// It never touches the simulation, so it cannot affect determinism.
        /// </summary>
        private void StartPingMonitor()
        {
            if (pingThread != null)
            {
                return;
            }

            pingThread = new Thread(PingLoop);
            pingThread.IsBackground = true;
            pingThread.Name = "DPTDOOM net ping";
            pingThread.Start();
        }

        private void PingLoop()
        {
            var watch = new Stopwatch();

            while (!disposed)
            {
                Thread.Sleep(2000);

                if (disposed)
                {
                    return;
                }

                Peer peer = null;

                lock (sync)
                {
                    // Every peer is checked for silence, not only the one that is
                    // probed. A socket can die without a FIN, and in a three-player
                    // session the peer that goes quiet is often not peers[0].
                    var silent = new List<Peer>();

                    foreach (var candidate in peers)
                    {
                        var gap = unchecked(Environment.TickCount - candidate.LastFrameTicks);

                        if (gap > LinkSilenceMs)
                        {
                            silent.Add(candidate);
                        }
                    }

                    foreach (var dead in silent)
                    {
                        Console.WriteLine("Net: no traffic from '" + dead.Name + "' for " +
                                          (LinkSilenceMs / 1000) + " s, treating the link as dropped.");

                        // Closing the socket unblocks the read loop, which then does
                        // exactly what it does for an ordinary drop.
                        CloseGuest(dead.Client, dead.Stream);
                    }

                    // Only one peer is probed: with a small co-op session they are
                    // all on the same link, and probing every one of them would
                    // multiply the traffic for a number nobody reads twice.
                    if (peers.Count > 0)
                    {
                        peer = peers[0];
                    }
                }

                if (peer == null || peer.Stream == null)
                {
                    continue;
                }

                var payload = new byte[4];
                NetProtocol.WriteUInt32(payload, 0, (uint)pingSequence++);

                lock (pingLock)
                {
                    // Start the clock at send time. Reading a stopwatch before
                    // restarting it measured the previous probe's age, which
                    // produced a number that grew by two seconds per round trip.
                    pingWatch.Restart();
                    pingOutstanding = true;
                }

                SendFrame(peer.Stream, NetProtocol.TypePing, payload, peer);
            }
        }

        private void CompletePing(int roundTripMs)
        {
            // A gentle average: the raw figure jumps with every hiccup, and a
            // number that flickers between 60 and 400 reads as noise rather than
            // information.
            lock (pingLock)
            {
                pingMs = pingMs <= 0 ? roundTripMs : (pingMs * 3 + roundTripMs) / 4;
            }
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

            if (disposed || finished || faulted)
            {
                return;
            }

            // A peer that a resumed socket has replaced is not a lost peer: its
            // read loop unwinds because the old socket was closed on purpose, and
            // treating that as a disconnect would immediately undo the resume.
            lock (sync)
            {
                if (!peers.Contains(peer))
                {
                    return;
                }
            }

            if (isHost)
            {
                BeginPeerLoss(peer);
            }
            else
            {
                BeginReconnect(peer);
            }
        }

        ////////////////////////////////////////////////////////////
        // Reconnection
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Clock arithmetic that survives the signed wrap of
        /// <see cref="Environment.TickCount"/> every 24.8 days.
        ///
        /// Comparing the raw values would make a deadline appear to be decades in
        /// the future - or long past - and the grace period would either never
        /// expire or expire immediately.
        /// </summary>
        private static bool DeadlinePassed(int deadline)
        {
            return unchecked(Environment.TickCount - deadline) >= 0;
        }

        /// <summary>
        /// How long a peer may stay completely silent before the link counts as
        /// dropped rather than merely late.
        /// </summary>
        private const int LinkSilenceMs = 8000;

        /// <summary>
        /// Host side: a guest's socket died. Keep the slot and wait.
        ///
        /// The slot is deliberately not freed. Re-issuing it would hand the
        /// newcomer the dropping player's commands and inventory, and the two
        /// ends would already disagree about who player N is.
        /// </summary>
        private void BeginPeerLoss(Peer peer)
        {
            lock (sync)
            {
                // The socket can die and be replaced before this runs: the guest
                // redials as soon as its own read fails, which may be before the
                // host's read loop unwinds. A peer that has already been replaced
                // must not start a wait that nobody is coming back from.
                if (!peers.Contains(peer))
                {
                    return;
                }

                peers.Remove(peer);
                awaitingResume[peer.PlayerIndex] = unchecked(Environment.TickCount + reconnectGraceSeconds * 1000);
                reconnecting = true;
            }

            Console.WriteLine("Net: lost '" + peer.Name + "', holding the game for up to " +
                              reconnectGraceSeconds + " s.");

            CloseGuest(peer.Client, peer.Stream);
        }

        /// <summary>
        /// Guest side: the link to the host died. Redial until the grace period
        /// runs out.
        /// </summary>
        private void BeginReconnect(Peer peer)
        {
            // No dialer means this side cannot re-open the link (a host, or a
            // test). No token means the host predates reconnection and would
            // read the resume hello as a brand new join.
            if (reconnectDialer == null || resumeToken == 0)
            {
                Fault("connection to '" + peer.Name + "' was lost");
                return;
            }

            lock (sync)
            {
                if (reconnecting || disposed || finished || !peers.Contains(peer))
                {
                    return;
                }

                reconnecting = true;
                reconnectAttempts = 0;
                reconnectDeadline = unchecked(Environment.TickCount + reconnectGraceSeconds * 1000);
                peers.Remove(peer);
            }

            Console.WriteLine("Net: lost the host, reconnecting for up to " + reconnectGraceSeconds + " s.");

            CloseGuest(peer.Client, peer.Stream);

            var thread = new Thread(ReconnectLoop);
            thread.IsBackground = true;
            thread.Name = "DPTDOOM net reconnect";
            thread.Start();
        }

        private void ReconnectLoop()
        {
            while (!disposed && !finished && !faulted)
            {
                if (DeadlinePassed(reconnectDeadline))
                {
                    break;
                }

                try
                {
                    var stream = reconnectDialer();

                    lock (sync)
                    {
                        if (disposed || finished || !reconnecting)
                        {
                            // The session was torn down while the dial was in
                            // flight; drop the socket we just opened rather than
                            // leaving an unused peer attached to it.
                            try
                            {
                                stream.Close();
                            }
                            catch
                            {
                            }

                            return;
                        }
                    }

                    Handshake(stream, true);

                    var peer = new Peer();
                    peer.Client = null;
                    peer.Stream = stream;
                    peer.PlayerIndex = 0;
                    peer.Name = "host";
                    peer.Token = resumeToken;

                    lock (sync)
                    {
                        peers.Add(peer);
                        reconnecting = false;
                    }

                    var thread = new Thread(() => ReadLoop(peer));
                    thread.IsBackground = true;
                    thread.Name = "DPTDOOM net host (resumed)";
                    peer.Thread = thread;
                    thread.Start();

                    // Everything this side sent while the host was unreachable.
                    // Whatever the host missed, it now has; whatever it already
                    // had, it ignores, because a command is keyed by tic number.
                    ReplayHistory(null);

                    Console.WriteLine("Net: reconnected to the host after " + reconnectAttempts + " attempt(s).");
                    return;
                }
                catch (Exception e)
                {
                    lock (sync)
                    {
                        reconnectAttempts++;
                    }

                    if (!disposed && !finished && !faulted)
                    {
                        Console.WriteLine("Net: reconnect attempt failed (" + e.Message + ").");
                    }

                    try
                    {
                        Thread.Sleep(1200);
                    }
                    catch
                    {
                        return;
                    }
                }
            }

            if (!disposed && !finished && !faulted)
            {
                Fault("connection to the host was lost");
            }
        }

        /// <summary>
        /// Re-sends the commands this machine originated, so a peer that was away
        /// can be brought up to date.
        ///
        /// Only commands, never state. That is enough for lockstep: a tic is a
        /// record with a number, the peer applies the ones it is missing in order
        /// and ends up in exactly the same place - which is why reconnection is
        /// possible here at all, and why it costs a few kilobytes rather than a
        /// world snapshot.
        /// </summary>
        private void ReplayHistory(Peer target)
        {
            var snapshot = localHistory.Snapshot();

            if (snapshot.Length == 0)
            {
                return;
            }

            Stream stream;

            if (target != null)
            {
                stream = target.Stream;
            }
            else
            {
                lock (sync)
                {
                    if (peers.Count == 0)
                    {
                        return;
                    }

                    target = peers[0];
                    stream = target.Stream;
                }
            }

            foreach (var entry in snapshot)
            {
                var payload = EncodeTic(entry.Player, entry.Tic);
                SendFrame(stream, NetProtocol.TypeTic, payload, target);
            }

            if (!isHost)
            {
                return;
            }

            // And the other guests' commands, which this machine forwarded but
            // did not originate. Without them a resumed guest in a three-player
            // session still lacks one player's tics for the lost window, and the
            // session stalls again on the first tic it cannot complete.
            foreach (var entry in relayedHistory.Snapshot())
            {
                var payload = EncodeTic(entry.Player, entry.Tic);
                SendFrame(stream, NetProtocol.TypeTic, payload, target);
            }
        }

        /// <summary>
        /// Expires a reconnect that never completed.
        ///
        /// Called from the game loop rather than from a watchdog thread, because
        /// the loop already runs 35 times a second and an extra thread would only
        /// add something else to shut down in the right order.
        /// </summary>
        public void PollReconnect()
        {
            if (disposed || finished || faulted)
            {
                return;
            }

            if (isHost)
            {
                var expired = -1;

                lock (sync)
                {
                    if (awaitingResume.Count == 0)
                    {
                        return;
                    }

                    foreach (var pair in awaitingResume)
                    {
                        if (DeadlinePassed(pair.Value))
                        {
                            expired = pair.Key;
                            break;
                        }
                    }
                }

                if (expired >= 0)
                {
                    Fault("player " + (expired + 1) + " did not come back");
                }

                return;
            }

            lock (sync)
            {
                if (!reconnecting || !DeadlinePassed(reconnectDeadline))
                {
                    return;
                }
            }

            Fault("connection to the host was lost");
        }

        /// <summary>
        /// Lets the session re-open its own link. A session without a dialer -
        /// an automated test, or a host - simply fails on a drop, as before.
        /// </summary>
        public void SetReconnectDialer(ReconnectDialer dialer, int graceSeconds)
        {
            reconnectDialer = dialer;

            if (graceSeconds > 0)
            {
                reconnectGraceSeconds = graceSeconds;
            }
        }

        /// <summary>
        /// Closes the socket of one peer, as if the link had vanished.
        ///
        /// Exists for tests only: a TCP connection cannot be made to die on
        /// demand from managed code, and everything worth checking about
        /// reconnection - the redial, the token, the replay, the tic stream
        /// staying intact - is only reachable if the link can be broken halfway
        /// through a live game.
        /// </summary>
        internal void DropPeerForTest(int playerIndex)
        {
            Peer target = null;

            lock (sync)
            {
                foreach (var peer in peers)
                {
                    if (peer.PlayerIndex == playerIndex)
                    {
                        target = peer;
                        break;
                    }
                }
            }

            if (target != null)
            {
                CloseGuest(target.Client, target.Stream);
            }
        }

        public bool Reconnecting => reconnecting;

        public int ReconnectSecondsLeft
        {
            get
            {
                lock (sync)
                {
                    if (!reconnecting)
                    {
                        return 0;
                    }

                    var deadline = isHost && awaitingResume.Count > 0
                        ? FirstDeadline()
                        : reconnectDeadline;

                    var left = unchecked(deadline - Environment.TickCount);
                    return left <= 0 ? 0 : left / 1000;
                }
            }
        }

        private int FirstDeadline()
        {
            var best = 0;
            var first = true;

            foreach (var pair in awaitingResume)
            {
                if (first || unchecked(pair.Value - best) < 0)
                {
                    best = pair.Value;
                    first = false;
                }
            }

            return best;
        }

        /// <summary>
        /// One ASCII line describing what the session is waiting for. The screen
        /// draws with Doom's font, which has no Cyrillic, so this stays English
        /// like the other in-game status text.
        /// </summary>
        public string ReconnectStatus
        {
            get
            {
                if (!reconnecting)
                {
                    return null;
                }

                var seconds = ReconnectSecondsLeft;

                if (!isHost)
                {
                    return "RECONNECTING " + seconds;
                }

                var name = "PLAYER";

                lock (sync)
                {
                    foreach (var pair in awaitingResume)
                    {
                        name = pair.Key < names.Count ? names[pair.Key] : "PLAYER " + (pair.Key + 1);
                        break;
                    }
                }

                return "WAITING FOR " + name.ToUpperInvariant() + "  " + seconds;
            }
        }

        private void HandleFrame(Peer peer, ushort type, byte[] payload)
        {
            peer.LastFrameTicks = Environment.TickCount;

            switch (type)
            {
                case NetProtocol.TypeTic:
                    HandleTic(peer, payload);
                    break;

                case NetProtocol.TypeBye:
                    finished = true;
                    break;

                case NetProtocol.TypePing:
                    // Echo the sequence number so the sender can match the reply
                    // to the probe, and so a stale pong cannot be mistaken for a
                    // fresh measurement.
                    SendFrame(peer.Stream, NetProtocol.TypePong, payload, peer);
                    break;

                case NetProtocol.TypeChat:
                    HandleChat(peer, payload);
                    break;

                case NetProtocol.TypePong:
                    HandlePong(payload);
                    break;
            }
        }

        private void HandlePong(byte[] payload)
        {
            lock (pingLock)
            {
                if (!pingOutstanding)
                {
                    return;
                }

                // Any pong answers the outstanding probe; the sequence number is
                // carried so the protocol can grow into per-probe timing later.
                pingOutstanding = false;
                CompletePing((int)pingWatch.ElapsedMilliseconds);
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
                relayedHistory.Add(player, tic);

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
            var payload = EncodeTic(player, tic);

            // Recorded before the send, not after: the interesting moment is a
            // socket that dies mid-write, and the command still has to be there
            // when the peer comes back.
            localHistory.Add(player, tic);

            if (!isHost)
            {
                Peer host;

                lock (sync)
                {
                    if (peers.Count == 0)
                    {
                        // Between the drop and the resume there is no socket to
                        // write to; the history above is what will carry it.
                        return;
                    }

                    host = peers[0];
                }

                SendFrame(host.Stream, NetProtocol.TypeTic, payload, host);
                return;
            }

            StoreTic(player, tic);

            List<Peer> targets;

            lock (sync)
            {
                targets = new List<Peer>(peers);
            }

            foreach (var peer in targets)
            {
                SendFrame(peer.Stream, NetProtocol.TypeTic, payload, peer);
            }
        }

        private static byte[] EncodeTic(int player, NetTic tic)
        {
            var payload = new byte[10];
            NetProtocol.WriteUInt32(payload, 0, (uint)tic.Number);
            payload[4] = (byte)player;
            payload[5] = tic.Forward;
            payload[6] = tic.Side;
            payload[7] = (byte)(tic.AngleTurn & 0xFF);
            payload[8] = (byte)((tic.AngleTurn >> 8) & 0xFF);
            payload[9] = tic.Buttons;
            return payload;
        }

        /// <summary>
        /// Sends one chat line, prefixed with who said it.
        ///
        /// The sender's index rather than their name travels on the wire: names
        /// are already known to everyone from the welcome, and a name could be
        /// longer than the message.
        ///
        /// The host forwards a guest's line to the other guests, exactly as it
        /// forwards tics. A guest only ever talks to the host, so without the
        /// forward a three-player session would have two players who never see
        /// each other's messages.
        /// </summary>
        public void SendChat(int player, string text)
        {
            if (string.IsNullOrEmpty(text) || player < 0 || player >= NetProtocol.MaxPlayers)
            {
                return;
            }

            var bytes = Encoding.ASCII.GetBytes(text);
            var limit = Math.Min(bytes.Length, Chat.MaxLength);

            var payload = new byte[1 + limit];
            payload[0] = (byte)player;
            Array.Copy(bytes, 0, payload, 1, limit);

            List<Peer> targets;

            if (!isHost)
            {
                targets = new List<Peer>();
                lock (sync)
                {
                    if (peers.Count > 0)
                    {
                        targets.Add(peers[0]);
                    }
                }
            }
            else
            {
                lock (sync)
                {
                    targets = new List<Peer>(peers);
                }
            }

            foreach (var peer in targets)
            {
                SendFrame(peer.Stream, NetProtocol.TypeChat, payload, peer);
            }
        }

        /// <summary>
        /// Takes one received chat line, already formatted as "Name: text".
        ///
        /// Formatted here rather than by the caller so that both ends show the
        /// same thing: the name comes from the session's own name table, which is
        /// the only place that knows the mapping from a player index to a person.
        /// </summary>
        public bool TryTakeChat(out string text)
        {
            lock (sync)
            {
                if (chatIncoming.Count == 0)
                {
                    text = null;
                    return false;
                }

                text = chatIncoming.Dequeue();
                return true;
            }
        }

        private void HandleChat(Peer peer, byte[] payload)
        {
            if (payload.Length < 1)
            {
                return;
            }

            var player = payload[0];

            if (player >= NetProtocol.MaxPlayers)
            {
                return;
            }

            var body = Encoding.ASCII.GetString(payload, 1, payload.Length - 1);

            if (body.Length == 0)
            {
                return;
            }

            string name;

            lock (sync)
            {
                name = player < names.Count ? names[player] : "player " + (player + 1);
                chatIncoming.Enqueue(name + ": " + body);
            }

            // The host is the hub: a guest's line has to reach the other guests,
            // and they are only connected to the host.
            if (isHost && !ReferenceEquals(peer, null))
            {
                List<Peer> others;

                lock (sync)
                {
                    others = new List<Peer>();
                    foreach (var other in peers)
                    {
                        if (!ReferenceEquals(other, peer))
                        {
                            others.Add(other);
                        }
                    }
                }

                foreach (var other in others)
                {
                    SendFrame(other.Stream, NetProtocol.TypeChat, payload, other);
                }
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
            public ulong Token;
            public readonly object WriteLock = new object();

            /// <summary>
            /// When a frame last arrived. A TCP read blocks forever on a link
            /// that died without a FIN - a Wi-Fi drop, a NAT mapping that expired -
            /// so silence has to be noticed by something that is not blocked.
            /// </summary>
            public int LastFrameTicks = Environment.TickCount;
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

        /// <summary>
        /// Slots filled by bots rather than by people.
        ///
        /// A bot is driven by the host and its command is sent like any other
        /// player's, so the guests neither run bot logic nor need to: they just
        /// receive the tics. That is why a guest does not need to know anything
        /// about the bot except that the slot will not be taken by a human.
        /// </summary>
        public int BotCount => botCount;

        /// <summary>Slots that have to be filled by a real connection.</summary>
        public int HumansNeeded => expectedPlayers - botCount;

        public void AttachBots(int count)
        {
            botCount = MathEx.Clamp(count, 0, expectedPlayers - 1);
        }
        public SessionSettings Settings => settings;

        /// <summary>
        /// Input delay in tics, agreed during the handshake. Zero on a LAN.
        /// </summary>
        public int Delay => delay;

        /// <summary>
        /// Live round-trip time in milliseconds, averaged over the last probes.
        /// Zero until the first reply arrives, or when the session has no peers.
        /// </summary>
        public int PingMs
        {
            get
            {
                lock (pingLock)
                {
                    return pingMs;
                }
            }
        }
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
