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
using System.Security.Cryptography;
using System.Threading;
using DPTDOOM.Shared;

namespace DPTDOOM.Master
{
    /// <summary>
    /// The master directory server: bounded, one request per connection, no
    /// outgoing traffic and no engine linkage.
    /// </summary>
    internal sealed class MasterServer
    {
        private const int RequestDeadlineMs = 3000;
        private const int RateLimitWindowMs = 10000;
        private const int RateLimitRequests = 60;
        private const int RateLimitIps = 512;

        private readonly MasterConfig config;
        private readonly MasterLog log;
        private readonly SessionDirectory directory;
        private readonly List<AddressRule> bans;
        private readonly List<AddressRule> allowed;

        private readonly Dictionary<uint, RateBucket> buckets = new Dictionary<uint, RateBucket>();

        private TcpListener listener;
        private RelayService relay;
        private volatile bool stopping;
        private long startTime;

        public MasterServer(MasterConfig config, MasterLog log)
        {
            this.config = config;
            this.log = log;

            directory = new SessionDirectory(config.LeaseSeconds);

            var warnings = new List<string>();
            bans = AddressRule.Load(config.BanListPath, warnings);
            allowed = AddressRule.Load(config.AllowListPath, warnings);

            foreach (var warning in warnings)
            {
                log.Warn(warning);
            }

            if (config.AllowListOnly && allowed.Count == 0)
            {
                throw new Exception("access-mode=allowlist needs at least one rule in the allow list");
            }
        }

