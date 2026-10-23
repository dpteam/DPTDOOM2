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

namespace DPTDOOM
{
    /// <summary>
    /// The ways a bot can play a level. They differ in what the bot is willing
    /// to spend time on, and the brain learns which one suits a given map.
    /// </summary>
    public enum BotStyle
    {
        /// <summary>Straight for the exit, fights only what is in the way.</summary>
        Rush = 0,

        /// <summary>Clears the map first, then leaves. Slow, safe, thorough.</summary>
        Sweeper = 1,

        /// <summary>Hunts monsters; treats the level as a shooting gallery.</summary>
        Hunter = 2,

        /// <summary>Backs off when hurt and keeps its distance.</summary>
        Cautious = 3
    }

    /// <summary>
    /// A bot's knowledge of one map, kept between runs.
    ///
    /// This is deliberately **not** a neural network. The idea is borrowed from
    /// the fly connectome work in `refs/Fly`, where behaviour comes out of a
    /// handful of populations of leaky neurons: the difference between the left
    /// and right steering populations becomes a turn bias, the forward population
    /// becomes a walking drive, and a single giant-fibre spike means "escape".
    /// Nothing there is a deep network either — it is a small number of
    /// accumulators whose rates are compared.
    ///
    /// The same shape is used here:
    ///
    ///   - each <see cref="BotStyle"/> keeps a running score, like a population
    ///     rate that rises when the style does well;
    ///   - the bot picks the style with the highest score, but explores
    ///     occasionally instead of always exploiting, so a map whose route
    ///     changes (a lift, a key door) does not lock the bot into one habit;
    ///   - the route that actually worked is remembered and reused next time.
    ///
    /// Everything is a float average over a handful of runs, so the cost is
    /// nothing next to rendering a frame, and the file is plain text that a
    /// person can read and edit.
    ///
    /// A run is only counted when the **bot itself** reached the exit. If the
    /// human player left the level first, the bot's run was cut short by
    /// something other than its own performance, and scoring it would teach the
    /// brain a false lesson.
    /// </summary>
    public sealed class BotBrain
    {
        public const int StyleCount = 4;

        private const string FileHeader = "dptdoom-bot-brain";
        private const int FileVersion = 1;
        private const int MaxRouteNodes = 512;

        /// <summary>How often a style other than the best one is tried.</summary>
        private const double ExploreChance = 0.15;

        /// <summary>Prior weight, so one lucky run does not decide everything.</summary>
        private const double PriorScore = 0.5;
        private const int PriorRuns = 1;

        private readonly string folder;
        private readonly string path;

        private readonly int[] styleRuns = new int[StyleCount];
        private readonly float[] styleScore = new float[StyleCount];

        private int[] bestRoute = new int[0];

        private BotBrain()
        {
            // Disabled: used by tests and by -nobotbrain.
        }

        public BotBrain(string folder, string mapKey)
        {
            this.folder = folder;
            MapKey = mapKey ?? "unknown";

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            path = Path.Combine(folder, SafeFileName(MapKey) + ".brain");

            Load();
        }

        /// <summary>A brain that remembers nothing and writes nothing.</summary>
        public static BotBrain Disabled { get { return new BotBrain(); } }

        public bool Enabled { get { return path != null; } }

        public string MapKey { get; private set; }

        /// <summary>Best completion time in tics, or 0 when the map is unknown.</summary>
        public int BestTics { get; private set; }

        /// <summary>Style that holds the best time.</summary>
        public BotStyle BestStyle { get; private set; }

        /// <summary>How many runs have been scored on this map.</summary>
        public int Runs { get; private set; }

        /// <summary>Node route of the best run, empty when unknown.</summary>
        public int[] BestRoute { get { return bestRoute; } }

