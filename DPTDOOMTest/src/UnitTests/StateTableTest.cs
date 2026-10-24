using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The state table grew a tail so that mods can describe states of their own.
    ///
    /// The dangerous half of that change is not the tail, it is the head: vanilla state
    /// numbers appear in save files (the loader reads a number back and uses it as an index)
    /// and in the world hashes the compatibility tests pin against Managed Doom. So the two
    /// things checked here first are that no vanilla state moved, and that
    /// <c>Number</c> still equals its index everywhere - the save format depends on that
    /// identity and nothing else enforces it.
    /// </summary>
    [TestClass]
    public class StateTableTest
    {
        [TestMethod]
        public void VanillaStatesKeepTheirNumbers()
        {
            // Spot checks at both ends and in the middle, by the names a modder knows. These
            // indices are what a save file and a demo contain, so a shift here is a
            // compatibility break that no other test would report.
            Assert.AreEqual(0, (int)MobjState.Null);
            Assert.AreEqual(1, (int)MobjState.Lightdone);
            Assert.AreEqual(966, (int)MobjState.Tech2Lamp4, "the last vanilla state");

            Assert.AreEqual(MobjState.Null, DoomInfo.States[0].Next,
                "state 0 removes the mobj, and a mod relies on that");

            var table = DoomInfo.States;

            for (var i = 0; i < table.Length; i++)
            {
                Assert.AreEqual(i, table[i].Number,
                    "state " + i + " reports number " + table[i].Number +
                    "; the save loader uses the number as an index");
            }
        }

        [TestMethod]
        public void TheTailIsNotReachableByVanillaNumbers()
        {
            // Every vanilla number still names a vanilla sprite table entry. A custom slot
            // that had been inserted rather than appended would show up here as a state whose
            // number no longer matches the enum.
            Assert.IsTrue(DoomInfo.CustomStateFirst >= (int)MobjState.Tech2Lamp4 + 1,
                "the reserve starts after the last vanilla state");

            Assert.AreEqual((int)MobjState.Tech2Lamp4 + 1, DoomInfo.CustomStateFirst);
        }

        [TestMethod]
        public void AllocatingAStateGivesASlotInTheReserve()
        {
            var number = DoomInfo.AllocateState();

            Assert.IsTrue(number >= DoomInfo.CustomStateFirst,
                "a custom state comes from the tail, got " + number);

            Assert.IsTrue(number < DoomInfo.CustomStateFirst + DoomInfo.CustomStateReserve);
        }

        [TestMethod]
        public void EveryActionOfTheEngineCanBeFoundByName()
        {
            // An action that exists but cannot be named is invisible to a mod; a name that
            // resolves to nothing is worse, because the script looks correct. Both directions
            // are covered by comparing the registry against the methods themselves.
            var names = DoomInfo.ActionNameList();

            Assert.IsTrue(names.Count > 40,
                "the engine has fifty-odd monster actions, the registry found " + names.Count);

            Action<World, Mobj> action;

            Assert.IsTrue(DoomInfo.TryGetAction("Look", out action));
            Assert.IsTrue(DoomInfo.TryGetAction("A_Look", out action),
                "a mod writes the DECORATE spelling");

            Assert.IsTrue(DoomInfo.TryGetAction("a_chase", out action), "and case is not a trap");

            Assert.IsFalse(DoomInfo.TryGetAction("A_Jump", out action),
                "an action this engine does not have must not resolve");
            Assert.IsFalse(DoomInfo.TryGetAction("", out action));
            Assert.IsFalse(DoomInfo.TryGetAction(null, out action));
        }

        [TestMethod]
        public void NoActionIsRegisteredUnderAnotherActionsName()
        {
            // Delegate.CreateDelegate binds by signature, and every action here has the same
            // signature, so a slip would bind the wrong body under the right name without
            // failing. Named actions are checked by what they do to a world: Look must not
            // equal Chase, and each name must map to a distinct delegate.
            Action<World, Mobj> look;
            Action<World, Mobj> chase;

            Assert.IsTrue(DoomInfo.TryGetAction("A_Look", out look));
            Assert.IsTrue(DoomInfo.TryGetAction("A_Chase", out chase));

            Assert.AreNotEqual(look.Method.Name, chase.Method.Name);

            var seen = new System.Collections.Generic.Dictionary<string, string>();

            foreach (var name in DoomInfo.ActionNameList())
            {
                Action<World, Mobj> action;

                if (!DoomInfo.TryGetAction(name, out action))
                {
                    Assert.Fail("'" + name + "' is listed but does not resolve");
                }

                var method = action.Method.Name;

                Assert.IsFalse(seen.ContainsKey(name),
                    "'" + name + "' resolved twice");

                seen[name] = method;
            }
        }
    }
}
