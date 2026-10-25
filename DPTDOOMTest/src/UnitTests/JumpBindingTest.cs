using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// A feature nobody can find is not a feature, so jump has to be bound by default and the
    /// binding has to survive a config file.
    ///
    /// The second half is the part that actually breaks: `Config.Save` writes every binding
    /// by name and the loading constructor reads them back by name, and a binding that is
    /// written but never read is silently lost on the next start - which is exactly the sort
    /// of thing nobody notices until a player says "the key stopped working".
    /// </summary>
    [TestClass]
    public class JumpBindingTest
    {
        [TestMethod]
        public void JumpIsBoundByDefault()
        {
            var config = new Config();

            Assert.IsNotNull(config.key_jump, "there is a jump binding at all");
            Assert.IsTrue(config.key_jump.Keys.Count > 0, "and it has a key");

            // Not Space: Space is Use and has been since Doom. Taking it would break the
            // muscle memory of everyone who has ever opened a door.
            foreach (var key in config.key_jump.Keys)
            {
                Assert.AreNotEqual(DoomKey.Space, key, "jump must not take the Use key");
            }
        }

        [TestMethod]
        public void TheBindingSurvivesSaveAndLoad()
        {
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-jump-binding.cfg");

            try
            {
                var saved = new Config();

                // Lower case, because that is how DoomKeyEx writes and reads key names:
                // "g" round-trips, "G" is not a name it knows.
                saved.key_jump = KeyBinding.Parse("g");
                saved.Save(path);

                var loaded = new Config(path);

                Assert.AreEqual(1, loaded.key_jump.Keys.Count,
                    "one key, as written");

                Assert.AreEqual(DoomKey.G, loaded.key_jump.Keys[0],
                    "a rebound jump key is read back, not reset to the default");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
