using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Every setting the config file understands has to be reachable from the in-game
    /// Options screen, or be on a written list of things deliberately left out.
    ///
    /// The rule exists because the alternative rots quietly. A setting that is read from
    /// the file but has no row anywhere in the game is invisible: the player who wants
    /// it does not know it exists, and the next person to touch the config cannot tell
    /// whether it is used, planned, or forgotten. Both answers are acceptable; not
    /// knowing is not.
    ///
    /// Read from the source rather than from reflection on purpose. Reflection would
    /// prove the field exists and prove nothing about the menu; the question here is
    /// whether the menu *mentions* the field, and the honest way to answer that is to
    /// look at the menu.
    /// </summary>
    [TestClass]
    public class ConfigCoverageTest
    {
        /// <summary>
        /// Settings that are not in the Options screen, each with the reason.
        ///
        /// A reason, not a category: "it is a startup value" is not something a reader
        /// can check, while "the window is created from it at launch, so a menu row
        /// would need a window rebuild" is.
        /// </summary>
        private static readonly Dictionary<string, string> NotInMenu =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "video_screenwidth",
                  "the window is created at this size before the first frame; changing it " +
                  "mid-game would mean rebuilding the window and every GDI resource under it" },
                { "video_screenheight",
                  "same as the width - it sizes the window at launch" },
                { "video_fullscreen",
                  "owned by the window menu (Alt+Enter), which is where a player looks for it" },
                { "video_gamescreensize",
                  "the 'SCREEN SIZE' row already drives it through the renderer, and that " +
                  "path also recreates the 3D view" },
                { "video_gammacorrection",
                  "the 'GAMMA' row drives it through the renderer, which rebuilds the palette" },
                { "hud_crosshair_light",
                  "the 'CROSS COLOUR' row picks it by name out of the palette" },
                { "hud_crosshair_dark",
                  "the 'CROSS SHADOW' row picks it by name out of the palette" },
                { "mouse_sensitivity",
                  "the 'MOUSE SPEED' row drives it through the input backend" },
                { "audio_soundvolume",
                  "the 'SOUND VOLUME' row drives it through the sound backend" },
                { "audio_musicvolume",
                  "the 'MUSIC VOLUME' row drives it through the music backend" },
                { "video_displaymessage",
                  "the 'MESSAGES' row drives it through the renderer, which owns the message line" }
            };

        [TestMethod]
        public void EveryConfigFieldIsInTheMenuOrExplained()
        {
            var config = Source("DPTDOOM/src/Config.cs");
            var menu = Source("DPTDOOM/src/Doom/Menu/DoomMenu.cs");

            var fields = new List<string>();
            var pattern = new Regex(@"^\s+public\s+(?:bool|int|string|float|double)\s+(\w+)\s*;",
                RegexOptions.Multiline);

            foreach (Match match in pattern.Matches(config))
            {
                fields.Add(match.Groups[1].Value);
            }

            Assert.IsTrue(fields.Count > 10, "the config was parsed (" + fields.Count + " settings)");

            var missing = new List<string>();

            foreach (var field in fields)
            {
                if (NotInMenu.ContainsKey(field))
                {
                    continue;
                }

                if (!menu.Contains(field))
                {
                    missing.Add(field);
                }
            }

            Assert.AreEqual(0, missing.Count,
                "these settings are read from the config and never appear in the Options " +
                "menu, and are not on the list of things deliberately left out: " +
                string.Join(", ", missing.ToArray()));
        }

        [TestMethod]
        public void TheExemptListDoesNotRot()
        {
            // An exemption for a setting that no longer exists is a lie that outlives the
            // reason: it makes the list look complete while the entry protects nothing.
            var config = Source("DPTDOOM/src/Config.cs");

            var stale = new List<string>();

            foreach (var name in NotInMenu.Keys)
            {
                if (!Regex.IsMatch(config, @"^\s+public\s+(?:bool|int|string|float|double)\s+" +
                                           Regex.Escape(name) + @"\s*;", RegexOptions.Multiline))
                {
                    stale.Add(name);
                }
            }

            Assert.AreEqual(0, stale.Count,
                "these exemptions name settings the config no longer has: " +
                string.Join(", ", stale.ToArray()));
        }

        [TestMethod]
        public void EveryExemptionSaysWhy()
        {
            foreach (var pair in NotInMenu)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Value),
                    "'" + pair.Key + "' is exempt without a reason");

                Assert.IsTrue(pair.Value.Length > 20,
                    "'" + pair.Key + "' has a reason too short to check: " + pair.Value);
            }
        }

        /// <summary>
        /// Reads a file from the repository, walking up from the test binaries.
        ///
        /// The tests run from bin/Release/net9.0, and the depth of that path is a build
        /// detail: walking up until the repository root appears is what keeps this from
        /// breaking the next time the output layout changes.
        /// </summary>
        private static string Source(string relative)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            for (var i = 0; i < 10 && directory != null; i++)
            {
                var candidate = Path.Combine(directory.FullName, relative);

                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                directory = directory.Parent;
            }

            Assert.Fail("could not find " + relative + " above " + AppDomain.CurrentDomain.BaseDirectory);
            return null;
        }
    }
}
