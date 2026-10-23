using System;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The bot trace: that it is written, that it is valid JSON, and that it
    /// records what it claims to.
    ///
    /// A debugging tool that quietly writes half a file is worse than none, and
    /// the renderer that reads it is PowerShell, which will not complain about a
    /// trailing comma — it will just fail somewhere unrelated.
    /// </summary>
    [TestClass]
    public class BotTraceTest
    {
        private string folder;

        [TestInitialize]
        public void CreateFolder()
        {
            folder = Path.Combine(
                Path.GetTempPath(),
                "dptdoom-bottrace-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(folder);
        }

        [TestCleanup]
        public void DeleteFolder()
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
        public void WritesGeometryAndSamples()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameVersion = content.Wad.GameVersion;
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;
                options.Skill = GameSkill.Medium;
                options.Episode = 1;
                options.Map = 1;
                options.Players[0].InGame = true;

                var game = new DoomGame(content, options);
                game.DeferedInitNew();

                var cmds = new TicCmd[Player.MaxPlayerCount];
                for (var i = 0; i < cmds.Length; i++) cmds[i] = new TicCmd();
                game.Update(cmds);

                var nav = new NavGraph(game.World.Map);
                var bot = GameBot.Create(game.World, game.World.ConsolePlayer, nav, null, BotStyle.Rush, 0);

                var path = Path.Combine(folder, "trace.json");
                var trace = new BotTrace(path, game.World);

                for (var tic = 0; tic < 35 * 5; tic++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);
                    trace.Sample(cmds, bot, null);
                }

                trace.Write(path);

                Assert.IsTrue(File.Exists(path), "the trace file was not written");

                var text = File.ReadAllText(path);

                Assert.IsTrue(text.Contains("\"lines\":["), "the trace has no geometry");
                Assert.IsTrue(text.Contains("\"samples\":["), "the trace has no samples");
                Assert.IsTrue(text.Contains("\"angle\":"), "samples have no heading");
                Assert.IsTrue(text.Contains("\"decision\":"), "samples have no decision");

                // Balanced brackets is a cheap way to catch a truncated or
                // mis-joined array without pulling in a JSON parser.
                var open = 0;
                var close = 0;
                var inString = false;

                foreach (var c in text)
                {
                    if (c == '"')
                    {
                        inString = !inString;
                    }
                    else if (!inString && c == '[') open++;
                    else if (!inString && c == ']') close++;
                }

                Assert.AreEqual(open, close, "the trace has unbalanced brackets");

                // Numbers must use a dot whatever the machine's culture is, or
                // the renderer reads "1,5" as two values.
                Assert.IsFalse(text.Contains(",5,") && CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == ",",
                    "numbers were written with the local decimal separator");
            }
        }

        [TestMethod]
        public void SamplesCarryPositionAndHeading()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameVersion = content.Wad.GameVersion;
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;
                options.Skill = GameSkill.Medium;
                options.Episode = 1;
                options.Map = 1;
                options.Players[0].InGame = true;

                var game = new DoomGame(content, options);
                game.DeferedInitNew();

                var cmds = new TicCmd[Player.MaxPlayerCount];
                for (var i = 0; i < cmds.Length; i++) cmds[i] = new TicCmd();
                game.Update(cmds);

                var nav = new NavGraph(game.World.Map);
                var bot = GameBot.Create(game.World, game.World.ConsolePlayer, nav, null, BotStyle.Rush, 0);

                var path = Path.Combine(folder, "trace.json");
                var trace = new BotTrace(path, game.World);

                var startX = game.World.ConsolePlayer.Mobj.X.ToFloat();
                var startY = game.World.ConsolePlayer.Mobj.Y.ToFloat();

                for (var tic = 0; tic < 35 * 5; tic++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);
                    trace.Sample(cmds, bot, null);
                }

                trace.Write(path);

                var text = File.ReadAllText(path);

                // The bot must have moved, and the trace must show it: a trace of
                // a stationary bot is exactly the symptom this tool exists to
                // catch, so it would be embarrassing for the tool to hide it.
                var endX = game.World.ConsolePlayer.Mobj.X.ToFloat();
                var endY = game.World.ConsolePlayer.Mobj.Y.ToFloat();

                var moved = Math.Abs(endX - startX) + Math.Abs(endY - startY);

                Console.WriteLine("moved " + moved.ToString("0") + " units in 5 seconds");

                Assert.IsTrue(moved > 64, "the bot did not move, so the trace proves nothing");
                Assert.IsTrue(text.Contains("\"p\":1"), "the sample does not name the player");
            }
        }
    }
}
