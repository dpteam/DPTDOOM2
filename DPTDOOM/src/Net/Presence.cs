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
using System.IO;
using System.Threading;
using DPTDOOM.Shared;

namespace DPTDOOM.Net
{
    /// <summary>
    /// "host" or "host:port" on the command line, in one place.
    ///
    /// Splitting on the last colon is deliberate: it keeps a bare host working,
    /// and there is no IPv6 in this protocol, so the ambiguity that would
    /// otherwise justify a stricter parser does not exist.
    /// </summary>
    public static class NetEndpoint
    {
        public static bool TryParse(string text, int defaultPort, out string host, out int port)
        {
            host = (text ?? "").Trim();
            port = defaultPort;

            if (host.Length == 0)
            {
                return false;
            }

            var colon = host.LastIndexOf(':');

            if (colon >= 0)
            {
                int parsed;
                if (!int.TryParse(host.Substring(colon + 1), out parsed) ||
                    parsed < 1 || parsed > 65535)
                {
                    return false;
                }

                port = parsed;
                host = host.Substring(0, colon);
                return host.Length > 0;
            }

            return true;
        }
    }

    /// <summary>
    /// Keeps a session listed on the master and, when relay is enabled, hands the
    /// relay attach the credential it needs.
    ///
    /// The game registers itself rather than letting the launcher do it: the
    /// launcher exits as soon as the game starts, so a listing created there
    /// would die with it. Registering from the process that actually owns the
    /// session means the heartbeat lives exactly as long as the session.
    /// </summary>
    public sealed class Presence : IDisposable
    {
        private readonly string host;
        private readonly int port;
        private readonly byte[] auth;
        private readonly int intervalMs;

        private Thread thread;
        private volatile bool stopping;

        private Presence(string host, int port, byte[] auth, int intervalMs)
        {
            this.host = host;
            this.port = port;
            this.auth = auth;
            this.intervalMs = intervalMs;
        }

        public static Presence Register(string masterHost, int masterPort, MasterSessionInfo info, int timeoutMs)
        {
            var lease = MasterClient.Register(masterHost, masterPort, info, timeoutMs);

            var presence = new Presence(
                masterHost,
                masterPort,
                lease.Auth,
                Math.Max(lease.HeartbeatSeconds, 3) * 1000);

            presence.thread = new Thread(presence.HeartbeatLoop);
            presence.thread.IsBackground = true;
            presence.thread.Name = "DPTDOOM master heartbeat";
            presence.thread.Start();

            return presence;
        }

        /// <summary>The 40-byte lease: 16-byte public id followed by the secret.</summary>
        public byte[] Auth => auth;

        public string IdHex
        {
            get
            {
                var id = new byte[16];
                Array.Copy(auth, id, 16);
                return MasterProtocol.IdToHex(id);
            }
        }

        private void HeartbeatLoop()
        {
            var step = 100;

            while (!stopping)
            {
                var waited = 0;

                while (waited < intervalMs && !stopping)
                {
                    Thread.Sleep(step);
                    waited += step;
                }

                if (stopping)
                {
                    return;
                }

                try
                {
                    MasterClient.Heartbeat(host, port, auth, 4000);
                }
                catch (Exception e)
                {
                    // A missed heartbeat is not fatal: the listing expires, and
                    // the next one puts it back. Killing the game because the
                    // directory is briefly unreachable would be worse.
                    Console.WriteLine("Net: master heartbeat failed: " + e.Message);
                }
            }
        }

        public void Dispose()
        {
            if (stopping)
            {
                return;
            }

            stopping = true;

            try
            {
                // Removing the listing also closes the relay waiting channel,
                // because the master ties a waiting channel to its lease. Without
                // this, a clean exit would leave a phantom session in the browser
                // until the lease ran out.
                MasterClient.Remove(host, port, auth, 2000);
            }
            catch
            {
            }
        }
    }
}
