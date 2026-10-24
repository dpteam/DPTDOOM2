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
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DPTDOOM
{
    /// <summary>
    /// The startup report in the .NET console window.
    ///
    /// Doom's own startup printed a banner and then one line per subsystem, so a
    /// player could see where it stopped when it stopped. This is that, for the
    /// things this port actually does: the WAD, the palette, the three graphic
    /// caches, the session.
    ///
    /// Two rules decide the whole design:
    ///
    ///  - **When the output is redirected, nothing changes.** A test, a launcher or a
    ///    pipe gets exactly the lines it got before: no banner, no colours, no
    ///    padding. Sixteen scripts parse this output, and decorating a log that a
    ///    machine reads is how a beautiful console breaks a build.
    ///  - **ASCII only.** The project runs on Windows XP, where the console code page
    ///    and the font between them decide whether a box-drawing character appears
    ///    or a question mark does. Every character here is plain 7-bit ASCII, so the
    ///    banner looks the same in a Russian cmd, an English one and a redirected
    ///    file.
    /// </summary>
    public static class ConsoleUi
    {
        private const int StdOutputHandle = -11;
        private const uint FileTypeChar = 2;

        /// <summary>
        /// Column where the status of a step starts.
        ///
        /// Chosen so the longest label in the project ("base palette") still leaves
        /// room for a count on an 80-column window, which is what a default console
        /// is and what an XP one always was.
        /// </summary>
        private const int StatusColumn = 44;

        private static bool? interactive;
        private static bool disabled;
        private static readonly Stopwatch since = Stopwatch.StartNew();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(IntPtr handle);

        /// <summary>
        /// True when stdout is a real console window rather than a file or a pipe.
        ///
        /// Asked of the OS rather than of <c>Console.IsOutputRedirected</c>, which
        /// arrived in .NET 4.5 - this build has to run on 4.0. On a console the
        /// decoration appears; anywhere else the output stays plain.
        /// </summary>
        public static bool IsInteractive
        {
            get
            {
                if (disabled)
                {
                    return false;
                }

                if (interactive == null)
                {
                    try
                    {
                        var handle = GetStdHandle(StdOutputHandle);
                        interactive = handle != IntPtr.Zero &&
                                      handle != new IntPtr(-1) &&
                                      GetFileType(handle) == FileTypeChar;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        // Not Windows, or a sandbox without kernel32: plain output.
                        interactive = false;
                    }
                    catch (DllNotFoundException)
                    {
                        interactive = false;
                    }
                }

                return interactive.Value;
            }
        }

        /// <summary>
        /// Turns the decoration off for the rest of the run. Used by "-nobanner",
        /// and by anything that wants to parse the output of an interactive run.
        /// </summary>
        public static void Disable()
        {
            disabled = true;
        }

        /// <summary>Seconds since the process started reporting. Used for "ready in".</summary>
        public static double ElapsedSeconds
        {
            get { return since.Elapsed.TotalSeconds; }
        }

        private static void Color(ConsoleColor color)
        {
            if (!IsInteractive)
            {
                return;
            }

            try
            {
                Console.ForegroundColor = color;
            }
            catch
            {
                // A console that refuses a colour is still a console.
            }
        }

        private static void Reset()
        {
            if (!IsInteractive)
            {
                return;
            }

            try
            {
                Console.ResetColor();
            }
            catch
            {
            }
        }

        /// <summary>
        /// The banner. Six lines of block letters, a rule above and below, and one
        /// line saying what this program is.
        ///
        /// Printed only on a console. In a redirected run the caller prints the plain
        /// title instead, so nothing a script reads changes.
        /// </summary>
        public static void Banner()
        {
            if (!IsInteractive)
            {
                Console.WriteLine(ApplicationInfo.Title);
                return;
            }

            var rule = new string('=', 78);

            Color(ConsoleColor.DarkCyan);
            Console.WriteLine();
            Console.WriteLine("  " + rule);
            Console.WriteLine();

            Color(ConsoleColor.Cyan);
            Console.WriteLine("    ____  ____ _____      ____   ___   ___  __  __");
            Console.WriteLine("   |  _ \\|  _ \\_   _|    |  _ \\ / _ \\ / _ \\|  \\/  |");
            Console.WriteLine("   | | | | |_) || |      | | | | | | | | | | |\\/| |");
            Console.WriteLine("   | |_| |  __/ | |      | |_| | |_| | |_| | |  | |");
            Console.WriteLine("   |____/|_|    |_|      |____/ \\___/ \\___/|_|  |_|");

            Color(ConsoleColor.DarkCyan);
            Console.WriteLine();
            Console.WriteLine("   " + ApplicationInfo.Title +
                              "   .NET Framework 4.0   Windows XP to 11");
            Console.WriteLine();
            Console.WriteLine("  " + rule);
            Reset();
            Console.WriteLine();
        }

        /// <summary>A section heading, with the rule under it.</summary>
        public static void Section(string name)
        {
            if (!IsInteractive)
            {
                return;
            }

            Console.WriteLine();

            Color(ConsoleColor.Cyan);
            Console.WriteLine("  " + name);

            Color(ConsoleColor.DarkCyan);
            Console.WriteLine("  " + new string('-', name.Length));
            Reset();
        }

        /// <summary>
        /// Starts a step: the label, then dots up to the status column.
        ///
        /// Two calls rather than one because the work happens between them, and the
        /// result is only known afterwards. Printing the label first is what lets a
        /// long load show where it is instead of sitting silent and then printing
        /// everything at once.
        /// </summary>
        public static void Begin(string label)
        {
            if (!IsInteractive)
            {
                Console.Write(label + ": ");
                return;
            }

            Console.Write("    ");

            Color(ConsoleColor.Gray);
            Console.Write(label);
            Reset();

            var width = 4 + label.Length;
            var dots = StatusColumn - width;

            if (dots < 1)
            {
                Console.Write(" ");
                return;
            }

            Color(ConsoleColor.DarkGray);
            Console.Write(" " + new string('.', dots - 1) + " ");
            Reset();
        }

        /// <summary>Finishes a step with a result.</summary>
        public static void End(bool ok, string detail)
        {
            if (!IsInteractive)
            {
                Console.WriteLine(ok ? "OK" + (detail == null ? "" : " (" + detail + ")") : "Failed");
                return;
            }

            Color(ok ? ConsoleColor.Green : ConsoleColor.Red);
            Console.Write(ok ? "[ ok ]" : "[ !! ]");
            Reset();

            if (!string.IsNullOrEmpty(detail))
            {
                Console.Write(" ");

                Color(ok ? ConsoleColor.DarkGray : ConsoleColor.Gray);
                Console.Write(detail);
                Reset();
            }

            Console.WriteLine();
        }

        public static void Ok(string detail)
        {
            End(true, detail);
        }

        public static void Failed(string detail)
        {
            End(false, detail);
        }

        /// <summary>
        /// A line under a step: what was skipped and why.
        ///
        /// Dim and indented, so it reads as a remark about the line above rather than
        /// as a step of its own - and it never changes the status.
        /// </summary>
        public static void Detail(string text)
        {
            if (!IsInteractive)
            {
                Console.WriteLine("  " + text);
                return;
            }

            Color(ConsoleColor.DarkGray);
            Console.WriteLine("           " + text);
            Reset();
        }

        /// <summary>A warning that does not stop anything.</summary>
        public static void Warn(string text)
        {
            if (!IsInteractive)
            {
                Console.WriteLine("Warning: " + text);
                return;
            }

            Color(ConsoleColor.Yellow);
            Console.WriteLine("  [ .. ] " + text);
            Reset();
        }

        /// <summary>
        /// The last line of the startup: how long it took, and that the window is
        /// about to open.
        ///
        /// Doom ended its startup on the status bar appearing, with no ceremony. This
        /// is the same idea with a number attached, because "why does it take so
        /// long" is a question a number answers and a blank line does not.
        /// </summary>
        public static void Ready()
        {
            if (!IsInteractive)
            {
                return;
            }

            Console.WriteLine();

            Color(ConsoleColor.DarkCyan);
            Console.Write("  " + new string('=', 78));
            Console.WriteLine();

            Color(ConsoleColor.Green);
            Console.Write("   READY");

            Color(ConsoleColor.DarkGray);
            Console.WriteLine("   " + ElapsedSeconds.ToString("0.00") + " s");
            Reset();
            Console.WriteLine();
        }

        /// <summary>
        /// The shutdown line. Printed in the finally block so a crash still says
        /// something about where the run ended.
        /// </summary>
        public static void Closing(string reason)
        {
            if (!IsInteractive)
            {
                return;
            }

            Console.WriteLine();

            Color(ConsoleColor.DarkGray);
            Console.WriteLine("  " + reason + "   " + ElapsedSeconds.ToString("0.00") + " s");
            Reset();
        }
    }
}
