//
// Copyright (C) 2026 DartPower Team
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// The functions a script may call while describing one actor.
    ///
    /// Every function is a property of the actor being described: reading it with no
    /// argument, writing it with one. That symmetry is the whole API, and it is what
    /// lets a script say <c>Health(Health() * 2)</c> without a separate vocabulary for
    /// getters.
    ///
    /// What is deliberately absent, and why:
    ///
    ///  - randomness. A definition is evaluated once, on every machine, and must reach
    ///    the same result on all of them. The engine's RNG belongs to the world, which
    ///    does not exist yet when definitions are applied, and reaching for the system
    ///    RNG would make two players' actors differ before the first tic.
    ///
    ///  - the clock, the filesystem, anything by name. There is no path to them in the
    ///    grammar; this class would not expose them even if there were.
    ///
    ///  - states as numbers. States are given by name, because a modder knows "See" and
    ///    does not know that it is index 428 in this engine's enum.
    /// </summary>
    public sealed class ScriptActorApi
    {
        private readonly MobjInfo info;

        /// <summary>
        /// This actor's index in the table, which is what a sound override is keyed by.
        /// </summary>
        private readonly int type;

        public ScriptActorApi(MobjInfo info, int type)
        {
            this.info = info;
            this.type = type;
        }

        // ------------------------------------------------------------ states for a mod

        /// <summary>
        /// The states this actor's definition created, by the name the script gave them.
        /// </summary>
        private readonly Dictionary<string, int> customStates =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Frames whose "next" name is not resolved yet.
        ///
        /// A DECORATE chain names its successor before that successor exists - "See" points at
        /// "See2" and "See2" points back - so the links can only be made once the whole
        /// definition has run. This is what <see cref="Finish"/> is for.
        /// </summary>
        private readonly List<PendingLink> pendingLinks = new List<PendingLink>();

        private sealed class PendingLink
        {
            public int Number;
            public string NextName;
        }

        /// <summary>
        /// Whether the definition created any state of its own.
        ///
        /// Used by the library to decide whether the actor's states need finishing at all:
        /// resolving nothing is cheap, but calling it on every vanilla actor would look like
        /// work being done when none is.
        /// </summary>
        public bool HasCustomStates
        {
            get { return customStates.Count > 0; }
        }

        /// <summary>
        /// Defines one frame of a state chain.
        ///
        /// <paramref name="frame"/> is a single letter as DECORATE writes it: A to Z for the
        /// normal rotation, then [ \ ] ^ _ ` for the mirrored set. A leading "+" makes the
        /// frame full bright, which is how DECORATE spells what the engine stores as 0x8000.
        ///
        /// <paramref name="next"/> is a name given to another frame of this actor, or "" to
        /// stop. It is resolved in <see cref="Finish"/>, so it may name a frame that does not
        /// exist yet - which it always does, since a chain is a cycle.
        /// </summary>
        private void RegisterStates(ScriptHost host)
        {
            host.Register("State", 5, 6, call =>
            {
                var name = call.AsText(0);

                if (string.IsNullOrEmpty(name))
                {
                    throw call.Fail("a state needs a name");
                }

                if (customStates.ContainsKey(name))
                {
                    // Two frames with one name means the chain is ambiguous, and the second
                    // silently losing is the kind of thing a modder finds after a day.
                    throw call.Fail("the state '" + name + "' is already defined for this actor");
                }

                Sprite sprite;

                if (!TryParseSprite(call.AsText(1), out sprite))
                {
                    throw call.Fail("no such sprite '" + call.AsText(1) +
                                    "'; sprites are the four-letter WAD names such as TROO");
                }

                int frame;

                if (!TryParseFrame(call.AsText(2), out frame))
                {
                    throw call.Fail("frame '" + call.AsText(2) +
                                    "' is not one letter from A to Z or [ to `");
                }

                var tics = call.AsInt(3);

                if (tics < -1)
                {
                    throw call.Fail("tics cannot be less than -1, which means never");
                }

                Action<World, Mobj> action = null;
                var actionName = call.AsText(4);

                if (actionName.Length > 0 && !DoomInfo.TryGetAction(actionName, out action))
                {
                    // Named with the near misses listed, because "A_Jump" is a reasonable
                    // thing to write and the answer "no such action" alone does not tell a
                    // modder that this engine has no jumping.
                    throw call.Fail("no such action '" + actionName +
                                    "'; this engine has: " + string.Join(", ", ToArray(DoomInfo.ActionNameList())));
                }

                var nextName = call.Count > 5 ? call.AsText(5) : "";
                var number = DoomInfo.AllocateState();

                if (number < 0)
                {
                    throw call.Fail("no free state slots left; the reserve is " +
                                    DoomInfo.CustomStateReserve + " states per run");
                }

                DoomInfo.DefineState(number, sprite, frame, tics, action, MobjState.Null);

                customStates[name] = number;

                var link = new PendingLink();
                link.Number = number;
                link.NextName = nextName;
                pendingLinks.Add(link);

                return ScriptValue.FromInt(number);
            });
        }

        /// <summary>
        /// Resolves every "next" name and reports the ones that lead nowhere.
        ///
        /// Called once, after the actor's definition has run. A frame whose successor is
        /// missing keeps pointing at Null, which removes the mobj: the actor behaves as if it
        /// died at that frame, which is visible at once and named in the report.
        /// </summary>
        public void Finish(IList<string> problems, string source)
        {
            foreach (var link in pendingLinks)
            {
                if (string.IsNullOrEmpty(link.NextName))
                {
                    continue;
                }

                int next;

                if (customStates.TryGetValue(link.NextName, out next))
                {
                    DoomInfo.SetStateNext(link.Number, (MobjState)next);
                    continue;
                }

                // A name that is not one of this actor's own may still be a vanilla state,
                // which is how "Goto See" reaches the engine's own state from a mod's chain.
                MobjState engineState;

                if (TryParseState(link.NextName, out engineState))
                {
                    DoomInfo.SetStateNext(link.Number, engineState);
                    continue;
                }

                problems.Add(source + ": the state '" + NameOf(link.Number) +
                             "' continues to '" + link.NextName +
                             "', which is neither a state of this actor nor an engine state; " +
                             "the actor stops there");
            }

            pendingLinks.Clear();
        }

        private string NameOf(int number)
        {
            foreach (var pair in customStates)
            {
                if (pair.Value == number)
                {
                    return pair.Key;
                }
            }

            return number.ToString();
        }

        private static string[] ToArray(IList<string> list)
        {
            var array = new string[list.Count];

            for (var i = 0; i < array.Length; i++)
            {
                array[i] = list[i];
            }

            return array;
        }

        /// <summary>
        /// A sprite by its WAD name, which is what a mod knows.
        ///
        /// The engine's enum is not the same list: the shotgun guy's sprite is <c>SHTG</c> in
        /// this enumeration and <c>POSS</c> in the WAD and in DECORATE, because the enum is
        /// named after the first frame that uses it. So the name is looked up in the WAD's own
        /// table, where index and enum value agree.
        /// </summary>
        public static bool TryParseSprite(string name, out Sprite sprite)
        {
            sprite = Sprite.TROO;

            if (string.IsNullOrEmpty(name) || name.Length != 4)
            {
                return false;
            }

            var names = DoomInfo.SpriteNames;

            for (var i = 0; i < names.Length; i++)
            {
                if (!string.Equals(names[i].ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i >= Enum.GetValues(typeof(Sprite)).Length)
                {
                    return false;
                }

                sprite = (Sprite)i;
                return true;
            }

            return false;
        }

        /// <summary>
        /// One letter to a frame index, plus the full-bright bit.
        ///
        /// DECORATE's frame letters run A-Z and then [ \ ] ^ _ ` for the mirrored rotations,
        /// and the engine stores the same thing as 0..31 with 0x8000 for full bright.
        /// </summary>
        public static bool TryParseFrame(string text, out int frame)
        {
            frame = 0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var index = 0;
            var fullBright = false;

            if (text[0] == '+')
            {
                fullBright = true;
                index = 1;

                if (text.Length == 1)
                {
                    return false;
                }
            }

            if (text.Length != index + 1)
            {
                return false;
            }

            var c = text[index];

            int value;

            if (c >= 'A' && c <= 'Z')
            {
                value = c - 'A';
            }
            else if (c >= 'a' && c <= 'z')
            {
                value = c - 'a';
            }
            else if (c >= '[' && c <= '`')
            {
                value = 26 + (c - '[');
            }
            else
            {
                return false;
            }

            frame = fullBright ? value | 0x8000 : value;
            return true;
        }

        public void Register(ScriptHost host)
        {
            // Numbers, each read or written through one function.
            RegisterNumber(host, "Health", () => info.SpawnHealth, value => info.SpawnHealth = value);
            RegisterNumber(host, "Speed", () => info.Speed, value => info.Speed = value);
            RegisterNumber(host, "PainChance", () => info.PainChance, value => info.PainChance = value);
            RegisterNumber(host, "ReactionTime", () => info.ReactionTime, value => info.ReactionTime = value);
            RegisterNumber(host, "Mass", () => info.Mass, value => info.Mass = value);
            RegisterNumber(host, "Damage", () => info.Damage, value => info.Damage = value);
            RegisterNumber(host, "DoomEdNum", () => info.DoomEdNum, value => info.DoomEdNum = value);

            // Sizes are fixed-point, the engine's own type, so a script writes 1.5 and
            // means one and a half map units - not "1.5 pixels" or a converted float.
            RegisterSize(host, "Radius", () => info.Radius, value => info.Radius = value);
            RegisterSize(host, "Height", () => info.Height, value => info.Height = value);

            // States by name: See, Pain, Death, and so on. Numbers would be an
            // implementation detail of this engine, and they differ between ports.
            RegisterState(host, "SpawnState", () => info.SpawnState, value => info.SpawnState = value);
            RegisterState(host, "SeeState", () => info.SeeState, value => info.SeeState = value);
            RegisterState(host, "PainState", () => info.PainState, value => info.PainState = value);
            RegisterState(host, "MeleeState", () => info.MeleeState, value => info.MeleeState = value);
            RegisterState(host, "MissileState", () => info.MissileState, value => info.MissileState = value);
            RegisterState(host, "DeathState", () => info.DeathState, value => info.DeathState = value);
            RegisterState(host, "XdeathState", () => info.XdeathState, value => info.XdeathState = value);
            RegisterState(host, "RaiseState", () => info.RaiseState, value => info.RaiseState = value);

            // Flags: read with a name, written with a name and a value.
            host.Register("Flag", 1, 2, call =>
            {
                var name = call.AsText(0);
                MobjFlags flag;

                if (!TryParseFlag(name, out flag))
                {
                    throw call.Fail("no such flag '" + name + "'");
                }

                if (call.Count == 1)
                {
                    return ScriptValue.FromBool((info.Flags & flag) != 0);
                }

                if (call.AsBool(1))
                {
                    info.Flags |= flag;
                }
                else
                {
                    info.Flags &= ~flag;
                }

                return ScriptValue.Void;
            });

            // Sound names, so a script can replace one: "see", "pain", "death".
            RegisterSfx(host, "SeeSound", ActorSoundSlot.See,
                () => info.SeeSound, value => info.SeeSound = value);
            RegisterSfx(host, "AttackSound", ActorSoundSlot.Attack,
                () => info.AttackSound, value => info.AttackSound = value);
            RegisterSfx(host, "PainSound", ActorSoundSlot.Pain,
                () => info.PainSound, value => info.PainSound = value);
            RegisterSfx(host, "DeathSound", ActorSoundSlot.Death,
                () => info.DeathSound, value => info.DeathSound = value);
            RegisterSfx(host, "ActiveSound", ActorSoundSlot.Active,
                () => info.ActiveSound, value => info.ActiveSound = value);

            // Diagnostics: the only way a script can say something is wrong, since it
            // cannot throw. Collected by the library and reported with the line.
            host.Register("Fail", 1, call =>
            {
                throw call.Fail(call.AsText(0));
            });

            // Conversions are named rather than implicit in the narrowing direction.
            // Widening an integer to a fixed value cannot lose anything and happens by
            // itself; narrowing truncates, and the truncation should be visible in the
            // text rather than a surprise at the far end of an expression.
            host.Register("ToInt", 1, call => ScriptValue.FromInt(call.AsInt(0)));
            host.Register("ToFixed", 1, call => ScriptValue.FromFixed(call.AsFixed(0)));

            // States of the actor's own, for a mod that brings new frames.
            RegisterStates(host);
        }

        private static void RegisterNumber(ScriptHost host, string name, Func<int> get, Action<int> set)
        {
            host.Register(name, 0, 1, call =>
            {
                if (call.Count == 0)
                {
                    return ScriptValue.FromInt(get());
                }

                set(call.AsInt(0));
                return ScriptValue.Void;
            });
        }

        private static void RegisterSize(ScriptHost host, string name, Func<Fixed> get, Action<Fixed> set)
        {
            host.Register(name, 0, 1, call =>
            {
                if (call.Count == 0)
                {
                    return ScriptValue.FromFixed(get());
                }

                set(call.AsFixed(0));
                return ScriptValue.Void;
            });
        }

        private void RegisterState(ScriptHost host, string name,
                                   Func<MobjState> get, Action<MobjState> set)
        {
            host.Register(name, 0, 1, call =>
            {
                if (call.Count == 0)
                {
                    return ScriptValue.FromText(get().ToString());
                }

                var stateName = call.AsText(0);

                // A state this actor defined itself comes first: "SeeState("See")" after the
                // definition of See has to mean the new chain, and that is the whole point of
                // letting a mod describe states.
                int custom;

                if (customStates.TryGetValue(stateName, out custom))
                {
                    set((MobjState)custom);
                    return ScriptValue.Void;
                }

                MobjState state;

                if (!TryParseState(stateName, out state))
                {
                    // Named, because "428 is out of range" tells a modder nothing while
                    // "no such state 'See2'" tells them they mistyped.
                    throw call.Fail("no such state '" + stateName + "'");
                }

                set(state);
                return ScriptValue.Void;
            });
        }

        /// <summary>
        /// A property that can be read as a sound name and written as one.
        ///
        /// The write accepts two kinds of name, and the order matters:
        ///
        ///  - a name the engine's own enumeration knows (`POSIT1`) sets the actor's
        ///    <see cref="Sfx"/>, which is what the table always held;
        ///  - anything else is kept as a **named sound** for this slot, resolved through SNDINFO
        ///    when it is played. That is the DECORATE case: `SeeSound "imp/sight"` names a sound
        ///    the engine has never heard of.
        ///
        /// Setting an empty name removes the override and restores the actor's own sound, which is
        /// what makes the two forms composable: a mod may set a name, then change its mind.
        /// </summary>
        private void RegisterSfx(ScriptHost host, string name, ActorSoundSlot slot,
                                 Func<Sfx> get, Action<Sfx> set)
        {
            host.Register(name, 0, 1, call =>
            {
                if (call.Count == 0)
                {
                    // Reading gives back the name that will actually be played, so a script that
                    // reads and writes the property sees the same thing the game hears.
                    var named = ActorSounds.Get(type, slot);

                    return ScriptValue.FromText(named ?? get().ToString());
                }

                var soundName = call.AsText(0);

                if (string.IsNullOrEmpty(soundName))
                {
                    ActorSounds.Set(type, slot, null);
                    return ScriptValue.Void;
                }

                Sfx sound;

                if (TryParseSfx(soundName, out sound))
                {
                    // An engine sound: clear any named override, or it would keep winning and the
                    // write would appear to do nothing.
                    ActorSounds.Set(type, slot, null);
                    set(sound);
                    return ScriptValue.Void;
                }

                ActorSounds.Set(type, slot, soundName);
                return ScriptValue.Void;
            });
        }

        /// <summary>
        /// A state name from the engine's own enumeration, as a public lookup.
        ///
        /// Exposed so the DECORATE converter can resolve "Goto See" to vanilla: it works in
        /// names, and the engine's state is the only place a name like See has a meaning.
        /// </summary>
        public static bool TryParseEngineState(string name, out MobjState state)
        {
            return TryParseState(name, out state);
        }

        private static bool TryParseState(string name, out MobjState state)
        {
            // Enum.TryParse is case-sensitive; names in the enum are PascalCase and a
            // modder writes "See". Both spellings are accepted rather than making the
            // case of a name a trap.
            return Enum.TryParse(name, true, out state) && Enum.IsDefined(typeof(MobjState), state);
        }

        private static bool TryParseSfx(string name, out Sfx sound)
        {
            return Enum.TryParse(name, true, out sound) && Enum.IsDefined(typeof(Sfx), sound);
        }

        /// <summary>
        /// A flag name, as a public lookup.
        ///
        /// Exposed so the DECORATE converter can check the name before emitting it. Without
        /// that check one unknown flag throws while the script runs, and the throw costs the
        /// whole actor block - including states that had nothing to do with it. A modder who
        /// wrote +PAINLESS would lose the animation and be told about a script problem, which
        /// is true and useless.
        /// </summary>
        public static bool TryParseFlagName(string name, out MobjFlags flag)
        {
            return TryParseFlag(name, out flag);
        }

        private static bool TryParseFlag(string name, out MobjFlags flag)
        {
            if (!Enum.TryParse(name, true, out flag))
            {
                return false;
            }

            // Only single flags: a script that writes "Solid, Shootable" would otherwise
            // silently set both when it meant one name, and the flag names are meant to
            // be read one at a time.
            return Enum.IsDefined(typeof(MobjFlags), flag) && IsPowerOfTwo((int)flag);
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value != 0 && (value & (value - 1)) == 0;
        }
    }
}