        /// <summary>
        /// Which style to play this run. Exploitation most of the time, with a
        /// slice of exploration so the brain can notice a change.
        /// </summary>
        public BotStyle ChooseStyle(int runSeed)
        {
            var random = new Random(Seed(MapKey, runSeed + Runs));

            // Nothing known about this map yet: pick at random instead of always
            // taking the first style. With every score equal, "the best" is
            // whichever comes first, and the bot would play the same style on a
            // new map until exploration happened to try another one.
            if (Runs == 0)
            {
                return (BotStyle)random.Next(StyleCount);
            }

            if (random.NextDouble() < ExploreChance)
            {
                return (BotStyle)random.Next(StyleCount);
            }

            var best = 0;
            var bestWeight = -1.0;

            for (var i = 0; i < StyleCount; i++)
            {
                var weight = (styleScore[i] + PriorScore) / (styleRuns[i] + PriorRuns);

                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    best = i;
                }
            }

            return (BotStyle)best;
        }

        /// <summary>Current score of a style, for diagnostics.</summary>
        public double StyleWeight(BotStyle style)
        {
            var index = (int)style;

            return (styleScore[index] + PriorScore) / (styleRuns[index] + PriorRuns);
        }

        public int StyleRuns(BotStyle style)
        {
            return styleRuns[(int)style];
        }

        /// <summary>
        /// Records one finished run.
        ///
        /// <paramref name="finished"/> is false when the bot did not leave the
        /// level itself — it died, or the human player exited first. Such a run
        /// is still counted against the style, because a style that keeps dying
        /// should lose, but it can never become the best time.
        /// </summary>
        public void RecordRun(BotStyle style, int tics, bool finished, int[] route, int routeLength)
        {
            var index = (int)style;
            if (index < 0 || index >= StyleCount)
            {
                index = 0;
            }

            Runs++;

            if (!finished || tics <= 0)
            {
                // Failure: a small score, not zero, so the style is not written
                // off after a single bad run on a map it has barely seen.
                styleRuns[index]++;
                styleScore[index] += 0.15F;

                Save();
                return;
            }

            var previousBest = BestTics;

            // Reward is how close this run came to the best known time, capped at
            // one. A first completion scores one, a slower repeat scores less.
            var reward = previousBest > 0
                ? Math.Min(1.0, (double)previousBest / tics)
                : 1.0;

            styleRuns[index]++;
            styleScore[index] += (float)reward;

            if (previousBest == 0 || tics < previousBest)
            {
                BestTics = tics;
                BestStyle = style;

                if (route != null && routeLength > 0)
                {
                    var length = Math.Min(routeLength, MaxRouteNodes);
                    var copy = new int[length];
                    Array.Copy(route, copy, length);
                    bestRoute = copy;
                }
            }

            Save();
        }

        /// <summary>Human-readable summary for the console and the overlay.</summary>
        public string Describe()
        {
            if (!Enabled)
            {
                return "brain off";
            }

            if (BestTics == 0)
            {
                return "no run yet";
            }

            return FormatTime(BestTics) + " (" + BestStyle + ", " + Runs + " runs)";
        }

        /// <summary>Tics to m:ss.t, the way Doom shows a level time.</summary>
        public static string FormatTime(int tics)
        {
            if (tics <= 0)
            {
                return "--:--";
            }

            var totalSeconds = tics / 35.0;
            var minutes = (int)(totalSeconds / 60);
            var seconds = totalSeconds - minutes * 60;

            return minutes.ToString(CultureInfo.InvariantCulture) + ":" +
                   seconds.ToString("00.0", CultureInfo.InvariantCulture);
        }

        ////////////////////////////////////////////////////////////
        // Persistence
        ////////////////////////////////////////////////////////////

        private void Load()
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                var lines = File.ReadAllLines(path);

                if (lines.Length == 0)
                {
                    return;
                }

                var header = lines[0].Split(' ');
                if (header.Length < 2 || header[0] != FileHeader)
                {
                    // Not ours, or a future version. Start clean rather than
                    // guessing at fields.
                    return;
                }

