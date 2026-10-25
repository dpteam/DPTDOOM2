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
            this.world = world;
            this.mobj = mobj;

            try
            {
                var value = new ScriptInterpreter(unit, host).Call(function);

                return value.AsBool();
            }
            catch (ScriptException e)
            {
                Report(e.Message);
                return false;
            }
            catch (Exception e)
            {
                Report(e.Message);
                return false;
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

            target.Register("X", 0, call => ScriptValue.FromFixed(Current().X));
            target.Register("Y", 0, call => ScriptValue.FromFixed(Current().Y));
            target.Register("Z", 0, call => ScriptValue.FromFixed(Current().Z));

            // The tics left in the current frame, which is what a mod asks when it wants to act
            // once per animation rather than once per tic.
            target.Register("Tics", 0, call => ScriptValue.FromInt(Current().Tics));

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
