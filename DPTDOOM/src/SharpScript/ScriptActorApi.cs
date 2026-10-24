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

        public ScriptActorApi(MobjInfo info)
        {
            this.info = info;
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
            RegisterSfx(host, "SeeSound", () => info.SeeSound, value => info.SeeSound = value);
            RegisterSfx(host, "AttackSound", () => info.AttackSound, value => info.AttackSound = value);
            RegisterSfx(host, "PainSound", () => info.PainSound, value => info.PainSound = value);
            RegisterSfx(host, "DeathSound", () => info.DeathSound, value => info.DeathSound = value);
            RegisterSfx(host, "ActiveSound", () => info.ActiveSound, value => info.ActiveSound = value);

            // Diagnostics: the only way a script can say something is wrong, since it
            // cannot throw. Collected by the library and reported with the line.
            host.Register("Fail", 1, call =>
            {
                throw call.Fail(call.AsText(0));
            });
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

        private static void RegisterState(ScriptHost host, string name,
                                          Func<MobjState> get, Action<MobjState> set)
        {
            host.Register(name, 0, 1, call =>
            {
                if (call.Count == 0)
                {
                    return ScriptValue.FromText(get().ToString());
                }

                var stateName = call.AsText(0);
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

        private static void RegisterSfx(ScriptHost host, string name, Func<Sfx> get, Action<Sfx> set)
        {
            host.Register(name, 0, 1, call =>
            {
                if (call.Count == 0)
                {
                    return ScriptValue.FromText(get().ToString());
                }

                var soundName = call.AsText(0);
                Sfx sound;

                if (!TryParseSfx(soundName, out sound))
                {
                    throw call.Fail("no such sound '" + soundName + "'");
                }

                set(sound);
                return ScriptValue.Void;
            });
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
