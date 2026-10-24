//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
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
using System.IO;
using System.Linq;

namespace DPTDOOM
{
    public sealed class CommandLineArgs
    {
        public readonly Arg<string> iwad;
        public readonly Arg<string[]> file;
        public readonly Arg<string[]> deh;

        public readonly Arg<Tuple<int, int>> warp;
        public readonly Arg<int> episode;
        public readonly Arg<int> skill;

        public readonly Arg deathmatch;
        public readonly Arg altdeath;
        public readonly Arg fast;
        public readonly Arg respawn;
        public readonly Arg nomonsters;
        public readonly Arg solonet;

        public readonly Arg noblock;
        public readonly Arg coopweapons;
        public readonly Arg nosharedkeys;

        public readonly Arg<string> playdemo;
        public readonly Arg<string> timedemo;

        public readonly Arg<int> loadgame;

        public readonly Arg nomouse;
        public readonly Arg nosound;
        public readonly Arg nosfx;
        public readonly Arg nomusic;

        public readonly Arg nodeh;

        public readonly Arg headless;
        public readonly Arg<int> tics;
        public readonly Arg<string> shot;
        public readonly Arg version;

        public readonly Arg<string> net;
        public readonly Arg<int> port;
        public readonly Arg<int> players;
        public readonly Arg<string> address;
        public readonly Arg<string> name;
        public readonly Arg<string> master;
        public readonly Arg relay;
        public readonly Arg<string> relayMaster;
        public readonly Arg<string> relaySession;
        public readonly Arg<int> netdelay;
        public readonly Arg botinput;
        public readonly Arg<string> userdir;
        public readonly Arg<string> soundtest;
        public readonly Arg<string> sfx;
        public readonly Arg<string> fpslog;
        public readonly Arg<string> record;
        public readonly Arg wireframe;
        public readonly Arg freeze;
        public readonly Arg maponly;
        public readonly Arg profile;
        public readonly Arg<int> crosshair;
        public readonly Arg freelook;
        public readonly Arg<int> pitch;
        public readonly Arg showsettings;
        public readonly Arg showconsole;
        public readonly Arg showtriggers;
        public readonly Arg showblockmap;
        public readonly Arg bots;
        public readonly Arg<int> coopbots;
        public readonly Arg<string> botdump;
        public readonly Arg<int> botstyle;

        public CommandLineArgs(string[] args)
        {
            iwad = GetString(args, "-iwad");
            file = Check_file(args);
            deh = Check_deh(args);

            warp = Check_warp(args);
            episode = GetInt(args, "-episode");
            skill = GetInt(args, "-skill");

            deathmatch = new Arg(args.Contains("-deathmatch"));
            altdeath = new Arg(args.Contains("-altdeath"));
            fast = new Arg(args.Contains("-fast"));
            respawn = new Arg(args.Contains("-respawn"));
            nomonsters = new Arg(args.Contains("-nomonsters"));
            solonet = new Arg(args.Contains("-solo-net"));

            // Co-op rules. Each has a default that suits co-op, and a switch that
            // forces the other way; the host's choice travels to the guest in the
            // welcome because these change the simulation.
            noblock = new Arg(args.Contains("-noblock"));
            coopweapons = new Arg(args.Contains("-coopweapons"));
            nosharedkeys = new Arg(args.Contains("-nosharedkeys"));

            playdemo = GetString(args, "-playdemo");
            timedemo = GetString(args, "-timedemo");

            loadgame = GetInt(args, "-loadgame");

            nomouse = new Arg(args.Contains("-nomouse"));
            nosound = new Arg(args.Contains("-nosound"));
            nosfx = new Arg(args.Contains("-nosfx"));
            nomusic = new Arg(args.Contains("-nomusic"));

            nodeh = new Arg(args.Contains("-nodeh"));

            headless = new Arg(args.Contains("-headless"));
            tics = GetInt(args, "-tics");
            shot = GetString(args, "-shot");
            version = new Arg(args.Contains("-version") || args.Contains("--version"));

            net = GetString(args, "-net");
            port = GetInt(args, "-port");
            players = GetInt(args, "-players");
            address = GetString(args, "-address");
            name = GetString(args, "-name");
            master = GetString(args, "-master");
            relay = new Arg(args.Contains("-relay"));
            relayMaster = GetString(args, "-relay-master");
            relaySession = GetString(args, "-relay-session");
            netdelay = GetInt(args, "-netdelay");
            botinput = new Arg(args.Contains("-botinput"));
            userdir = GetString(args, "-userdir");
            soundtest = GetString(args, "-soundtest");
            sfx = GetString(args, "-sfx");
            fpslog = GetString(args, "-fpslog");
            record = GetString(args, "-record");
            wireframe = new Arg(args.Contains("-wireframe"));
            freeze = new Arg(args.Contains("-freeze"));
            maponly = new Arg(args.Contains("-maponly"));
            profile = new Arg(args.Contains("-profile"));

            // Presentation only: a crosshair and a tilting view are per-machine, so
            // they are neither sent over the network nor written into a demo.
            crosshair = GetInt(args, "-crosshair");
            freelook = new Arg(args.Contains("-freelook"));
            pitch = GetInt(args, "-pitch");

            // Open a screen straight away, for screenshots and for tests: without
            // these the only way to photograph the menu or the console is to drive
            // the keyboard from outside the process.
            showsettings = new Arg(args.Contains("-showsettings"));
            showconsole = new Arg(args.Contains("-showconsole"));
            showtriggers = new Arg(args.Contains("-showtriggers"));
            showblockmap = new Arg(args.Contains("-showblockmap"));
            bots = new Arg(args.Contains("-bots"));
            coopbots = GetInt(args, "-coopbots");
            botdump = GetString(args, "-botdump");
            botstyle = GetInt(args, "-botstyle");

            // Check for drag & drop.
            if (args.Length > 0 && args.All(arg => arg.FirstOrDefault() != '-'))
            {
                string iwadPath = null;
                var pwadPaths = new List<string>();
                var dehPaths = new List<string>();

                foreach (var path in args)
                {
                    var extension = Path.GetExtension(path).ToLower();

                    if (extension == ".wad")
                    {
                        if (ConfigUtilities.IsIwad(path))
                        {
                            iwadPath = path;
                        }
                        else
                        {
                            pwadPaths.Add(path);
                        }
                    }
                    else if (extension == ".pk3" || extension == ".zip")
                    {
                        // A PK3 is a resource container, never an IWAD, so it
                        // always goes in the load order.
                        pwadPaths.Add(path);
                    }
                    else if (extension == ".deh")
                    {
                        dehPaths.Add(path);
                    }
                }

                if (iwadPath != null)
                {
                    iwad = new Arg<string>(iwadPath);
                }

                if (pwadPaths.Count > 0)
                {
                    file = new Arg<string[]>(pwadPaths.ToArray());
                }

                if (dehPaths.Count > 0)
                {
                    deh = new Arg<string[]>(dehPaths.ToArray());
                }
            }
        }

