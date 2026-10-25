using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// ZDoom TEXTURES: composite textures built out of patches, described in text.
    ///
    /// This is the text form of what TEXTURE1 does in binary, and it is the form a mod can
    /// actually ship: one small file, no texture editor, no whole TEXTURE1 lump. The parser
    /// is tested on its own because a definition that reads wrong produces a picture that
    /// looks wrong, and the picture is the only thing the mod author was trying to change.
    ///
    /// What is deliberately *not* accepted is pinned here too. `XScale`, `Blend`, `Rotate` and
    /// the rest describe effects this renderer does not have; taking them and drawing the
    /// unscaled, unblended patch would be a texture that lies about itself.
    /// </summary>
    [TestClass]
    public class TextureDefinitionsTest
    {
        private static List<TextureDefinition> Parse(string text, out List<string> problems)
        {
            problems = new List<string>();
            return TextureDefinitions.Parse(text, "test", problems);
        }

        private static string All(List<string> problems)
        {
            return string.Join("; ", problems.ToArray());
        }

        [TestMethod]
        public void ATextureIsRead()
        {
            List<string> problems;

            var definitions = Parse(
                "Texture BIGDOOR, 128, 64\n" +
                "{\n" +
                "    Patch DOOR2_1, 0, 0\n" +
                "    Patch DOOR2_2, 64, 0\n" +
                "}", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(1, definitions.Count);

            var definition = definitions[0];

            Assert.AreEqual("BIGDOOR", definition.Name, "names are upper-cased, like lump names");
            Assert.AreEqual(128, definition.Width);
            Assert.AreEqual(64, definition.Height);
            Assert.AreEqual(2, definition.Patches.Count);
            Assert.AreEqual("DOOR2_1", definition.Patches[0].Name);
            Assert.AreEqual(64, definition.Patches[1].X);
            Assert.AreEqual(0, definition.Patches[1].Y);
        }

        [TestMethod]
        public void WidthAndHeightMayBeLeftOut()
        {
            // The common case in a hand-written file. The extent is the honest answer: a mod that
            // wrote only the patches gets the picture it described, not a cropped one.
            List<string> problems;

            var definitions = Parse(
                "Texture SMALL\n" +
                "{\n" +
                "    Patch P1, 0, 0\n" +
                "    Patch P2, 8, 4\n" +
                "}", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(-1, definitions[0].Width, "resolved from the patches later");
            Assert.AreEqual(-1, definitions[0].Height);
        }

        [TestMethod]
        public void CommasAreOptional()
        {
            // ZDoom's own examples always write them, but a file that leaves one out is
            // unambiguous, and refusing it would be a refusal about punctuation.
            List<string> problems;

            var definitions = Parse(
                "Texture NOCOMMAS 64 32\n" +
                "{\n" +
                "    Patch P1 0 0\n" +
                "}", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(64, definitions[0].Width);
            Assert.AreEqual(32, definitions[0].Height);
            Assert.AreEqual(1, definitions[0].Patches.Count);
        }

        [TestMethod]
        public void AWallTextureKeywordIsAccepted()
        {
            // GZDoom's long spelling, which some files use to distinguish a wall from a flat.
            List<string> problems;

            var definitions = Parse("WallTexture W 64, 64 { Patch P1, 0, 0 }", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(1, definitions.Count);
        }

        [TestMethod]
        public void NegativeOffsetsAreRead()
        {
            // A patch placed partly outside the texture is how a texture crops a larger picture,
            // and the minus sign must not be mistaken for a comment or a separator.
            List<string> problems;

            var definitions = Parse("Texture CROPPED, 32, 32 { Patch BIG, -16, -16 }", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(-16, definitions[0].Patches[0].X);
            Assert.AreEqual(-16, definitions[0].Patches[0].Y);
        }

        [TestMethod]
        public void AFlatIsNamedRatherThanSwallowed()
        {
            // Floors here are raw 64x64 pictures, not compositions of patches, so a Flat block has
            // nowhere to go. Accepting it would promise a floor that never appears.
            List<string> problems;

            var definitions = Parse(
                "Flat MYFLAT { Patch P1, 0, 0 }\n" +
                "Texture W 64, 64 { Patch P1, 0, 0 }", out problems);

            StringAssert.Contains(All(problems), "flat");
            StringAssert.Contains(All(problems), "not supported");
            Assert.AreEqual(1, definitions.Count, "and the texture after it still reads");
        }

        [TestMethod]
        public void AnUnsupportedPropertyIsNamed()
        {
            // The important case: a texture that claims to be scaled would be drawn unscaled, so
            // the mod author has to be told rather than left to wonder why it looks wrong.
            List<string> problems;

            var definitions = Parse(
                "Texture W, 64, 64\n" +
                "{\n" +
                "    XScale 2.0\n" +
                "    Patch P1, 0, 0\n" +
                "}", out problems);

            StringAssert.Contains(All(problems), "XScale");
            Assert.AreEqual(1, definitions[0].Patches.Count, "and the patch still reads");
        }

        [TestMethod]
        public void AnUnsupportedPatchOptionIsNamed()
        {
            List<string> problems;

            var definitions = Parse(
                "Texture W, 64, 64\n" +
                "{\n" +
                "    Patch P1, 0, 0 { Rotate 90 }\n" +
                "    Patch P2, 32, 0\n" +
                "}", out problems);

            StringAssert.Contains(All(problems), "Rotate");
            Assert.AreEqual(2, definitions[0].Patches.Count,
                "and the next patch on the following line still reads");
        }

        [TestMethod]
        public void ATextureWithNoPatchesIsReported()
        {
            // An empty rectangle renders as nothing at all, and the map that uses it looks like it
            // has a hole in it.
            List<string> problems;

            var definitions = Parse("Texture EMPTY, 64, 64 { }", out problems);

            StringAssert.Contains(All(problems), "no patches");
            Assert.AreEqual(0, definitions.Count);
        }

        [TestMethod]
        public void ATextureWithNoBlockIsReported()
        {
            List<string> problems;

            var definitions = Parse("Texture W, 64, 64", out problems);

            StringAssert.Contains(All(problems), "not followed by a block");
            Assert.AreEqual(0, definitions.Count);
        }

        [TestMethod]
        public void AnUnclosedBlockIsReportedAndTheTextureIsKept()
        {
            List<string> problems;

            var definitions = Parse("Texture W, 64, 64 { Patch P1, 0, 0", out problems);

            StringAssert.Contains(All(problems), "not closed");
            Assert.AreEqual(1, definitions.Count);
        }

        [TestMethod]
        public void ATextureWithNoNameIsReported()
        {
            List<string> problems;

            var definitions = Parse("Texture , 64, 64 { Patch P1, 0, 0 }", out problems);

            Assert.IsTrue(problems.Count > 0);
            Assert.AreEqual(0, definitions.Count);
        }

        [TestMethod]
        public void CommentsInBothStylesAreSkipped()
        {
            List<string> problems;

            var definitions = Parse(
                "// a line comment\n" +
                "/* and a block\n" +
                "   comment */\n" +
                "Texture W, 64, 64\n" +
                "{\n" +
                "    Patch P1, 0, 0 // trailing\n" +
                "}", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(1, definitions[0].Patches.Count);
        }

        [TestMethod]
        public void SeveralTexturesAreRead()
        {
            List<string> problems;

            var definitions = Parse(
                "Texture A, 64, 64 { Patch P1, 0, 0 }\n" +
                "Texture B, 64, 64 { Patch P2, 0, 0 }\n" +
                "Texture C, 64, 64 { Patch P3, 0, 0 }", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(3, definitions.Count);
            Assert.AreEqual("C", definitions[2].Name);
        }

        [TestMethod]
        public void OneBadTextureDoesNotCostTheNextOne()
        {
            List<string> problems;

            var definitions = Parse(
                "Texture BAD, 64, 64 { }\n" +
                "Texture GOOD, 64, 64 { Patch P1, 0, 0 }", out problems);

            Assert.IsTrue(problems.Count > 0);
            Assert.AreEqual(1, definitions.Count);
            Assert.AreEqual("GOOD", definitions[0].Name);
        }
    }
}
