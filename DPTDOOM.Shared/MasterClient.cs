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
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace DPTDOOM.Shared
{
    /// <summary>
    /// Blocking master client. Every call opens one connection, sends one
    /// request and reads one response, which matches the server's model and
    /// keeps the client free of background state.
    /// </summary>
    public static class MasterClient
    {
        /// <summary>
        /// Default time budget for one request, in milliseconds.
        ///
        /// Four seconds is deliberately short: the master is a directory, not a
        /// game server, so a slow answer means something is wrong. A launcher that
        /// waits thirty seconds on a closed port looks broken.
        /// </summary>
        public const int DefaultTimeoutMs = 4000;

        public static List<MasterServerEntry> List(string host, int port, int timeoutMs)
        {
            var body = Exchange(host, port, MasterProtocol.OpList, new byte[0], timeoutMs);

            var entries = new List<MasterServerEntry>();

            if (body.Length < 4)
            {
                return entries;
            }

            var count = (int)MasterProtocol.Get32(body, 0);
            if (count < 0 || count > MasterProtocol.MaxServers || body.Length < 4 + count * MasterProtocol.RowSize)
            {
                return entries;
            }

            for (var i = 0; i < count; i++)
            {
                var offset = 4 + i * MasterProtocol.RowSize;

                var entry = new MasterServerEntry();
                Array.Copy(body, offset, entry.Id, 0, 16);
                Array.Copy(body, offset + 16, entry.Address, 0, 4);
                entry.LeaseSecondsLeft = MasterProtocol.Get32(body, offset + 20);
                entry.Info = MasterSessionInfo.Decode(body, offset + 24);
                entry.RelayAvailable = MasterProtocol.Get32(body, offset + 156) != 0;

                entries.Add(entry);
            }

            return entries;
        }

        public static MasterLease Register(string host, int port, MasterSessionInfo info, int timeoutMs)
        {
            var body = Exchange(host, port, MasterProtocol.OpRegister, info.Encode(), timeoutMs);

            if (body.Length < MasterProtocol.LeaseSize)
            {
                throw new Exception("The master returned a truncated lease.");
            }

            var lease = new MasterLease();
            lease.Auth = new byte[MasterProtocol.AuthSize];
            Array.Copy(body, lease.Auth, MasterProtocol.AuthSize);
            lease.LeaseSeconds = (int)MasterProtocol.Get32(body, 40);
            lease.HeartbeatSeconds = (int)MasterProtocol.Get32(body, 44);
            return lease;
        }

        public static int Heartbeat(string host, int port, byte[] auth, int timeoutMs)
        {
            var body = Exchange(host, port, MasterProtocol.OpHeartbeat, auth, timeoutMs);
            return body.Length >= 4 ? (int)MasterProtocol.Get32(body, 0) : 0;
        }

        public static void Remove(string host, int port, byte[] auth, int timeoutMs)
        {
            Exchange(host, port, MasterProtocol.OpRemove, auth, timeoutMs);
        }

        /// <summary>
        /// One request, one response.
        ///
        /// The time budget is **total**, not per operation. The first version set
        /// a timeout on each step — connect, write, read header, read body — which
        /// meant a dead host could occupy four times the advertised limit before
        /// the launcher gave up. A single deadline is what a person means by
        /// "should answer in four seconds".
        ///
        /// Failures are surfaced as <see cref="SocketException"/> wherever possible,
        /// because the caller can turn a socket error code into advice ("port
        /// closed" versus "name not found") and cannot do that with a bare message.
        /// </summary>
        private static byte[] Exchange(string host, int port, ushort operation, byte[] payload, int timeoutMs)
        {
            var deadline = Environment.TickCount + timeoutMs;

            using (var client = new TcpClient())
            {
                client.NoDelay = true;

                var result = client.BeginConnect(host, port, null, null);

                if (!result.AsyncWaitHandle.WaitOne(Remaining(deadline), false))
                {
                    // Leave the socket in a known state before throwing, or the
                    // half-open connect keeps retrying in the background.
                    try
                    {
                        client.Close();
                    }
                    catch
                    {
                    }

                    throw new SocketException((int)SocketError.TimedOut);
                }

                client.EndConnect(result);

                using (var stream = client.GetStream())
                {
                    var header = MasterProtocol.Header(operation, payload.Length, 0);

                    WriteAll(stream, header, deadline);
                    WriteAll(stream, payload, deadline);
                    stream.Flush();

                    var responseHeader = new byte[MasterProtocol.HeaderSize];
                    ReadExactly(stream, responseHeader, 0, responseHeader.Length, deadline);

                    var status = (ushort)MasterProtocol.Get16(responseHeader, 12);
                    var length = (int)MasterProtocol.Get32(responseHeader, 8);

                    if (length < 0 || length > MasterProtocol.MaxBodySize)
                    {
                        throw new Exception("The master sent an invalid response length.");
                    }

                    var body = new byte[length];
                    ReadExactly(stream, body, 0, length, deadline);

                    if (status != MasterProtocol.StatusOk)
                    {
                        throw new MasterException(status, DescribeStatus(status));
                    }

                    return body;
                }
            }
        }

        /// <summary>Milliseconds left before the deadline, never below one.</summary>
        private static int Remaining(int deadline)
        {
            var left = deadline - Environment.TickCount;

            return left < 1 ? 1 : left;
        }

        private static void WriteAll(Stream stream, byte[] buffer, int deadline)
        {
            stream.WriteTimeout = Remaining(deadline);
            stream.Write(buffer, 0, buffer.Length);
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count, int deadline)
        {
            var read = 0;
            while (read < count)
            {
                stream.ReadTimeout = Remaining(deadline);

                var current = stream.Read(buffer, offset + read, count - read);
                if (current <= 0)
                {
                    throw new Exception("The master closed the connection.");
                }

                read += current;
            }
        }

        private static string DescribeStatus(ushort status)
        {
            switch (status)
            {
                case MasterProtocol.StatusBadRequest: return "the request was rejected as malformed";
                case MasterProtocol.StatusUnauthorized: return "the session lease is not valid";
                case MasterProtocol.StatusConflict: return "another session with the same identity exists";
                case MasterProtocol.StatusCapacity: return "the master is full";
                case MasterProtocol.StatusRedirect: return "the master redirected the request";
                default: return "status " + status;
            }
        }
    }

    public sealed class MasterLease
    {
        public byte[] Auth;
        public int LeaseSeconds;
        public int HeartbeatSeconds;
    }

    public sealed class MasterException : Exception
    {
        public MasterException(ushort status, string message) : base(message)
        {
            Status = status;
        }

        public ushort Status { get; private set; }
    }
}
