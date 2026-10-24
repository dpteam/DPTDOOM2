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
using System.Globalization;
using System.Runtime.InteropServices;

namespace DPTDOOM
{
    /// <summary>
    /// The startup report in the .NET console window, in the shape of a kernel log.
    ///
    /// Doom's own startup printed a banner and one line per subsystem. This is that,
    /// with a timestamp in front of every line:
    ///
    /// <code>
    /// [    0.412817] wad      : 1900 lumps, DOOM 2
    /// [    0.913167] flats    : 147
    /// [    1.453751] net      : hosting on 5029
    /// </code>
    ///
    /// The timestamp is the point of the format. It answers "what was slow" without a
    /// profiler, and a fixed-width column keeps a screenful of lines readable in a
    /// proportional font, where aligning by hand never lines up twice.
    ///
    /// Three rules decide the rest:
    ///
    ///  - **When the output is redirected, nothing changes.** A test, a launcher or a
    ///    pipe gets exactly the lines it got before: no banner, no colours, no
    ///    timestamps, no padding. Sixteen scripts parse this output, and decorating a
    ///    log that a machine reads is how a beautiful console breaks a build.
    ///  - **ASCII only.** The project runs on Windows XP, where the console code page
    ///    and the font between them decide whether a box-drawing character appears or
    ///    a question mark does.
    ///  - **A step that is still running prints a provisional line.** A load that takes
    ///    a second would otherwise sit silent and then print everything at once, which
    ///    looks like a hang. The provisional is overwritten in place, so what is left
    ///    on screen is exactly the final log.
    /// </summary>
    public static class ConsoleUi
    {
        private const int StdOutputHandle = -11;
        private const uint FileTypeChar = 2;

        /// <summary>
        /// Width of the subsystem column, before the colon.
        ///
        /// Wide enough for the longest name in the project ("dehacked"), narrow enough
        /// that a message still starts before column 30 on an 80-column window.
        /// </summary>
        private const int SubsystemColumn = 9;

        private static bool? interactive;
        private static bool disabled;
        private static readonly Stopwatch since = Stopwatch.StartNew();

        /// <summary>The step currently running, or null between steps.</summary>
        private static string current;

        /// <summary>
        /// The last subsystem that reported, so a <see cref="Detail"/> printed outside
        /// a step still says who it is about.
        /// </summary>
        private static string last;

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

        /// <summary>
        /// Forces the interactive decision, for tests.
        ///
        /// Text formatting is exactly the thing a test verifies better than an eye, and
        /// a build server has no console window to check it against.
        /// </summary>
        public static void ForceInteractive(bool value)
        {
            interactive = value;
        }

        /// <summary>Seconds since the process started reporting.</summary>
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
        /// The time column: <c>[    1.453751] </c>.
        ///
        /// Six decimals, right-aligned in a fixed width, the way a kernel log has done
        /// it for decades. The trailing space is part of the column, not of the
        /// message: without it the closing bracket touches the subsystem name and the
        /// whole line reads as a run-on.
        /// </summary>
        private static string Timestamp()
        {
            return "[" + ElapsedSeconds.ToString("0.000000", CultureInfo.InvariantCulture)
                                         .PadLeft(12) + "] ";
        }

        private static void WriteTimestamp()
        {
            Color(ConsoleColor.DarkGray);
            Console.Write(Timestamp());
            Reset();
        }

        private static void WriteSubsystem(string name)
        {
            Color(ConsoleColor.Cyan);
            Console.Write((name ?? "?").PadRight(SubsystemColumn));
            Reset();

            Console.Write(": ");
        }

