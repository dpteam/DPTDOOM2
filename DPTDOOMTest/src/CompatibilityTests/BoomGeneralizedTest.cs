using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom's generalized linedef encoding.
    ///
    /// The bit layout is not something a map author can see, so a decoder that is
    /// wrong is wrong silently: a door opens as a lift, a floor goes up instead of
    /// down, and the only symptom is a map that does not work. These tests pin the
    /// numbers against the Boom source in refs/prboom-plus.
    /// </summary>
    [TestClass]
    public class BoomGeneralizedTest
    {
        [TestMethod]
        public void FamiliesAreChosenByBase()
        {
            // The bases are from p_spec.h. The family is the highest base a
            // special reaches, which is why the ranges are not all the same size.
            Assert.AreEqual(BoomGeneralized.Family.Floor,
                BoomGeneralized.Decode(0x6000).Family);
            Assert.AreEqual(BoomGeneralized.Family.Ceiling,
                BoomGeneralized.Decode(0x4000).Family);
            Assert.AreEqual(BoomGeneralized.Family.Door,
                BoomGeneralized.Decode(0x3c00).Family);
            Assert.AreEqual(BoomGeneralized.Family.LockedDoor,
                BoomGeneralized.Decode(0x3800).Family);
            Assert.AreEqual(BoomGeneralized.Family.Lift,
                BoomGeneralized.Decode(0x3400).Family);
            Assert.AreEqual(BoomGeneralized.Family.Stairs,
                BoomGeneralized.Decode(0x3000).Family);
            Assert.AreEqual(BoomGeneralized.Family.Crusher,
                BoomGeneralized.Decode(0x2f80).Family);

            // Just below the first base is not generalized at all.
            Assert.AreEqual(BoomGeneralized.Family.None,
                BoomGeneralized.Decode(0x2f7f).Family);
            Assert.IsFalse(BoomGeneralized.IsGeneralized(1));
            Assert.IsFalse(BoomGeneralized.IsGeneralized(0));
        }

        [TestMethod]
        public void TriggerIsTheLowThreeBits()
        {
            // WalkOnce is 0, and every family starts there, so the base value of
            // each family must decode as a walk-once trigger.
            Assert.AreEqual(BoomGeneralized.Trigger.WalkOnce,
                BoomGeneralized.Decode(0x6000).Trigger);

            Assert.AreEqual(BoomGeneralized.Trigger.WalkMany,
                BoomGeneralized.Decode(0x6001).Trigger);
            Assert.AreEqual(BoomGeneralized.Trigger.SwitchOnce,
                BoomGeneralized.Decode(0x6002).Trigger);
            Assert.AreEqual(BoomGeneralized.Trigger.SwitchMany,
                BoomGeneralized.Decode(0x6003).Trigger);
            Assert.AreEqual(BoomGeneralized.Trigger.GunOnce,
                BoomGeneralized.Decode(0x6004).Trigger);
            Assert.AreEqual(BoomGeneralized.Trigger.GunMany,
                BoomGeneralized.Decode(0x6005).Trigger);
            Assert.AreEqual(BoomGeneralized.Trigger.PushOnce,
                BoomGeneralized.Decode(0x6006).Trigger);
            Assert.AreEqual(BoomGeneralized.Trigger.PushMany,
                BoomGeneralized.Decode(0x6007).Trigger);
        }

        [TestMethod]
        public void TriggerHelpersMatch()
        {
            Assert.IsTrue(BoomGeneralized.IsWalk(BoomGeneralized.Trigger.WalkOnce));
            Assert.IsTrue(BoomGeneralized.IsWalk(BoomGeneralized.Trigger.WalkMany));
            Assert.IsFalse(BoomGeneralized.IsWalk(BoomGeneralized.Trigger.SwitchOnce));

            Assert.IsTrue(BoomGeneralized.IsGun(BoomGeneralized.Trigger.GunMany));
            Assert.IsFalse(BoomGeneralized.IsGun(BoomGeneralized.Trigger.PushMany));

            Assert.IsTrue(BoomGeneralized.IsManual(BoomGeneralized.Trigger.SwitchOnce));
            Assert.IsTrue(BoomGeneralized.IsManual(BoomGeneralized.Trigger.PushMany));
            Assert.IsFalse(BoomGeneralized.IsManual(BoomGeneralized.Trigger.WalkOnce));

            // Only the "many" variants repeat.
            Assert.IsFalse(BoomGeneralized.IsRepeatable(BoomGeneralized.Trigger.WalkOnce));
            Assert.IsTrue(BoomGeneralized.IsRepeatable(BoomGeneralized.Trigger.WalkMany));
            Assert.IsFalse(BoomGeneralized.IsRepeatable(BoomGeneralized.Trigger.GunOnce));
            Assert.IsTrue(BoomGeneralized.IsRepeatable(BoomGeneralized.Trigger.SwitchMany));
        }

        [TestMethod]
        public void FloorFieldsDecodeCorrectly()
        {
            // Build a floor type by hand: walk-once trigger, turbo speed, up,
            // crush, target "own ceiling", texture change to zero.
            var special = BoomGeneralized.GenFloorBase
                          | (1 << 12)   // FloorCrush
                          | (1 << 10)   // FloorChange = 1 (change to zero)
                          | (4 << 7)    // FloorTarget = 4 (own ceiling)
                          | (1 << 6)    // FloorDirection = up
                          | (3 << 3);   // FloorSpeed = turbo

            var definition = BoomGeneralized.Decode(special);

            Assert.AreEqual(BoomGeneralized.Family.Floor, definition.Family);
            Assert.AreEqual(BoomGeneralized.Trigger.WalkOnce, definition.Trigger);
            Assert.IsTrue(definition.Crush, "the crush bit must be read");
            Assert.AreEqual(1, definition.Change, "the change field must be read");
            Assert.AreEqual(4, definition.Target, "the target field must be read");
            Assert.IsTrue(definition.Up, "the direction bit must be read");
            Assert.AreEqual(BoomGeneralized.Speed.Turbo, definition.Speed);
        }

        [TestMethod]
        public void DirectionAndSpeedAreIndependent()
        {
            // Down at slow speed, no flags.
            var down = BoomGeneralized.Decode(BoomGeneralized.GenFloorBase);
            Assert.IsFalse(down.Up, "the direction bit clear means down");
            Assert.AreEqual(BoomGeneralized.Speed.Slow, down.Speed);

            // Up at normal speed.
            var up = BoomGeneralized.Decode(BoomGeneralized.GenFloorBase | (1 << 6) | (1 << 3));
            Assert.IsTrue(up.Up);
            Assert.AreEqual(BoomGeneralized.Speed.Normal, up.Speed);
        }

        [TestMethod]
        public void SpeedsMatchBoomsTables()
        {
            // Compared through .Data: Fixed.Equals throws by design in this
            // codebase, so Assert.AreEqual on two Fixed values fails for the wrong
            // reason. That trap is recorded in the project notes.
            Action<int, Fixed, string> check = (expected, actual, what) =>
                Assert.AreEqual(expected, actual.Data, what);

            // Floors and ceilings: base speed times 1, 2, 4, 8.
            check(Fixed.One.Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Floor, BoomGeneralized.Speed.Slow), "floor slow");
            check((Fixed.One * 2).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Floor, BoomGeneralized.Speed.Normal), "floor normal");
            check((Fixed.One * 4).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Floor, BoomGeneralized.Speed.Fast), "floor fast");
            check((Fixed.One * 8).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Floor, BoomGeneralized.Speed.Turbo), "floor turbo");

            // Doors: VDOORSPEED times 2, 4, 8, 16 — twice a floor at every step.
            check((Fixed.One * 2).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Door, BoomGeneralized.Speed.Slow), "door slow");
            check((Fixed.One * 16).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Door, BoomGeneralized.Speed.Turbo), "door turbo");

            // Lifts: PLATSPEED times 2, 4, 8, 16.
            check((Fixed.One * 2).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Lift, BoomGeneralized.Speed.Slow), "lift slow");

            // Stairs: quarter, half, double, quadruple.
            check((Fixed.One / 4).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Stairs, BoomGeneralized.Speed.Slow), "stairs slow");
            check((Fixed.One / 2).Data,
                BoomGeneralized.SpeedOf(BoomGeneralized.Family.Stairs, BoomGeneralized.Speed.Normal), "stairs normal");
        }

        [TestMethod]
        public void DoorFieldsDecodeCorrectly()
        {
            // Delay 2, monster-triggerable, kind 1 (open and stay), fast.
            var special = BoomGeneralized.GenDoorBase
                          | (2 << 8)   // DoorDelay
                          | (1 << 7)   // DoorMonster
                          | (1 << 5)   // DoorKind
                          | (2 << 3);  // DoorSpeed = fast

            var definition = BoomGeneralized.Decode(special);

            Assert.AreEqual(BoomGeneralized.Family.Door, definition.Family);
            Assert.AreEqual(2, definition.Delay);
            Assert.IsTrue(definition.MonstersCanTrigger);
            Assert.AreEqual(1, definition.DoorKind);
            Assert.AreEqual(BoomGeneralized.Speed.Fast, definition.Speed);
        }

        [TestMethod]
        public void LiftFieldsDecodeCorrectly()
        {
            var special = BoomGeneralized.GenLiftBase
                          | (1 << 8)   // LiftTarget
                          | (3 << 6)   // LiftDelay
                          | (1 << 5)   // LiftMonster
                          | (2 << 3);  // LiftSpeed

            var definition = BoomGeneralized.Decode(special);

            Assert.AreEqual(BoomGeneralized.Family.Lift, definition.Family);
            Assert.AreEqual(1, definition.Target);
            Assert.AreEqual(3, definition.Delay);
            Assert.IsTrue(definition.MonstersCanTrigger);
            Assert.AreEqual(BoomGeneralized.Speed.Fast, definition.Speed);
        }

        [TestMethod]
        public void LockedDoorFieldsDecodeCorrectly()
        {
            // One key, red card (1), fast.
            var special = BoomGeneralized.GenLockedBase
                          | (1 << 9)   // LockedNKeys
                          | (1 << 6)   // LockedKey = red card
                          | (2 << 3);  // LockedSpeed

            var definition = BoomGeneralized.Decode(special);

            Assert.AreEqual(BoomGeneralized.Family.LockedDoor, definition.Family);
            Assert.IsTrue(definition.SkullsCountAsCards, "the bit means skulls count as cards");
            Assert.AreEqual(1, definition.LockedKey);
            Assert.AreEqual(BoomGeneralized.Speed.Fast, definition.Speed);
        }

        [TestMethod]
        public void TheSkullBitIsABitAndNotACount()
        {
            // Boom's macro is called LockedNKeys, which reads like a number of keys. It is not:
            // it is a single bit meaning "a skull opens a card lock". Reading it as a count
            // would make a one-key lock look like it wants one key of something.
            var without = BoomGeneralized.Decode(BoomGeneralized.GenLockedBase | (2 << 6));
            var with = BoomGeneralized.Decode(BoomGeneralized.GenLockedBase | (1 << 9) | (2 << 6));

            Assert.IsFalse(without.SkullsCountAsCards);
            Assert.IsTrue(with.SkullsCountAsCards);
            Assert.AreEqual(2, without.LockedKey, "and the key itself is unchanged");
            Assert.AreEqual(2, with.LockedKey);
        }

        [TestMethod]
        public void CrusherFieldsDecodeCorrectly()
        {
            var silent = BoomGeneralized.Decode(BoomGeneralized.GenCrusherBase | (1 << 6) | (3 << 3));

            Assert.AreEqual(BoomGeneralized.Family.Crusher, silent.Family);
            Assert.IsTrue(silent.Silent);
            Assert.AreEqual(BoomGeneralized.Speed.Turbo, silent.Speed);
        }

        [TestMethod]
        public void StairsStepComesFromBoomsTable()
        {
            // Boom's table, from EV_DoGenStairs: the two-bit field means 4, 8, 16, 24 units.
            // The previous version of this decoder mapped selector 0 to eight and everything else
            // to sixteen, so two of the four values built the wrong staircase.
            Assert.AreEqual(4, BoomGeneralized.Decode(BoomGeneralized.GenStairsBase).StairStep,
                "selector 0");

            Assert.AreEqual(8, BoomGeneralized.Decode(
                BoomGeneralized.GenStairsBase | (1 << 6)).StairStep, "selector 1");

            Assert.AreEqual(16, BoomGeneralized.Decode(
                BoomGeneralized.GenStairsBase | (2 << 6)).StairStep, "selector 2");

            Assert.AreEqual(24, BoomGeneralized.Decode(
                BoomGeneralized.GenStairsBase | (3 << 6)).StairStep, "selector 3");
        }

        [TestMethod]
        public void StairsDirectionAndTextureBitsAreRead()
        {
            var plain = BoomGeneralized.Decode(BoomGeneralized.GenStairsBase);

            Assert.IsFalse(plain.Up, "direction bit clear means down, as Boom reads it");
            Assert.IsFalse(plain.StairIgnoresTexture);

            var up = BoomGeneralized.Decode(BoomGeneralized.GenStairsBase | (1 << 8));
            Assert.IsTrue(up.Up, "the direction bit means up");

            var ignore = BoomGeneralized.Decode(BoomGeneralized.GenStairsBase | (1 << 9));
            Assert.IsTrue(ignore.StairIgnoresTexture, "and 0x200 means the texture is ignored");
        }

        [TestMethod]
        public void OrdinaryDoomSpecialsAreNotGeneralized()
        {
            // The decoder is called for every line, so it must not claim a
            // numbered Doom special.
            foreach (var special in new[] { 0, 1, 11, 31, 90, 97, 128, 999 })
            {
                var definition = BoomGeneralized.Decode(special);
                Assert.IsFalse(definition.IsGeneralized, "special " + special + " must not decode as Boom");
            }
        }
    }
}
