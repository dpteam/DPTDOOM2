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
using System.Text;

namespace DPTDOOM
{
    /// <summary>
    /// Text entry for the in-game chat.
    ///
    /// Deliberately outside the world and outside the simulation: a message is
    /// not game state, is never saved, never recorded into a demo and never
    /// hashed. It could not be, either - two players typing at each other must
    /// not be able to desynchronise a lockstep session.
    ///
    /// The class owns one line of input and nothing else. What happens to the
    /// finished line - sending it, showing it, dropping it - is the caller's
    /// business, which is what keeps it testable without a network.
    /// </summary>
    public sealed class Chat
    {
        /// <summary>
        /// Longest message, in characters.
        ///
        /// The limit exists twice: here, so the player sees what they can type,
        /// and in the framer, so a hand-written packet cannot be longer. Well
        /// under NetProtocol.MaxPayload either way.
        /// </summary>
        public const int MaxLength = 96;

        private readonly StringBuilder text = new StringBuilder();

        /// <summary>True while the player is typing.</summary>
        public bool Active { get; private set; }

        /// <summary>True when Enter was pressed and the caller should send.</summary>
        public bool Submitted { get; private set; }

        public string Text
        {
            get { return text.ToString(); }
        }

        public void Open()
        {
            Active = true;
            Submitted = false;
            text.Length = 0;
        }

        public void Close()
        {
            Active = false;
            Submitted = false;
            text.Length = 0;
        }

        /// <summary>
        /// Consumes one event while typing. Returns true when the event belonged
        /// to the chat, so the caller can stop processing it.
        ///
        /// Every event is swallowed, including the ones this method ignores: while
        /// a player is typing "idkfa" into the chat, the cheat code must not fire,
        /// and neither must weapon selection or the automap.
        /// </summary>
        public bool Handle(DoomEvent e)
        {
            if (!Active)
            {
                return false;
            }

            if (e.Type != EventType.KeyDown)
            {
                return true;
            }

            switch (e.Key)
            {
                case DoomKey.Escape:
                    Close();
                    return true;

                case DoomKey.Enter:
                    Submitted = true;
                    Active = false;
                    return true;

                case DoomKey.Backspace:
                    if (text.Length > 0)
                    {
                        text.Length--;
                    }

                    return true;
            }

            var ch = e.Key.GetChar();

            // Printable ASCII only: the Doom font has no glyphs beyond that, and a
            // message the receiver cannot draw is worse than one that was refused
            // while typing.
            if (ch >= ' ' && ch <= '~' && text.Length < MaxLength)
            {
                text.Append(ch);
            }

            return true;
        }
    }
}
