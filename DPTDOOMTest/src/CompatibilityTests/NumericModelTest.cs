using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom's numeric model: which picture a generalized mover copies.
    ///
    /// The rule that is easy to get backwards is the *which surface to look at* part. A floor
    /// moving to a ceiling height cannot be level with a neighbour's floor - it is going to a
    /// ceiling - so it looks for a neighbour whose **ceiling** is where it will end up. Boom's
    /// source says exactly that, in a comment that reads like a correction of a previous mistake
    /// ("jff 5/23/98 find model with ceiling at target height if target is a ceiling type").
    ///
    /// The target numbers are pinned here for the same reason the key numbers were: they are what
    /// a map's linedef carries, so they are not ours to renumber.
    /// </summary>
    [TestClass]
    public class NumericModelTest
    {
        [TestMethod]
        public void AFloorLooksAtCeilingsOnlyForCeilingTargets()
        {
            // floortarget_e: FtoHnF, FtoLnF, FtoNnF, FtoLnC, FtoC, FbyST, Fby24, Fby32.
            Assert.IsFalse(NumericModel.FloorLooksAtCeiling(0), "FtoHnF");
            Assert.IsFalse(NumericModel.FloorLooksAtCeiling(1), "FtoLnF");
            Assert.IsFalse(NumericModel.FloorLooksAtCeiling(2), "FtoNnF");
            Assert.IsTrue(NumericModel.FloorLooksAtCeiling(3), "FtoLnC");
            Assert.IsTrue(NumericModel.FloorLooksAtCeiling(4), "FtoC");
            Assert.IsFalse(NumericModel.FloorLooksAtCeiling(5), "FbyST");
            Assert.IsFalse(NumericModel.FloorLooksAtCeiling(6), "Fby24");
            Assert.IsFalse(NumericModel.FloorLooksAtCeiling(7), "Fby32");
        }

        [TestMethod]
        public void ACeilingLooksAtFloorsOnlyForFloorTargets()
        {
            // ceilingtarget_e: CtoHnC, CtoLnC, CtoNnC, CtoHnF, CtoF, CbyST, Cby24, Cby32.
            Assert.IsFalse(NumericModel.CeilingLooksAtFloor(0), "CtoHnC");
            Assert.IsFalse(NumericModel.CeilingLooksAtFloor(1), "CtoLnC");
            Assert.IsFalse(NumericModel.CeilingLooksAtFloor(2), "CtoNnC");
            Assert.IsTrue(NumericModel.CeilingLooksAtFloor(3), "CtoHnF");
            Assert.IsTrue(NumericModel.CeilingLooksAtFloor(4), "CtoF");
            Assert.IsFalse(NumericModel.CeilingLooksAtFloor(5), "CbyST");
            Assert.IsFalse(NumericModel.CeilingLooksAtFloor(6), "Cby24");
            Assert.IsFalse(NumericModel.CeilingLooksAtFloor(7), "Cby32");
        }

        [TestMethod]
        public void TheRulesAreMirrorImages()
        {
            // Not a coincidence: the two tables are the same eight shapes with the words swapped,
            // and a change to one that is not made to the other is a bug waiting to be written.
            for (var target = 0; target < 8; target++)
            {
                Assert.AreEqual(NumericModel.FloorLooksAtCeiling(target),
                    NumericModel.CeilingLooksAtFloor(target),
                    "target " + target + " should mean the same thing for both");
            }
        }
    }
}