        private static Arg<string[]> Check_file(string[] args)
        {
            var values = GetValues(args, "-file");
            if (values.Length >= 1)
            {
                return new Arg<string[]>(values);
            }

            return new Arg<string[]>();
        }

        private static Arg<string[]> Check_deh(string[] args)
        {
            var values = GetValues(args, "-deh");
            if (values.Length >= 1)
            {
                return new Arg<string[]>(values);
            }

            return new Arg<string[]>();
        }

        private static Arg<Tuple<int, int>> Check_warp(string[] args)
        {
            var values = GetValues(args, "-warp");
            if (values.Length == 1)
            {
                int map;
                if (int.TryParse(values[0], out map))
                {
                    return new Arg<Tuple<int, int>>(Tuple.Create(1, map));
                }
            }
            else if (values.Length == 2)
            {
                int episode;
                int map;
                if (int.TryParse(values[0], out episode) && int.TryParse(values[1], out map))
                {
                    return new Arg<Tuple<int, int>>(Tuple.Create(episode, map));
                }
            }

            return new Arg<Tuple<int, int>>();
        }

        private static Arg<string> GetString(string[] args, string name)
        {
            var values = GetValues(args, name);
            if (values.Length == 1)
            {
                return new Arg<string>(values[0]);
            }

            return new Arg<string>();
        }

        private static Arg<int> GetInt(string[] args, string name)
        {
            var values = GetValues(args, name);
            if (values.Length == 1)
            {
                int result;
                if (int.TryParse(values[0], out result))
                {
                    return new Arg<int>(result);
                }
            }

            return new Arg<int>();
        }

        private static string[] GetValues(string[] args, string name)
        {
            return args
                .SkipWhile(arg => arg != name)
                .Skip(1)
                .TakeWhile(arg => !IsOption(arg))
                .ToArray();
        }

        /// <summary>
        /// Whether an argument starts a new option rather than being a value.
        ///
        /// A leading '-' used to be enough, which quietly broke every numeric option
        /// that can be negative: "-pitch -40" read as "no value", and the tilt
        /// silently became zero. A lone "-" or a negative number is a value; anything
        /// else starting with '-' is the next option.
        /// </summary>
        private static bool IsOption(string arg)
        {
            if (string.IsNullOrEmpty(arg) || arg[0] != '-')
            {
                return false;
            }

            if (arg.Length == 1)
            {
                return false;
            }

            // -40, -0.5, -1e3: a number, not a switch.
            double parsed;
            return !double.TryParse(
                arg,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed);
        }



        public class Arg
        {
            private bool present;

            public Arg()
            {
                this.present = false;
            }

            public Arg(bool present)
            {
                this.present = present;
            }

            public bool Present => present;
        }

        public class Arg<T>
        {
            private bool present;
            private T value;

            public Arg()
            {
                this.present = false;
                this.value = default;
            }

            public Arg(T value)
            {
                this.present = true;
                this.value = value;
            }

            public bool Present => present;
            public T Value => value;
        }
    }
}
