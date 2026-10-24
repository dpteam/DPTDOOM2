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

namespace DPTDOOM.Net
{
    /// <summary>
    /// Everything a host uses to decide who gets in, and to remember who did.
    ///
    /// Grouped into one object because these three travel together through the same
    /// layers - command line, launcher, session - and passing them as three parameters
    /// would mean every future addition changes five signatures. It also gives one
    /// place to document what a host guard is and is not: it keeps casual strangers
    /// out of a game for friends. It does not encrypt anything.
    ///
    /// A null instance means an open, unlimited, unlogged session, which is the
    /// default and must stay the cheap path.
    /// </summary>
    public sealed class HostGuard
    {
        /// <summary>
        /// What a guest must offer to enter. Empty means the session is open.
        /// </summary>
        public string Password = "";

        /// <summary>
        /// Addresses and nicknames that are not let in. Null means nothing is banned.
        /// </summary>
        public BanList Bans;

        /// <summary>
        /// Where joins, leaves and refusals are written. Null means not logged to a file.
        /// </summary>
        public ConnectionLog Log;

        public bool BansAddress(System.Net.IPAddress address)
        {
            return Bans != null && Bans.IsAddressBanned(address, System.DateTime.UtcNow);
        }

        public bool BansName(string name)
        {
            return Bans != null && Bans.IsNameBanned(name, System.DateTime.UtcNow);
        }

        /// <summary>
        /// The rule that refused this peer, for the log and for the refusal reason.
        /// </summary>
        public string Explain(System.Net.IPAddress address, string name)
        {
            return Bans == null ? "" : Bans.Explain(address, name, System.DateTime.UtcNow);
        }
    }
}
