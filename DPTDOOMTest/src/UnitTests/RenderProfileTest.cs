using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Video;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The frame profiler's arithmetic, on its own.
    ///
    /// The integration test (tools/test-profile.ps1) proves the profiler does not
    /// change the frame it measures; this one pins what the numbers mean, which
    /// that test cannot see. Two mistakes are easy to make here and both produce a
    /// report that looks plausible:
    ///
    ///   - dividing by frames that were never measured, so turning the profiler on
    ///     halfway through a session dilutes every average with zeros;
    ///   - counting a nested section as a sibling of its parent, so the sections
    ///     add up to more than the frame.
    /// </summary>
    [TestClass]
    public class RenderProfileTest
    {
        [TestMethod]
        public void DisabledRecordsNothing()
        {
            var profile = new RenderProfile();

            profile.BeginFrame();
            profile.Add(RenderProfile.Section.Bsp, 1000);
            profile.Count(RenderProfile.Counter.Segments);
            profile.EndFrame();

            Assert.AreEqual(0, profile.Frames, "nothing was measured, so no frame was recorded");

            var text = new StringWriter();
            profile.WriteSummary(text);

            StringAssert.Contains(text.ToString(), "disabled");
        }

        [TestMethod]
        public void EnabledAveragesOnlyMeasuredFrames()
        {
            var profile = new RenderProfile();

            // A frame drawn before the profiler was switched on. It must not count:
            // averaging it in would report a frame that was never sampled.
            profile.BeginFrame();
            profile.EndFrame();

            profile.Enabled = true;

            for (var i = 0; i < 4; i++)
            {
                profile.BeginFrame();
                profile.Add(RenderProfile.Section.Bsp, 0);
                profile.EndFrame();
            }

            Assert.AreEqual(4, profile.Frames, "only the frames measured while enabled were counted");
        }

        [TestMethod]
        public void SectionsAccumulateAndTheLastFrameIsKept()
        {
            var profile = new RenderProfile();
            profile.Enabled = true;

            profile.BeginFrame();
            profile.Add(RenderProfile.Section.Bsp, 0);
            profile.Add(RenderProfile.Section.Hud, 0);
            profile.EndFrame();

            profile.BeginFrame();
            profile.Add(RenderProfile.Section.Bsp, 0);
            profile.EndFrame();

            // The last frame had no HUD work, and the overlay has to show zero
            // rather than the previous frame's figure: a stale number reads as a
            // measurement of the frame the player is looking at.
            Assert.AreEqual(0.0, profile.LastMs(RenderProfile.Section.Hud), 1e-9);
        }

        [TestMethod]
        public void TheReportNamesEverySection()
        {
            var profile = new RenderProfile();
            profile.Enabled = true;

            profile.BeginFrame();
            profile.Add(RenderProfile.Section.Bsp, 10000);
            profile.Count(RenderProfile.Counter.Segments, 7);
            profile.EndFrame();

            var text = new StringWriter();
            profile.WriteSummary(text);
            var report = text.ToString();

            foreach (var name in new[] { "setup", "bsp", "walls", "flats", "sprites", "masked", "player", "sky", "convert", "hud" })
            {
                StringAssert.Contains(report, name, "the report has a '" + name + "' line");
            }

            StringAssert.Contains(report, "segs 7.0", "counters are printed per frame");
        }

        [TestMethod]
        public void TheNumbersUseTheInvariantCulture()
        {
            // The report is read by scripts and by people copying it into a bug
            // report. A comma decimal separator would make it unparseable in the
            // very locale the project's own documentation is written in.
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;

            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("ru-RU");

                var profile = new RenderProfile();
                profile.Enabled = true;

                profile.BeginFrame();
                profile.Add(RenderProfile.Section.Bsp, 100000);
                profile.EndFrame();

                var text = new StringWriter();
                profile.WriteSummary(text);

                // A comma between digits would be a decimal separator taken from
                // the machine's culture. The report has ordinary commas in its
                // prose ("ms per frame, worst"), so the check is specifically
                // about digits.
                Assert.IsFalse(
                    System.Text.RegularExpressions.Regex.IsMatch(text.ToString(), @"\d,\d"),
                    "no decimal comma in the report: " + text);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
