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
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace DPTDOOM.Net
{
    /// <summary>
    /// Who joined, who left, and who was turned away - on the host's own disk.
    ///
    /// This cannot live on the master, and that is not a preference. The master sees a
    /// tunnel open and close; it never sees a nickname and never learns why a guest
    /// went away. "Who joined and who left" only exists where the handshake happens.
    ///
    /// One line per event, append-only, flushed as it is written: a log that is lost
    /// when the process dies is a log that is missing exactly the event somebody wants
    /// to read - the crash, the kill, the cable pulled out.
    ///
    /// Never contains the session password. It is written where the host asked for it,
    /// and a password in a log file is a password in every backup of that file.
    /// </summary>
    public sealed class ConnectionLog : IDisposable
    {
        /// <summary>
        /// Rotated at this size rather than unbounded.
        ///
        /// A host that runs for weeks with a public game accumulates lines, and a log
        /// that has grown to a hundred megabytes is one nobody opens. One step of
        /// rotation is enough: the previous file is kept, the older one is dropped.
        /// </summary>
        private const long MaxBytes = 1024 * 1024;

        private readonly object sync = new object();
        private readonly string path;
        private StreamWriter writer;
        private bool failed;

        public ConnectionLog(string path)
        {
            this.path = path;
        }

        public bool Active => path != null;

        /// <summary>
        /// A guest got a slot.
        /// </summary>
        /// <param name="address">The source address, or null when it is not known.</param>
        public void Join(string name, IPAddress address, int player)
        {
            Write("join", name, address, "player=" + (player + 1));
        }

        /// <summary>
        /// A guest is gone.
        ///
        /// The reason is the whole point of the line: "left" alone does not distinguish
        /// a player who quit from one whose router died from one the host gave up on,
        /// and those are three different things to investigate.
        /// </summary>
        public void Leave(string name, IPAddress address, int player, string reason)
        {
            Write("leave", name, address, "player=" + (player + 1) + " reason=" + reason);
        }

        /// <summary>A connection was refused before it got a slot.</summary>
        public void Refuse(string name, IPAddress address, string reason)
        {
            Write("refuse", name, address, "reason=" + reason);
        }

        /// <summary>
        /// A guest that was lost is back.
        ///
        /// Its own event rather than a second "join": the slot was never given up, so a
        /// log that only showed joins and leaves would miss the interesting middle.
        /// </summary>
        public void Resume(string name, IPAddress address, int player)
        {
            Write("resume", name, address, "player=" + (player + 1));
        }

        private void Write(string kind, string name, IPAddress address, string details)
        {
            if (path == null)
            {
                return;
            }

            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            var line = stamp + " " + kind +
                       " name=" + Quote(name) +
                       " address=" + (address == null ? "-" : address.ToString()) +
                       " " + details;

            lock (sync)
            {
                if (failed)
                {
                    return;
                }

                try
                {
                    if (writer == null)
                    {
                        Open();
                    }

                    writer.WriteLine(line);
                    writer.Flush();
                }
                catch (Exception e)
                {
                    // A log is a convenience, not a reason for a session to fail. Said
                    // once - a log that cannot be written would otherwise produce a line
                    // per event, and the console would be the loud part instead.
                    failed = true;
                    Console.WriteLine("Net: connection log disabled (" + e.Message + ").");
                }
            }
        }

        private void Open()
        {
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    var previous = path + ".1";

                    if (File.Exists(previous))
                    {
                        File.Delete(previous);
                    }

                    File.Move(path, previous);
                }
            }
            catch (Exception)
            {
                // Rotating is optional; appending to an oversized file is better than
                // losing the log entirely.
            }

            writer = new StreamWriter(path, true, Encoding.UTF8);
        }

        /// <summary>
        /// A nickname goes inside quotes, with the quote and the newline escaped.
        ///
        /// Nicks are player-supplied text that ends up in a file somebody reads: an
        /// unescaped newline forges an extra log line, and an unescaped quote makes the
        /// rest of the line look like it belongs to a different field.
        /// </summary>
        private static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            var text = new StringBuilder(value.Length + 2);
            text.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        text.Append("\\\"");
                        break;

                    case '\\':
                        text.Append("\\\\");
                        break;

                    case '\n':
                        text.Append("\\n");
                        break;

                    case '\r':
                        text.Append("\\r");
                        break;

                    default:
                        // Control characters would break the one-line-per-event shape.
                        text.Append(c < 0x20 ? '?' : c);
                        break;
                }
            }

            text.Append('"');
            return text.ToString();
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (writer == null)
                {
                    return;
                }

                try
                {
                    writer.Flush();
                    writer.Close();
                }
                catch (Exception)
                {
                }

                writer = null;
            }
        }
    }
}