                for (var i = 1; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(' ');

                    if (parts.Length == 0 || parts[0].Length == 0)
                    {
                        continue;
                    }

                    switch (parts[0])
                    {
                        case "runs":
                            Runs = Int(parts, 1, Runs);
                            break;

                        case "best":
                            BestTics = Int(parts, 1, BestTics);
                            break;

                        case "beststyle":
                            BestStyle = (BotStyle)MathEx.Clamp(Int(parts, 1, 0), 0, StyleCount - 1);
                            break;

                        case "style":
                            LoadStyle(parts);
                            break;

                        case "route":
                            LoadRoute(parts);
                            break;
                    }
                }
            }
            catch (IOException)
            {
                // A brain file is a convenience. A read-only or missing folder
                // must never stop the game from starting.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void LoadStyle(string[] parts)
        {
            if (parts.Length < 4)
            {
                return;
            }

            var index = Int(parts, 1, -1);
            if (index < 0 || index >= StyleCount)
            {
                return;
            }

            styleRuns[index] = Math.Max(0, Int(parts, 2, 0));
            styleScore[index] = Float(parts, 3, 0);
        }

        private void LoadRoute(string[] parts)
        {
            if (parts.Length < 2)
            {
                return;
            }

            var count = Math.Min(parts.Length - 1, MaxRouteNodes);
            var route = new int[count];

            for (var i = 0; i < count; i++)
            {
                route[i] = Int(parts, i + 1, -1);
            }

            bestRoute = route;
        }

        private void Save()
        {
            if (!Enabled)
            {
                return;
            }

            var text = new StringBuilder();

            text.Append(FileHeader).Append(' ').Append(FileVersion).Append('\n');
            text.Append("key ").Append(MapKey).Append('\n');
            text.Append("runs ").Append(Runs).Append('\n');
            text.Append("best ").Append(BestTics).Append('\n');
            text.Append("beststyle ").Append((int)BestStyle).Append('\n');

            for (var i = 0; i < StyleCount; i++)
            {
                text.Append("style ").Append(i)
                    .Append(' ').Append(styleRuns[i])
                    .Append(' ').Append(styleScore[i].ToString("0.####", CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            if (bestRoute.Length > 0)
            {
                text.Append("route");
                for (var i = 0; i < bestRoute.Length; i++)
                {
                    text.Append(' ').Append(bestRoute[i]);
                }
                text.Append('\n');
            }

            try
            {
                Directory.CreateDirectory(folder);

                // Write beside the target and copy over it, so a crash halfway
                // through does not leave a half-written brain behind.
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, text.ToString(), Encoding.UTF8);
                File.Copy(temporary, path, true);
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        ////////////////////////////////////////////////////////////
        // Small helpers
        ////////////////////////////////////////////////////////////

        private static int Int(string[] parts, int index, int fallback)
        {
            int value;

            if (index < parts.Length &&
                int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return fallback;
        }

        private static float Float(string[] parts, int index, float fallback)
        {
            float value;

            if (index < parts.Length &&
                float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return fallback;
        }

        /// <summary>
        /// File name for a map key. Keys contain the game and map name, so they
        /// are already safe, but a PWAD path could sneak in punctuation.
        /// </summary>
        private static string SafeFileName(string key)
        {
            var result = new StringBuilder(key.Length);

            foreach (var c in key)
            {
                var safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                           (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';

                result.Append(safe ? c : '_');
            }

            return result.ToString();
        }

        /// <summary>
        /// Stable seed. string.GetHashCode is randomised per process on modern
        /// .NET, which would make the bot choose a different style on every
        /// launch — and the same headless run would stop reproducing.
        /// </summary>
        private static int Seed(string key, int salt)
        {
            var hash = 2166136261u;

            foreach (var c in key ?? "")
            {
                hash = (hash ^ c) * 16777619u;
            }

            hash = (hash ^ (uint)salt) * 16777619u;

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
