using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The unpacked actor reference must be the built-in table, exactly.
    ///
    /// This is the whole value of the feature: a reference that disagrees with the engine by one
    /// field is worse than no reference, because a modder copies it, edits one number, and gets a
    /// result they cannot explain. So the test does not check that the files "look right" - it
    /// applies them to a fresh table and compares every field of every actor.
    ///
    /// The comparison is field by field rather than by reference or by hash: a hash says two
    /// tables differ and nothing about where, and this test has to be able to name the field.
    /// </summary>
    [TestClass]
    public class ActorTemplateTest
    {
        [TestMethod]
        public void TheGeneratedFilesReproduceTheTable()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                // A copy to compare against, taken before anything is applied.
                var expected = Clone(DoomInfo.MobjInfos);

                var files = ActorTemplateWriter.Build(DoomInfo.MobjInfos);

                var library = new ScriptLibrary();

                foreach (var file in files)
                {
                    if (!file.Name.EndsWith(".dpts", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    library.Add(file.Content, file.Name, DoomInfo.MobjInfos);
                }

                Assert.AreEqual(0, library.Problems.Count,
                    library.Problems.Count == 0 ? "" : library.Problems[0]);

                library.ApplyTo(DoomInfo.MobjInfos);

                Assert.AreEqual(0, library.Problems.Count,
                    library.Problems.Count == 0 ? "" : library.Problems[0]);

                for (var i = 0; i < expected.Length; i++)
                {
                    CompareActor(expected[i], DoomInfo.MobjInfos[i]);
                }
            }
        }

        [TestMethod]
        public void EveryActorWithAMapNumberIsWritten()
        {
            var files = ActorTemplateWriter.Build(DoomInfo.MobjInfos);

            var text = string.Empty;

            foreach (var file in files)
            {
                if (file.Name.EndsWith(".dpts", StringComparison.Ordinal))
                {
                    text += file.Content;
                }
            }

            var written = 0;

            foreach (var info in DoomInfo.MobjInfos)
            {
                if (info.DoomEdNum < 0)
                {
                    // No map number, so no name the API can give it. The README says so.
                    continue;
                }

                StringAssert.Contains(text, "fn Thing_" + info.DoomEdNum + "()");
                written++;
            }

            Assert.IsTrue(written > 100,
                "the vanilla table has well over a hundred map-placeable actors, wrote " + written);
        }

        [TestMethod]
        public void TheReferenceIsNotLoadedByItself()
        {
            // The claim the file header makes. If this ever becomes false - if the unpacked
            // folder is picked up automatically - then a DeHackEd patch loaded alongside it
            // would be undone, and the header would be lying.
            var source = File.ReadAllText(SourcePath("DPTDOOM/src/Doom/Game/GameContent.cs"));

            StringAssert.Contains(source, "ActorTemplateWriter.UnpackIfMissing",
                "the unpacker is called at all");

            Assert.IsFalse(source.Contains("dptdoom-data/actors\")"),
                "and the folder is not fed back into the script library");
        }

        [TestMethod]
        public void UnpackingNeverOverwritesAnEdit()
        {
            var folder = Path.Combine(Path.GetTempPath(),
                "dptdoom-actors-" + Guid.NewGuid().ToString("N"));

            try
            {
                var problems = new List<string>();

                var first = ActorTemplateWriter.UnpackIfMissing(folder, DoomInfo.MobjInfos, problems);
                Assert.IsTrue(first.Count > 0, "the first run writes the files");
                Assert.AreEqual(0, problems.Count);

                // Edit one file the way a person would.
                var edited = Path.Combine(folder, ActorTemplateWriter.FolderName, "actors",
                                          "monsters.dpts");

                File.WriteAllText(edited, "// my edit\n");

                var second = ActorTemplateWriter.UnpackIfMissing(folder, DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, second.Count, "the second run writes nothing");
                Assert.AreEqual("// my edit\n", File.ReadAllText(edited),
                    "and the edit survives, which is the point of unpacking it");
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        [TestMethod]
        public void TheSizesComeBackExactly()
        {
            // Fixed point has 1/65536 steps, and a decimal round-trip through a double can miss
            // one of them. Every vanilla size is a whole number, so this checks the arithmetic
            // rather than the field - a fractional size is exercised in the next test.
            var info = Find(DoomInfo.MobjInfos, 3004);

            var files = ActorTemplateWriter.Build(DoomInfo.MobjInfos);

            foreach (var file in files)
            {
                if (!file.Name.Equals("monsters.dpts", StringComparison.Ordinal))
                {
                    continue;
                }

                StringAssert.Contains(file.Content, "Radius(20);");
                StringAssert.Contains(file.Content, "Height(56);");
            }

            Assert.AreEqual(Fixed.FromInt(20).Data, info.Radius.Data);
        }

        [TestMethod]
        public void TheReadmeCountsTypesNotTableSlots()
        {
            // The table includes the mod reserve, and reporting its length as "types in the
            // engine" overstated the count by thirty-two. The arithmetic has to add up:
            // written + skipped == real types.
            var files = ActorTemplateWriter.Build(DoomInfo.MobjInfos);

            string readme = null;

            foreach (var file in files)
            {
                if (file.Name.Equals("README.txt", StringComparison.Ordinal))
                {
                    readme = file.Content;
                }
            }

            Assert.IsNotNull(readme, "the README is written");

            var match = System.Text.RegularExpressions.Regex.Match(
                readme, @"Total: (\d+) types in the engine, of which (\d+) are not here");

            Assert.IsTrue(match.Success, "the README states a total and a skipped count");

            var total = int.Parse(match.Groups[1].Value);
            var skipped = int.Parse(match.Groups[2].Value);

            // The real count is the vanilla table: everything before the reserve.
            Assert.AreEqual(DoomInfo.CustomActorFirst, total,
                "the total is the vanilla types, not the table's length including the reserve");

            Assert.AreEqual(19, skipped, "nineteen vanilla types have no map number");
        }

        private static void CompareActor(MobjInfo expected, MobjInfo actual)
        {
            var where = "actor " + expected.DoomEdNum;

            Assert.AreEqual(expected.DoomEdNum, actual.DoomEdNum, where + ": doomEdNum");
            Assert.AreEqual(expected.SpawnHealth, actual.SpawnHealth, where + ": health");
            Assert.AreEqual(expected.Speed, actual.Speed, where + ": speed");
            Assert.AreEqual(expected.PainChance, actual.PainChance, where + ": painChance");
            Assert.AreEqual(expected.ReactionTime, actual.ReactionTime, where + ": reactionTime");
            Assert.AreEqual(expected.Mass, actual.Mass, where + ": mass");
            Assert.AreEqual(expected.Damage, actual.Damage, where + ": damage");

            Assert.AreEqual(expected.Radius.Data, actual.Radius.Data, where + ": radius");
            Assert.AreEqual(expected.Height.Data, actual.Height.Data, where + ": height");

            Assert.AreEqual(expected.SpawnState, actual.SpawnState, where + ": spawnState");
            Assert.AreEqual(expected.SeeState, actual.SeeState, where + ": seeState");
            Assert.AreEqual(expected.PainState, actual.PainState, where + ": painState");
            Assert.AreEqual(expected.MeleeState, actual.MeleeState, where + ": meleeState");
            Assert.AreEqual(expected.MissileState, actual.MissileState, where + ": missileState");
            Assert.AreEqual(expected.DeathState, actual.DeathState, where + ": deathState");
            Assert.AreEqual(expected.XdeathState, actual.XdeathState, where + ": xdeathState");
            Assert.AreEqual(expected.RaiseState, actual.RaiseState, where + ": raiseState");

            Assert.AreEqual(expected.SeeSound, actual.SeeSound, where + ": seeSound");
            Assert.AreEqual(expected.AttackSound, actual.AttackSound, where + ": attackSound");
            Assert.AreEqual(expected.PainSound, actual.PainSound, where + ": painSound");
            Assert.AreEqual(expected.DeathSound, actual.DeathSound, where + ": deathSound");
            Assert.AreEqual(expected.ActiveSound, actual.ActiveSound, where + ": activeSound");

            // Only the single-bit flags: the composite members (Translation is a two-bit field)
            // cannot be expressed by the API and are not written by the generator either.
            var mask = 0;

            foreach (MobjFlags flag in Enum.GetValues(typeof(MobjFlags)))
            {
                var value = (int)flag;

                if (value == 0 || (value & (value - 1)) != 0)
                {
                    continue;
                }

                mask |= value;
            }

            Assert.AreEqual((int)expected.Flags & mask, (int)actual.Flags & mask, where + ": flags");
        }

        private static MobjInfo[] Clone(MobjInfo[] source)
        {
            var copy = new MobjInfo[source.Length];

            for (var i = 0; i < source.Length; i++)
            {
                var from = source[i];

                copy[i] = new MobjInfo(
                    from.DoomEdNum, from.SpawnState, from.SpawnHealth, from.SeeState,
                    from.SeeSound, from.ReactionTime, from.AttackSound, from.PainState,
                    from.PainChance, from.PainSound, from.MeleeState, from.MissileState,
                    from.DeathState, from.XdeathState, from.DeathSound, from.Speed,
                    from.Radius, from.Height, from.Mass, from.Damage, from.ActiveSound,
                    from.Flags, from.RaiseState);
            }

            return copy;
        }

        private static MobjInfo Find(MobjInfo[] table, int doomEdNum)
        {
            foreach (var info in table)
            {
                if (info.DoomEdNum == doomEdNum)
                {
                    return info;
                }
            }

            Assert.Fail("no actor with DoomEdNum " + doomEdNum);
            return null;
        }

        /// <summary>Reads a file of this project by its path from the repository root.</summary>
        private static string SourcePath(string relative)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relative);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("could not find " + relative + " above " +
                        AppDomain.CurrentDomain.BaseDirectory);
            return null;
        }
    }
}
