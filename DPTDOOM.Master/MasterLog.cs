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
using System.Text;

namespace DPTDOOM.Master
{
    /// <summary>
    /// Console plus file logging. Never writes lease secrets: only events,
    /// source addresses and bounded detail strings.
    /// </summary>
    internal sealed class MasterLog : IDisposable
    {
        private readonly object sync = new object();
        private readonly StreamWriter writer;

        /// <summary>
        /// Whether per-request events are written.
        ///
        /// A field rather than a constructor parameter so nothing else has to change:
        /// every caller already uses one MasterLog, and the only thing that varies is
        /// how much it says.
        /// </summary>
        private bool eventsEnabled = true;

        /// <summary>
        /// Turns per-request logging on or off.
        ///
        /// Startup lines and warnings are never suppressed: the one thing an operator
        /// needs from a quiet log is that it still says what the master is doing and
        /// what went wrong.
        /// </summary>
        public bool EventsEnabled
        {
            get { return eventsEnabled; }
            set { eventsEnabled = value; }
        }

        public MasterLog(string path)
        {
            if (path == null)
            {
                return;
            }
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
                writer.AutoFlush = true;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Cannot open log '" + path + "': " + e.Message);
            }
        }

        public void Info(string message)
        {
            Write("info", message);
        }

        public void Event(string message)
        {
            if (!eventsEnabled)
            {
                return;
            }

            Write("event", message);
        }

        public void Warn(string message)
        {
            Write("warn", message);
        }

        private void Write(string level, string message)
        {
            var line = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
                       " " + level + " " + message;

            lock (sync)
            {
                Console.WriteLine(line);

                if (writer != null)
                {
                    try
                    {
                        writer.WriteLine(line);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void Dispose()
        {
            if (writer != null)
            {
                try
                {
                    writer.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
