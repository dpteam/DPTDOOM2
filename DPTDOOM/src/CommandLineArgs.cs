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

        /// <summary>
        /// SharpScript files, by path, or read from a <c>SCRIPTS</c> lump in the WAD.
        ///
        /// Separate from DeHackEd rather than folded into it: the two describe the same
        /// kind of thing (actor properties) in different languages, and a mod may use
        /// both. Scripts are applied after DeHackEd so a script can read what a patch
        /// wrote, which is the order a modder expects from the newer tool.
        /// </summary>
        public readonly Arg<string[]> script;

        /// <summary>
        /// DECORATE files, by path, or read from a <c>DECORATE</c> lump in the WAD.
        ///
        /// Converted into SharpScript rather than interpreted directly, so it takes the same
        /// path as a hand-written script: one parser, one sandbox, one application pass, and
        /// one behaviour to keep correct. The conversion is honest about its limits and says
        /// so in the load report - the numbers come across, the States section does not.
        /// </summary>
        public readonly Arg<string[]> decorate;

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

        /// <summary>
        /// Print the world hash when the run reaches this tic, not only at the end.
        ///
        /// Two networked processes stop at different moments - the host reaches its
        /// -tics limit and quits while the guest is still a tic or two behind - so
        /// comparing their final State lines compares different tics and reports a
        /// divergence that is not one.
        /// </summary>
        public readonly Arg<int> stateat;
        public readonly Arg<string> shot;
        public readonly Arg version;

        public readonly Arg<string> net;
        public readonly Arg<int> port;
        public readonly Arg<int> players;
        public readonly Arg<string> address;
        public readonly Arg<string> name;

        /// <summary>
        /// Session password. On the host it is the requirement, on a guest the answer.
        ///
        /// Kept out of every log line and out of the session settings that travel in
        /// the welcome and get published to the master: a password printed once is a
        /// password that no longer keeps anybody out.
        /// </summary>
        public readonly Arg<string> password;

        /// <summary>
        /// Where the ban list is. Absent means the default file in the data directory,
        /// if it exists.
        /// </summary>
        public readonly Arg<string> bans;

        /// <summary>
        /// Where joins, leaves and refusals are appended. Absent means the default file
        /// in the data directory, written only while hosting.
        /// </summary>
        public readonly Arg<string> netlog;

        /// <summary>
        /// True when "-password" was given but its value parsed as another option.
        ///
        /// A password is the one string a player types freely, so "-password -secret"
        /// is entirely plausible - and the argument reader treats a leading dash as the
        /// next switch. Left alone that reads as "no password at all": the host thinks
        /// the session is open, and it is. The flag is here so the caller can say so
        /// instead of starting a game that silently has no password.
        /// </summary>
        public readonly bool passwordValueLookedLikeAnOption;
        public readonly Arg<string> master;
        public readonly Arg relay;

        /// <summary>
        /// Whether to ask the router to forward the game port, using UPnP.
        ///
        /// Opt-in rather than automatic: a program that silently opens a port on the
        /// internet the first time it runs is a program that asked no permission. A host
        /// who wants it says so.
        /// </summary>
        public readonly Arg upnp;
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
        public readonly Arg<int> profiler;
        public readonly Arg<string> consolecmd;
        public readonly Arg showhelp;
        public readonly Arg showabout;
        public readonly Arg showtriggers;
        public readonly Arg showblockmap;
        public readonly Arg bots;
        public readonly Arg<int> coopbots;
        public readonly Arg<string> botdump;
        public readonly Arg<int> botstyle;

        /// <summary>
        /// Do not load or save the bots' learned map knowledge.
        ///
        /// The brain is a text file beside the executable that grows with every run, which
        /// is what makes bots better over time. It also makes two runs of the same command
        /// behave differently, so anything that measures a bot - the bot test, a
        /// reproduction of a reported bug - has to be able to turn it off.
        /// </summary>
        public readonly Arg nobotbrain;

        /// <summary>
        /// Where the bots' learned map knowledge lives. Defaults to "bots" beside the
        /// executable.
        ///
        /// Exists so a test can point at a throwaway directory: the knowledge is real
        /// state that survives between runs, and a check that reads the developer's
        /// accumulated file is measuring the developer's file.
        /// </summary>
        public readonly Arg<string> botbraindir;

        /// <summary>
        /// Do not write the actor reference into the data directory.
        ///
        /// Exists for tests and for build machines: a run that unpacks files writes to the
        /// machine, and a check that leaves artefacts behind is a check that has to be cleaned
        /// up by hand.
        /// </summary>
        public readonly Arg nodata;

        /// <summary>
        /// MAPINFO files, by path, or read from a <c>MAPINFO</c> lump in the WAD.
        ///
        /// Levels, not actors: the title on the automap and intermission, the sky, and the music
        /// track. Separate from scripts because it describes a level rather than a thing, and a
        /// mod may ship either without the other.
        /// </summary>
        public readonly Arg<string[]> mapinfo;

        /// <summary>
        /// SNDINFO files, by path, or read from a <c>SNDINFO</c> lump in the WAD.
        ///
        /// Which lump a sound name plays, so a mod can give a vanilla sound its own recording without
        /// touching a line of code. The names are the engine's own Sfx names, because Sfx is an enum
        /// and an enum cannot grow at runtime.
        /// </summary>
        public readonly Arg<string[]> sndinfo;

        public CommandLineArgs(string[] args)
        {
            iwad = GetString(args, "-iwad");
            file = Check_file(args);
            deh = Check_deh(args);
            script = Check_script(args);
            decorate = Check_decorate(args);

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
            stateat = GetInt(args, "-stateat");
            shot = GetString(args, "-shot");
            version = new Arg(args.Contains("-version") || args.Contains("--version"));

            net = GetString(args, "-net");
            port = GetInt(args, "-port");
            players = GetInt(args, "-players");
            address = GetString(args, "-address");
            name = GetString(args, "-name");
            password = GetString(args, "-password");
            passwordValueLookedLikeAnOption = args.Contains("-password") && !password.Present;
            bans = GetString(args, "-bans");
            netlog = GetString(args, "-netlog");
            master = GetString(args, "-master");
            relay = new Arg(args.Contains("-relay"));
            upnp = new Arg(args.Contains("-upnp"));
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
            profiler = GetInt(args, "-profiler");

            // One console command, run on the first frame of the level. Exists so a
            // console command can be exercised by a script: without it the only way to
            // type one is to drive the keyboard from outside the process.
            consolecmd = GetString(args, "-consolecmd");
            showhelp = new Arg(args.Contains("-showhelp"));
            showabout = new Arg(args.Contains("-showabout"));
            showtriggers = new Arg(args.Contains("-showtriggers"));
            showblockmap = new Arg(args.Contains("-showblockmap"));
            bots = new Arg(args.Contains("-bots"));
            coopbots = GetInt(args, "-coopbots");
            botdump = GetString(args, "-botdump");
            botstyle = GetInt(args, "-botstyle");
            nobotbrain = new Arg(args.Contains("-nobotbrain"));
            botbraindir = GetString(args, "-botbraindir");
            nodata = new Arg(args.Contains("-nodata"));
            mapinfo = Check_mapinfo(args);
            sndinfo = Check_sndinfo(args);

            // Check for drag & drop.
            if (args.Length > 0 && args.All(arg => arg.FirstOrDefault() != '-'))
            {
                string iwadPath = null;
                var pwadPaths = new List<string>();
                var dehPaths = new List<string>();
                var scriptPaths = new List<string>();
                var decoratePaths = new List<string>();

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
                    else if (extension == ".dpts")
                    {
                        // SharpScript, by extension, so "dptdoom.exe doom2.wad mod.dpts"
                        // works the way a WAD or a .deh does: no flags to remember.
                        scriptPaths.Add(path);
                    }
                    else if (extension == ".decorate" || extension == ".dec")
                    {
                        // DECORATE by extension, same reason. ".dec" is what files in the
                        // wild are usually called even though the lump inside a PWAD is
                        // always DECORATE.
                        decoratePaths.Add(path);
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

                if (scriptPaths.Count > 0)
                {
                    script = new Arg<string[]>(scriptPaths.ToArray());
                }

                if (decoratePaths.Count > 0)
                {
                    decorate = new Arg<string[]>(decoratePaths.ToArray());
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

        private static Arg<string[]> Check_script(string[] args)
        {
            var values = GetValues(args, "-script");
            if (values.Length >= 1)
            {
                return new Arg<string[]>(values);
            }

            return new Arg<string[]>();
        }

        private static Arg<string[]> Check_mapinfo(string[] args)
        {
            var values = GetValues(args, "-mapinfo");
            if (values.Length >= 1)
            {
                return new Arg<string[]>(values);
            }

            return new Arg<string[]>();
        }

        private static Arg<string[]> Check_sndinfo(string[] args)
        {
            var values = GetValues(args, "-sndinfo");
            if (values.Length >= 1)
            {
                return new Arg<string[]>(values);
            }

            return new Arg<string[]>();
        }

        private static Arg<string[]> Check_decorate(string[] args)
        {
            var values = GetValues(args, "-decorate");
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
