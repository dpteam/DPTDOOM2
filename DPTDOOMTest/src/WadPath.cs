using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DPTDOOMTest
{
    /// <summary>
    /// IWAD/PWAD names used by the compatibility tests. The game data itself is
    /// not part of the repository, so a missing file turns the test into an
    /// inconclusive result instead of a failure. Put the WADs in refs/iwads (or
    /// next to the test binaries) to run the full suite.
    /// </summary>
    public static class WadPath
    {
        public static string Doom1Shareware => Require("DOOM1.WAD");
        public static string Doom1 => Require("DOOM.WAD");
        public static string Doom2 => Require("DOOM2.WAD");
        public static string Tnt => Require("TNT.WAD");
        public static string Plutonia => Require("PLUTONIA.WAD");

        public static string Requiem => Require("REQUIEM.WAD");
        public static string TntBlood => Require("TNTBLOOD.WAD");
        public static string MementoMori => Require("MM.WAD");

        private static string Require(string name)
        {
            if (!File.Exists(name))
            {
                Assert.Inconclusive("'" + name + "' is not available in the test directory.");
            }

            return name;
        }
    }
}