        /// <summary>
        /// The banner. Five lines of block letters and one line saying what this
        /// program is.
        ///
        /// Still a banner rather than a log line: it is the first thing anyone sees and
        /// it has to say what the program is before the log starts explaining what it
        /// is doing. In a redirected run the caller prints the plain title instead, so
        /// nothing a script reads changes.
        /// </summary>
        public static void Banner()
        {
            if (!IsInteractive)
            {
                Console.WriteLine(ApplicationInfo.Title);
                return;
            }

            Console.WriteLine();

            Color(ConsoleColor.Cyan);
            Console.WriteLine("    ____  ____ _____      ____   ___   ___  __  __");
            Console.WriteLine("   |  _ \\|  _ \\_   _|    |  _ \\ / _ \\ / _ \\|  \\/  |");
            Console.WriteLine("   | | | | |_) || |      | | | | | | | | | | |\\/| |");
            Console.WriteLine("   | |_| |  __/ | |      | |_| | |_| | |_| | |  | |");
            Console.WriteLine("   |____/|_|    |_|      |____/ \\___/ \\___/|_|  |_|");

            Color(ConsoleColor.DarkGray);
            Console.WriteLine("   " + ApplicationInfo.Title +
                              "   .NET Framework 4.0   Windows XP to 11");
            Reset();
            Console.WriteLine();
        }

        /// <summary>
        /// A blank line between groups of steps.
        ///
        /// A kernel log has no sections, and adding headings would break the one
        /// property that makes it readable - every line the same shape. A gap does the
        /// same work without pretending to be a line of output.
        /// </summary>
        public static void Section(string name)
        {
            if (!IsInteractive)
            {
                return;
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Starts a step. Nothing is printed yet: the line appears when the step ends.
        ///
        /// An earlier version printed a provisional line and overwrote it at the end,
        /// which looked like progress until a step printed a <see cref="Detail"/>
        /// partway through - those lines landed on top of the half-written one and left
        /// their tails sticking out. A kernel log prints a line when the event
        /// completes, so a timestamp means "this finished at 0.41 s", which is what
        /// anyone reading it wants to know. The previous line already shows that
        /// something is happening; a pause before the next one is not a hang.
        /// </summary>
        public static void Begin(string subsystem)
        {
            if (!IsInteractive)
            {
                Console.Write(subsystem + ": ");
                return;
            }

            current = subsystem;
        }

        /// <summary>Finishes a step with a result.</summary>
        public static void End(bool ok, string detail)
        {
            if (!IsInteractive)
            {
                Console.WriteLine(ok ? "OK" + (detail == null ? "" : " (" + detail + ")") : "Failed");
                return;
            }

            var body = ok ? detail : "FAILED" + (string.IsNullOrEmpty(detail) ? "" : " - " + detail);

            WriteTimestamp();
            WriteSubsystem(current ?? last ?? "?");

            Color(ok ? ConsoleColor.Gray : ConsoleColor.Red);
            Console.WriteLine(body ?? "");
            Reset();

            last = current ?? last;
            current = null;
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
        /// Same shape as a step, same subsystem, no timestamp of its own - it happened
        /// as part of the step above it, and giving it a fresh time would suggest it
        /// was a separate event.
        /// </summary>
        public static void Detail(string text)
        {
            if (!IsInteractive)
            {
                Console.WriteLine("  " + text);
                return;
            }

            // The timestamp column is left blank rather than filled with a dash: the
            // line belongs to the step above it, and a fresh time would suggest it was
            // a separate event.
            Console.Write(new string(' ', Timestamp().Length));
            WriteSubsystem(current ?? last ?? "?");

            Color(ConsoleColor.DarkGray);
            Console.WriteLine(text ?? "");
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

            WriteTimestamp();
            WriteSubsystem("warn");

            Color(ConsoleColor.Yellow);
            Console.WriteLine(text ?? "");
            Reset();
        }

        /// <summary>
        /// A line with no step around it: a single remark, such as where the master is
        /// listening.
        /// </summary>
        public static void Line(string subsystem, string text)
        {
            if (!IsInteractive)
            {
                Console.WriteLine(subsystem + ": " + text);
                return;
            }

            WriteTimestamp();
            WriteSubsystem(subsystem);

            Console.WriteLine(text ?? "");
        }

        /// <summary>
        /// The last line of the startup: how long it took, and that the window is
        /// about to open.
        ///
        /// "why does it take so long" is a question a number answers and a blank line
        /// does not.
        /// </summary>
        public static void Ready()
        {
            if (!IsInteractive)
            {
                return;
            }

            Line("ready", "window opening, " +
                          ElapsedSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s");
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

            Line("exit", reason + ", " +
                          ElapsedSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s");
        }
    }
}
