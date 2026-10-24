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
using System.Globalization;
using System.IO;
using System.Text;

namespace DPTDOOM
{
    /// <summary>
    /// Records where the bots went and how they turned, one sample per tic, and
    /// writes it as JSON.
    ///
    /// This exists because "the bots walk in circles" is a report that cannot be
    /// argued with, only measured. Reading the code suggests one cause, watching
    /// the numbers shows another: a bot can orbit a waypoint, grind along a wall,
    /// or stand still turning, and all three look identical on screen. A trace of
    /// position, heading and movement input per tic tells them apart.
    ///
    /// The output is meant to be drawn: <c>tools/render-bot-trace.ps1</c> turns it
    /// into a PNG with the level geometry, the walked path and the heading at
    /// each sample, plus a JSON sidecar for anything the picture cannot show.
    ///
    /// Enabled with <c>-botdump trace.json</c>; the cost is a few numbers per tic,
    /// and it is off by default.
    /// </summary>
    public sealed class BotTrace
    {
        /// <summary>
        /// Samples are taken every tic. A long run is still only a few hundred
        /// kilobytes of text, and decimating would hide exactly the short loops
        /// this is meant to catch.
        /// </summary>
        private const int MaxSamples = 200000;

        private string path;
        private readonly World world;

        private readonly StringBuilder samples = new StringBuilder();

        private int sampleCount;

        public BotTrace(string path, World world)
        {
            this.path = path;
            this.world = world;
        }

        /// <summary>True when recording is switched on.</summary>
        public bool Active { get { return path != null && sampleCount < MaxSamples; } }

        /// <summary>
        /// Takes one sample of every bot. Called after the world has been
        /// updated, so the position is where the bot actually ended up and the
        /// command is the one that moved it there.
        ///
        /// The bots are passed in rather than remembered: a new level builds new
        /// ones, and a trace that held the old references would record nothing
        /// after the first level change.
        /// </summary>
        public void Sample(TicCmd[] cmds, DoomBot single, DoomBot[] coop)
        {
            if (!Active || world == null)
            {
                return;
            }

            if (sampleCount == 0)
            {
                samples.Append("[");
            }
            else
            {
                samples.Append(",");
            }

            samples.Append("\n{");
            samples.Append("\"tic\":").Append(world.LevelTime);
            samples.Append(",\"bots\":[");

            var first = true;

            first = WriteBot(first, 0, cmds, single);

            if (coop != null)
            {
                for (var i = 1; i < coop.Length; i++)
                {
                    first = WriteBot(first, i, cmds, coop[i]);
                }
            }

            samples.Append("]}");

            sampleCount++;
        }

        private bool WriteBot(bool first, int index, TicCmd[] cmds, DoomBot runner)
        {
            if (runner == null || cmds == null || index >= cmds.Length)
            {
                return first;
            }

            var player = world.Options.Players[index];

            if (!player.InGame || player.Mobj == null)
            {
                return first;
            }

            var mobj = player.Mobj;

            if (!first)
            {
                samples.Append(",");
            }

            samples.Append("{\"p\":").Append(index + 1);
            samples.Append(",\"style\":\"").Append(runner.Style).Append("\"");
            samples.Append(",\"x\":").Append(Number(mobj.X.ToFloat()));
            samples.Append(",\"y\":").Append(Number(mobj.Y.ToFloat()));

            // Heading as degrees, 0 to 360, the way a compass would read it.
            // Angle.Data counts 1/2^32 of a turn, so this is exact.
            var degrees = (mobj.Angle.Data / 4294967296.0) * 360.0;
            samples.Append(",\"angle\":").Append(Number(degrees));

            samples.Append(",\"fwd\":").Append(cmds[index].ForwardMove);
            samples.Append(",\"side\":").Append(cmds[index].SideMove);
            samples.Append(",\"turn\":").Append(cmds[index].AngleTurn);

            // Buttons, because "the bot spams doors" is a claim about Use and a trace
            // without it can only show that the bot stood still. With the bit flags in
            // the sample, door spam is a number: count the Use bits over a level and
            // compare against the number of doors on the map.
            samples.Append(",\"buttons\":").Append(cmds[index].Buttons);
            samples.Append(",\"sector\":").Append(mobj.Subsector.Sector.Number);
            samples.Append(",\"health\":").Append(player.Health);
            samples.Append(",\"target\":\"").Append(Escape(runner.Status)).Append("\"");
            samples.Append(",\"decision\":\"").Append(Escape(runner.Decision)).Append("\"");
            samples.Append("}");

            return false;
        }

        /// <summary>
        /// Writes the whole trace: map geometry, then the samples.
        ///
        /// The geometry is included so the picture can be drawn without reading
        /// the WAD again, and so a trace file stays useful on its own.
        /// </summary>
        public void Write(string target)
        {
            if (target == null || sampleCount == 0)
            {
                return;
            }

            path = target;

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var text = new StringBuilder();

            text.Append("{\n");
            text.Append("\"map\":\"").Append(Escape(MapName())).Append("\",\n");
            text.Append("\"skill\":").Append((int)world.Options.Skill + 1).Append(",\n");
            text.Append("\"levelTime\":").Append(world.LevelTime).Append(",\n");

            WriteLines(text);
            WriteSectors(text);

            text.Append(",\n\"samples\":");
            text.Append(samples.ToString());
            text.Append("\n]\n}\n");

            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private void WriteLines(StringBuilder text)
        {
            var map = world.Map;

            text.Append("\"lines\":[");

            for (var i = 0; i < map.Lines.Length; i++)
            {
                var line = map.Lines[i];

                if (i > 0)
                {
                    text.Append(",");
                }

                text.Append("{\"x1\":").Append(Number(line.Vertex1.X.ToFloat()));
                text.Append(",\"y1\":").Append(Number(line.Vertex1.Y.ToFloat()));
                text.Append(",\"x2\":").Append(Number(line.Vertex2.X.ToFloat()));
                text.Append(",\"y2\":").Append(Number(line.Vertex2.Y.ToFloat()));
                text.Append(",\"two\":").Append(line.BackSector != null ? "true" : "false");
                text.Append(",\"special\":").Append((int)line.Special);
                text.Append("}");
            }

            text.Append("]");
        }

        private void WriteSectors(StringBuilder text)
        {
            var map = world.Map;

            text.Append(",\n\"sectors\":[");

            for (var i = 0; i < map.Sectors.Length; i++)
            {
                var sector = map.Sectors[i];

                if (i > 0)
                {
                    text.Append(",");
                }

                text.Append("{\"n\":").Append(sector.Number);
                text.Append(",\"floor\":").Append(Number(sector.FloorHeight.ToFloat()));
                text.Append(",\"ceil\":").Append(Number(sector.CeilingHeight.ToFloat()));
                text.Append("}");
            }

            text.Append("]");
        }

        private string MapName()
        {
            return world.Options.GameMode == GameMode.Commercial
                ? "MAP" + world.Options.Map.ToString("00")
                : "E" + world.Options.Episode + "M" + world.Options.Map;
        }

        /// <summary>
        /// Numbers with a fixed culture. A trace written on a machine with a
        /// decimal comma and read on one with a decimal point (or by a JSON
        /// parser that expects a point) would otherwise be silently wrong.
        /// </summary>
        private static string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            var result = new StringBuilder(value.Length + 8);

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': break;
                    case '\t': result.Append(' '); break;
                    default:
                        if (c < ' ')
                        {
                            result.Append(' ');
                        }
                        else
                        {
                            result.Append(c);
                        }
                        break;
                }
            }

            return result.ToString();
        }
    }
}
