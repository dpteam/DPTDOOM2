using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The actor archive the engine builds for itself: written on first run, read back when the
    /// editable folder is not there.
    ///
    /// This is the first step of what ZDoom does with `zdoom.pk3` - the engine's own content as one
    /// archive it produces itself, rather than as numbers in a constructor. The difference from that
    /// port is deliberate and worth stating: there the archive is assembled by the build scripts,
    /// which a player does not run. Here the engine assembles it.
    ///
    /// The two copies have different jobs, which is why both exist:
    ///
    ///  - the **folder** is what a person edits, and is never overwritten;
    ///  - the **archive** is one file that can be copied to another machine as-is, and it is the
    ///    fallback when the folder is absent.
    ///
    /// The folder wins when both are there. An edit is a decision; a regenerated artifact is not.
    /// </summary>
    [TestClass]
    public class ActorArchiveTest
    {
        private string folder;

        [TestInitialize]
        public void Before()
        {
            folder = Path.Combine(Path.GetTempPath(), "dptdoom-actpk3-" +
                                                      Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(folder);
        }

        [TestCleanup]
        public void After()
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (IOException)
            {
            }
        }

        [TestMethod]
        public void TheArchiveIsWritten()
        {
            var problems = new List<string>();

            Assert.IsTrue(ActorTemplateWriter.WritePk3(folder, DoomInfo.MobjInfos, problems),
                "the archive was written");

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var path = ActorTemplateWriter.Pk3Path(folder);

            Assert.IsTrue(File.Exists(path));

            var archive = new ZipArchiveReader(path);

            Assert.AreEqual(4, archive.Entries.Count, "three actor files and the README");
        }

        [TestMethod]
        public void TheArchiveCarriesTheFingerprint()
        {
            // The fingerprint is what says whether the archive matches this build. It is read from
            // the README the archive carries rather than from a marker of its own, so the archive
            // and the folder cannot disagree about what build they came from.
            var problems = new List<string>();

            ActorTemplateWriter.WritePk3(folder, DoomInfo.MobjInfos, problems);

            var archive = new ZipArchiveReader(ActorTemplateWriter.Pk3Path(folder));
            var found = false;

            foreach (var entry in archive.Entries)
            {
                if (!entry.Name.EndsWith("README.txt", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = System.Text.Encoding.UTF8.GetString(archive.ReadEntry(entry));

                StringAssert.Contains(text, "Fingerprint: " +
                    ActorTemplateWriter.CurrentFingerprint(DoomInfo.MobjInfos));

                found = true;
            }

            Assert.IsTrue(found, "the archive has its README");
        }

        [TestMethod]
        public void TheArchiveIsAppliedWhenThereIsNoFolder()
        {
            // The case this exists for: somebody was handed dptdoom.pk3 and never unpacked it.
            // The engine has to be as correct with the archive alone as with the folder.
            var problems = new List<string>();

            ActorTemplateWriter.WritePk3(folder, DoomInfo.MobjInfos, problems);

            var snapshot = ActorTableSnapshot.Take();

            try
            {
                // Damage a number the archive describes, so applying it is observable.
                DoomInfo.MobjInfos[(int)MobjType.Possessed].SpawnHealth = 1;

                var applied = ActorTemplateWriter.Load(folder, DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
                Assert.IsTrue(applied > 0, "actors were applied from the archive");
            }
            finally
            {
                snapshot.Restore();
            }
        }

        [TestMethod]
        public void TheFolderWinsWhenBothExist()
        {
            // An edit in the folder is a decision. The archive must not undo it.
            var problems = new List<string>();

            ActorTemplateWriter.WritePk3(folder, DoomInfo.MobjInfos, problems);

            var actorFolder = Path.Combine(folder, ActorTemplateWriter.FolderName, "actors");
            Directory.CreateDirectory(actorFolder);

            // One file, describing one actor with a health nobody would ship.
            File.WriteAllText(Path.Combine(actorFolder, ActorTemplateWriter.ActorFiles[0]),
                "fn Thing_" + DoomInfo.MobjInfos[(int)MobjType.Possessed].DoomEdNum + "() {\n" +
                "    Health(7);\n" +
                "}\n");

            var snapshot = ActorTableSnapshot.Take();

            try
            {
                ActorTemplateWriter.Load(folder, DoomInfo.MobjInfos, problems);

                Assert.AreEqual(7, DoomInfo.MobjInfos[(int)MobjType.Possessed].SpawnHealth,
                    "the folder's value, not the archive's");
            }
            finally
            {
                snapshot.Restore();
            }
        }

        [TestMethod]
        public void ACorruptArchiveIsReportedAndIgnored()
        {
            // A half-written file must not stop the game: the built-in table is already correct
            // without it.
            Directory.CreateDirectory(Path.Combine(folder, ActorTemplateWriter.FolderName));

            File.WriteAllBytes(ActorTemplateWriter.Pk3Path(folder),
                new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0 });

            var problems = new List<string>();

            var applied = ActorTemplateWriter.Load(folder, DoomInfo.MobjInfos, problems);

            Assert.AreEqual(0, applied);
            Assert.IsTrue(problems.Count > 0, "and it was reported");
        }
    }
}
