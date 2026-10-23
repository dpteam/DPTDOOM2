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
using DPTDOOM.Shared;

namespace DPTDOOM.Master
{
    /// <summary>
    /// The bounded session directory. No sockets, no engine dependencies: the
    /// transport calls into this and nothing else.
    /// </summary>
    internal sealed class SessionDirectory
    {
        private readonly SessionEntry[] entries = new SessionEntry[MasterProtocol.MaxServers];
        private readonly int leaseSeconds;

        public SessionDirectory(int leaseSeconds)
        {
            this.leaseSeconds = leaseSeconds;

            for (var i = 0; i < entries.Length; i++)
            {
                entries[i] = new SessionEntry();
            }
        }

        public ushort Register(byte[] host, byte[] info, byte[] entropy, long now, out byte[] lease)
        {
            lease = null;

            if (MasterSessionInfo.Validate(info, 0) != null)
            {
                return MasterProtocol.StatusBadRequest;
            }

            Prune(now);

            SessionEntry free = null;
            var sameHost = 0;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];

                if (!entry.Active)
                {
                    if (free == null)
                    {
                        free = entry;
                    }

                    continue;
                }

                if (SameBytes(entry.Id, entropy, 0, 16))
                {
                    return MasterProtocol.StatusConflict;
                }

                if (SameBytes(entry.Advertised, host, 0, 4) && MasterProtocol.Get16(entry.Info, 124) == MasterProtocol.Get16(info, 124))
                {
                    return MasterProtocol.StatusConflict;
                }

                if (SameBytes(entry.Host, host, 0, 4))
                {
                    sameHost++;
                    if (MasterProtocol.Get16(entry.Info, 124) == MasterProtocol.Get16(info, 124))
                    {
                        return MasterProtocol.StatusConflict;
                    }
                }
            }

            if (free == null || sameHost >= MasterProtocol.MaxServersPerIp)
            {
                return MasterProtocol.StatusCapacity;
            }

            free.Active = true;
            Array.Copy(host, free.Host, 4);
            Array.Copy(host, free.Advertised, 4);
            Array.Copy(info, free.Info, MasterProtocol.InfoSize);
            Array.Copy(entropy, free.Id, 16);
            Array.Copy(entropy, 16, free.Token, 0, 24);
            free.Expires = now + leaseSeconds * 1000L;

            lease = new byte[MasterProtocol.LeaseSize];
            Array.Copy(entropy, lease, MasterProtocol.AuthSize);
            MasterProtocol.Put32(lease, 40, (uint)leaseSeconds);
            MasterProtocol.Put32(lease, 44, (uint)(leaseSeconds / 3));

            return MasterProtocol.StatusOk;
        }

        public ushort Renew(byte[] host, byte[] auth, bool remove, long now, out int newLease)
        {
            newLease = 0;
            Prune(now);

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];

                if (!entry.Active || !SameBytes(entry.Id, auth, 0, 16))
                {
                    continue;
                }

                if (!SameBytes(entry.Host, host, 0, 4) || !SameBytes(entry.Token, auth, 16, 24))
                {
                    return MasterProtocol.StatusUnauthorized;
                }

                if (remove)
                {
                    entry.Clear();
                }
                else
                {
                    entry.Expires = now + leaseSeconds * 1000L;
                    newLease = leaseSeconds;
                }

                return MasterProtocol.StatusOk;
            }

            return MasterProtocol.StatusUnauthorized;
        }

        public byte[] List(long now)
        {
            Prune(now);

            var body = new byte[4 + MasterProtocol.RowSize * MasterProtocol.MaxServers];
            var count = 0;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!entry.Active)
                {
                    continue;
                }

                var row = 4 + count * MasterProtocol.RowSize;

                Array.Copy(entry.Id, 0, body, row, 16);
                Array.Copy(entry.Advertised, 0, body, row + 16, 4);

                var remaining = (uint)Math.Max((entry.Expires - now) / 1000, 0);
                MasterProtocol.Put32(body, row + 20, remaining);

                Array.Copy(entry.Info, 0, body, row + 24, MasterProtocol.InfoSize);

                count++;
            }

            MasterProtocol.Put32(body, 0, (uint)count);

            var used = 4 + count * MasterProtocol.RowSize;
            var result = new byte[used];
            Array.Copy(body, result, used);
            return result;
        }

        public int Prune(long now)
        {
            var removed = 0;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Active && entry.Expires <= now)
                {
                    entry.Clear();
                    removed++;
                }
            }

            return removed;
        }

        public int ActiveCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private static bool SameBytes(byte[] left, byte[] right, int rightOffset, int length)
        {
            for (var i = 0; i < length; i++)
            {
                if (left[i] != right[rightOffset + i])
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class SessionEntry
        {
            public bool Active;
            public readonly byte[] Host = new byte[4];
            public readonly byte[] Advertised = new byte[4];
            public readonly byte[] Info = new byte[MasterProtocol.InfoSize];
            public readonly byte[] Id = new byte[16];
            public readonly byte[] Token = new byte[24];
            public long Expires;

            public void Clear()
            {
                Active = false;
                Array.Clear(Host, 0, Host.Length);
                Array.Clear(Advertised, 0, Advertised.Length);
                Array.Clear(Info, 0, Info.Length);
                Array.Clear(Id, 0, Id.Length);
                Array.Clear(Token, 0, Token.Length);
                Expires = 0;
            }
        }
    }
}
