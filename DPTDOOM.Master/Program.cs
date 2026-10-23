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
using System.Threading;
using DPTDOOM.Shared;

namespace DPTDOOM.Master
{
    public static class ApplicationInfo
    {
        public const string Name = "DPT.DOOM master";
        public const string Version = "0.1.0";
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            string configPath = null;
            string bind = null;
            string logPath = null;
            int? port = null;
            int? lease = null;

            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--config":
                            configPath = Next(args, ref i);
                            break;

                        case "--bind":
                            bind = Next(args, ref i);
                            break;

                        case "--port":
                            port = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                            break;

                        case "--lease-seconds":
                            lease = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                            break;

                        case "--log":
                            logPath = Next(args, ref i);
                            break;

                        case "--version":
                            Console.WriteLine(ApplicationInfo.Name + " v" + ApplicationInfo.Version);
                            return 0;

                        case "--help":
                        case "-h":
                            Usage();
                            return 0;

                        default:
                            Console.Error.WriteLine("Unknown option: " + args[i]);
                            Usage();
                            return 2;
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Bad command line: " + e.Message);
                return 2;
            }

            var warnings = new List<string>();
            MasterConfig config;

            try
            {
                config = MasterConfig.Load(configPath, warnings);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Configuration error: " + e.Message);
                return 2;
            }

            // Explicit command line values win over the config file.
            if (bind != null)
            {
                config.Bind = bind;
            }

            if (port.HasValue)
            {
                if (port.Value < 1 || port.Value > 65535)
                {
                    Console.Error.WriteLine("--port must be 1..65535");
                    return 2;
                }

                config.Port = port.Value;
            }

            if (lease.HasValue)
            {
                if (lease.Value < 3 || lease.Value > 300)
                {
                    Console.Error.WriteLine("--lease-seconds must be 3..300");
                    return 2;
                }

                config.LeaseSeconds = lease.Value;
            }

            if (logPath != null)
            {
                config.LogPath = logPath;
            }

            if (config.LogPath == null)
            {
                config.LogPath = Path.Combine(PortablePaths.GetDataDirectory(), "dptdoom-master.log");
            }

            using (var log = new MasterLog(config.LogPath))
            {
                foreach (var warning in warnings)
                {
                    log.Warn(warning);
                }

                MasterServer server;

                try
                {
                    server = new MasterServer(config, log);
                }
                catch (Exception e)
                {
                    log.Warn("cannot start: " + e.Message);
                    return 2;
                }

                var stop = false;
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    stop = true;
                };

                try
                {
                    server.Run();
                }
                catch (Exception e)
                {
                    log.Warn("cannot listen: " + e.Message);
                    return 2;
                }

                while (!stop)
                {
                    Thread.Sleep(200);
                }

                server.Stop();
                log.Info("stopped");
            }

            return 0;
        }

        private static string Next(string[] args, ref int index)
        {
            index++;
            if (index >= args.Length)
            {
                throw new Exception("missing value for " + args[index - 1]);
            }

            return args[index];
        }

        private static void Usage()
        {
            Console.WriteLine("DPT.DOOM master server " + ApplicationInfo.Version);
            Console.WriteLine();
            Console.WriteLine("Usage: dptdoom-master [options]");
            Console.WriteLine();
            Console.WriteLine("  --config <file>        read bind/port/lease/banlist/allowlist from a file");
            Console.WriteLine("  --bind <ipv4>          listen address (default 127.0.0.1)");
            Console.WriteLine("  --port <1..65535>      listen port (default 25135)");
            Console.WriteLine("  --lease-seconds <3..300>  listing lifetime (default 60)");
            Console.WriteLine("  --log <file>           log file (default user/dptdoom-master.log)");
            Console.WriteLine("  --version              print the version");
            Console.WriteLine("  --help                 print this text");
        }
    }
}
