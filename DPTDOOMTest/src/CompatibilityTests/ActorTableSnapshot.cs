using System;
using System.Collections.Generic;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Saves and restores the engine's actor table around a test.
    ///
    /// The table is static and shared by the whole process, which is right for the game - there
    /// is one actor table - and a trap for tests: a test that changes an actor's health leaves it
    /// changed for every test after it, and the failure then appears in an unrelated place. That
    /// has already happened once in this project: a test set the imp to 400 health and broke a
    /// demo hash comparison three files away.
    ///
    /// Every field is copied, not just the ones a given test touches: a partial snapshot is a
    /// snapshot that silently misses the field that leaked.
    /// </summary>
    public sealed class ActorTableSnapshot
    {
        private readonly MobjInfo[] table;
        private readonly Entry[] entries;
        private readonly Dictionary<string, int> customNames;

        private sealed class Entry
        {
            public MobjState SpawnState;
            public int SpawnHealth;
            public MobjState SeeState;
            public Sfx SeeSound;
            public int ReactionTime;
            public Sfx AttackSound;
            public MobjState PainState;
            public int PainChance;
            public Sfx PainSound;
            public MobjState MeleeState;
            public MobjState MissileState;
            public MobjState DeathState;
            public MobjState XdeathState;
            public Sfx DeathSound;
            public int Speed;
            public Fixed Radius;
            public Fixed Height;
            public int Mass;
            public int Damage;
            public Sfx ActiveSound;
            public MobjFlags Flags;
            public MobjState RaiseState;
        }

        private ActorTableSnapshot(MobjInfo[] table)
        {
            this.table = table;
            customNames = ActorNames.SaveCustomTypes();
            entries = new Entry[table.Length];

            for (var i = 0; i < table.Length; i++)
            {
                var from = table[i];

                entries[i] = new Entry
                {
                    SpawnState = from.SpawnState,
                    SpawnHealth = from.SpawnHealth,
                    SeeState = from.SeeState,
                    SeeSound = from.SeeSound,
                    ReactionTime = from.ReactionTime,
                    AttackSound = from.AttackSound,
                    PainState = from.PainState,
                    PainChance = from.PainChance,
                    PainSound = from.PainSound,
                    MeleeState = from.MeleeState,
                    MissileState = from.MissileState,
                    DeathState = from.DeathState,
                    XdeathState = from.XdeathState,
                    DeathSound = from.DeathSound,
                    Speed = from.Speed,
                    Radius = from.Radius,
                    Height = from.Height,
                    Mass = from.Mass,
                    Damage = from.Damage,
                    ActiveSound = from.ActiveSound,
                    Flags = from.Flags,
                    RaiseState = from.RaiseState
                };
            }
        }

        /// <summary>Takes the snapshot. Dispose it to put the table back.</summary>
        public static ActorTableSnapshot Take()
        {
            return new ActorTableSnapshot(DoomInfo.MobjInfos);
        }

        public void Restore()
        {
            ActorNames.RestoreCustomTypes(customNames);

            for (var i = 0; i < table.Length; i++)
            {
                var to = table[i];
                var from = entries[i];

                to.SpawnState = from.SpawnState;
                to.SpawnHealth = from.SpawnHealth;
                to.SeeState = from.SeeState;
                to.SeeSound = from.SeeSound;
                to.ReactionTime = from.ReactionTime;
                to.AttackSound = from.AttackSound;
                to.PainState = from.PainState;
                to.PainChance = from.PainChance;
                to.PainSound = from.PainSound;
                to.MeleeState = from.MeleeState;
                to.MissileState = from.MissileState;
                to.DeathState = from.DeathState;
                to.XdeathState = from.XdeathState;
                to.DeathSound = from.DeathSound;
                to.Speed = from.Speed;
                to.Radius = from.Radius;
                to.Height = from.Height;
                to.Mass = from.Mass;
                to.Damage = from.Damage;
                to.ActiveSound = from.ActiveSound;
                to.Flags = from.Flags;
                to.RaiseState = from.RaiseState;
            }
        }
    }
}
