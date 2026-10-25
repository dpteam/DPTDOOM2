using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The actor folder is the source of truth, loaded before DeHackEd.
    ///
    /// Three claims, and each one is a way the feature could be useless while looking correct:
    ///
    ///  - an edit to the folder reaches the game. Without this the folder is decoration;
    ///  - a DeHackEd patch still wins, because it is applied after. Getting the order backwards
    ///    would silently undo every patch, which is the failure the previous design was built to
    ///    avoid;
    ///  - a folder from a different build is reported, not silently trusted.
    /// </summary>
    [TestClass]
    public class ActorFolderTest
    {
        private ActorTableSnapshot snapshot;

        /// <summary>
        /// Puts the actor table back after every test.
        ///
        /// Not optional: the folder is applied to the static table, so without this one test's
        /// edit is the next test's starting value - which is exactly how a failure ends up in a
        /// file that has nothing to do with the change.
        /// </summary>
        [TestInitialize]
        public void Before()
        {
            snapshot = ActorTableSnapshot.Take();
        }

        [TestCleanup]
        public void After()
        {
            snapshot.Restore();
        }

        [TestMethod]
        public void AnEditToTheFolderReachesTheTable()
        {
            WithFolder(dir =>
            {
                var args = new CommandLineArgs(new[] { "-iwad", WadPath.Doom2 });

                using (new GameContent(args))
                {
                    Assert.AreEqual(20, Health(3004), "the built-in value to begin with");
                }

                var file = Path.Combine(dir, "dptdoom-data", "actors", "monsters.dpts");
                var text = File.ReadAllText(file);

                // The zombieman's function specifically, not the first "Health(20)" in the file:
                // a blanket replace would also hit whatever else happens to share the number.
                var start = text.IndexOf("fn Thing_3004()", StringComparison.Ordinal);
                Assert.IsTrue(start >= 0, "the zombieman is in the file");

                var end = text.IndexOf("\n}", start, StringComparison.Ordinal);
                Assert.IsTrue(end > start);

                var body = text.Substring(start, end - start);
                Assert.IsTrue(body.Contains("Health(20);"));

                text = text.Substring(0, start) + body.Replace("Health(20);", "Health(500);") +
                       text.Substring(end);

                File.WriteAllText(file, text);

                using (new GameContent(args))
                {
                    Assert.AreEqual(500, Health(3004), "the edit is what the game uses");
                }
            });
        }

        [TestMethod]
        public void ADeHackEdPatchStillWinsOverTheFolder()
        {
            // The order that makes both features usable together. The folder is applied first, so
            // the patch overwrites it - which is what somebody loading both expects.
            WithFolder(dir =>
            {
                var file = Path.Combine(dir, "dptdoom-data", "actors", "monsters.dpts");
                var text = File.ReadAllText(file);
                var start = text.IndexOf("fn Thing_3004()", StringComparison.Ordinal);
                var end = text.IndexOf("\n}", start, StringComparison.Ordinal);

                text = text.Substring(0, start) +
                       text.Substring(start, end - start).Replace("Health(20);", "Health(500);") +
                       text.Substring(end);

                File.WriteAllText(file, text);

                var deh = Path.Combine(dir, "patch.deh");

                File.WriteAllText(deh,
                    "Patch File for DeHackEd v3.0\n" +
                    "Doom version = 21\n" +
                    "Patch format = 6\n\n" +
                    "Thing 2 (Zombieman)\n" +
                    "Hit points = 77\n");

                var args = new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-deh", deh });

                using (new GameContent(args))
                {
                    Assert.AreEqual(77, Health(3004),
                        "the patch is applied after the folder and wins");
                }
            });
        }

        [TestMethod]
        public void AFolderFromAnotherBuildIsReported()
        {
            WithFolder(dir =>
            {
                var readme = Path.Combine(dir, "dptdoom-data", "actors", "README.txt");
                var text = File.ReadAllText(readme);

                var start = text.IndexOf("Fingerprint:", StringComparison.Ordinal);
                Assert.IsTrue(start >= 0, "the README records which build wrote it");

                var lineEnd = text.IndexOf('\n', start);
                text = text.Substring(0, start) + "Fingerprint: deadbeef" + text.Substring(lineEnd);

                File.WriteAllText(readme, text);

                LoadReport.Clear();

                var args = new CommandLineArgs(new[] { "-iwad", WadPath.Doom2 });

                using (new GameContent(args))
                {
                    Assert.IsTrue(LoadReport.HasProblems, "a stale folder is not silently trusted");

                    var description = LoadReport.Describe();

                    StringAssert.Contains(description, "deadbeef");
                    StringAssert.Contains(description, "may be stale");
                }
            });
        }

        [TestMethod]
        public void AFolderThatMatchesTheBuildSaysNothing()
        {
            // The case on almost every run. If this reported anything, the report would be noise
            // and the stale warning above would stop being read.
            WithFolder(dir =>
            {
                LoadReport.Clear();

                var args = new CommandLineArgs(new[] { "-iwad", WadPath.Doom2 });

                using (new GameContent(args))
                {
                    Assert.IsFalse(LoadReport.HasProblems,
                        "a folder this build wrote is not a problem:\n" + LoadReport.Describe());
                }
            });
        }

        [TestMethod]
        public void WithoutAFolderTheBuiltInValuesAreUsed()
        {
            WithFolder(dir =>
            {
                Directory.Delete(Path.Combine(dir, "dptdoom-data"), true);

                // -nodata as well, so this also checks the other half of the promise: a run that
                // is told not to touch the machine does not recreate the folder either.
                var args = new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-nodata" });

                using (new GameContent(args))
                {
                    Assert.AreEqual(20, Health(3004), "the compiled table is the fallback");
                    Assert.IsFalse(Directory.Exists(Path.Combine(dir, "dptdoom-data")),
                        "-nodata means nothing is written either");
                }
            });
        }

        private static int Health(int doomEdNum)
        {
            foreach (var info in DoomInfo.MobjInfos)
            {
                if (info.DoomEdNum == doomEdNum)
                {
                    return info.SpawnHealth;
                }
            }

            Assert.Fail("no actor with DoomEdNum " + doomEdNum);
            return 0;
        }

        /// <summary>
        /// Runs the body with the data directory pointed at a throwaway folder.
        ///
        /// The actor folder is found through the data directory, so this is the only way to test
        /// it without writing into the machine's real one. -nodata is not passed: the body is
        /// testing what happens when the folder is written and read.
        /// </summary>
        private static void WithFolder(Action<string> body)
        {
            var dir = Path.Combine(Path.GetTempPath(), "dptdoom-actorfolder-" +
                                                       Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(dir);

            var previous = ConfigUtilities.OverrideDataDirectory;
            ConfigUtilities.OverrideDataDirectory = dir;

            try
            {
                // One run to create the folder, then the body works with it.
                using (new GameContent(new CommandLineArgs(new[] { "-iwad", WadPath.Doom2 })))
                {
                }

                body(dir);
            }
            finally
            {
                ConfigUtilities.OverrideDataDirectory = previous;

                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
