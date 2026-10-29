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
        /// <summary>
        /// The compiled script this actor was described by, kept so a conditional jump can be
        /// answered later.
        ///
        /// A condition is a question about the actor at the moment the frame ends, which is long
        /// after the definition has run - so the condition has to keep the unit and evaluate on
        /// demand. Null when the caller had no unit to give, in which case a condition is refused
        /// rather than half-supported.
        /// </summary>
        private readonly ScriptUnit unit;

        private readonly MobjInfo info;

        /// <summary>
        /// This actor's index in the table, which is what a sound override is keyed by.
        /// </summary>
        private readonly int type;

        public ScriptActorApi(MobjInfo info, int type, ScriptUnit unit)
        {
            this.info = info;
            this.type = type;
            this.unit = unit;
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

            /// <summary>Jump target by name, or null. Resolved with the successor.</summary>
            public string JumpName;

            /// <summary>Chance out of 256; 256 means always.</summary>
            public int JumpChance;

            /// <summary>
            /// Function that decides the jump instead of the chance, or null.
            ///
            /// A name rather than the compiled thing, because the function is checked and wrapped
            /// in <see cref="Finish"/> along with every other link.
            /// </summary>
            public string JumpConditionName;
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
            host.Register("State", 5, 9, call =>
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

                // Optional 7th and 8th arguments: a jump target and its chance out of 256. Absent
                // means "no jump", which is what every frame without A_Jump gets.
                if (call.Count > 6)
                {
                    link.JumpName = call.AsText(6);

                    // A 9th argument names a script function instead of a chance: A_JumpIf asks a
                    // question rather than rolling a die. The two are exclusive because they are
                    // different instructions, and the state table enforces that.
                    if (call.Count > 8)
                    {
                        link.JumpConditionName = call.AsText(8);
                    }
                    else
                    {
                        link.JumpChance = call.Count > 7 ? call.AsInt(7) : 256;
                    }
                }

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
                if (!string.IsNullOrEmpty(link.NextName))
                {
                    int next;

                    if (ResolveState(link.NextName, out next))
                    {
                        DoomInfo.SetStateNext(link.Number, (MobjState)next);
                    }
                    else
                    {
                        problems.Add(source + ": the state '" + NameOf(link.Number) +
                                     "' continues to '" + link.NextName +
                                     "', which is neither a state of this actor nor an engine state; " +
                                     "the actor stops there");
                    }
                }

                if (!string.IsNullOrEmpty(link.JumpName))
                {
                    int target;

                    if (ResolveState(link.JumpName, out target))
                    {
                        if (!string.IsNullOrEmpty(link.JumpConditionName))
                        {
                            ApplyCondition(link, target, problems, source);
                        }
                        else
                        {
                            DoomInfo.SetStateJump(link.Number, target, link.JumpChance);
                        }
                    }
                    else
                    {
                        // The frame is kept without its jump rather than dropped: the animation is
                        // still what the author wrote, and only the jump is missing.
                        problems.Add(source + ": the state '" + NameOf(link.Number) +
                                     "' jumps to '" + link.JumpName +
                                     "', which is neither a state of this actor nor an engine state; " +
                                     "the jump is dropped");
                    }
                }
            }

            pendingLinks.Clear();
        }

        /// <summary>
        /// A state name as a number: this actor's own first, then the engine's.
        ///
        /// The order matters. A mod that calls its own chain "See" must jump to its own, not to the
        /// engine's, or the whole point of replacing a chain would be lost.
        /// </summary>
        private bool ResolveState(string name, out int number)
        {
            if (customStates.TryGetValue(name, out number))
            {
                return true;
            }

            // A name that is not one of this actor's own may still be a vanilla state, which is
            // how "Goto See" reaches the engine's own state from a mod's chain.
            MobjState engineState;

            if (TryParseState(name, out engineState))
            {
                number = (int)engineState;
                return true;
            }

            number = -1;
            return false;
        }

        /// <summary>
        /// Wraps a named function into a condition the state can ask.
        ///
        /// The check that the function exists happens here rather than at play time, because a
        /// missing name is a modder's typo and the load report is where typos are read. At play time
        /// the delegate catches everything anyway - see <see cref="ScriptCondition.Evaluate"/>.
        /// </summary>
        /// <summary>
        /// The state number a frame name refers to, or a refusal.
        ///
        /// Shared by the calls that attach work to a frame - a sound, a projectile, an item - so
        /// that "no such frame" is worded the same way in each and a modder reads one rule rather
        /// than three.
        /// </summary>
        /// <summary>Rejects an AAPTR value this engine cannot resolve, at load time.</summary>
        private static void CheckActionPointer(ScriptCall call, int pointer)
        {
            if (!ActorPointer.IsSupported(pointer))
            {
                throw call.Fail("this AAPTR pointer is not supported here; target and tracer " +
                                "are supported, master and player/line pointers are not");
            }
        }

        /// <summary>
        /// Resolves one of the expression functions generated for A_SpawnItemEx.
        /// </summary>
        private ScriptCondition ResolveSpawnExpression(ScriptCall call, int index)
        {
            var name = call.AsText(index);

            if (unit == null)
            {
                throw call.Fail("A_SpawnItemEx needs the script unit that holds its expressions");
            }

            if (unit.Find(name) == null)
            {
                throw call.Fail("no such spawn expression '" + name + "'");
            }

            return new ScriptCondition(unit, name, "A_SpawnItemEx");
        }

        private ScriptCondition ResolveOptionalSpawnExpression(ScriptCall call, int index)
        {
            if (call.AsText(index).Length == 0)
            {
                return null;
            }

            return ResolveSpawnExpression(call, index);
        }

        /// <summary>
        /// Converts a fixed-point degree value without a floating-point round trip.
        ///
        /// A mod can write 22.5 degrees. Doing this through double would make the result depend on
        /// the platform's rounding, while the rest of the spawn arithmetic is fixed point.
        /// </summary>
        private static Angle AngleFromFixedDegrees(Fixed degrees)
        {
            const long fullTurn = 0x100000000L;
            var numerator = (long)degrees.Data * fullTurn;
            var denominator = 360L * Fixed.FracUnit;

            if (numerator >= 0)
            {
                numerator += denominator / 2;
            }
            else
            {
                numerator -= denominator / 2;
            }

            return new Angle((uint)(numerator / denominator));
        }

        /// <summary>
        /// One of A_Chase's two state arguments: -1 for "the actor's own", otherwise the state.
        ///
        /// An empty string is how the converter writes "use the actor's own", because a script has
        /// no null literal and -1 would read as a state number.
        /// </summary>
        private int ResolveChaseState(ScriptCall call, int index)
        {
            var name = call.AsText(index);

            if (name.Length == 0)
            {
                return -1;
            }

            int state;

            if (!ResolveState(name, out state))
            {
                throw call.Fail("no such state '" + name +
                                "' for 'A_Chase'; it is neither a state of this actor nor one of " +
                                "the engine's");
            }

            return state;
        }

        private int FrameNumber(string frameName, ScriptCall call)
        {
            int number;

            if (!customStates.TryGetValue(frameName, out number))
            {
                throw call.Fail("no such frame '" + frameName +
                                "'; only a frame this actor defined can carry work like this");
            }

            return number;
        }

        private void ApplyCondition(PendingLink link, int target, IList<string> problems,
                                    string source)
        {
            if (unit == null)
            {
                problems.Add(source + ": the state '" + NameOf(link.Number) +
                             "' has a condition, but this actor was described without a compiled " +
                             "script to ask; the jump is dropped");
                return;
            }

            var function = unit.Find(link.JumpConditionName);

            if (function == null)
            {
                problems.Add(source + ": the state '" + NameOf(link.Number) +
                             "' asks '" + link.JumpConditionName +
                             "', which is not a function in this script; the jump is dropped");
                return;
            }

            if (function.Parameters.Count != 0)
            {
                // The condition is asked with no arguments: everything it needs it reads from the
                // actor through its own host. A function that wants arguments is not a question
                // about this actor, and guessing which actor to pass would be a guess.
                problems.Add(source + ": the condition '" + link.JumpConditionName +
                             "' takes " + function.Parameters.Count +
                             " parameter(s), but a condition is asked with none; the jump is dropped");
                return;
            }

            var condition = new ScriptCondition(unit, link.JumpConditionName, source);

            DoomInfo.SetStateCondition(link.Number, target, condition.Evaluate);
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

            // The sound one frame plays, attached after the frame exists. DECORATE's A_PlaySound:
            // the name is a sound name from SNDINFO, and the volume is a percentage.
            host.Register("PlaySound", 2, 3, call =>
            {
                var frameName = call.AsText(0);
                var soundName = call.AsText(1);
                var volume = call.Count > 2 ? call.AsInt(2) : 100;

                var number = FrameNumber(frameName, call);

                if (volume < 0)
                {
                    volume = 0;
                }
                else if (volume > 100)
                {
                    volume = 100;
                }

                // The name is captured, not looked up now: the sound system is built after the
                // content is read, and SNDINFO may be read after this script runs.
                var captured = soundName;
                var capturedVolume = volume;

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.StartSound(mobj, captured, SfxType.Misc, capturedVolume));

                return ScriptValue.Void;
            });

            // A_SetArg(index, value): one of the actor's own five numbers. The value is an
            // expression, because ademo1.wad writes floorz - ceilingz and random(190,250) * 8 in it.
            host.Register("SetArg", 3, 3, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var index = call.AsInt(1);
                var value = ResolveSpawnExpression(call, 2);

                if (index < 0 || index >= Mobj.ArgumentCount)
                {
                    throw call.Fail("arg " + index + " does not exist; an actor has " +
                                    Mobj.ArgumentCount + " (args[0] to args[" +
                                    (Mobj.ArgumentCount - 1) + "])");
                }

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    mobj.SetArg(index, value.EvaluateValue(world, mobj).AsInt()));

                return ScriptValue.Void;
            });

            // A_GiveInventory and A_TakeInventory: named counters on one actor.
            //
            // The amount is an expression, because ademo1.wad writes random(1,3) in it, and it is
            // evaluated when the frame runs - the same rule as every other argument here.
            host.Register("GiveItem", 4, 4, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var name = call.AsText(1);
                var amount = ResolveSpawnExpression(call, 2);
                var pointer = call.AsInt(3);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    var receiver = ActorPointer.Resolve(mobj, pointer);

                    if (receiver == null)
                    {
                        return;
                    }

                    receiver.GiveItem(name, amount.EvaluateValue(world, mobj).AsInt());
                });

                return ScriptValue.Void;
            });

            host.Register("TakeItem", 4, 4, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var name = call.AsText(1);
                var amount = ResolveSpawnExpression(call, 2);
                var pointer = call.AsInt(3);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    var receiver = ActorPointer.Resolve(mobj, pointer);

                    if (receiver == null)
                    {
                        return;
                    }

                    receiver.TakeItem(name, amount.EvaluateValue(world, mobj).AsInt());
                });

                return ScriptValue.Void;
            });

            // A_FaceTracer: turn toward the tracer, which is a different actor from the target and
            // must stay one - retargeting the monster to face it would change what it attacks.
            host.Register("FaceTracer", 3, 3, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var maxTurn = AngleFromFixedDegrees(Fixed.FromInt(call.AsInt(1)));
                var offset = AngleFromFixedDegrees(Fixed.FromInt(call.AsInt(2)));

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.FaceTarget(mobj, mobj.Tracer, maxTurn, offset));

                return ScriptValue.Void;
            });

            // A_MonsterRefire: a *keep going* chance plus a jump when the target is gone, dead or
            // hidden. The label is resolved here, while the actor's own chains are known.
            host.Register("MonsterRefire", 3, 3, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var chance = call.AsInt(1);
                var label = call.AsText(2);

                int target;

                if (!ResolveState(label, out target))
                {
                    throw call.Fail("no such state '" + label +
                                    "' for 'A_MonsterRefire'; it is neither a state of this actor " +
                                    "nor one of the engine's");
                }

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.MonsterRefire(mobj, chance, target));

                return ScriptValue.Void;
            });

            // A_AlertMonsters is simulation, not audible sound: it marks sectors so their monsters
            // acquire the selected target on a later look. Keep it on the ordinary noise path.
            host.Register("AlertMonsters", 2, 2, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var flags = call.AsInt(1);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.AlertMonsters(mobj, flags));

                return ScriptValue.Void;
            });

            // A projectile the frame fires. DECORATE's A_CustomMissile names it, so the type is
            // resolved here, when the file is read, and closed over: at play time there is no name
            // to look up and no way to report a typo.
            host.Register("Missile", 2, 3, call =>
            {
                var frameName = call.AsText(0);
                var actorName = call.AsText(1);

                var number = FrameNumber(frameName, call);

                var type = ActorNames.TypeOf(actorName);

                if (type < 0)
                {
                    throw call.Fail("no such actor '" + actorName +
                                    "'; this engine knows the built-in actors by their ZDoom names");
                }

                var height = call.Count > 2 ? call.AsFixed(2) : Fixed.FromInt(32);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.ThingAllocation.SpawnScriptMissile(
                        mobj, mobj.Target, (MobjType)type, height));

                return ScriptValue.Void;
            });

            // Something the frame places in the world: DECORATE's A_SpawnItem. Not aimed and not
            // moving, which is the difference from Missile and the reason they are two calls.
            host.Register("SpawnItem", 2, 3, call =>
            {
                var frameName = call.AsText(0);
                var actorName = call.AsText(1);

                var number = FrameNumber(frameName, call);

                var type = ActorNames.TypeOf(actorName);

                if (type < 0)
                {
                    throw call.Fail("no such actor '" + actorName +
                                    "'; this engine knows the built-in actors by their ZDoom names");
                }

                var distance = call.Count > 2 ? call.AsFixed(2) : Fixed.Zero;

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    var x = mobj.X + (distance * Trig.Cos(mobj.Angle));
                    var y = mobj.Y + (distance * Trig.Sin(mobj.Angle));

                    world.ThingAllocation.SpawnMobj(x, y, mobj.Z + mobj.Height, (MobjType)type);
                });

                return ScriptValue.Void;
            });

            // A_CustomPunch: the damage and range are expressions, the puff is an actor name.
            host.Register("CustomPunch", 8, 8, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var damage = ResolveSpawnExpression(call, 1);
                var noRandom = call.AsBool(2);
                var flags = call.AsInt(3);
                var puffName = call.AsText(4);
                var range = ResolveSpawnExpression(call, 5);
                var hitSound = call.AsText(6);
                var missSound = call.AsText(7);

                var type = ActorNames.TypeOf(puffName);

                if (type < 0)
                {
                    throw call.Fail("no such actor '" + puffName + "' for A_CustomPunch");
                }

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.CustomPunch(
                        mobj, damage.EvaluateValue(world, mobj).AsInt(), noRandom, flags,
                        (MobjType)type, range.EvaluateValue(world, mobj).AsFixed(),
                        hitSound, missSound));

                return ScriptValue.Void;
            });

            // A_CustomMeleeAttack: damage is an expression, because mods write random(20,24)
            // rather than a fixed number. Sounds are names resolved at play time through SNDINFO.
            host.Register("CustomMeleeAttack", 4, 4, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var damage = ResolveSpawnExpression(call, 1);
                var hitSound = call.AsText(2);
                var missSound = call.AsText(3);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.CustomMeleeAttack(
                        mobj, damage.EvaluateValue(world, mobj).AsInt(), hitSound, missSound));

                return ScriptValue.Void;
            });

            // A_SpawnItemEx: the seven numeric arguments are function names, not values. Each
            // function is evaluated when the frame runs, because it may contain random().
            host.Register("SpawnItemEx", 11, 11, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var actorName = call.AsText(1);

                var type = ActorNames.TypeOf(actorName);

                if (type < 0)
                {
                    throw call.Fail("no such actor '" + actorName +
                                    "'; this engine knows the built-in actors by their ZDoom names");
                }

                var expressions = new ScriptCondition[7];

                for (var i = 0; i < expressions.Length; i++)
                {
                    expressions[i] = ResolveSpawnExpression(call, i + 2);
                }

                var failChance = ResolveOptionalSpawnExpression(call, 10);
                var flags = call.AsInt(9);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    // ZDoom tests failchance before doing any of the spawn arithmetic. In
                    // particular, a failed spawn must not evaluate the other random expressions.
                    if (failChance != null)
                    {
                        var chance = failChance.EvaluateValue(world, mobj).AsInt();

                        if (chance > 0 && world.Random.Next() < chance)
                        {
                            return;
                        }
                    }

                    var parameters = new SpawnItemExParams();
                    parameters.Type = (MobjType)type;
                    parameters.XOffset = expressions[0].EvaluateValue(world, mobj).AsFixed();
                    parameters.YOffset = expressions[1].EvaluateValue(world, mobj).AsFixed();
                    parameters.ZOffset = expressions[2].EvaluateValue(world, mobj).AsFixed();
                    parameters.XVelocity = expressions[3].EvaluateValue(world, mobj).AsFixed();
                    parameters.YVelocity = expressions[4].EvaluateValue(world, mobj).AsFixed();
                    parameters.ZVelocity = expressions[5].EvaluateValue(world, mobj).AsFixed();
                    parameters.Angle = AngleFromFixedDegrees(expressions[6]
                        .EvaluateValue(world, mobj).AsFixed());
                    parameters.Flags = flags;

                    world.ThingAllocation.SpawnItemEx(mobj, parameters);
                });

                return ScriptValue.Void;
            });

            // A_NoBlocking: clear SOLID. DropItem metadata has no counterpart and is reported by
            // the property parser, not hidden in this action.
            host.Register("NoBlocking", 2, 2, call =>
            {
                var number = FrameNumber(call.AsText(0), call);

                // The drop argument is read by the converter for compatibility, but there is no
                // metadata list to drop from this engine.
                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.NoBlocking(mobj));

                return ScriptValue.Void;
            });

            // A_SetHealth: health is evaluated on the frame, not folded into a load-time constant.
            host.Register("SetHealth", 3, 3, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var health = ResolveSpawnExpression(call, 1);
                var pointer = call.AsInt(2);

                CheckActionPointer(call, pointer);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    var target = ActorPointer.Resolve(mobj, pointer);

                    if (target != null)
                    {
                        world.MonsterBehavior.SetHealth(
                            target, health.EvaluateValue(world, mobj).AsInt());
                    }
                });

                return ScriptValue.Void;
            });

            // A_SetAngle: the angle is a fixed-point degree expression.
            host.Register("SetAngle", 4, 4, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var angle = ResolveSpawnExpression(call, 1);
                var flags = call.AsInt(2);
                var pointer = call.AsInt(3);

                CheckActionPointer(call, pointer);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    var target = ActorPointer.Resolve(mobj, pointer);

                    if (target != null)
                    {
                        // Interpolation flags affect presentation, not the simulation's angle.
                        world.MonsterBehavior.SetAngle(
                            target, AngleFromFixedDegrees(angle.EvaluateValue(world, mobj).AsFixed()));
                    }
                });

                return ScriptValue.Void;
            });

            // A_ChangeVelocity: x/y/z are expressions; CVF_RELATIVE and CVF_REPLACE are honoured.
            host.Register("ChangeVelocity", 6, 6, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var x = ResolveSpawnExpression(call, 1);
                var y = ResolveSpawnExpression(call, 2);
                var z = ResolveSpawnExpression(call, 3);
                var flags = call.AsInt(4);
                var pointer = call.AsInt(5);

                CheckActionPointer(call, pointer);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    var target = ActorPointer.Resolve(mobj, pointer);

                    if (target != null)
                    {
                        world.MonsterBehavior.ChangeVelocity(
                            target,
                            x.EvaluateValue(world, mobj).AsFixed(),
                            y.EvaluateValue(world, mobj).AsFixed(),
                            z.EvaluateValue(world, mobj).AsFixed(),
                            flags);
                    }
                });

                return ScriptValue.Void;
            });

            // A_ScaleVelocity: one factor applied to all three components.
            host.Register("ScaleVelocity", 3, 3, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var scale = ResolveSpawnExpression(call, 1);
                var pointer = call.AsInt(2);

                CheckActionPointer(call, pointer);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    var target = ActorPointer.Resolve(mobj, pointer);

                    if (target != null)
                    {
                        world.MonsterBehavior.ScaleVelocity(
                            target, scale.EvaluateValue(world, mobj).AsFixed());
                    }
                });

                return ScriptValue.Void;
            });

            // A_StartSound: a named sound with a CHAN_* slot, flags, volume and attenuation.
            host.Register("StartSound", 6, 6, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var soundName = call.AsText(1);
                var slot = call.AsInt(2);
                var flags = call.AsInt(3);
                var volume = ResolveSpawnExpression(call, 4);
                var attenuation = ResolveSpawnExpression(call, 5);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.StartSound(
                        mobj, soundName, slot, flags,
                        volume.EvaluateValue(world, mobj).AsFixed(),
                        attenuation.EvaluateValue(world, mobj).AsFixed()));

                return ScriptValue.Void;
            });

            // The two projectile actions have different flag sets and different defaults. Keeping
            // their script entry points separate prevents FPF_TRANSFERTRANSLATION (2) from being
            // mistaken for CMF_AIMDIRECTION (2), which has the same numeric value.
            RegisterProjectile(host, false);
            RegisterProjectile(host, true);
            RegisterGrenade(host);

            // A flag the frame sets or clears. DECORATE's A_ChangeFlag names a flag, so the name is
            // resolved here, when the file is read: a typo must reach the report, not disappear into
            // a frame that quietly changes nothing.
            host.Register("ChangeFlag", 3, 3, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var name = call.AsText(1);
                var value = call.AsBool(2);

                MobjFlags flag;

                if (!TryParseFlagName(name, out flag))
                {
                    throw call.Fail("no such flag '" + name +
                                    "'; this engine's flags are the vanilla set plus the ones " +
                                    "DECORATE needed");
                }

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    if (value)
                    {
                        mobj.Flags |= flag;
                    }
                    else
                    {
                        mobj.Flags &= ~flag;
                    }
                });

                return ScriptValue.Void;
            });

            // A_SetTics: how long this frame lasts. It writes the actor's own tic counter, which is
            // what the state loop reads on the next pass - so the frame that is running keeps
            // whatever it had, and the change takes effect from here on.
            host.Register("SetTics", 2, 2, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var tics = call.AsInt(1);

                if (tics < -1)
                {
                    throw call.Fail("A_SetTics cannot set fewer than -1 tics, which means never");
                }

                DoomInfo.SetStateAction(number, (world, mobj) => mobj.Tics = tics);

                return ScriptValue.Void;
            });

            // A_Recoil: a push backwards along the actor's own facing.
            host.Register("Recoil", 2, 2, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var distance = call.AsFixed(1);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.Recoil(mobj, distance));

                return ScriptValue.Void;
            });

            // A_FaceTarget with ZDoom's arguments: the first is a limit on how far the actor may
            // turn, not an offset. A mod writing A_FaceTarget(90) wants a gradual turn.
            host.Register("FaceTarget", 3, 3, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var maxTurn = Angle.FromDegree(call.AsInt(1));
                var offset = Angle.FromDegree(call.AsInt(2));

                if (maxTurn.Data < 0)
                {
                    throw call.Fail("A_FaceTarget's turn limit cannot be negative");
                }

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.FaceTarget(mobj, maxTurn, offset));

                return ScriptValue.Void;
            });

            // A_Explode: a blast around this actor.
            //
            // The flag argument is the one that decides the most: XF_HURTSOURCE is ZDoom's default
            // and is the opposite of what a rocket does, so a mod writing A_Explode() means "this
            // thing hurts whoever caused it" - which is what a barrel does.
            host.Register("Explode", 4, 4, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var damage = call.AsInt(1);
                var radius = call.AsInt(2);
                var flags = call.AsInt(3);

                var hurtSource = (flags & ExplosionFlags.HurtSource) != 0;

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.RadiusExplode(mobj, damage, radius, hurtSource));

                return ScriptValue.Void;
            });

            // A_Chase with its arguments.
            //
            // The argument order is the one that trips people up, so it is written out here and in
            // the docs: ZDoom's signature is A_Chase(melee, missile, flags), which makes flags the
            // **third** argument. A_Chase(CHF_DONTMOVE) is not a turret - it is a state label of 16,
            // and ZDoom would look for a state of that name.
            host.Register("Chase", 4, 4, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var flags = call.AsInt(3);

                var melee = ResolveChaseState(call, 1);
                var missile = ResolveChaseState(call, 2);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.Chase(mobj, flags, melee, missile));

                return ScriptValue.Void;
            });

            // A_LookEx: the wake-up test with a window, a cone and a choice of state.
            //
            // One call with the parameters written out, rather than six small setters: the
            // parameters only mean anything together, and a frame that set the cone but not the
            // distance would otherwise look like it had asked for both.
            host.Register("LookEx", 6, 7, call =>
            {
                var number = FrameNumber(call.AsText(0), call);

                var parameters = new LookExParams();

                parameters.Flags = call.AsInt(1);
                parameters.MinSeeDist = call.AsFixed(2);
                parameters.MaxSeeDist = call.AsFixed(3);
                parameters.MaxHearDist = call.AsFixed(4);

                // DECORATE writes the cone in degrees and means "half a circle" by zero, which is
                // also what plain A_Look uses. Converting here, once, keeps the per-tic check down
                // to one comparison and one subtraction.
                var fov = call.AsInt(5);

                parameters.Fov = fov <= 0
                    ? Angle.Ang180
                    : Angle.FromDegree(fov > 360 ? 360 : fov);

                if (call.Count > 6 && call.AsText(6).Length > 0)
                {
                    var name = call.AsText(6);

                    int state;

                    if (!ResolveState(name, out state))
                    {
                        throw call.Fail("no such state '" + name +
                                        "' for 'A_LookEx'; it is neither a state of this actor " +
                                        "nor one of the engine's");
                    }

                    parameters.SeeState = (MobjState)state;
                }

                DoomInfo.SetStateAction(number, (world, mobj) =>
                    world.MonsterBehavior.LookEx(mobj, parameters));

                return ScriptValue.Void;
            });

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

        /// <summary>
        /// Registers A_SpawnProjectile or A_FireProjectile. Numeric arguments are functions so
        /// random() is evaluated by the world when the frame runs, not while content is loaded.
        /// </summary>
        private void RegisterProjectile(ScriptHost host, bool fire)
        {
            host.Register(fire ? "FireProjectile" : "SpawnProjectile", 7, 7, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var actorName = call.AsText(1);
                var type = ActorNames.TypeOf(actorName);

                if (type < 0)
                {
                    throw call.Fail("no such projectile actor '" + actorName + "'");
                }

                var height = ResolveSpawnExpression(call, 2);
                var side = ResolveSpawnExpression(call, 3);
                var angle = ResolveSpawnExpression(call, 4);
                var flags = call.AsInt(5);
                var pointer = fire ? ActorPointer.Null : call.AsInt(6);
                var useAmmo = fire && call.AsBool(6);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    Fixed heightValue;
                    Fixed sideValue;
                    Fixed angleValue;

                    // Evaluate in the order of the original DECORATE signature. Besides making
                    // random() reproducible, this keeps its world-RNG consumption predictable.
                    if (fire)
                    {
                        angleValue = angle.EvaluateValue(world, mobj).AsFixed();
                        sideValue = side.EvaluateValue(world, mobj).AsFixed();
                        heightValue = height.EvaluateValue(world, mobj).AsFixed();

                        if (mobj.Player == null)
                        {
                            return;
                        }

                        if (useAmmo && !world.WeaponBehavior.TryUseCustomProjectileAmmo(mobj.Player))
                        {
                            return;
                        }

                        world.ThingAllocation.FireCustomProjectile(
                            mobj, (MobjType)type, sideValue, heightValue,
                            AngleFromFixedDegrees(angleValue),
                            (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
                        return;
                    }

                    heightValue = height.EvaluateValue(world, mobj).AsFixed();
                    sideValue = side.EvaluateValue(world, mobj).AsFixed();
                    angleValue = angle.EvaluateValue(world, mobj).AsFixed();

                    var target = ActorPointer.Resolve(mobj, pointer);
                    var aimDirection = (flags & 3) == 2;

                    if (aimDirection)
                    {
                        target = null;
                    }
                    else if (target == null)
                    {
                        // Unlike A_CustomMissile, A_SpawnProjectile does not fire without its
                        // reference target unless CMF_AIMDIRECTION explicitly asks for direction.
                        return;
                    }

                    world.ThingAllocation.SpawnCustomProjectile(
                        mobj, (MobjType)type, sideValue, heightValue,
                        AngleFromFixedDegrees(angleValue), (flags & 128) != 0,
                        target, false);
                });

                return ScriptValue.Void;
            });
        }

        /// <summary>Registers A_ThrowGrenade with its three live numeric expressions.</summary>
        private void RegisterGrenade(ScriptHost host)
        {
            host.Register("ThrowGrenade", 6, 6, call =>
            {
                var number = FrameNumber(call.AsText(0), call);
                var actorName = call.AsText(1);
                var type = ActorNames.TypeOf(actorName);

                if (type < 0)
                {
                    throw call.Fail("no such grenade actor '" + actorName + "'");
                }

                var height = ResolveSpawnExpression(call, 2);
                var xyVelocity = ResolveSpawnExpression(call, 3);
                var zVelocity = ResolveSpawnExpression(call, 4);
                var useAmmo = call.AsBool(5);

                DoomInfo.SetStateAction(number, (world, mobj) =>
                {
                    // The original action receives already evaluated arguments, even when it
                    // subsequently declines to spend ammo or spawn. Keep that order for random().
                    var heightValue = height.EvaluateValue(world, mobj).AsFixed();
                    var xyValue = xyVelocity.EvaluateValue(world, mobj).AsFixed();
                    var zValue = zVelocity.EvaluateValue(world, mobj).AsFixed();

                    if (useAmmo && mobj.Player != null &&
                        !world.WeaponBehavior.TryUseCustomProjectileAmmo(mobj.Player))
                    {
                        return;
                    }

                    world.ThingAllocation.ThrowCustomGrenade(
                        mobj, (MobjType)type, heightValue, xyValue, zValue);
                });

                return ScriptValue.Void;
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
