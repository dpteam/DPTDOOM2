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
    /// A script function used as the condition of a state transition.
    ///
    /// DECORATE's <c>A_JumpIf(health &lt; 100, "Pain")</c> asks a question about the actor at the
    /// moment the frame ends. That cannot be answered at load time, so the question is kept as a
    /// function of the unit and evaluated on demand - see <see cref="Evaluate"/>.
    ///
    /// The host is built **once**, with the actor it is about held in a field, rather than once per
    /// evaluation. A condition is asked on every pass through its frame, which for a chasing monster
    /// is several times a second per actor; building a host each time would allocate inside the
    /// simulation for no gain. The field is safe because a tic is single-threaded and a condition
    /// only reads.
    /// </summary>
    public sealed class ScriptCondition
    {
        private readonly ScriptUnit unit;
        private readonly string function;
        private readonly ScriptHost host;
        private readonly string source;

        private World world;
        private Mobj mobj;

        /// <summary>True once a failure has been reported, so a broken condition says it once.</summary>
        private bool reported;

        public ScriptCondition(ScriptUnit unit, string function, string source)
        {
            this.unit = unit;
            this.function = function;
            this.source = source;

            host = new ScriptHost();
            RegisterActorFunctions(host);
        }

        /// <summary>
        /// Asks the question. Never throws: a condition that fails is false, and the failure is
        /// said out loud once.
        ///
        /// Throwing here would abort the tic in the middle of a state transition, with the actor
        /// half moved - the worst place in the engine for an exception, because the world would then
        /// be neither in the old state nor the new one and lockstep would have nothing to agree on.
        /// A false answer leaves the actor where the frame's successor says, which is a visible
        /// behaviour rather than a corrupted world.
        ///
        /// Saying it out loud matters as much as not throwing. A condition that silently answers
        /// false for ever looks like a mod whose jump never fires, and the modder has no way to tell
        /// that from a condition that is simply never true. One line, once, names the function and
        /// the file.
        /// </summary>
        public bool Evaluate(World world, Mobj mobj)
        {
            return EvaluateValue(world, mobj).AsBool();
        }

        /// <summary>
        /// Evaluates the function as a value, not just as a yes/no answer.
        ///
        /// A_SpawnItemEx needs the same machinery as a conditional jump: an expression may contain
        /// random(), and it must be evaluated when the frame runs rather than when the mod is
        /// loaded. Returning zero on a reported failure gives the caller a safe inert value, just
        /// as a failed condition answers false.
        /// </summary>
        public ScriptValue EvaluateValue(World world, Mobj mobj)
        {
            this.world = world;
            this.mobj = mobj;

            try
            {
                return new ScriptInterpreter(unit, host).Call(function);
            }
            catch (ScriptException e)
            {
                Report(e.Message);
                return ScriptValue.FromInt(0);
            }
            catch (Exception e)
            {
                Report(e.Message);
                return ScriptValue.FromInt(0);
            }
        }

        /// <summary>
        /// Tells the player, once per condition, which is the most a broken script can get.
        ///
        /// A warning rather than a load-report entry, because this happens during play: the load
        /// report was printed before the first tic, and a condition is only asked when a frame ends.
        /// Once, because the same condition is asked several times a second per actor and a
        /// repeated warning is a warning nobody reads.
        /// </summary>
        private void Report(string message)
        {
            if (reported)
            {
                return;
            }

            reported = true;

            ConsoleUi.Warn("condition '" + function + "' in " + source + " failed: " + message +
                           "; it now answers false");
        }

        /// <summary>
        /// What a condition may look at: the actor, read only.
        ///
        /// Deliberately a smaller vocabulary than the definition host. A condition runs while the
        /// world is being simulated, so anything here that wrote would be a way for a mod to change
        /// the world from inside a transition - and a write in the middle of `SetState` would fight
        /// with the state the loop is already walking.
        ///
        /// Names are the ones DECORATE uses, capitalised the way this engine's own API capitalises.
        /// </summary>
        private void RegisterActorFunctions(ScriptHost target)
        {
            target.Register("Health", 0, call => ScriptValue.FromInt(Current().Health));

            // "Does this actor have a target" is the question DECORATE's conditions actually ask,
            // and `target != null` is how they write it. Exposed as a count rather than a bool so a
            // script can also compare it, which costs nothing.
            target.Register("Target", 0, call => ScriptValue.FromInt(Current().Target != null ? 1 : 0));

            target.Register("MomX", 0, call => ScriptValue.FromFixed(Current().MomX));
            target.Register("MomY", 0, call => ScriptValue.FromFixed(Current().MomY));
            target.Register("MomZ", 0, call => ScriptValue.FromFixed(Current().MomZ));

            // Distance to the target, which is what DECORATE's A_JumpIfCloser asks. Answered as a
            // fixed-point number, the same unit the engine's own geometry uses, so a condition can
            // add to it or compare two of them without a conversion the modder cannot see.
            target.Register("Distance", 0, call =>
            {
                var mobj = Current();
                var target = mobj.Target;

                if (target == null)
                {
                    // No target is infinitely far: a condition asking "is it close" must not be
                    // true for a monster standing alone. A large number rather than a thrown
                    // error, because the question is legitimate and the honest answer is "no".
                    return ScriptValue.FromFixed(Fixed.MaxValue);
                }

                return ScriptValue.FromFixed(
                    Geometry.AproxDistance(mobj.X - target.X, mobj.Y - target.Y));
            });

            target.Register("X", 0, call => ScriptValue.FromFixed(Current().X));
            target.Register("Y", 0, call => ScriptValue.FromFixed(Current().Y));
            target.Register("Z", 0, call => ScriptValue.FromFixed(Current().Z));

            // The tics left in the current frame, which is what a mod asks when it wants to act
            // once per animation rather than once per tic.
            target.Register("Tics", 0, call => ScriptValue.FromInt(Current().Tics));

            // DECORATE's angle is in degrees, like A_SetAngle's argument. Convert without a
            // floating-point round trip so angle + 0.4 remains exact enough for lockstep.
            target.Register("Angle", 0, call =>
            {
                var data = (long)Current().Angle.Data;
                var numerator = data * 360L * Fixed.FracUnit;
                const long denominator = 0x100000000L;

                if (numerator >= 0)
                {
                    numerator += denominator / 2;
                }
                else
                {
                    numerator -= denominator / 2;
                }

                return ScriptValue.FromFixed(new Fixed((int)(numerator / denominator)));
            });

            // DECORATE's random(lo, hi), inclusive. It draws from the WORLD generator, and that is
            // the point: a condition is part of the simulation, so every end of a session must make
            // the same draws. The presentation random the mixer uses would desynchronise the world.
            target.Register("Random", 2, call =>
            {
                var low = call.AsInt(0);
                var high = call.AsInt(1);

                if (high < low)
                {
                    return ScriptValue.FromInt(low);
                }

                return ScriptValue.FromInt(low + world.Random.Next() % (high - low + 1));
            });

            // A_CheckFloor: the actor has come down. The comparison is the same one the original
            // action makes, and it reads the floor the mover has already worked out, so a mod's
            // jump agrees with where the actor actually stands.
            target.Register("OnFloor", 0, call => ScriptValue.FromBool(Current().Z <= Current().FloorZ));

            // A_CheckCeiling: the mirror image, and the same comparison GZDoom makes - the top of
            // the actor against the ceiling, not its origin.
            target.Register("OnCeiling", 0, call =>
                ScriptValue.FromBool(Current().Z + Current().Height >= Current().CeilingZ));

            // Distance to the tracer, the companion of Distance() for A_JumpIfTracerCloser.
            target.Register("TracerDistance", 0, call =>
            {
                var mobj = Current();
                var tracer = mobj.Tracer;

                if (tracer == null)
                {
                    return ScriptValue.FromFixed(Fixed.MaxValue);
                }

                return ScriptValue.FromFixed(
                    Geometry.AproxDistance(mobj.X - tracer.X, mobj.Y - tracer.Y));
            });

            // A_JumpIfCloser and A_JumpIfTracerCloser, with GZDoom's own rule.
            //
            // The horizontal distance alone is not the whole answer: the original also requires the
            // two actors to overlap vertically, unless `noz` asks for horizontal only. Three calls in
            // ademo1.wad pass noz=1, and treating the test as horizontal-only would have made a
            // monster react to something directly above or below it.
            target.Register("CloserToTarget", 2, call => Closer(Current().Target, call));
            target.Register("CloserToTracer", 2, call => Closer(Current().Tracer, call));

            // A_JumpIfTargetInsideMeleeRange and its Outside counterpart. The same check the melee
            // attacks use, so a mod's branch agrees with what a punch would actually reach.
            target.Register("InMeleeRange", 0, call =>
                ScriptValue.FromBool(world.MonsterBehavior.IsInMeleeRange(Current())));

            // A_JumpIfInventory(name, amount, ptr). ZDoom's rule for amount <= 0 is "is the item
            // full", and this engine has no per-item maximum - so an item is never full and the
            // answer is false. That is the honest reading of "unbounded", and the converter says so
            // once per actor rather than leaving a modder to wonder why the branch never fires.
            target.Register("HasItem", 3, call =>
            {
                var name = call.AsText(0);
                var amount = call.AsInt(1);
                var pointer = call.AsInt(2);

                var owner = ActorPointer.Resolve(Current(), pointer);

                if (owner == null)
                {
                    return ScriptValue.FromBool(false);
                }

                var count = owner.GetItem(name);

                return ScriptValue.FromBool(amount > 0 ? count >= amount : false);
            });

            // A_CheckFlag: is a named flag set on one of the actor's pointers? The name is checked
            // by the converter, but a hand-written script can still misspell it, so a wrong name is
            // reported once instead of answering false for ever with no explanation.
            target.Register("HasFlag", 2, call =>
            {
                var name = call.AsText(0);
                var pointer = call.AsInt(1);

                var actor = ActorPointer.Resolve(Current(), pointer);

                // A pointer that leads nowhere is not an error: "the tracer has DONTTHRUST" is a
                // perfectly good question with the answer "no" when there is no tracer.
                if (actor == null)
                {
                    return ScriptValue.FromBool(false);
                }

                MobjFlags flag;

                if (!TryFlag(name, out flag))
                {
                    throw call.Fail("no such flag '" + name + "'");
                }

                return ScriptValue.FromBool((actor.Flags & flag) != 0);
            });
        }

        /// <summary>
        /// GZDoom's CheckIfCloser, spelled out.
        ///
        /// A missing actor answers false rather than throwing: "is the thing I am chasing close" is a
        /// legitimate question with the answer "no" when there is nothing to chase. The vertical
        /// part is the part that is easy to drop and hard to notice - a monster would then react to
        /// something a floor above it - so it is written here exactly as the original does it.
        /// </summary>
        private ScriptValue Closer(Mobj other, ScriptCall call)
        {
            var self = Current();

            if (other == null)
            {
                return ScriptValue.FromBool(false);
            }

            var distance = call.AsFixed(0);
            var noz = call.AsBool(1);

            if (Geometry.AproxDistance(other.X - self.X, other.Y - self.Y) >= distance)
            {
                return ScriptValue.FromBool(false);
            }

            if (noz)
            {
                return ScriptValue.FromBool(true);
            }

            // Overlapping vertically, counted from whichever actor is higher.
            var above = self.Z > other.Z
                ? self.Z - other.Z - other.Height
                : other.Z - self.Z - self.Height;

            return ScriptValue.FromBool(above < distance);
        }

        /// <summary>
        /// Flag names already looked up, so a condition does not parse a string on every frame.
        ///
        /// A chasing monster asks its condition several times a second, and parsing the name on each
        /// ask would put string work inside the simulation. The set of names a mod uses is tiny and
        /// fixed once the file has loaded, which is exactly what a cache is for.
        /// </summary>
        private static readonly Dictionary<string, MobjFlags> knownFlags =
            new Dictionary<string, MobjFlags>(StringComparer.OrdinalIgnoreCase);

        private static bool TryFlag(string name, out MobjFlags flag)
        {
            lock (knownFlags)
            {
                if (knownFlags.TryGetValue(name, out flag))
                {
                    return true;
                }
            }

            if (!ScriptActorApi.TryParseFlagName(name, out flag))
            {
                return false;
            }

            lock (knownFlags)
            {
                knownFlags[name] = flag;
            }

            return true;
        }

        private Mobj Current()
        {
            if (mobj == null)
            {
                throw new ScriptException("this condition was asked about no actor", 0);
            }

            return mobj;
        }
    }
}
