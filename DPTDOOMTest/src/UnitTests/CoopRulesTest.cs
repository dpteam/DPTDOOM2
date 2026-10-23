using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The co-op rule switches.
    ///
    /// These three rules change the simulation, so the one thing that absolutely
    /// must hold is that both ends read back the same values. They travel in the
    /// session settings record, and if a flag were dropped the host and guest
    /// would build different worlds and desync on the first tic - a failure that
    /// looks like a network problem and is not one.
    /// </summary>
    [TestClass]
    public class CoopRulesTest
    {
        private static SessionSettings RoundTrip(SessionSettings settings)
        {
            var bytes = settings.Serialize();

            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                return SessionSettings.Deserialize(reader);
            }
        }

        [TestMethod]
        public void DefaultsAreCooperative()
        {
            var settings = new SessionSettings();

            // Two players blocking each other is the game. Four is a traffic jam,
            // which the host handles by player count, not by changing the default.
            Assert.IsTrue(settings.TeammatesBlock, "players block by default");

            // Deathmatch duplicates are clutter in co-op.
            Assert.IsFalse(settings.CoopDeathmatchWeapons, "duplicate DM weapons stay off by default");

            // A co-op run deadlocks without shared keys.
            Assert.IsTrue(settings.SharedKeys, "keys are shared by default");
        }

        [TestMethod]
        public void RulesSurviveTheHandshake()
        {
            var settings = new SessionSettings();
            settings.TeammatesBlock = false;
            settings.CoopDeathmatchWeapons = true;
            settings.SharedKeys = false;

            var back = RoundTrip(settings);

            Assert.IsFalse(back.TeammatesBlock);
            Assert.IsTrue(back.CoopDeathmatchWeapons);
            Assert.IsFalse(back.SharedKeys);
        }

        [TestMethod]
        public void ClassRulesAreUnchanged()
        {
            // The new bits sit in the same byte as the classic rules, so adding
            // them must not shift what the old ones mean.
            var settings = new SessionSettings();
            settings.Deathmatch = true;
            settings.FastMonsters = true;
            settings.RespawnMonsters = false;
            settings.NoMonsters = false;

            var back = RoundTrip(settings);

            Assert.IsTrue(back.Deathmatch);
            Assert.IsTrue(back.FastMonsters);
            Assert.IsFalse(back.RespawnMonsters);
            Assert.IsFalse(back.NoMonsters);

            Assert.IsTrue(back.TeammatesBlock);
            Assert.IsFalse(back.CoopDeathmatchWeapons);
            Assert.IsTrue(back.SharedKeys);
        }

        [TestMethod]
        public void SettingsRecordSizeIsUnchanged()
        {
            // The welcome message has a fixed layout. Growing the settings record
            // would silently shift the name list that follows it.
            //
            // 5 bytes of map/skill/mode/pack, one flags byte, 4 bytes of WAD
            // checksum.
            var settings = new SessionSettings();
            Assert.AreEqual(10, settings.Serialize().Length, "settings record must stay 10 bytes");
        }
    }
}
