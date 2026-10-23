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

        private static byte[] Exchange(string host, int port, ushort operation, byte[] payload, int timeoutMs)
        {
            using (var client = new TcpClient())
            {
                client.NoDelay = true;

                var result = client.BeginConnect(host, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs, false))
                {
                    throw new Exception("Master " + host + ":" + port + " did not answer.");
                }

                client.EndConnect(result);

                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;

                    var header = MasterProtocol.Header(operation, payload.Length, 0);
                    stream.Write(header, 0, header.Length);

                    if (payload.Length > 0)
                    {
                        stream.Write(payload, 0, payload.Length);
                    }

                    stream.Flush();

                    var responseHeader = new byte[MasterProtocol.HeaderSize];
                    ReadExactly(stream, responseHeader, 0, responseHeader.Length);

                    var status = (ushort)MasterProtocol.Get16(responseHeader, 12);
                    var length = (int)MasterProtocol.Get32(responseHeader, 8);

                    if (length < 0 || length > MasterProtocol.MaxBodySize)
                    {
                        throw new Exception("The master sent an invalid response length.");
                    }

                    var body = new byte[length];
                    ReadExactly(stream, body, 0, length);

                    if (status != MasterProtocol.StatusOk)
                    {
                        throw new MasterException(status, DescribeStatus(status));
                    }

                    return body;
                }
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (read < count)
            {
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