        public void Run()
        {
            var bind = MasterConfig.ParseBind(config.Bind);

            listener = new TcpListener(bind, config.Port);
            listener.Start(64);

            startTime = Now();

            log.Info("DPT.DOOM master v" + ApplicationInfo.Version + " listening on " + bind + ":" + config.Port);
            log.Info("lease=" + config.LeaseSeconds + "s bans=" + bans.Count +
                     " allow=" + allowed.Count + " mode=" + (config.AllowListOnly ? "allowlist" : "open"));

            if (config.RelayEnabled)
            {
                // A separate listener, not the control port: a relay connection
                // lives for hours and moves megabytes, while a directory request
                // is one bounded exchange. Sharing one port would mean one accept
                // loop with two completely different lifetimes, and one slow
                // reader would start holding up LIST replies.
                relay = new RelayService(config, log, directory, IsRelaySourceAllowed);
                relay.Start();
            }

            var acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Name = "DPTDOOM master accept";
            acceptThread.Start();

            var maintenanceThread = new Thread(MaintenanceLoop);
            maintenanceThread.IsBackground = true;
            maintenanceThread.Name = "DPTDOOM master maintenance";
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

        private void MaintenanceLoop()
        {
            while (!stopping)
            {
                Thread.Sleep(1000);

                try
                {
                    directory.Prune(Now());

                    lock (buckets)
                    {
                        if (buckets.Count > RateLimitIps)
                        {
                            var stale = new List<uint>();
                            var now = Now();

                            foreach (var pair in buckets)
                            {
                                if (now - pair.Value.WindowStart > RateLimitWindowMs * 6)
                                {
                                    stale.Add(pair.Key);
                                }
                            }

                            foreach (var key in stale)
                            {
                                buckets.Remove(key);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Warn("maintenance: " + e.Message);
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            var remote = client.Client.RemoteEndPoint as IPEndPoint;
            var host = remote == null ? new byte[4] : remote.Address.GetAddressBytes();

            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = RequestDeadlineMs;
                client.SendTimeout = RequestDeadlineMs;

                using (var stream = client.GetStream())
                {
                    var header = new byte[MasterProtocol.HeaderSize];

                    if (!ReadExactly(stream, header, 0, header.Length))
                    {
                        log.Event(Describe(host, remote) + " closed before a request");
                        return;
                    }

                    var error = MasterProtocol.HeaderError(header);
                    if (error != null)
                    {
                        log.Event(Describe(host, remote) + " rejected: " + error);
                        Reply(stream, 0, new byte[0], MasterProtocol.StatusBadRequest);
                        return;
                    }

                    var operation = (ushort)MasterProtocol.Get16(header, 6);
                    var length = MasterProtocol.RequestLength(header);

                    // Drain the request body before any reply. Replying and then
                    // closing with unread bytes still in the receive buffer makes
                    // Windows send a reset instead of a clean shutdown, and the
                    // reset destroys the reply that was just written. The client
                    // then sees "connection forcibly closed" for what should have
                    // been a clear Unauthorized or Redirect.
                    var body = new byte[length];
                    if (length > 0 && !ReadExactly(stream, body, 0, length))
                    {
                        log.Event(Describe(host, remote) + " truncated request");
                        return;
                    }

                    if (IsBanned(host))
                    {
                        log.Event(Describe(host, remote) + " rejected: banned source");
                        Reply(stream, operation, new byte[0], MasterProtocol.StatusUnauthorized);
                        return;
                    }

                    if (config.AllowListOnly && !IsAllowed(host))
                    {
                        log.Event(Describe(host, remote) + " rejected: source not in allow list");
                        Reply(stream, operation, new byte[0], MasterProtocol.StatusUnauthorized);
                        return;
                    }

                    if (operation == MasterProtocol.OpRelayHost || operation == MasterProtocol.OpRelayJoin)
                    {
                        // The request reached the control port, which never
                        // carries game bytes. Answer with the relay port instead
                        // of a refusal so a client only ever has to know the
                        // master's address: one extra round trip, no extra
                        // setting, and the operator stays free to move the relay.
                        //
                        // The ban and allow lists are checked first: a refused
                        // source should learn it is refused, not be handed the
                        // address of a second listener to try.
                        if (relay != null)
                        {
                            var redirect = new byte[2];
                            MasterProtocol.Put16(redirect, 0, relay.Port);
                            log.Event(Describe(host, remote) + " relay redirected to port " + relay.Port);
                            Reply(stream, operation, redirect, MasterProtocol.StatusRedirect);
                        }
                        else
                        {
                            log.Event(Describe(host, remote) + " relay rejected: not enabled on this master");
                            Reply(stream, operation, new byte[0], MasterProtocol.StatusBadRequest);
                        }

                        return;
                    }

                    if (!TakeRateLimit(host))
                    {
                        log.Event(Describe(host, remote) + " rejected: rate limit");
                        Reply(stream, operation, new byte[0], MasterProtocol.StatusCapacity);
                        return;
                    }

                    HandleRequest(stream, remote, host, operation, body);
                }
            }
            catch (Exception e)
            {
                log.Warn(Describe(host, remote) + " connection error: " + e.Message);
            }
            finally
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

        private void HandleRequest(NetworkStream stream, IPEndPoint remote, byte[] host, ushort operation, byte[] body)
        {
            var now = Now();

            switch (operation)
            {
                case MasterProtocol.OpList:
                {
                    var reply = directory.List(now, relay != null);
                    Reply(stream, operation, reply, MasterProtocol.StatusOk);
                    break;
                }

                case MasterProtocol.OpRegister:
                {
                    var entropy = new byte[MasterProtocol.AuthSize];
                    using (var random = RandomNumberGenerator.Create())
                    {
                        random.GetBytes(entropy);
                    }

                    byte[] lease;
                    var status = directory.Register(host, body, entropy, now, out lease);

                    if (status == MasterProtocol.StatusOk)
                    {
                        var info = MasterSessionInfo.Decode(body, 0);
                        log.Event(Describe(host, remote) + " registered '" + info.Name + "' " +
                                  info.MapText + " port=" + info.Port + " skill=" + info.Skill);
                    }
                    else
                    {
                        log.Event(Describe(host, remote) + " register rejected: status=" + status);
                    }

                    Reply(stream, operation, lease, status);
                    break;
                }

                case MasterProtocol.OpHeartbeat:
                case MasterProtocol.OpRemove:
                {
                    int lease;
                    var status = directory.Renew(host, body, operation == MasterProtocol.OpRemove, now, out lease);

                    if (status == MasterProtocol.StatusOk)
                    {
                        log.Event(Describe(host, remote) + (operation == MasterProtocol.OpRemove ? " removed its listing" : " heartbeat"));
                        var reply = new byte[4];
                        MasterProtocol.Put32(reply, 0, (uint)lease);
                        Reply(stream, operation, reply, MasterProtocol.StatusOk);
                    }
                    else
                    {
                        log.Event(Describe(host, remote) + " heartbeat rejected: status=" + status);
                        Reply(stream, operation, new byte[4], status);
                    }

                    break;
                }

                default:
                    Reply(stream, operation, new byte[0], MasterProtocol.StatusBadRequest);
                    break;
            }
        }

        private static void Reply(NetworkStream stream, ushort operation, byte[] body, ushort status)
        {
            if (body == null)
            {
                body = new byte[0];
            }

            var header = MasterProtocol.Header(operation, body.Length, status);

            stream.Write(header, 0, header.Length);

            if (body.Length > 0)
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

        private bool IsBanned(byte[] host)
        {
            var address = AddressRule.ToUInt32(new IPAddress(host));
            var now = UnixNow();

            foreach (var rule in bans)
            {
                if (rule.Matches(address, now))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAllowed(byte[] host)
        {
            var address = AddressRule.ToUInt32(new IPAddress(host));
            var now = UnixNow();

            foreach (var rule in allowed)
            {
                if (rule.Matches(address, now))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRelaySourceAllowed(byte[] host)
        {
            if (IsBanned(host))
            {
                return false;
            }

            return !config.AllowListOnly || IsAllowed(host);
        }

        private bool TakeRateLimit(byte[] host)
        {
            var key = AddressRule.ToUInt32(new IPAddress(host));
            var now = Now();

            lock (buckets)
            {
                RateBucket bucket;
                if (!buckets.TryGetValue(key, out bucket))
                {
                    if (buckets.Count >= RateLimitIps)
                    {
                        return false;
                    }

                    bucket = new RateBucket();
                    bucket.WindowStart = now;
                    buckets.Add(key, bucket);
                }

                if (now - bucket.WindowStart > RateLimitWindowMs)
                {
                    bucket.WindowStart = now;
                    bucket.Count = 0;
                }

                bucket.Count++;
                return bucket.Count <= RateLimitRequests;
            }
        }

        public void Stop()
        {
            stopping = true;

            if (relay != null)
            {
                relay.Stop();
            }

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

        private static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public long StartTime => startTime;

        private sealed class RateBucket
        {
            public long WindowStart;
            public int Count;
        }
    }
}
