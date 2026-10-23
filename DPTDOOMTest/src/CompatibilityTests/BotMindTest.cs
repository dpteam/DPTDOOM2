using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The parts of the bot that are about judgement rather than geometry:
    /// character, what a thing on the floor is worth, and remembering where it
    /// hurts.
    /// </summary>
    [TestClass]
    public class BotMindTest
    {
        [TestMethod]
        public void TemperamentsDifferAndRepeat()
        {
            var a = BotTemperament.FromSeed(BotTemperament.SeedFor("MAP01", 0));
            var b = BotTemperament.FromSeed(BotTemperament.SeedFor("MAP01", 1));
            var again = BotTemperament.FromSeed(BotTemperament.SeedFor("MAP01", 0));

            Console.WriteLine("bot 0: " + a.Describe() + " " + a.ToNumbers());
            Console.WriteLine("bot 1: " + b.Describe() + " " + b.ToNumbers());

            Assert.AreEqual(a.ToNumbers(), again.ToNumbers(),
                "the same seed must give the same character, or a bot changes on every launch");

            Assert.AreNotEqual(a.ToNumbers(), b.ToNumbers(),
                "two bots in the same game must not be the same person");

            foreach (var value in new[] { a.Greed, a.Caution, a.Aggression, a.Curiosity, a.Patience })
            {
                Assert.IsTrue(value >= 0F && value <= 1F, "temperament axes are 0..1");
            }
        }

        [TestMethod]
        public void HealthIsWorthMoreWhenItIsLow()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameVersion = content.Wad.GameVersion;
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;
                options.Episode = 1;
                options.Map = 1;
                options.Players[0].InGame = true;

                var game = new DoomGame(content, options);
                game.DeferedInitNew();

                var cmds = new TicCmd[Player.MaxPlayerCount];
                for (var i = 0; i < cmds.Length; i++) cmds[i] = new TicCmd();
                game.Update(cmds);

                var player = game.World.ConsolePlayer;

                var atFull = BotItemValue.Desire(BotItemValue.Kind.Health, player);

                player.Health = 20;
                var atTwenty = BotItemValue.Desire(BotItemValue.Kind.Health, player);

                Console.WriteLine("health desire: full=" + atFull.ToString("0.00") +
                                  " twenty=" + atTwenty.ToString("0.00"));

                Assert.AreEqual(0F, atFull, "a medikit at full health is not worth a detour");
                Assert.IsTrue(atTwenty > 1.5F, "a medikit at twenty health is worth a lot");

                // A key is always worth any walk: without it the level cannot end.
                Assert.IsTrue(BotItemValue.Desire(BotItemValue.Kind.Key, player) >= 3F);
            }
        }

        [TestMethod]
        public void DangerIsRememberedAndFades()
        {
            var danger = new BotDanger(10);

            // Take 40 damage in sector 3.
            danger.Observe(3, 100, 0);
            danger.Observe(3, 60, 1);

            Assert.IsTrue(danger.Heat(3) > 0F, "damage must warm the sector");
            Assert.AreEqual(3, danger.Hottest());
            Assert.IsTrue(danger.CostMultiplier(3) > 1F,
                "a warm sector must cost more to route through");
            Assert.AreEqual(1F, danger.CostMultiplier(7),
                "a sector with no damage must cost nothing extra");

            var before = danger.Heat(3);

            // Ten seconds later heat fades.
            danger.Observe(7, 60, 10 * 35 + 10);

            Console.WriteLine("heat before=" + before.ToString("0.00") +
                              " after=" + danger.Heat(3).ToString("0.00"));

            Assert.IsTrue(danger.Heat(3) < before,
                "heat must fade, or an old bad room scares the bot forever");
        }

        [TestMethod]
        public void DangerSurvivesASaveAndLoad()
        {
            var danger = new BotDanger(8);

            danger.Observe(2, 100, 0);
            danger.Observe(2, 50, 1);

            var text = danger.Serialize();

            var restored = new BotDanger(8);
            restored.Deserialize(text);

            Console.WriteLine("serialised: " + text);

            Assert.AreEqual(danger.Heat(2), restored.Heat(2), 0.001F);

            // A damaged or foreign line must be ignored, not crash.
            restored.Deserialize("nonsense 4:x 99:1 2:0.5");
            Assert.IsTrue(restored.Heat(2) > 0F);
        }
    }
}
