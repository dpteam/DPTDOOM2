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
using System.Globalization;
using System.Text;

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// One actor block read out of a DECORATE file.
    ///
    /// Kept as data rather than converted on the fly, because the two halves fail
    /// differently and a modder needs to know which one: reading is a matter of syntax, and
    /// applying is a matter of whether this engine has the actor and the property.
    /// </summary>
    public sealed class DecorateActor
    {
        public string Name;
        public string ParentName;
        public string Replaces;

        /// <summary>The map number, or -1 when the file did not give one.</summary>
        public int DoomEdNum = -1;

        /// <summary>Line of the ACTOR keyword, for a report a person can act on.</summary>
        public int Line;

        /// <summary>Integer properties, already normalised to the names this engine uses.</summary>
        public readonly List<KeyValuePair<string, int>> Numbers = new List<KeyValuePair<string, int>>();

        /// <summary>Flag names with the value the file asked for.</summary>
        public readonly List<KeyValuePair<string, bool>> Flags = new List<KeyValuePair<string, bool>>();

        /// <summary>Properties that were read and cannot be applied, with their text.</summary>
        public readonly List<KeyValuePair<string, string>> Unsupported =
            new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Text properties that **are** applied: the five sound names, already normalised to the
        /// names this engine uses.
        ///
        /// Separate from <see cref="Numbers"/> because a sound is a name and not a number, and
        /// separate from <see cref="Unsupported"/> because since named sounds exist these apply.
        /// </summary>
        public readonly List<KeyValuePair<string, string>> Sounds =
            new List<KeyValuePair<string, string>>();

        /// <summary>The line each unsupported property was written on, parallel to it.</summary>
        public readonly List<int> UnsupportedLines = new List<int>();

        /// <summary>Whether the block carried a States section.</summary>
        public bool HasStates;

        /// <summary>
        /// One label of the States section: "See", "Spawn", and so on.
        ///
        /// Kept in the order written, because the first frame of a label is what a Goto and a
        /// Loop refer to, and because a report naming a frame is easier to follow in order.
        /// </summary>
        public readonly List<StateChain> Chains = new List<StateChain>();
    }

    /// <summary>One labelled run of frames.</summary>
    public sealed class StateChain
    {
        public string Label;
        public int Line;

        public readonly List<StateFrame> Frames = new List<StateFrame>();

        /// <summary>What follows the last frame: Loop, Stop, Wait, or Goto a name.</summary>
        public string Next = "";
    }

    /// <summary>One frame of a chain, as DECORATE writes it.</summary>
    public sealed class StateFrame
    {
        public string Sprite;
        public string Letter;
        public int Tics;
        public string Action;
        public int Line;

        /// <summary>
        /// The label an <c>A_Jump</c> leaves for, or null.
        ///
        /// Kept apart from <see cref="Action"/> because a jump is not an action this engine has -
        /// it is a property of the state's transition, which is exactly how DECORATE means it.
        /// </summary>
        public string JumpLabel;

        /// <summary>Chance of the jump, out of 256.</summary>
        public int JumpChance = 256;

        /// <summary>
        /// The jump's condition, already translated into this engine's language, or null.
        ///
        /// A_JumpIf asks a question instead of rolling a die, so this and
        /// <see cref="JumpChance"/> are exclusive: a frame has one jump, and it is either certain
        /// when the question is true or taken on a chance.
        /// </summary>
        public string JumpCondition;

        /// <summary>
        /// Sound a frame plays when it runs, or null.
        ///
        /// DECORATE's A_PlaySound names a sound and this engine resolves that name through
        /// SNDINFO, so the frame only has to remember which name. Emitted as its own call because
        /// the sound is a piece of work, not a state argument - see EmitSounds.
        /// </summary>
        public string PlaySoundName;

        /// <summary>Volume for <see cref="PlaySoundName"/>, 0 to 100.</summary>
        public int PlaySoundVolume = 100;

        /// <summary>Actor the frame fires, by DECORATE name, or null.</summary>
        public string MissileName;

        /// <summary>Height above the actor the missile appears at.</summary>
        public int MissileHeight = 32;

        /// <summary>Actor the frame places, by DECORATE name, or null.</summary>
        public string SpawnName;

        /// <summary>How far in front of the actor the spawned thing appears.</summary>
        public int SpawnDistance;
    }

    /// <summary>
    /// Reads DECORATE and writes SharpScript.
    ///
    /// The conversion is the honest way to support DECORATE in this project, and not a
    /// shortcut. SharpScript already has a parser, a sandbox and an application order that
    /// is tested against the actor table; a second path that applies changes directly would
    /// need its own copy of all three and would drift from the first. Emitting source also
    /// means a modder can read what their DECORATE became - <c>-decorate-inspect</c> prints
    /// it - and that is the only way to find out why an actor did not come out as expected.
    ///
    /// What it converts, and what it refuses:
    ///
    ///  - numbers: Health, Speed, Radius, Height, PainChance, Mass, Damage, ReactionTime;
    ///  - flags: <c>+FLAG</c> and <c>-FLAG</c>, plus the MONSTER and PROJECTILE bundles;
    ///  - nothing else. A States section is read, counted and reported, because states need
    ///    entries in the engine's state table that do not exist yet, and pretending
    ///    otherwise would produce an actor that looks converted and behaves like the
    ///    original - a much worse outcome than a line in the report.
    /// </summary>
    public static class DecorateConverter
    {
        /// <summary>
        /// The properties this converter knows how to map, by DECORATE spelling.
        ///
        /// The values are the function names in the actor API, which are not always the
        /// same word: DECORATE says "Health" and so does the API, but DECORATE's
        /// "ReactionTime" is the same thing the API calls by that name, while "Scale",
        /// "Alpha" and "RenderStyle" have no counterpart at all.
        /// </summary>
        private static readonly Dictionary<string, string> NumberProperties =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "health", "Health" },
            { "speed", "Speed" },
            { "radius", "Radius" },
            { "height", "Height" },
            { "painchance", "PainChance" },
            { "mass", "Mass" },
            { "damage", "Damage" },
            { "reactiontime", "ReactionTime" }
        };

        /// <summary>
        /// Sound properties.
        ///
        /// In DECORATE the value is a **sound name** from SNDINFO, not a lump: `SeeSound
        /// "imp/sight"`. The engine's own actors hold an <see cref="Sfx"/>, and a name that is not
        /// one is kept as a named sound for that slot - so both spellings convert, and the same
        /// word means the same thing in the mod and in the engine.
        /// </summary>
        private static readonly Dictionary<string, string> SoundProperties =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "seesound", "SeeSound" },
            { "attacksound", "AttackSound" },
            { "painsound", "PainSound" },
            { "deathsound", "DeathSound" },
            { "activesound", "ActiveSound" }
        };

        /// <summary>
        /// The DECORATE bundles, expanded to the flags this engine has.
        ///
        /// MONSTER and PROJECTILE are not flags but groups of them. The expansion is taken
        /// from vanilla Doom's own actors - a rocket is MF_NOBLOCKMAP|MF_MISSILE|MF_DROPOFF|
        /// MF_NOGRAVITY, an imp is MF_SHOOTABLE|MF_SOLID|MF_COUNTKILL - rather than invented,
        /// because the whole point of the flags is to match what the engine already does.
        /// </summary>
        private static readonly string[] MonsterFlags = { "Solid", "Shootable", "CountKill" };

        private static readonly string[] ProjectileFlags =
        {
            "NoBlockMap", "Missile", "DropOff", "NoGravity"
        };

        /// <summary>
        /// Reads DECORATE and returns SharpScript source. Problems are appended to
        /// <paramref name="problems"/> and are all in DECORATE terms: the file and the line
        /// the modder wrote, never a line of the generated text.
        /// </summary>
        public static string Convert(string text, MobjInfo[] table, IList<string> problems)
        {
            var actors = Parse(text, problems);
            var script = new StringBuilder();

            script.Append("// Generated from DECORATE. Edits are lost: change the DECORATE file.\n");

            foreach (var actor in actors)
            {
                var target = ResolveTarget(actor, table, problems);

                if (target == null)
                {
                    continue;
                }

                for (var i = 0; i < actor.Unsupported.Count; i++)
                {
                    var property = actor.Unsupported[i];
                    var propertyLine = i < actor.UnsupportedLines.Count
                        ? actor.UnsupportedLines[i]
                        : actor.Line;

                    // The property's own line, not the actor's: a block with four refused
                    // properties all reported at line 1 makes the reader find them by hand.
                    problems.Add(actor.Name + " line " + propertyLine + ": '" + property.Key +
                                 " " + property.Value + "' has no counterpart in this engine, so it " +
                                 "is left as it was");
                }

                if (actor.HasStates && actor.Chains.Count == 0)
                {
                    // A States section was written and nothing came out of it - every label was
                    // empty or unreadable, and the per-chain reports above say why.
                    problems.Add(actor.Name + " line " + actor.Line +
                                 ": the States section produced no frames, so the actor keeps the " +
                                 "engine's own states");
                }

                if (actor.Numbers.Count == 0 && actor.Flags.Count == 0 && actor.Sounds.Count == 0 &&
                    actor.Chains.Count == 0)
                {
                    // Nothing to say, so nothing is emitted. An empty patch would still be
                    // an entry in the report and would look like work.
                    continue;
                }

                script.Append("\n").Append(ScriptSyntax.ActorDefinitionHeader(target.DoomEdNum))
                      .Append('\n');

                foreach (var pair in actor.Numbers)
                {
                    script.Append("    ").Append(pair.Key).Append('(')
                          .Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(");\n");
                }

                foreach (var pair in actor.Sounds)
                {
                    script.Append("    ").Append(pair.Key).Append("(\"")
                          .Append(pair.Value).Append("\");\n");
                }

                foreach (var pair in actor.Flags)
                {
                    MobjFlags flag;

                    if (!ScriptActorApi.TryParseFlagName(pair.Key, out flag))
                    {
                        // Skipped here rather than left for the script to refuse: a throw
                        // while the definition runs costs the whole actor block, states
                        // included, and reports itself as a script problem when the modder
                        // wrote DECORATE. One unknown flag must cost one flag.
                        problems.Add(actor.Name + " line " + actor.Line + ": there is no flag '" +
                                     pair.Key + "' in this engine, so it is left as it was");
                        continue;
                    }

                    script.Append("    Flag(\"").Append(pair.Key).Append("\", ")
                          .Append(pair.Value ? "true" : "false").Append(");\n");
                }

                EmitStates(script, actor, problems);

                // Sounds are attached to their frames as extra calls *inside* the definition: a
                // sound is a piece of work the frame performs, not something the transition
                // describes. They come after the frames exist, which is why this is a second pass.
                EmitSounds(script, actor);

                script.Append("}\n");

                // Conditions come after the definition they belong to, as top-level functions:
                // the state layer looks them up by name when the frame ends, which is long after
                // this script has run.
                EmitConditions(script, actor);
            }

            return script.ToString();
        }

        /// <summary>
        /// Finds the actor in the engine's table that a DECORATE block describes, or null
        /// with the reason.
        ///
        /// Three ways a block names its target, in the order DECORATE itself resolves them:
        /// the number written in the block, the name it replaces, and the parent it inherits
        /// from. A number this engine does not have creates a new actor in the reserve, copied
        /// from its parent - which is what "ACTOR Child : Parent" means, and what makes a mod
        /// able to add a monster rather than only change one.
        /// </summary>
        private static MobjInfo ResolveTarget(DecorateActor actor, MobjInfo[] table,
                                              IList<string> problems)
        {
            var number = actor.DoomEdNum;
            var parentType = -1;

            if (actor.ParentName != null)
            {
                int parentNumber;

                if (TryResolveName(actor.ParentName, out parentNumber))
                {
                    parentType = DoomInfo.TypeOf(parentNumber);
                }
                else
                {
                    // A parent this converter does not know is not fatal for a replacement:
                    // the number in the block decides. It is only fatal when the block has no
                    // number of its own, because then the parent was the only thing naming the
                    // actor.
                    if (number < 0)
                    {
                        problems.Add(actor.Name + " line " + actor.Line + ": the parent '" +
                                     actor.ParentName + "' is an actor name this converter does " +
                                     "not know, so the actor has nothing to inherit from");
                        return null;
                    }
                }
            }

            if (number < 0 && actor.Replaces != null)
            {
                if (!TryResolveName(actor.Replaces, out number))
                {
                    problems.Add(actor.Name + " line " + actor.Line + ": 'replaces " +
                                 actor.Replaces + "' is an actor name this converter does not " +
                                 "know, so the actor is not changed; write the DoomEdNum in the " +
                                 "block instead");
                    return null;
                }
            }

            if (number < 0)
            {
                problems.Add(actor.Name + " line " + actor.Line + ": the block names no DoomEdNum " +
                             "this converter can resolve, so nothing is changed");
                return null;
            }

            var existing = DoomInfo.TypeOf(number);

            if (existing >= 0)
            {
                return table[existing];
            }

            // A number this engine does not have: a new actor. It inherits from its parent, or
            // from the first entry in the table when the block names no parent at all - which
            // is what DECORATE does too, and it is why a block with states and no parent still
            // has something to be a copy of.
            var type = DoomInfo.AddActor(number, parentType);

            if (type < 0)
            {
                problems.Add(actor.Name + " line " + actor.Line + ": no free actor slots left; " +
                             "the reserve is " + DoomInfo.CustomActorReserve + " actors per run, " +
                             "so the actor is not created");
                return null;
            }

            return DoomInfo.MobjInfos[type];
        }

        private static bool TryResolveName(string name, out int number)
        {
            number = -1;

            var type = ActorNames.TypeOf(name);

            if (type < 0 || type >= DoomInfo.MobjInfos.Length)
            {
                return false;
            }

            // The number is read from the actor table rather than tabulated beside the name. The
            // table is the source of truth - a folder of actor definitions can change a DoomEdNum,
            // and a hand-written copy would then disagree with the engine without saying so.
            number = DoomInfo.MobjInfos[type].DoomEdNum;
            return number >= 0;
        }

        /// <summary>
        /// Writes one State(...) call per frame, then binds the labels the engine knows to the
        /// actor's own state slots.
        ///
        /// Frame names are qualified with the label and a number - "See#1", "See#2" - because
        /// the names are scoped to the actor and a chain that loops needs to refer to its own
        /// head. The separator is '#' so that a label really called "See1" cannot collide
        /// with the second frame of "See".
        /// </summary>
        private static void EmitStates(StringBuilder script, DecorateActor actor,
                                       IList<string> problems)
        {
            foreach (var chain in actor.Chains)
            {
                if (chain.Frames.Count == 0)
                {
                    problems.Add(actor.Name + " line " + chain.Line + ": the label '" +
                                 chain.Label + "' has no frames, so it is not converted");
                    continue;
                }

                for (var i = 0; i < chain.Frames.Count; i++)
                {
                    var frame = chain.Frames[i];
                    var next = "";

                    if (i + 1 < chain.Frames.Count)
                    {
                        next = FrameName(chain.Label, i + 2);
                    }
                    else
                    {
                        next = ResolveChainEnd(actor, chain, i + 1);
                    }

                    if (frame.Action.Length > 0)
                    {
                        Action<World, Mobj> action;

                        if (!DoomInfo.TryGetAction(frame.Action, out action))
                        {
                            // Named with the actions that exist, because the useful answer to
                            // "this action is missing" is not "no" but "not here, and here is
                            // what there is".
                            problems.Add(actor.Name + " line " + frame.Line + ": no such action '" +
                                         frame.Action + "'; this frame's action is dropped. " +
                                         "This engine has: " +
                                         string.Join(", ", DoomInfo.ActionNameList()));
                        }
                    }

                    script.Append("    State(\"")
                          .Append(FrameName(chain.Label, i + 1)).Append("\", \"")
                          .Append(frame.Sprite).Append("\", \"")
                          .Append(frame.Letter).Append("\", ")
                          .Append(frame.Tics.ToString(CultureInfo.InvariantCulture)).Append(", \"")
                          .Append(frame.Action).Append("\", \"")
                          .Append(next).Append("\"");

                    if (frame.JumpLabel != null)
                    {
                        // The jump is a state argument, not an action: what the frame does instead
                        // of continuing is a property of the transition.
                        //
                        // The target is this actor's own chain when it has one, and otherwise the
                        // name as written - which the script layer resolves as an engine state, the
                        // same way a "Goto See" does.
                        var jumpTo = HasChain(actor, frame.JumpLabel)
                            ? FrameName(frame.JumpLabel, 1)
                            : frame.JumpLabel;

                        script.Append(", \"")
                              .Append(jumpTo).Append("\", ");

                        if (frame.JumpCondition != null)
                        {
                            // A conditional jump: the chance slot carries nothing and the tenth
                            // argument names the function that answers the question.
                            var condition = ConditionName(actor, chain.Label, i + 1);

                            script.Append("0, \"").Append(condition).Append("\"");
                        }
                        else
                        {
                            script.Append(frame.JumpChance.ToString(CultureInfo.InvariantCulture));
                        }
                    }

                    script.Append(");\n");
                }

                var property = StatePropertyFor(chain.Label);

                if (property != null)
                {
                    script.Append("    ").Append(property).Append("(\"")
                          .Append(FrameName(chain.Label, 1)).Append("\");\n");
                }
            }
        }

        /// <summary>
        /// Attaches each frame's sound to that frame.
        ///
        /// A second call rather than a state argument, because a sound is something the frame
        /// **does**: the transition describes where the actor goes, and this describes what it
        /// plays on the way. The frame is addressed by the name it was created with, so the order
        /// of emission cannot change which sound belongs to which frame.
        /// </summary>
        private static void EmitSounds(StringBuilder script, DecorateActor actor)
        {
            foreach (var chain in actor.Chains)
            {
                for (var i = 0; i < chain.Frames.Count; i++)
                {
                    var frame = chain.Frames[i];

                    if (frame.PlaySoundName == null)
                    {
                        continue;
                    }

                    script.Append("    PlaySound(\"")
                          .Append(FrameName(chain.Label, i + 1)).Append("\", \"")
                          .Append(frame.PlaySoundName).Append("\", ")
                          .Append(frame.PlaySoundVolume.ToString(CultureInfo.InvariantCulture))
                          .Append(");\n");
                }
            }

            EmitSpawns(script, actor);
        }

        /// <summary>
        /// Attaches each frame's projectile or spawned thing.
        ///
        /// A separate pass from the sound for the same reason a sound is separate from a
        /// transition: these are work the frame performs. A frame carries at most one action in
        /// DECORATE, so at most one of these applies to any frame, and each is installed as that
        /// frame's action.
        /// </summary>
        private static void EmitSpawns(StringBuilder script, DecorateActor actor)
        {
            foreach (var chain in actor.Chains)
            {
                for (var i = 0; i < chain.Frames.Count; i++)
                {
                    var frame = chain.Frames[i];
                    var name = FrameName(chain.Label, i + 1);

                    if (frame.MissileName != null)
                    {
                        script.Append("    Missile(\"").Append(name).Append("\", \"")
                              .Append(frame.MissileName).Append("\", ")
                              .Append(FixedFromInt(frame.MissileHeight)).Append(");\n");
                    }

                    if (frame.SpawnName != null)
                    {
                        script.Append("    SpawnItem(\"").Append(name).Append("\", \"")
                              .Append(frame.SpawnName).Append("\", ")
                              .Append(FixedFromInt(frame.SpawnDistance)).Append(");\n");
                    }
                }
            }
        }

        /// <summary>
        /// A whole number of map units as a fixed-point literal.
        ///
        /// Written as "32.0" rather than "32" because the script call expects a fixed value and the
        /// language's own rule is that a literal with a dot is fixed. Passing a plain integer would
        /// work through the conversion, and would also read as if the unit were something else.
        /// </summary>
        private static string FixedFromInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture) + ".0";
        }

        private static string FrameName(string label, int index)
        {
            return label + "#" + index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The name of the function that answers one frame's condition.
        ///
        /// Built from the frame's identity rather than counted, so the emission order of frames
        /// cannot change which condition belongs to which - the same property the frame names
        /// themselves have. Characters that are not letters or digits are dropped because this has
        /// to be a legal identifier in the script, not merely a unique string.
        /// </summary>
        private static string ConditionName(DecorateActor actor, string label, int index)
        {
            var name = new System.Text.StringBuilder("Cond_");

            foreach (var c in actor.Name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    name.Append(c);
                }
            }

            name.Append('_');

            foreach (var c in label)
            {
                if (char.IsLetterOrDigit(c))
                {
                    name.Append(c);
                }
            }

            name.Append('_').Append(index.ToString(CultureInfo.InvariantCulture));
            return name.ToString();
        }

        /// <summary>
        /// Writes one function per conditional jump.
        ///
        /// The condition is already in this engine's language - <see cref="DecorateCondition"/>
        /// translated it while the frame was read - so this is only the wrapper. A condition that
        /// could not be translated left the frame with no jump at all and was reported there.
        /// </summary>
        private static void EmitConditions(StringBuilder script, DecorateActor actor)
        {
            for (var chainIndex = 0; chainIndex < actor.Chains.Count; chainIndex++)
            {
                var chain = actor.Chains[chainIndex];

                for (var i = 0; i < chain.Frames.Count; i++)
                {
                    var frame = chain.Frames[i];

                    if (frame.JumpLabel == null || frame.JumpCondition == null)
                    {
                        continue;
                    }

                    script.Append("\nfn ").Append(ConditionName(actor, chain.Label, i + 1))
                          .Append("() {\n")
                          .Append("    return ").Append(frame.JumpCondition).Append(";\n")
                          .Append("}\n");
                }
            }
        }

        /// <summary>True when this actor describes a chain with that label.</summary>
        private static bool HasChain(DecorateActor actor, string label)
        {
            foreach (var chain in actor.Chains)
            {
                if (string.Equals(chain.Label, label, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What the last frame of a chain continues to.
        ///
        /// A chain with no terminator - which is legal and common, the next label simply
        /// starting - falls through to the frame after it, and that is what this returns: the
        /// next label's first frame, or the stop when there is none.
        /// </summary>
        private static string ResolveChainEnd(DecorateActor actor, StateChain chain, int frameCount)
        {
            if (chain.Next == "Loop")
            {
                return FrameName(chain.Label, 1);
            }

            if (chain.Next == "Stop" || chain.Next.Length == 0)
            {
                // A chain that ends without a terminator: stop. Falling through to the next
                // label would be the literal reading, but a mod that meant that writes Goto.
                return "";
            }

            if (chain.Next == "Wait")
            {
                return FrameName(chain.Label, frameCount);
            }

            if (chain.Next.StartsWith("Goto ", StringComparison.OrdinalIgnoreCase))
            {
                var target = chain.Next.Substring(5).Trim();

                foreach (var other in actor.Chains)
                {
                    if (string.Equals(other.Label, target, StringComparison.OrdinalIgnoreCase))
                    {
                        return FrameName(other.Label, 1);
                    }
                }

                // Not one of this actor's labels, so it may be an engine state: "Goto See"
                // from a mod's chain has to reach vanilla, or a mod could never hand control
                // back.
                MobjState engineState;

                if (ScriptActorApi.TryParseEngineState(target, out engineState))
                {
                    return target;
                }

                return "";
            }

            return "";
        }

        /// <summary>
        /// The engine state property a DECORATE label initialises, or null when the label is
        /// only a jump target inside the actor.
        /// </summary>
        private static string StatePropertyFor(string label)
        {
            switch (label.ToLowerInvariant())
            {
                case "spawn": return "SpawnState";
                case "see": return "SeeState";
                case "pain": return "PainState";
                case "melee": return "MeleeState";
                case "missile": return "MissileState";
                case "death": return "DeathState";
                case "xdeath": return "XdeathState";
                case "raise": return "RaiseState";
                default: return null;
            }
        }

        // ------------------------------------------------------------ reading

        /// <summary>
        /// Reads every actor block in the text.
        ///
        /// Written by hand rather than with a parser generator for the same reason the rest
        /// of this project is: a dependency is a thing to ship. The grammar that matters is
        /// four tokens - a word, a number, a quoted string and a brace - and everything else
        /// is a comment.
        /// </summary>
        public static IList<DecorateActor> Parse(string text, IList<string> problems)
        {
            var actors = new List<DecorateActor>();
            var reader = new Reader(text);
            var line = 1;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    break;
                }

                if (!string.Equals(token.Text, "ACTOR", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var startLine = token.Line;
                var actor = new DecorateActor();
                actor.Line = startLine;

                // Everything up to the opening brace describes which actor this is. DECORATE
                // allows these in either order and in any combination, so they are read as a
                // bag rather than as a sequence.
                while (true)
                {
                    var header = reader.Next(ref line);

                    if (header == null)
                    {
                        problems.Add("line " + startLine + ": the actor block is not closed");
                        return actors;
                    }

                    if (header.Text == "{")
                    {
                        break;
                    }

                    if (header.Text == ":")
                    {
                        var parent = reader.Next(ref line);

                        if (parent != null && parent.Text != "{")
                        {
                            actor.ParentName = parent.Text;
                        }

                        continue;
                    }

                    if (string.Equals(header.Text, "replaces", StringComparison.OrdinalIgnoreCase))
                    {
                        var replaced = reader.Next(ref line);

                        if (replaced != null && replaced.Text != "{")
                        {
                            actor.Replaces = replaced.Text;
                        }

                        continue;
                    }

                    if (header.IsNumber)
                    {
                        actor.DoomEdNum = header.Number;
                        continue;
                    }

                    if (actor.Name == null)
                    {
                        actor.Name = header.Text;
                    }
                }

                if (string.IsNullOrEmpty(actor.Name))
                {
                    problems.Add("line " + startLine + ": the actor has no name");
                }

                actor.Name = actor.Name ?? "actor at line " + startLine;

                ReadBody(reader, ref line, actor, problems);
                actors.Add(actor);
            }

            return actors;
        }

        private static void ReadBody(Reader reader, ref int line, DecorateActor actor,
                                     IList<string> problems)
        {
            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    problems.Add(actor.Name + " line " + actor.Line + ": the actor block is not closed");
                    return;
                }

                if (token.Text == "}")
                {
                    return;
                }

                if (string.Equals(token.Text, "States", StringComparison.OrdinalIgnoreCase))
                {
                    actor.HasStates = true;
                    ReadStates(reader, ref line, actor, problems);
                    continue;
                }

                // +FLAG and -FLAG are a single token in the reader, because a lone "+" is
                // already the sign of a number and splitting the two would make the reader
                // ambiguous for no gain.
                if (token.Text.Length > 1 && (token.Text[0] == '+' || token.Text[0] == '-'))
                {
                    ReadFlag(actor, token.Text.Substring(1), token.Text[0] == '+');
                    continue;
                }

                if (string.Equals(token.Text, "MONSTER", StringComparison.OrdinalIgnoreCase))
                {
                    AddFlags(actor, MonsterFlags, true);
                    continue;
                }

                if (string.Equals(token.Text, "PROJECTILE", StringComparison.OrdinalIgnoreCase))
                {
                    AddFlags(actor, ProjectileFlags, true);
                    continue;
                }

                var value = reader.Next(ref line);

                if (value == null)
                {
                    return;
                }

                if (value.Text == "}")
                {
                    return;
                }

                if (string.Equals(token.Text, "spawnid", StringComparison.OrdinalIgnoreCase))
                {
                    // The doomednum as a property rather than after the actor name. Both
                    // spellings are used in the wild, and ACE - the engine whose DECORATE
                    // subset this converter is measured against - documents spawnid as the
                    // way to give a new actor a map number.
                    var number = value.IsNumber ? value.Number : -1;

                    if (number < 0 && !int.TryParse(value.Text, NumberStyles.Integer,
                                                    CultureInfo.InvariantCulture, out number))
                    {
                        problems.Add(actor.Name + " line " + value.Line +
                                     ": 'spawnid' needs a whole number, found '" + value.Text + "'");
                        continue;
                    }

                    actor.DoomEdNum = number;
                    continue;
                }

                string mapped;

                if (NumberProperties.TryGetValue(token.Text, out mapped))
                {
                    var number = value.IsNumber ? value.Number : -1;

                    if (number < 0 && !int.TryParse(value.Text, NumberStyles.Integer,
                                                    CultureInfo.InvariantCulture, out number))
                    {
                        problems.Add(actor.Name + " line " + value.Line + ": '" + token.Text +
                                     "' needs a whole number, found '" + value.Text + "'");
                        continue;
                    }

                    actor.Numbers.Add(new KeyValuePair<string, int>(mapped, number));
                    continue;
                }

                if (SoundProperties.TryGetValue(token.Text, out mapped))
                {
                    // A quoted name and a bare one are the same thing: DECORATE allows both, and
                    // the reader strips the quotes already.
                    actor.Sounds.Add(new KeyValuePair<string, string>(mapped, value.Text));
                    continue;
                }

                // Everything else is recorded rather than dropped: silence is what makes a
                // modder believe a property took effect.
                actor.Unsupported.Add(new KeyValuePair<string, string>(token.Text, value.Text));
                actor.UnsupportedLines.Add(token.Line);
            }
        }

        /// <summary>
        /// Reads a States section into labelled chains.
        ///
        /// The section is a list of labels and frames, and the only structural question is
        /// where a frame ends - which DECORATE answers with a rule, not a delimiter: a frame
        /// is a sprite name, a run of letters, then optional tics, then an optional action.
        /// So the reader decides by looking at the next token: a number is tics, a name that
        /// is a known action is an action, four capital letters are the next frame's sprite.
        ///
        /// Guessing wrong here produces a frame list that looks plausible and animates
        /// wrongly, so anything genuinely ambiguous is reported rather than resolved by
        /// preference.
        /// </summary>
        private static void ReadStates(Reader reader, ref int line, DecorateActor actor,
                                       IList<string> problems)
        {
            var open = reader.Next(ref line);

            if (open == null || open.Text != "{")
            {
                problems.Add(actor.Name + " line " + actor.Line +
                             ": 'States' is not followed by a block");
                return;
            }

            StateChain chain = null;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    problems.Add(actor.Name + " line " + actor.Line +
                                 ": the States block is not closed");
                    return;
                }

                if (token.Text == "}")
                {
                    return;
                }

                // A label: a bare word followed by a colon. The reader hands over the colon as
                // its own token, so both arrive here and the second one is simply dropped.
                if (IsLabel(reader, token, ref line, out var labelLine))
                {
                    chain = new StateChain();
                    chain.Label = token.Text;
                    chain.Line = token.Line;
                    actor.Chains.Add(chain);
                    continue;
                }

                if (chain == null)
                {
                    problems.Add(actor.Name + " line " + token.Line +
                                 ": a frame appears before any label, so it belongs to no chain");
                    continue;
                }

                if (string.Equals(token.Text, "Loop", StringComparison.OrdinalIgnoreCase))
                {
                    chain.Next = "Loop";
                    continue;
                }

                if (string.Equals(token.Text, "Stop", StringComparison.OrdinalIgnoreCase))
                {
                    chain.Next = "Stop";
                    continue;
                }

                if (string.Equals(token.Text, "Wait", StringComparison.OrdinalIgnoreCase))
                {
                    chain.Next = "Wait";
                    continue;
                }

                if (string.Equals(token.Text, "Goto", StringComparison.OrdinalIgnoreCase))
                {
                    var target = reader.Next(ref line);

                    if (target == null || target.Text == "}")
                    {
                        problems.Add(actor.Name + " line " + token.Line + ": 'Goto' names nothing");
                        chain.Next = "Stop";
                        continue;
                    }

                    chain.Next = "Goto " + target.Text;
                    continue;
                }

                if (string.Equals(token.Text, "Bright", StringComparison.OrdinalIgnoreCase))
                {
                    // Belongs to the frame before it; the frame reader has already taken it
                    // into account when it sees the word, so reaching here means a stray one.
                    continue;
                }

                ReadFrame(reader, ref line, actor, chain, token, problems);
            }
        }

        private static bool IsLabel(Reader reader, Token token, ref int line, out int labelLine)
        {
            labelLine = token.Line;

            if (token.IsNumber || token.Text.Length == 0)
            {
                return false;
            }

            // If the next token is a colon this word is a label. Anything else means it was
            // the start of a frame, and the caller has to look at it again - so the reader
            // keeps a one-token pushback.
            return reader.PeekIsColon(ref line);
        }

        private static void ReadFrame(Reader reader, ref int line, DecorateActor actor,
                                      StateChain chain, Token spriteToken, IList<string> problems)
        {
            var sprite = spriteToken.Text;

            if (!ScriptActorApi.TryParseSprite(sprite, out _))
            {
                problems.Add(actor.Name + " line " + spriteToken.Line + ": no such sprite '" +
                             sprite + "'; sprites are four-letter WAD names such as TROO. " +
                             "This frame is not converted");
                SkipRestOfFrame(reader, ref line);
                return;
            }

            var letters = reader.Next(ref line);

            if (letters == null || letters.Text == "}" || letters.Text.Length == 0)
            {
                problems.Add(actor.Name + " line " + spriteToken.Line +
                             ": '" + sprite + "' is followed by no frame letters");
                return;
            }

            var bright = false;
            var text = letters.Text;

            if (text.EndsWith("^", StringComparison.Ordinal))
            {
                bright = true;
                text = text.Substring(0, text.Length - 1);
            }

            // Where this line's frames begin. Everything below applies to *these* frames only: a
            // chain accumulates, and setting the tics or the action on every frame so far is how
            // "TROO A 4 A_Look" followed by "TROO B 4 A_Chase" ended up with two A_Chases - a bug
            // that stayed hidden until a chain with two actions in it was read.
            var firstFrame = chain.Frames.Count;

            foreach (var letter in text)
            {
                // Every letter in the run is its own frame, sharing the tics and action that
                // follow. "TROO AABB 4 A_Chase" is four frames, not one, and this is the part
                // of DECORATE most often misread by hand.
                var frame = new StateFrame();
                frame.Sprite = sprite;
                frame.Letter = letter.ToString();
                frame.Tics = 1;
                frame.Action = "";
                frame.Line = letters.Line;

                if (bright)
                {
                    frame.Letter = "+" + frame.Letter;
                }

                chain.Frames.Add(frame);
            }

            StateFrame last = chain.Frames[chain.Frames.Count - 1];

            var next = reader.Next(ref line);

            if (next == null)
            {
                return;
            }

            if (next.IsNumber)
            {
                last.Tics = next.Number;

                for (var i = firstFrame; i < chain.Frames.Count; i++)
                {
                    chain.Frames[i].Tics = next.Number;
                }

                next = reader.Next(ref line);

                if (next == null)
                {
                    return;
                }
            }

            if (string.Equals(next.Text, "Bright", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = firstFrame; i < chain.Frames.Count; i++)
                {
                    chain.Frames[i].Letter = "+" + chain.Frames[i].Letter.TrimStart('+');
                }

                next = reader.Next(ref line);

                if (next == null)
                {
                    return;
                }
            }

            if (IsActionName(next.Text))
            {
                for (var i = firstFrame; i < chain.Frames.Count; i++)
                {
                    chain.Frames[i].Action = next.Text;
                }

                last = chain.Frames[chain.Frames.Count - 1];

                ReadActionArguments(reader, ref line, actor, last, problems);

                if (last.JumpLabel != null)
                {
                    // A letter run shares one action, so the jump belongs to the last frame of the
                    // run and the others must not keep "A_Jump" as an action name - the engine has
                    // no action by that name, and they would each be reported.
                    for (var i = firstFrame; i < chain.Frames.Count; i++)
                    {
                        chain.Frames[i].Action = "";
                    }
                }
            }
            else
            {
                // Not an action, so it begins whatever comes next: a keyword, a label or the
                // sprite of the following frame. It goes back.
                reader.PushBack(next, ref line);
            }
        }

        /// <summary>
        /// Reads the parenthesised arguments after an action, and decides what they mean.
        ///
        /// Only <c>A_Jump</c> is honoured, because it is the one action whose arguments change what
        /// the frame does rather than how: it names another state to leave for. Everything else this
        /// engine can do takes no arguments, so a list after those is reported and dropped, frame
        /// and action intact - which is the difference between a mod that animates wrongly and one
        /// that does not animate at all.
        /// </summary>
        private static void ReadActionArguments(Reader reader, ref int line, DecorateActor actor,
                                                StateFrame frame, IList<string> problems)
        {
            var open = reader.Next(ref line);

            if (open == null)
            {
                return;
            }

            if (open.Text != "(")
            {
                reader.PushBack(open, ref line);
                return;
            }

            // Commas are kept as argument boundaries rather than dropped. A_JumpIf's first argument
            // is an expression of several tokens, so the split has to happen here - splitting the
            // flat stream later cannot tell `health < 100, "Pain"` from a comma inside a nested
            // call.
            //
            // Depth matters: `A_JumpIf(CountInv("Clip") > 0, "Pain")` has a closing bracket of its
            // own, and stopping at the first one ended the argument list inside the condition.
            var arguments = new List<List<Token>>();
            var current = new List<Token>();
            var depth = 0;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    problems.Add(actor.Name + " line " + frame.Line +
                                 ": the argument list of '" + frame.Action + "' is not closed");
                    return;
                }

                if (token.Text == "(")
                {
                    depth++;
                }
                else if (token.Text == ")")
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (token.Text == "," && depth == 0)
                {
                    arguments.Add(current);
                    current = new List<Token>();
                    continue;
                }

                current.Add(token);
            }

            arguments.Add(current);

            var isJump = string.Equals(frame.Action, "A_Jump", StringComparison.OrdinalIgnoreCase);
            var isJumpIf = string.Equals(frame.Action, "A_JumpIf", StringComparison.OrdinalIgnoreCase);

            // Two more spellings of the same question. DECORATE has a small family of them, and
            // they mean exactly what the equivalent A_JumpIf expression means - so they are
            // translated into it rather than given a second mechanism. One jump implementation,
            // several spellings of the question.
            var isHealthLower =
                string.Equals(frame.Action, "A_JumpIfHealthLower", StringComparison.OrdinalIgnoreCase);
            var isCloser =
                string.Equals(frame.Action, "A_JumpIfCloser", StringComparison.OrdinalIgnoreCase);

            if (isJumpIf || isHealthLower || isCloser)
            {
                ReadConditionalJump(arguments, actor, frame, problems, isHealthLower, isCloser);
                return;
            }

            if (string.Equals(frame.Action, "A_PlaySound", StringComparison.OrdinalIgnoreCase))
            {
                ReadPlaySound(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_CustomMissile", StringComparison.OrdinalIgnoreCase))
            {
                ReadMissile(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_SpawnItem", StringComparison.OrdinalIgnoreCase))
            {
                ReadSpawnItem(arguments, actor, frame, problems);
                return;
            }

            if (!isJump)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": '" + frame.Action +
                             "' takes no arguments in this engine, so they are ignored; " +
                             "the action itself still runs");
                return;
            }

            if (arguments.Count != 2)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_Jump' needs " +
                             "a chance and a label, so the jump is dropped");
                frame.Action = "";
                return;
            }

            var label = ReadLabel(arguments[1], actor, frame, problems);

            if (label == null)
            {
                frame.Action = "";
                return;
            }

            frame.JumpLabel = label;
            frame.Action = "";

            var chance = arguments[0];

            if (chance.Count != 1 || !chance[0].IsNumber)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_Jump' needs a whole chance such as 128; the jump is dropped");
                frame.JumpLabel = null;
                return;
            }

            var value = chance[0].Number;
            frame.JumpChance = value < 0 ? 0 : (value > 256 ? 256 : value);
        }

        /// <summary>
        /// A_JumpIf and its relatives: a question and a label.
        ///
        /// The three spellings differ only in how the question is written, so they end up in the
        /// same place: <see cref="StateFrame.JumpCondition"/> holds translated text, and the
        /// runtime asks it when the frame ends.
        /// </summary>
        private static void ReadConditionalJump(List<List<Token>> arguments, DecorateActor actor,
                                                StateFrame frame, IList<string> problems,
                                                bool healthLower, bool closer)
        {
            if (arguments.Count != 2)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": '" + frame.Action +
                             "' needs a condition and a label, so the jump is dropped");
                frame.Action = "";
                return;
            }

            var label = ReadLabel(arguments[1], actor, frame, problems);

            if (label == null)
            {
                frame.Action = "";
                return;
            }

            string condition;

            if (healthLower || closer)
            {
                // "(amount, label)": the question is the comparison, written by the file's own
                // spelling. Built here rather than parsed, because there is nothing to parse - the
                // first argument is a single number.
                if (arguments[0].Count != 1 || !arguments[0][0].IsNumber)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": '" + frame.Action +
                                 "' needs a whole number and a label, so the jump is dropped");
                    frame.Action = "";
                    return;
                }

                condition = (healthLower ? "Health() < " : "Distance() < ") +
                            arguments[0][0].Number.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                condition = DecorateCondition.Translate(
                    JoinTokens(arguments[0]), actor.Name, frame.Line, problems);

                if (condition == null)
                {
                    // Already reported. The frame keeps its animation and loses only the jump.
                    frame.Action = "";
                    return;
                }
            }

            frame.JumpLabel = label;
            frame.JumpCondition = condition;
            frame.Action = "";
        }

        /// <summary>
        /// The label an action jumps to, or null with the reason reported.
        ///
        /// A relative offset is legal DECORATE ("skip two frames") and is deliberately not guessed
        /// at: the number counts states, not frames of this chain, and a wrong guess would move the
        /// actor to a frame the author never meant.
        /// </summary>
        private static string ReadLabel(List<Token> tokens, DecorateActor actor, StateFrame frame,
                                        IList<string> problems)
        {
            if (tokens.Count == 1 && tokens[0].IsNumber)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": '" + frame.Action +
                             "' by a frame count (" + tokens[0].Text +
                             ") is not supported; name the label instead. The jump is dropped");
                return null;
            }

            if (tokens.Count != 1)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": '" + frame.Action +
                             "' ends with something that is not a single label; the jump is dropped");
                return null;
            }

            return tokens[0].Text;
        }

        /// <summary>
        /// A_CustomMissile: a projectile the frame fires.
        ///
        /// The name is resolved **now**, while the file is being read, so a typo is a line in the
        /// report rather than a projectile that silently does not appear. The type is then closed
        /// over by the generated call, which is why play time needs no name lookup at all.
        ///
        /// Supported: the actor name and its height. `xoffset` and `angle` shift the launch, and
        /// this engine's spawn places the missile at the actor's own position - so a non-zero value
        /// is named in the report instead of being silently dropped, because a missile fired at an
        /// angle is a different weapon.
        /// </summary>
        private static void ReadMissile(List<List<Token>> arguments, DecorateActor actor,
                                        StateFrame frame, IList<string> problems)
        {
            if (arguments.Count < 1 || arguments[0].Count != 1)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_CustomMissile' needs an actor name, so the frame fires nothing");
                frame.Action = "";
                return;
            }

            var name = arguments[0][0].Text;

            if (ActorNames.TypeOf(name) < 0)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": no such actor '" + name +
                             "' for 'A_CustomMissile', so the frame fires nothing. " +
                             "This engine knows the built-in actors by their ZDoom names");
                frame.Action = "";
                return;
            }

            frame.MissileName = name;
            frame.MissileHeight = 32;

            if (arguments.Count > 1)
            {
                if (arguments[1].Count == 1 && arguments[1][0].IsNumber)
                {
                    frame.MissileHeight = arguments[1][0].Number;
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line +
                                 ": 'A_CustomMissile' height must be a whole number; 32 is used");
                }
            }

            ReportIgnoredArguments(arguments, 2, actor, frame, problems, "A_CustomMissile",
                                   "xoffset and angle");

            frame.Action = "";
        }

        /// <summary>
        /// A_SpawnItem: something the frame places in the world.
        ///
        /// The difference from A_CustomMissile is not a detail: this spawns a thing where the actor
        /// stands, unaimed and motionless. A health pickup and a fireball are both "an actor", and
        /// only one of them travels.
        /// </summary>
        private static void ReadSpawnItem(List<List<Token>> arguments, DecorateActor actor,
                                          StateFrame frame, IList<string> problems)
        {
            if (arguments.Count < 1 || arguments[0].Count != 1)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_SpawnItem' needs an actor name, so the frame spawns nothing");
                frame.Action = "";
                return;
            }

            var name = arguments[0][0].Text;

            if (ActorNames.TypeOf(name) < 0)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": no such actor '" + name +
                             "' for 'A_SpawnItem', so the frame spawns nothing. " +
                             "This engine knows the built-in actors by their ZDoom names");
                frame.Action = "";
                return;
            }

            frame.SpawnName = name;
            frame.SpawnDistance = 0;

            if (arguments.Count > 1)
            {
                if (arguments[1].Count == 1 && arguments[1][0].IsNumber)
                {
                    frame.SpawnDistance = arguments[1][0].Number;
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line +
                                 ": 'A_SpawnItem' distance must be a whole number; zero is used");
                }
            }

            ReportIgnoredArguments(arguments, 2, actor, frame, problems, "A_SpawnItem", "zheight");

            frame.Action = "";
        }

        /// <summary>
        /// Says that arguments past a supported count are ignored, once per frame.
        ///
        /// Not silent, because these shift where a projectile appears or how high a spawned thing
        /// sits - and a mod that tuned them would otherwise get a subtly different actor with no
        /// hint that anything was dropped.
        /// </summary>
        private static void ReportIgnoredArguments(List<List<Token>> arguments, int from,
                                                   DecorateActor actor, StateFrame frame,
                                                   IList<string> problems, string action,
                                                   string what)
        {
            for (var i = from; i < arguments.Count; i++)
            {
                if (arguments[i].Count == 0)
                {
                    continue;
                }

                var zero = arguments[i].Count == 1 &&
                           ((arguments[i][0].IsNumber && arguments[i][0].Number == 0) ||
                            string.Equals(arguments[i][0].Text, "0", StringComparison.Ordinal));

                if (zero)
                {
                    continue;
                }

                problems.Add(actor.Name + " line " + frame.Line + ": '" + action + "' " + what +
                             " are ignored - this engine spawns at the actor's own position; " +
                             "the frame still spawns");

                // Once per frame is enough: the rest of the list is the same complaint.
                return;
            }
        }

        /// <summary>
        /// A_PlaySound: the sound a frame plays when it runs.
        ///
        /// The name is a **sound name**, resolved through SNDINFO when it is played, exactly like
        /// the sounds an actor carries. `channel` is reported rather than honoured: this engine has
        /// no looping sound channels, and accepting one would promise a sound that keeps playing.
        /// </summary>
        private static void ReadPlaySound(List<List<Token>> arguments, DecorateActor actor,
                                          StateFrame frame, IList<string> problems)
        {
            if (arguments.Count < 1 || arguments[0].Count != 1)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_PlaySound' needs a sound name, so the frame stays silent");
                frame.Action = "";
                return;
            }

            frame.PlaySoundName = arguments[0][0].Text;
            frame.Action = "";

            if (arguments.Count >= 2 && arguments[1].Count == 1 && arguments[1][0].IsNumber &&
                arguments[1][0].Number != 0)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_PlaySound' channel " +
                             arguments[1][0].Number + " is ignored - this engine has no looping " +
                             "sound channels; the sound plays once");
            }

            if (arguments.Count >= 3)
            {
                if (arguments[2].Count == 1 && arguments[2][0].IsNumber)
                {
                    var volume = arguments[2][0].Number;
                    frame.PlaySoundVolume = volume < 0 ? 0 : (volume > 100 ? 100 : volume);
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line +
                                 ": 'A_PlaySound' volume must be a whole number out of 100; the " +
                                 "default is used");
                }
            }
        }

        /// <summary>
        /// The tokens of one argument as text.
        ///
        /// Spaces between words are not preserved and do not need to be: the condition reader is
        /// whitespace-insensitive and the tokens are already separated.
        /// </summary>
        private static string JoinTokens(List<Token> tokens)
        {
            var text = new System.Text.StringBuilder();

            foreach (var token in tokens)
            {
                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(token.Text);
            }

            return text.ToString();
        }


        private static bool IsActionName(string text)
        {
            if (text.Length == 0 || !(text[0] == 'A' || text[0] == 'a'))
            {
                // A frame's action is always spelled A_<Something>; anything else is the next
                // frame or a keyword, which is what keeps "TROO A 10" followed by "SARG B 8"
                // from reading "SARG" as an action name.
                return false;
            }

            if (!text.StartsWith("A_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Known to the engine, or reported later by the emitter with the full list.
            return true;
        }

        private static void SkipRestOfFrame(Reader reader, ref int line)
        {
            // Only used after an unreadable sprite: the letters, tics and action of that frame
            // are consumed so the next frame is read from a sane place.
            var letters = reader.Next(ref line);

            if (letters == null)
            {
                return;
            }

            var next = reader.Next(ref line);

            if (next == null)
            {
                return;
            }

            if (!next.IsNumber)
            {
                reader.PushBack(next, ref line);
                return;
            }

            var action = reader.Next(ref line);

            if (action != null && !IsActionName(action.Text) &&
                !string.Equals(action.Text, "Bright", StringComparison.OrdinalIgnoreCase))
            {
                reader.PushBack(action, ref line);
            }
        }

        private static void ReadFlag(DecorateActor actor, string name, bool value)
        {
            if (name.Length == 0)
            {
                return;
            }

            actor.Flags.Add(new KeyValuePair<string, bool>(NormaliseFlagName(name), value));
        }

        private static void AddFlags(DecorateActor actor, string[] names, bool value)
        {
            foreach (var name in names)
            {
                actor.Flags.Add(new KeyValuePair<string, bool>(name, value));
            }
        }

        /// <summary>
        /// Turns DECORATE's shouting into the engine's PascalCase.
        ///
        /// Flag names are compared case-insensitively when applied, so this is cosmetic for
        /// the compiler - but the generated script is meant to be read by a person, and
        /// "NOGRAVITY" next to "Radius(20)" reads like two languages in one file.
        /// </summary>
        private static string NormaliseFlagName(string name)
        {
            // Underscores are legal in DECORATE flag names and absent in the engine's, so
            // "NO_BLOCKMAP" and "NOBLOCKMAP" both become NoBlockmap.
            var clean = name.Replace("_", "");

            if (clean.Length == 0)
            {
                return clean;
            }

            var text = new StringBuilder(clean.Length);
            text.Append(char.ToUpperInvariant(clean[0]));

            for (var i = 1; i < clean.Length; i++)
            {
                text.Append(char.ToLowerInvariant(clean[i]));
            }

            return text.ToString();
        }

        // ------------------------------------------------------------ tokens

        private sealed class Token
        {
            public string Text;
            public bool IsNumber;
            public int Number;
            public int Line;
        }

        /// <summary>
        /// The whole lexer: words, numbers, quoted strings, braces and colons.
        ///
        /// A word ends at whitespace, a brace, a colon, a quote or the start of a comment,
        /// which is every separator DECORATE uses. Numbers are recognised by a leading digit
        /// or sign so that a doomednum can be told from an actor name, and hexadecimal is
        /// accepted because doomednums are often written that way.
        ///
        /// One token can be pushed back, because a frame ends where the next thing stops being
        /// part of it and the reader has to look at that thing before deciding.
        /// </summary>
        private sealed class Reader
        {
            private readonly string text;
            private int position;
            private Token pushed;

            public Reader(string text)
            {
                this.text = text ?? "";
            }

            public void PushBack(Token token, ref int line)
            {
                pushed = token;
                line = token.Line;
            }

            /// <summary>
            /// Whether the next token is a colon, without consuming anything else.
            ///
            /// Used to tell a label ("See:") from a frame ("TROO A 10"): the word and the
            /// colon arrive as separate tokens, and looking one ahead is the whole difference.
            /// </summary>
            public bool PeekIsColon(ref int line)
            {
                var token = Next(ref line);

                if (token == null)
                {
                    return false;
                }

                if (token.Text == ":")
                {
                    return true;
                }

                PushBack(token, ref line);
                return false;
            }

            public Token Next(ref int line)
            {
                if (pushed != null)
                {
                    var saved = pushed;
                    pushed = null;
                    line = saved.Line;
                    return saved;
                }

                SkipTrivia(ref line);

                if (position >= text.Length)
                {
                    return null;
                }

                var token = new Token();
                token.Line = line;

                var c = text[position];

                if (c == '{' || c == '}' || c == ':' || c == '(' || c == ')' || c == ',')
                {
                    position++;
                    token.Text = c.ToString();
                    return token;
                }

                if (c == '"')
                {
                    position++;
                    var start = position;

                    while (position < text.Length && text[position] != '"')
                    {
                        if (text[position] == '\n')
                        {
                            line++;
                        }

                        position++;
                    }

                    token.Text = text.Substring(start, position - start);

                    if (position < text.Length)
                    {
                        position++;
                    }

                    return token;
                }

                var wordStart = position;

                // A sign belongs to the word only when a digit follows it, so "+SOLID" stays
                // a flag and "-5" stays a number.
                if ((c == '+' || c == '-') &&
                    position + 1 < text.Length && IsDigitOrDot(text[position + 1]))
                {
                    position++;
                }

                while (position < text.Length && !IsSeparator(text[position]))
                {
                    position++;
                }

                token.Text = text.Substring(wordStart, position - wordStart);

                if (token.Text.Length > 0 && (char.IsDigit(token.Text[0]) ||
                                              ((token.Text[0] == '-' || token.Text[0] == '+') &&
                                               token.Text.Length > 1)))
                {
                    int number;

                    if (TryParseNumber(token.Text, out number))
                    {
                        token.IsNumber = true;
                        token.Number = number;
                    }
                }

                return token;
            }

            private static bool TryParseNumber(string text, out int number)
            {
                number = 0;

                var body = text;
                var negative = false;

                if (body.Length > 0 && (body[0] == '-' || body[0] == '+'))
                {
                    negative = body[0] == '-';
                    body = body.Substring(1);
                }

                if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(body.Substring(2), NumberStyles.HexNumber,
                                      CultureInfo.InvariantCulture, out number))
                    {
                        return false;
                    }

                    if (negative)
                    {
                        number = -number;
                    }

                    return true;
                }

                if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    return false;
                }

                if (negative)
                {
                    number = -number;
                }

                return true;
            }

            private void SkipTrivia(ref int line)
            {
                while (position < text.Length)
                {
                    var c = text[position];

                    if (c == '\n')
                    {
                        line++;
                        position++;
                        continue;
                    }

                    if (char.IsWhiteSpace(c))
                    {
                        position++;
                        continue;
                    }

                    if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
                    {
                        while (position < text.Length && text[position] != '\n')
                        {
                            position++;
                        }

                        continue;
                    }

                    if (c == '/' && position + 1 < text.Length && text[position + 1] == '*')
                    {
                        position += 2;

                        while (position + 1 < text.Length &&
                               !(text[position] == '*' && text[position + 1] == '/'))
                        {
                            if (text[position] == '\n')
                            {
                                line++;
                            }

                            position++;
                        }

                        position = Math.Min(position + 2, text.Length);
                        continue;
                    }

                    return;
                }
            }

            private static bool IsSeparator(char c)
            {
                // Parentheses and the comma are separators so that `A_Jump(128, "See")` reads as
                // an action name and its arguments rather than as one long word. Nothing else in
                // DECORATE uses them outside a quoted string, and a quoted string is handled
                // before this is consulted.
                return char.IsWhiteSpace(c) || c == '{' || c == '}' || c == ':' || c == '"' ||
                       c == '(' || c == ')' || c == ',';
            }

            private static bool IsDigitOrDot(char c)
            {
                return char.IsDigit(c) || c == '.';
            }
        }
    }
}
