using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Audio;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_StartSound: CHAN_* slots, volume and attenuation, evaluated when the frame runs.
    ///
    /// A small recording ISound is used rather than a real device: the action must choose the
    /// right channel and volume, while the sample itself and the mixer already have their own
    /// tests. This also proves the action does not touch the world RNG merely to play a sound.
    /// </summary>
    [TestClass]
    public class StartSoundTest
    {
        private ActorTableSnapshot snapshot;

        [TestInitialize]
        public void Before()
        {
            snapshot = ActorTableSnapshot.Take();
        }

        [TestCleanup]
        public void After()
        {
            snapshot.Restore();
        }

        private sealed class RecordingSound : ISound
        {
            public string LastName;
            public SfxType LastType;
            public int LastVolume;
            public int Calls;

            public void SetListener(Mobj listener) { }
            public void Update() { }
            public void StartSound(Sfx sfx) { }
            public void StartSound(Mobj mobj, Sfx sfx, SfxType type) { }
            public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume) { }
            public void StartSound(Mobj mobj, string soundName, SfxType type, int volume)
            {
                LastName = soundName;
                LastType = type;
                LastVolume = volume;
                Calls++;
            }
            public void StopSound(Mobj mobj) { }
            public void Reset() { }
            public void Pause() { }
            public void Resume() { }
            public int MaxVolume { get { return 127; } }
            public int Volume { get; set; }
        }

        private static DoomGame NewGame(GameContent content, ISound sound)
        {
            var options = new GameOptions();
            options.GameVersion = content.Wad.GameVersion;
            options.GameMode = content.Wad.GameMode;
            options.MissionPack = content.Wad.MissionPack;
            options.Skill = GameSkill.Medium;
            options.Episode = 1;
            options.Map = 1;
            options.Players[0].InGame = true;
            options.Sound = sound;

            var game = new DoomGame(content, options);
            game.DeferedInitNew();

            var cmds = new TicCmd[Player.MaxPlayerCount];
            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);
            return game;
        }

        private static Mobj FirstImp(World world)
        {
            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj != null && mobj.Type == MobjType.Troop)
                {
                    return mobj;
                }
            }

            return null;
        }

        [TestMethod]
        public void WeaponChannelAndHalfVolumeAreUsed()
        {
            var sound = new RecordingSound();

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content, sound).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                world.MonsterBehavior.StartSound(
                    imp, "weapon/test", 1, 0, new Fixed(Fixed.FracUnit / 2), Fixed.One);

                Assert.AreEqual("weapon/test", sound.LastName);
                Assert.AreEqual(SfxType.Weapon, sound.LastType);
                Assert.AreEqual(50, sound.LastVolume);
            }
        }

        [TestMethod]
        public void AttenuationNoneUsesTheDiffusePathAtFullVolume()
        {
            var sound = new RecordingSound();

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content, sound).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                world.MonsterBehavior.StartSound(
                    imp, "enemy/test", 2, 0, Fixed.One, Fixed.Zero);

                Assert.AreEqual(SfxType.Diffuse, sound.LastType);
                Assert.AreEqual(100, sound.LastVolume);
            }
        }

        [TestMethod]
        public void BodyChannelUsesTheOrdinaryMiscPath()
        {
            var sound = new RecordingSound();

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content, sound).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                world.MonsterBehavior.StartSound(
                    imp, "enemy/test", 4, 0, Fixed.One, Fixed.One);

                Assert.AreEqual(SfxType.Misc, sound.LastType);
            }
        }

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR TestImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  Melee:\n" +
                "    TROO A 4 " + body + "\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void TheConverterKeepsChannelAndVolumeExpressions()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_StartSound(\"enemy/test\", CHAN_WEAPON, 0, 0.5, ATTN_NONE)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "StartSound(\"Melee#1\", \"enemy/test\", 1, 0,");
            StringAssert.Contains(script, "0.5");
            StringAssert.Contains(script, "return 0;", "ATTN_NONE becomes the mixer's zero attenuation value");
        }

        [TestMethod]
        public void LoopingAndLocalPoliciesAreReported()
        {
            var problems = new List<string>();

            Convert(
                "A_StartSound(\"enemy/test\", CHAN_VOICE, CHANF_LOOP|CHANF_OVERLAP|CHANF_LOCAL, 1, 1)",
                problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "CHANF_LOOP");
            StringAssert.Contains(text, "CHANF_OVERLAP");
            StringAssert.Contains(text, "CHANF_LOCAL");
        }

        [TestMethod]
        public void TheScriptPlaysTheSoundThroughTheBackend()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_StartSound(\"enemy/test\", CHAN_WEAPON, 0, 0.5, ATTN_NONE)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));

            var sound = new RecordingSound();

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content, sound).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MeleeState);

                Assert.AreEqual(1, sound.Calls, "the frame asked for exactly one sound");
                Assert.AreEqual("enemy/test", sound.LastName);
                Assert.AreEqual(SfxType.Diffuse, sound.LastType);
                Assert.AreEqual(50, sound.LastVolume);
            }
        }
    }
}
