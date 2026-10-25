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

namespace DPTDOOM
{
    /// <summary>One line of chat on screen: who said it, and what.</summary>
    public sealed class ChatEntry
    {
        /// <summary>Which player said it, for a colour or a prefix.</summary>
        public int Player;

        /// <summary>The name shown on screen.</summary>
        public string Name;

        /// <summary>What they said, without the name.</summary>
        public string Text;

        /// <summary>Tics left before the line disappears.</summary>
        public int Tics;
    }

    /// <summary>
    /// The last few chat lines, and how long each stays up.
    ///
    /// Before this, a chat message went through the same field as a pickup message: it was
    /// overwritten by the next "picked up a medikit" and it lived for eight seconds. For a
    /// single line at a time that is tolerable - you are looking at the screen when you type.
    /// It is not tolerable for a conversation, where a message arrives while you are fighting,
    /// and a player who looks up a second later has no way to know anything was said.
    ///
    /// So the lines are kept separately from the message line, a few of them, with their own
    /// timers. The pickup message still uses the message line and still gets overwritten by the
    /// next pickup; chat no longer competes with it.
    ///
    /// Client-side state, like the chat input: nothing here enters a tic, a demo, a save or the
    /// world hash. Two players typing at each other must not be able to desynchronise a lockstep
    /// session, and this is the reason the class is in Common rather than in the world.
    /// </summary>
    public sealed class ChatLog
    {
        /// <summary>
        /// How many lines are kept.
        ///
        /// Three, because the message area is at the top of the view and more than three lines of
        /// text over the crosshair is worse than a line that has gone. A player who missed more
        /// than three messages was not looking at the screen for a long time.
        /// </summary>
        public const int MaxLines = 3;

        /// <summary>
        /// How long a line stays up, in tics.
        ///
        /// Longer than the four seconds a pickup message gets, because a message has to be read
        /// rather than recognised. Eight seconds is roughly two lines of a slow conversation.
        /// </summary>
        public static readonly int Lifetime = 8 * GameConst.TicRate;

        /// <summary>
        /// How many tics of the lifetime a line spends fading.
        ///
        /// Not implemented as a fade yet - the renderer draws the line whole and it disappears
        /// between one frame and the next. The constant exists so the renderer has one place to
        /// learn when to start, rather than inventing a number of its own.
        /// </summary>
        public static readonly int FadeTics = GameConst.TicRate;

        private readonly List<ChatEntry> entries = new List<ChatEntry>();

        /// <summary>The lines still on screen, oldest first.</summary>
        public IList<ChatEntry> Entries
        {
            get { return entries; }
        }

        public int Count
        {
            get { return entries.Count; }
        }

        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>
        /// Adds a line. The oldest goes when the log is full.
        ///
        /// The name is passed in rather than looked up: the network layer already resolves it, and
        /// a name is not something this class should know how to find.
        /// </summary>
        public void Add(int player, string name, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var entry = new ChatEntry();
            entry.Player = player;
            entry.Name = string.IsNullOrEmpty(name) ? "player " + (player + 1) : name;
            entry.Text = text;
            entry.Tics = Lifetime;

            entries.Add(entry);

            while (entries.Count > MaxLines)
            {
                entries.RemoveAt(0);
            }
        }

        /// <summary>
        /// Ages the log by one tic.
        ///
        /// Called from the game loop rather than from rendering: rendering may run several times
        /// per tic, and ageing there would make a line's lifetime depend on the frame rate - which
        /// is exactly the kind of thing that makes one machine's screen differ from another's.
        /// </summary>
        public void Update()
        {
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                entries[i].Tics--;

                if (entries[i].Tics <= 0)
                {
                    entries.RemoveAt(i);
                }
            }
        }

        /// <summary>The text as it is drawn: "Name: what they said".</summary>
        public static string LineOf(ChatEntry entry)
        {
            return entry.Name + ": " + entry.Text;
        }
    }
}
