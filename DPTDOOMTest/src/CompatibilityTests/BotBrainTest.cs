using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The bots' memory of a map: what it keeps, what it refuses to keep, and
    /// whether remembering actually helps.
    /// </summary>
    [TestClass]
    public class BotBrainTest
    {
        private string folder;

        [TestInitialize]
        public void CreateFolder()
        {
            folder = Path.Combine(
                Path.GetTempPath(),
                "dptdoom-botbrain-" + Guid.NewGuid().ToString("N"));

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
        public void RemembersTheBestTime()
        {
            var brain = new BotBrain(folder, "MAP01-test");

            brain.RecordRun(BotStyle.Rush, 2100, true, new[] { 1, 2, 3 }, 3);
            brain.RecordRun(BotStyle.Hunter, 3000, true, new[] { 9, 9 }, 2);

            Assert.AreEqual(2100, brain.BestTics);
            Assert.AreEqual(BotStyle.Rush, brain.BestStyle);
            Assert.AreEqual(2, brain.Runs);

            // A slower run must not overwrite the best time or the route.
            brain.RecordRun(BotStyle.Sweeper, 5000, true, new[] { 7 }, 1);

            Assert.AreEqual(2100, brain.BestTics);
            Assert.AreEqual(3, brain.BestRoute.Length);
            Assert.AreEqual(1, brain.BestRoute[0]);
        }

        [TestMethod]
        public void ReloadsFromDisk()
        {
            var first = new BotBrain(folder, "MAP01-test");
            first.RecordRun(BotStyle.Cautious, 1800, true, new[] { 4, 5, 6 }, 3);

            var second = new BotBrain(folder, "MAP01-test");

            Assert.AreEqual(1800, second.BestTics);
            Assert.AreEqual(1, second.Runs);
            Assert.AreEqual(BotStyle.Cautious, second.BestStyle);
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, second.BestRoute);
        }

        [TestMethod]
        public void ARunFinishedBySomebodyElseIsNotABestTime()
        {
            // This is the rule the owner asked for: if the human player left the
            // level first, the bot's run was cut short by something other than
            // its own performance, and scoring it would teach the brain a false
            // lesson.
            var brain = new BotBrain(folder, "MAP01-test");

            brain.RecordRun(BotStyle.Rush, 0, false, null, 0);

            Assert.AreEqual(0, brain.BestTics);
            Assert.AreEqual(0, brain.BestRoute.Length);
            Assert.AreEqual(1, brain.Runs);
            Assert.AreEqual(1, brain.StyleRuns(BotStyle.Rush));
        }

        [TestMethod]
        public void PrefersTheStyleThatWins()
        {
            var brain = new BotBrain(folder, "MAP01-test");

            // Hunter wins repeatedly; Rush loses. Exploration is allowed, so
            // this is checked over many draws rather than a single one.
            for (var i = 0; i < 6; i++)
            {
                brain.RecordRun(BotStyle.Hunter, 1500 + i, true, null, 0);
                brain.RecordRun(BotStyle.Rush, 4000 + i, true, null, 0);
            }

            var hunter = 0;
            var rush = 0;

            for (var seed = 0; seed < 200; seed++)
            {
                var style = brain.ChooseStyle(seed);

                if (style == BotStyle.Hunter) hunter++;
                else if (style == BotStyle.Rush) rush++;
            }

            Console.WriteLine("hunter=" + hunter + " rush=" + rush);

            Assert.IsTrue(hunter > rush * 2,
                "the brain picked the losing style too often: hunter=" + hunter + " rush=" + rush);
        }

        [TestMethod]
        public void BadFileIsIgnored()
        {
            File.WriteAllText(Path.Combine(folder, "MAP01-test.brain"), "not a brain file\n");

            var brain = new BotBrain(folder, "MAP01-test");

            Assert.AreEqual(0, brain.BestTics);
            Assert.AreEqual(0, brain.Runs);
        }

        [TestMethod]
        public void FormatsTimeLikeDoom()
        {
            Assert.AreEqual("0:35.0", BotBrain.FormatTime(35 * 35));
            Assert.AreEqual("1:00.0", BotBrain.FormatTime(60 * 35));
            Assert.AreEqual("--:--", BotBrain.FormatTime(0));
        }
    }
}
