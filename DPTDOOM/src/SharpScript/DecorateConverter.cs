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

        /// <summary>True when the frame's action was A_SpawnItemEx.</summary>
        public bool HasSpawnItemEx;

        /// <summary>Actor type to spawn, by DECORATE name.</summary>
        public string SpawnExName;

        /// <summary>
        /// The seven numeric arguments, already translated to SharpScript expressions:
        /// x/y/z offsets, x/y/z velocities and angle. Kept as text because random() must run
        /// when the frame runs, not while the mod is being loaded.
        /// </summary>
        public string[] SpawnExValues;

        /// <summary>The expression for failchance, or null when there is no chance.</summary>
        public string SpawnExFailChance;

        /// <summary>A_SpawnItemEx's supported flags, summed from SXF_ names.</summary>
        public int SpawnExFlags;

        /// <summary>
        /// True when the frame's action was A_LookEx.
        ///
        /// A flag rather than "LookExFov != 0", because zero is a meaningful field of view: it is
        /// how a mod writes "half a circle", and reading it as "no A_LookEx here" would turn the
        /// most common call in the family into plain A_Look.
        /// </summary>
        public bool HasLookEx;

        /// <summary>A_LookEx's flags, already summed from the LOF_ names.</summary>
        public int LookExFlags;

        /// <summary>A_LookEx's minimum sight distance, in map units. Zero means no minimum.</summary>
        public int LookExMinSee;

        /// <summary>A_LookEx's maximum sight distance, in map units. Zero means no maximum.</summary>
        public int LookExMaxSee;

        /// <summary>A_LookEx's maximum hearing distance, in map units. Zero means no maximum.</summary>
        public int LookExMaxHear;

        /// <summary>A_LookEx's field of view in degrees. Zero means half a circle.</summary>
        public int LookExFov;

        /// <summary>State to enter instead of the see state, or null for the actor's own.</summary>
        public string LookExSeeState;

        /// <summary>Flag the frame sets or clears, by the engine's own name, or null.</summary>
        public string ChangeFlagName;

        /// <summary>Whether <see cref="ChangeFlagName"/> is set rather than cleared.</summary>
        public bool ChangeFlagValue;

        /// <summary>True when the frame's action was A_SetTics.</summary>
        public bool HasSetTics;

        /// <summary>How long the frame lasts, in tics.</summary>
        public int SetTicsValue;

        /// <summary>True when the frame's action was A_Recoil.</summary>
        public bool HasRecoil;

        /// <summary>How far the frame pushes the actor backwards, in map units.</summary>
        public int RecoilDistance;

        /// <summary>True when the frame's action was A_FaceTarget with arguments.</summary>
        public bool HasFaceTarget;

        /// <summary>How far the actor may turn toward its target, in degrees. Zero means all the way.</summary>
        public int FaceMaxTurn;

        /// <summary>Offset added to the resulting angle, in degrees.</summary>
        public int FaceOffset;

        /// <summary>True when the frame's action was A_Chase with arguments.</summary>
        public bool HasChase;

        /// <summary>A_Chase's flags, already summed from the CHF_ names.</summary>
        public int ChaseFlags;

        /// <summary>State to use instead of the actor's melee state, or null for the actor's own.</summary>
        public string ChaseMeleeState;

        /// <summary>State to use instead of the actor's missile state, or null for the actor's own.</summary>
        public string ChaseMissileState;

        /// <summary>True when the frame's action was A_Explode with arguments.</summary>
        public bool HasExplode;

        /// <summary>How much damage the blast does at its centre.</summary>
        public int ExplodeDamage = 128;

        /// <summary>How far the blast reaches. Zero or negative means the same as the damage.</summary>
        public int ExplodeRadius;

        /// <summary>A_Explode's flags, already summed from the XF_ names.</summary>
        public int ExplodeFlags = ExplosionFlags.HurtSource;

        /// <summary>True when the frame's action was A_CustomMeleeAttack.</summary>
        public bool HasCustomMelee;

        /// <summary>The damage expression, evaluated when the frame runs.</summary>
        public string MeleeDamageExpression;

        /// <summary>Sound on a hit, by SNDINFO name, or null.</summary>
        public string MeleeHitSound;

        /// <summary>Sound on a miss, by SNDINFO name, or null.</summary>
        public string MeleeMissSound;

        /// <summary>True when the frame's action was A_CustomPunch.</summary>
        public bool HasCustomPunch;

        /// <summary>The punch's damage expression, evaluated when the frame runs.</summary>
        public string PunchDamageExpression;

        /// <summary>Whether the punch skips the random 1..8 multiplier.</summary>
        public bool PunchNoRandom;

        /// <summary>The supported CPF flags.</summary>
        public int PunchFlags;

        /// <summary>Puff actor name, by DECORATE name.</summary>
        public string PunchPuffName;

        /// <summary>Punch range in map units, as an expression.</summary>
        public string PunchRangeExpression;

        /// <summary>Sound on a miss, by SNDINFO name, or null.</summary>
        public string PunchMissSound;

        /// <summary>Sound on a hit, by SNDINFO name, or null.</summary>
        public string PunchHitSound;

        /// <summary>True for A_NoBlocking, including its optional drop argument.</summary>
        public bool HasNoBlocking;

        /// <summary>Whether A_NoBlocking was asked to drop metadata (unsupported here).</summary>
        public bool NoBlockingDrop = true;

        /// <summary>True for A_SetHealth.</summary>
        public bool HasSetHealth;

        /// <summary>Health expression and AAPTR pointer.</summary>
        public string SetHealthExpression;
        public int SetHealthPointer;

        /// <summary>True for A_SetAngle.</summary>
        public bool HasSetAngle;

        /// <summary>Angle expression, flags and AAPTR pointer.</summary>
        public string SetAngleExpression;
        public int SetAngleFlags;
        public int SetAnglePointer;

        /// <summary>True for A_ChangeVelocity.</summary>
        public bool HasChangeVelocity;

        /// <summary>x/y/z expressions, CVF flags and AAPTR pointer.</summary>
        public string[] ChangeVelocityExpressions;
        public int ChangeVelocityFlags;
        public int ChangeVelocityPointer;

        /// <summary>True for A_ScaleVelocity.</summary>
        public bool HasScaleVelocity;

        /// <summary>Scale expression and AAPTR pointer.</summary>
        public string ScaleVelocityExpression;
        public int ScaleVelocityPointer;

        /// <summary>True for A_StartSound.</summary>
        public bool HasStartSound;

        /// <summary>Sound name, channel slot, supported presentation flags.</summary>
        public string StartSoundName;
        public int StartSoundSlot;
        public int StartSoundFlags;

        /// <summary>Volume and attenuation expressions, evaluated when the frame runs.</summary>
        public string StartSoundVolumeExpression;
        public string StartSoundAttenuationExpression;
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
        /// DoomEdNums assigned to actors that DECORATE names but does not number.
        ///
        /// These are internal identities only: a map thing is 16-bit and can never ask for them,
        /// so they cannot collide with real map placement. They let the existing actor table,
        /// script names and A_SpawnItemEx work without inventing a second type system.
        /// </summary>
        private const int FirstInternalDoomEdNum = 100000;

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

            // Resolve every target before emitting any state. A_SpawnItemEx can name the actor it
            // belongs to, or one defined later in the file, so registering names as a side effect of
            // emission would make valid references depend on their position in the text.
            var targets = new Dictionary<DecorateActor, MobjInfo>();

            foreach (var actor in actors)
            {
                var target = ResolveTarget(actor, table, problems);

                if (target == null)
                {
                    continue;
                }

                targets[actor] = target;

                // The friendly name is needed by A_SpawnItemEx and by a later block that refines
                // the same actor. Register it for every real target, not only for an unnumbered
                // actor: a mod may give a custom actor an explicit spawnid and then spawn it by
                // name. The name is registered only after a real target exists, so a refused block
                // cannot leave a name pointing at nothing.
                ActorNames.Register(actor.Name, DoomInfo.TypeOf(target.DoomEdNum));
            }

            foreach (var actor in actors)
            {
                MobjInfo target;

                if (!targets.TryGetValue(actor, out target))
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
                EmitSounds(script, actor, problems);

                script.Append("}\n");

                // Conditions and spawn expressions come after the definition they belong to, as
                // top-level functions: the state layer looks them up by name when the frame runs,
                // which is long after this script has run.
                EmitConditions(script, actor);
                EmitSpawnItemExpressions(script, actor);
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

            // A named actor without a number may already have been defined in an earlier lump.
            // Resolve that name before allocating anything: refining an actor must not consume a
            // second slot. Built-in names resolve through the same path, so "ACTOR DoomImp" works
            // just as a custom "ACTOR kgGibs" does.
            if (number < 0)
            {
                var namedType = ActorNames.TypeOf(actor.Name);

                if (namedType >= 0 && namedType < table.Length)
                {
                    number = table[namedType].DoomEdNum;
                }
            }

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
                // A DECORATE actor does not need a DoomEdNum. Give it an internal one so the
                // existing actor table and script names can represent it. Only a block with no
                // definition at all is left alone; an unsupported property still gets its report
                // below rather than being silently swallowed.
                if (actor.Numbers.Count == 0 && actor.Flags.Count == 0 &&
                    actor.Sounds.Count == 0 && actor.Chains.Count == 0 &&
                    actor.Unsupported.Count == 0)
                {
                    return null;
                }

                number = NextInternalDoomEdNum(table);
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

        private static int NextInternalDoomEdNum(MobjInfo[] table)
        {
            for (var candidate = FirstInternalDoomEdNum; candidate < int.MaxValue; candidate++)
            {
                var used = false;

                for (var i = 0; i < table.Length; i++)
                {
                    if (table[i].DoomEdNum == candidate)
                    {
                        used = true;
                        break;
                    }
                }

                if (!used)
                {
                    return candidate;
                }
            }

            // There is no realistic way to reach this, but returning an existing number would be
            // worse than exhausting the reserve: it would silently rewrite somebody else's actor.
            return FirstInternalDoomEdNum;
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
        private static void EmitSounds(StringBuilder script, DecorateActor actor,
                                           IList<string> problems)
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

            EmitFrameActions(script, actor, problems);
        }

        /// <summary>
        /// Attaches each frame's projectile, spawned thing, or look test.
        ///
        /// A separate pass from the sound for the same reason a sound is separate from a
        /// transition: these are work the frame performs. A frame carries at most one action in
        /// DECORATE, so at most one of these applies to any frame, and each is installed as that
        /// frame's action.
        /// </summary>
        private static void EmitFrameActions(StringBuilder script, DecorateActor actor,
                                                    IList<string> problems)
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

                    if (frame.ChangeFlagName != null)
                    {
                        script.Append("    ChangeFlag(\"").Append(name).Append("\", \"")
                              .Append(frame.ChangeFlagName).Append("\", ")
                              .Append(frame.ChangeFlagValue ? "true" : "false").Append(");\n");
                    }

                    if (frame.HasSetTics)
                    {
                        script.Append("    SetTics(\"").Append(name).Append("\", ")
                              .Append(frame.SetTicsValue.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasRecoil)
                    {
                        script.Append("    Recoil(\"").Append(name).Append("\", ")
                              .Append(FixedFromInt(frame.RecoilDistance)).Append(");\n");
                    }

                    if (frame.HasFaceTarget)
                    {
                        script.Append("    FaceTarget(\"").Append(name).Append("\", ")
                              .Append(frame.FaceMaxTurn.ToString(CultureInfo.InvariantCulture))
                              .Append(", ")
                              .Append(frame.FaceOffset.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasChase)
                    {
                        // An empty name means "the actor's own state", which is what the script
                        // call reads as -1. A state this actor declared is written as its frame
                        // name; anything else is written as-is and resolved by the script layer,
                        // the same rule as a jump.
                        script.Append("    Chase(\"").Append(name).Append("\", \"")
                              .Append(ChaseStateName(actor, frame.ChaseMeleeState, problems, frame))
                              .Append("\", \"")
                              .Append(ChaseStateName(actor, frame.ChaseMissileState, problems, frame))
                              .Append("\", ")
                              .Append(frame.ChaseFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasExplode)
                    {
                        script.Append("    Explode(\"").Append(name).Append("\", ")
                              .Append(frame.ExplodeDamage.ToString(CultureInfo.InvariantCulture))
                              .Append(", ")
                              .Append(frame.ExplodeRadius.ToString(CultureInfo.InvariantCulture))
                              .Append(", ")
                              .Append(frame.ExplodeFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasSpawnItemEx)
                    {
                        // Name resolution happens here, after every actor in the file has been
                        // given a type. The frame is read before that table exists, so checking
                        // the name at read time rejected a valid custom actor - including the
                        // actor that owns this very frame.
                        if (ActorNames.TypeOf(frame.SpawnExName) < 0)
                        {
                            problems.Add(actor.Name + " line " + frame.Line + ": no such actor '" +
                                         frame.SpawnExName + "' for 'A_SpawnItemEx', so the frame " +
                                         "spawns nothing. This engine knows the built-in actors " +
                                         "by their ZDoom names");
                            continue;
                        }

                        script.Append("    SpawnItemEx(\"").Append(name).Append("\", \"")
                              .Append(frame.SpawnExName).Append("\", ");

                        for (var value = 0; value < frame.SpawnExValues.Length; value++)
                        {
                            script.Append('"')
                                  .Append(SpawnExFunctionName(actor, chain.Label, i + 1, value))
                                  .Append("\", ");
                        }

                        var failName = frame.SpawnExFailChance == null
                            ? ""
                            : SpawnExFunctionName(actor, chain.Label, i + 1, 7);

                        script.Append(frame.SpawnExFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(", \"").Append(failName).Append("\");\n");
                    }

                    if (frame.HasCustomPunch)
                    {
                        script.Append("    CustomPunch(\"").Append(name).Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 8))
                              .Append("\", ")
                              .Append(frame.PunchNoRandom ? "true" : "false")
                              .Append(", ")
                              .Append(frame.PunchFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(", \"")
                              .Append(frame.PunchPuffName ?? "BulletPuff")
                              .Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 9))
                              .Append("\", \"")
                              .Append(frame.PunchHitSound ?? "")
                              .Append("\", \"")
                              .Append(frame.PunchMissSound ?? "")
                              .Append("\");\n");
                    }

                    if (frame.HasCustomMelee)
                    {
                        script.Append("    CustomMeleeAttack(\"").Append(name).Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 8))
                              .Append("\", \"")
                              .Append(frame.MeleeHitSound ?? "")
                              .Append("\", \"")
                              .Append(frame.MeleeMissSound ?? "")
                              .Append("\");\n");
                    }

                    if (frame.HasNoBlocking)
                    {
                        script.Append("    NoBlocking(\"").Append(name).Append("\", ")
                              .Append(frame.NoBlockingDrop ? "true" : "false").Append(");\n");
                    }

                    if (frame.HasSetHealth)
                    {
                        script.Append("    SetHealth(\"").Append(name).Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 10))
                              .Append("\", ")
                              .Append(frame.SetHealthPointer.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasSetAngle)
                    {
                        script.Append("    SetAngle(\"").Append(name).Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 11))
                              .Append("\", ")
                              .Append(frame.SetAngleFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(", ")
                              .Append(frame.SetAnglePointer.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasChangeVelocity)
                    {
                        script.Append("    ChangeVelocity(\"").Append(name).Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 12))
                              .Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 13))
                              .Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 14))
                              .Append("\", ")
                              .Append(frame.ChangeVelocityFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(", ")
                              .Append(frame.ChangeVelocityPointer.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasScaleVelocity)
                    {
                        script.Append("    ScaleVelocity(\"").Append(name).Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 15))
                              .Append("\", ")
                              .Append(frame.ScaleVelocityPointer.ToString(CultureInfo.InvariantCulture))
                              .Append(");\n");
                    }

                    if (frame.HasStartSound)
                    {
                        script.Append("    StartSound(\"").Append(name).Append("\", \"")
                              .Append(frame.StartSoundName).Append("\", ")
                              .Append(frame.StartSoundSlot.ToString(CultureInfo.InvariantCulture))
                              .Append(", ")
                              .Append(frame.StartSoundFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 16))
                              .Append("\", \"")
                              .Append(SpawnExFunctionName(actor, chain.Label, i + 1, 17))
                              .Append("\");\n");
                    }

                    if (frame.HasLookEx)
                    {
                        // The see state is written as a frame of this actor when the mod named one
                        // of its own chains, and as the bare name otherwise - which the script
                        // layer resolves the same way a "Goto See" does. Same rule as a jump, for
                        // the same reason: a mod's own chain has to win over the engine's.
                        var seeState = "";

                        if (frame.LookExSeeState != null)
                        {
                            if (HasChain(actor, frame.LookExSeeState))
                            {
                                seeState = FrameName(frame.LookExSeeState, 1);
                            }
                            else
                            {
                                // The actor's own see state is used, which for a mod that did not
                                // write a chain of that name is the inherited one - the same thing
                                // it asked for. Saying so beats emitting a name the script would
                                // refuse to resolve and losing the whole actor over it.
                                problems.Add(actor.Name + " line " + frame.Line + ": 'A_LookEx' " +
                                             "names the state '" + frame.LookExSeeState +
                                             "', which is not one of this actor's own chains, so " +
                                             "the actor's own see state is used");
                            }
                        }

                        script.Append("    LookEx(\"").Append(name).Append("\", ")
                              .Append(frame.LookExFlags.ToString(CultureInfo.InvariantCulture))
                              .Append(", ")
                              .Append(FixedFromInt(frame.LookExMinSee)).Append(", ")
                              .Append(FixedFromInt(frame.LookExMaxSee)).Append(", ")
                              .Append(FixedFromInt(frame.LookExMaxHear)).Append(", ")
                              .Append(frame.LookExFov.ToString(CultureInfo.InvariantCulture))
                              .Append(", \"").Append(seeState).Append("\");\n");
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
        /// The function that evaluates one A_SpawnItemEx numeric argument at play time.
        ///
        /// The name is derived from the actor, label, frame and argument, so it remains stable when
        /// the emission order changes and cannot collide with a conditional jump's Cond_ prefix.
        /// </summary>
        private static string SpawnExFunctionName(DecorateActor actor, string label, int index,
                                                  int argument)
        {
            return "SpawnEx_" + ConditionName(actor, label, index).Substring(5) + "_" +
                   argument.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Writes the expressions as functions rather than folding them into the action call.
        /// A function is needed because random() must draw when the frame runs; evaluating while
        /// the mod loads would make every run identical and would draw from no world at all.
        /// </summary>
        private static void EmitSpawnItemExpressions(StringBuilder script, DecorateActor actor)
        {
            for (var chainIndex = 0; chainIndex < actor.Chains.Count; chainIndex++)
            {
                var chain = actor.Chains[chainIndex];

                for (var i = 0; i < chain.Frames.Count; i++)
                {
                    var frame = chain.Frames[i];

                    if (!frame.HasSpawnItemEx && !frame.HasCustomMelee && !frame.HasCustomPunch &&
                        !frame.HasSetHealth && !frame.HasSetAngle && !frame.HasChangeVelocity &&
                        !frame.HasScaleVelocity && !frame.HasStartSound)
                    {
                        continue;
                    }

                    if (frame.HasSpawnItemEx)
                    {
                        for (var value = 0; value < frame.SpawnExValues.Length; value++)
                        {
                            EmitSpawnExpression(script, actor, chain.Label, i + 1, value,
                                frame.SpawnExValues[value]);
                        }

                        if (frame.SpawnExFailChance != null)
                        {
                            EmitSpawnExpression(script, actor, chain.Label, i + 1, 7,
                                frame.SpawnExFailChance);
                        }
                    }

                    if (frame.HasCustomMelee)
                    {
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 8,
                            frame.MeleeDamageExpression);
                    }

                    if (frame.HasCustomPunch)
                    {
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 8,
                            frame.PunchDamageExpression);
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 9,
                            frame.PunchRangeExpression);
                    }

                    if (frame.HasSetHealth)
                    {
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 10,
                            frame.SetHealthExpression);
                    }

                    if (frame.HasSetAngle)
                    {
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 11,
                            frame.SetAngleExpression);
                    }

                    if (frame.HasChangeVelocity)
                    {
                        for (var value = 0; value < frame.ChangeVelocityExpressions.Length; value++)
                        {
                            EmitSpawnExpression(script, actor, chain.Label, i + 1, 12 + value,
                                frame.ChangeVelocityExpressions[value]);
                        }
                    }

                    if (frame.HasScaleVelocity)
                    {
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 15,
                            frame.ScaleVelocityExpression);
                    }

                    if (frame.HasStartSound)
                    {
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 16,
                            frame.StartSoundVolumeExpression);
                        EmitSpawnExpression(script, actor, chain.Label, i + 1, 17,
                            frame.StartSoundAttenuationExpression);
                    }
                }
            }
        }

        private static void EmitSpawnExpression(StringBuilder script, DecorateActor actor,
                                                string label, int index, int argument,
                                                string expression)
        {
            script.Append("\n").Append(ScriptSyntax.FunctionKeyword).Append(' ')
                  .Append(SpawnExFunctionName(actor, label, index, argument))
                  .Append("() {\n")
                  .Append("    return ").Append(expression).Append(";\n")
                  .Append("}\n");
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

            // An empty argument list - "A_Look()" - is not an argument list. DECORATE allows the
            // parentheses and mods write them, so complaining about them would be a report line
            // about correct input. The call is still dispatched: "A_LookEx()" is a perfectly good
            // request for every default, and skipping the handler would lose it.
            var hasArguments = !(arguments.Count == 1 && arguments[0].Count == 0);

            if (!hasArguments)
            {
                arguments = new List<List<Token>>();
            }

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

            if (string.Equals(frame.Action, "A_SpawnItemEx", StringComparison.OrdinalIgnoreCase))
            {
                ReadSpawnItemEx(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_LookEx", StringComparison.OrdinalIgnoreCase))
            {
                ReadLookEx(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_ChangeFlag", StringComparison.OrdinalIgnoreCase))
            {
                ReadChangeFlag(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_SetTics", StringComparison.OrdinalIgnoreCase))
            {
                ReadSetTics(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_Recoil", StringComparison.OrdinalIgnoreCase))
            {
                ReadRecoil(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_FaceTarget", StringComparison.OrdinalIgnoreCase))
            {
                ReadFaceTarget(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_Chase", StringComparison.OrdinalIgnoreCase))
            {
                ReadChase(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_Explode", StringComparison.OrdinalIgnoreCase))
            {
                ReadExplode(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_CustomMeleeAttack", StringComparison.OrdinalIgnoreCase))
            {
                ReadCustomMelee(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_CustomPunch", StringComparison.OrdinalIgnoreCase))
            {
                ReadCustomPunch(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_NoBlocking", StringComparison.OrdinalIgnoreCase))
            {
                ReadNoBlocking(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_SetHealth", StringComparison.OrdinalIgnoreCase))
            {
                ReadSetHealth(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_SetAngle", StringComparison.OrdinalIgnoreCase))
            {
                ReadSetAngle(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_ChangeVelocity", StringComparison.OrdinalIgnoreCase))
            {
                ReadChangeVelocity(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_ScaleVelocity", StringComparison.OrdinalIgnoreCase))
            {
                ReadScaleVelocity(arguments, actor, frame, problems);
                return;
            }

            if (string.Equals(frame.Action, "A_StartSound", StringComparison.OrdinalIgnoreCase))
            {
                ReadStartSound(arguments, actor, frame, problems);
                return;
            }

            if (!isJump)
            {
                if (hasArguments)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": '" + frame.Action +
                                 "' takes no arguments in this engine, so they are ignored; " +
                                 "the action itself still runs");
                }

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
        /// A_SpawnItemEx: an object with offsets, velocity, angle, flags and a fail chance.
        ///
        /// The signature is taken from ZDoom's attacks.zs, not from memory:
        /// (missile, xofs, yofs, zofs, xvel, yvel, zvel, angle, flags, failchance, tid).
        /// The numeric arguments are translated rather than evaluated now: ademo1.wad uses
        /// expressions such as "15 - random(0,255) * 0.1171875", and random() belongs to the
        /// moment the frame runs, on the world generator.
        /// </summary>
        private static void ReadSpawnItemEx(List<List<Token>> arguments, DecorateActor actor,
                                            StateFrame frame, IList<string> problems)
        {
            if (arguments.Count < 1 || arguments[0].Count != 1)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_SpawnItemEx' needs an actor name, so the frame spawns nothing");
                frame.Action = "";
                return;
            }

            var name = arguments[0][0].Text;

            frame.SpawnExName = name;
            frame.SpawnExValues = new string[7];
            frame.SpawnExFlags = 0;
            frame.HasSpawnItemEx = true;
            frame.Action = "";

            for (var i = 0; i < frame.SpawnExValues.Length; i++)
            {
                frame.SpawnExValues[i] = ReadSpawnExpression(
                    arguments, i + 1, actor, frame, problems, "0");
            }

            if (arguments.Count > 8 && arguments[8].Count > 0)
            {
                int flags;
                string unknown;
                string unsupported;

                if (TryReadSpawnFlags(arguments[8], out flags, out unknown, out unsupported))
                {
                    frame.SpawnExFlags = flags;
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_SpawnItemEx' flag '" +
                                 unknown + "' is not one this engine knows, so the spawn keeps " +
                                 "the flags it did understand");
                    frame.SpawnExFlags = flags;
                }

                if (unsupported.Length > 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_SpawnItemEx' flags " +
                                 unsupported + " are not supported here (master, pitch, render, " +
                                 "specials or TIDs have no counterpart); the supported flags " +
                                 "still run");
                }

                if ((flags & SpawnItemExFlags.TransferPointers) != 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_SpawnItemEx' " +
                                 "SXF_TRANSFERPOINTERS copies target and tracer here; the master " +
                                 "pointer has no counterpart in this engine");
                }
            }

            if (arguments.Count > 9 && arguments[9].Count > 0)
            {
                frame.SpawnExFailChance = ReadSpawnExpression(
                    arguments, 9, actor, frame, problems, "0");
            }

            if (arguments.Count > 10 && arguments[10].Count > 0)
            {
                int tid;

                if (!TryReadWholeNumber(arguments[10], out tid))
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_SpawnItemEx' tid must " +
                                 "be a whole number, so it is ignored");
                }
                else if (tid != 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_SpawnItemEx' tid " +
                                 tid.ToString(CultureInfo.InvariantCulture) +
                                 " is ignored - this engine has no actor thing IDs");
                }
            }
        }

        /// <summary>Joins the tokens of one numeric argument and translates it for later evaluation.</summary>
        private static string ReadSpawnExpression(List<List<Token>> arguments, int index,
                                                  DecorateActor actor, StateFrame frame,
                                                  IList<string> problems, string fallback)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return fallback;
            }

            var text = JoinTokens(arguments[index]);
            var translated = DecorateCondition.TranslateExpression(
                text, actor.Name, frame.Line, problems);

            return translated ?? fallback;
        }

        /// <summary>
        /// Reads SXF_ names, returning the flags this engine can honour and naming the others.
        /// A wrong name must not erase the useful part of the call.
        /// </summary>
        private static bool TryReadSpawnFlags(List<Token> argument, out int flags,
                                              out string unknown, out string unsupported)
        {
            flags = 0;
            unknown = null;
            unsupported = "";
            var unsupportedNames = new List<string>();

            var token = argument[0];

            if (argument.Count == 1 && token.IsNumber)
            {
                flags = token.Number;

                var unsupportedBits = flags & ~SpawnItemExFlags.Supported;
                if (unsupportedBits != 0)
                {
                    unsupported = "0x" + unsupportedBits.ToString("x", CultureInfo.InvariantCulture);
                }

                flags &= SpawnItemExFlags.Supported;
                return true;
            }

            // The DECORATE reader separates whitespace, so "A | B" arrives as three tokens while
            // "A|B" arrives as one. Join them before splitting on the bar, or the common spaced
            // spelling would be rejected as an unreadable argument.
            var text = JoinTokens(argument).Replace(" ", "");

            foreach (var part in text.Split('|'))
            {
                var name = part.Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                int bit;

                if (TrySpawnFlag(name, out bit))
                {
                    flags |= bit;
                }
                else if (IsUnsupportedSpawnFlag(name))
                {
                    if (!unsupportedNames.Contains(name))
                    {
                        unsupportedNames.Add(name);
                    }
                }
                else
                {
                    unknown = name;
                    return false;
                }
            }

            unsupported = string.Join("|", unsupportedNames.ToArray());
            return true;
        }

        private static bool TrySpawnFlag(string name, out int bit)
        {
            bit = 0;

            switch (name.ToUpperInvariant())
            {
                case "SXF_TRANSFERTRANSLATION": bit = SpawnItemExFlags.TransferTranslation; break;
                case "SXF_ABSOLUTEPOSITION": bit = SpawnItemExFlags.AbsolutePosition; break;
                case "SXF_ABSOLUTEANGLE": bit = SpawnItemExFlags.AbsoluteAngle; break;
                case "SXF_ABSOLUTEVELOCITY":
                case "SXF_ABSOLUTEMOMENTUM": bit = SpawnItemExFlags.AbsoluteVelocity; break;
                case "SXF_NOCHECKPOSITION": bit = SpawnItemExFlags.NoCheckPosition; break;
                case "SXF_TRANSFERAMBUSHFLAG": bit = SpawnItemExFlags.TransferAmbushFlag; break;
                case "SXF_TRANSFERPOINTERS": bit = SpawnItemExFlags.TransferPointers; break;
                case "SXF_MULTIPLYSPEED": bit = SpawnItemExFlags.MultiplySpeed; break;
                case "SXF_SETTARGET": bit = SpawnItemExFlags.SetTarget; break;
                case "SXF_SETTRACER": bit = SpawnItemExFlags.SetTracer; break;
                case "SXF_NOPOINTERS": bit = SpawnItemExFlags.NoPointers; break;
                case "SXF_ORIGINATOR": bit = SpawnItemExFlags.Originator; break;
                case "SXF_TRANSFERSPRITEFRAME": bit = SpawnItemExFlags.TransferSpriteFrame; break;
                case "SXF_ISTARGET": bit = SpawnItemExFlags.IsTarget; break;
                case "SXF_ISTRACER": bit = SpawnItemExFlags.IsTracer; break;
                default: return false;
            }

            return true;
        }

        private static bool IsUnsupportedSpawnFlag(string name)
        {
            switch (name.ToUpperInvariant())
            {
                case "SXF_SETMASTER":
                case "SXF_TELEFRAG":
                case "SXF_CLIENTSIDE":
                case "SXF_TRANSFERPITCH":
                case "SXF_USEBLOODCOLOR":
                case "SXF_CLEARCALLERTID":
                case "SXF_TRANSFERSCALE":
                case "SXF_TRANSFERSPECIAL":
                case "SXF_CLEARCALLERSPECIAL":
                case "SXF_TRANSFERSTENCILCOL":
                case "SXF_TRANSFERALPHA":
                case "SXF_TRANSFERRENDERSTYLE":
                case "SXF_TRANSFERROLL":
                case "SXF_ISMASTER":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A_LookEx: the wake-up test with a window, a cone and a choice of state.
        ///
        /// The engine has one look routine and this configures it, rather than adding a second
        /// look. Six arguments and six flags is exactly the case where a separate implementation
        /// would drift: the vanilla path is the one every monster in the IWAD takes, and a copy
        /// would be the one nobody runs until a mod does.
        ///
        /// LOF_DONTCHASEGOAL is accepted and does nothing, because this engine has no patrol goals
        /// for a monster to chase. That is said in the report rather than passed on silently: a mod
        /// that gave a monster a route expects it to walk it.
        /// </summary>
        private static void ReadLookEx(List<List<Token>> arguments, DecorateActor actor,
                                       StateFrame frame, IList<string> problems)
        {
            frame.Action = "";
            frame.HasLookEx = true;

            if (arguments.Count > 0 && arguments[0].Count > 0)
            {
                int flags;
                string unknown;

                if (TryReadLookFlags(arguments[0], out flags, out unknown))
                {
                    frame.LookExFlags = flags;

                    if ((flags & LookExFlags.DontChaseGoal) != 0)
                    {
                        problems.Add(actor.Name + " line " + frame.Line + ": 'A_LookEx' " +
                                     "LOF_DONTCHASEGOAL is accepted and has no effect - this " +
                                     "engine gives monsters no patrol goal to ignore");
                    }
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_LookEx' flag '" +
                                 unknown + "' is not one this engine knows, so the look runs " +
                                 "without it");
                }
            }

            frame.LookExMinSee = ReadLookDistance(arguments, 1, actor, frame, problems, "minseedist");
            frame.LookExMaxSee = ReadLookDistance(arguments, 2, actor, frame, problems, "maxseedist");
            frame.LookExMaxHear = ReadLookDistance(arguments, 3, actor, frame, problems, "maxheardist");

            if (arguments.Count > 4 && arguments[4].Count > 0)
            {
                int fov;

                if (TryReadWholeNumber(arguments[4], out fov))
                {
                    if (fov < 0)
                    {
                        problems.Add(actor.Name + " line " + frame.Line + ": 'A_LookEx' fov is " +
                                     "negative, so half a circle is used");
                        fov = 0;
                    }
                    else if (fov > 360)
                    {
                        problems.Add(actor.Name + " line " + frame.Line + ": 'A_LookEx' fov is " +
                                     "more than a full circle, so a full circle is used");
                        fov = 360;
                    }

                    frame.LookExFov = fov;
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_LookEx' fov must be " +
                                 "a whole number of degrees; half a circle is used");
                }
            }

            if (arguments.Count > 5 && arguments[5].Count == 1 && arguments[5][0].Text.Length > 0)
            {
                // The name is kept as written and resolved when the states are emitted. It cannot
                // be checked here: the actor's chains are still being read, so the chain this
                // points at may not exist yet, and checking now would reject a call that names a
                // label further down the same block.
                frame.LookExSeeState = arguments[5][0].Text;
            }
        }

        /// <summary>One of A_LookEx's three distance arguments.</summary>
        private static int ReadLookDistance(List<List<Token>> arguments, int index,
                                            DecorateActor actor, StateFrame frame,
                                            IList<string> problems, string name)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return 0;
            }

            int value;

            if (TryReadWholeNumber(arguments[index], out value))
            {
                return value;
            }

            problems.Add(actor.Name + " line " + frame.Line + ": 'A_LookEx' " + name +
                         " is not a whole number of map units, so no limit is applied");

            return 0;
        }

        /// <summary>
        /// A_LookEx's flag argument, which DECORATE writes as LOF_ names joined by bars, as a
        /// number, or as a mix of the two.
        ///
        /// The numbers are ZDoom's own (p_enemy.h), not a guess: a wrong one would silently mean a
        /// different flag, and these flags are about what a monster can see.
        /// </summary>
        private static bool TryReadLookFlags(List<Token> argument, out int flags, out string unknown)
        {
            flags = 0;
            unknown = null;

            if (argument.Count != 1)
            {
                unknown = "the whole argument";
                return false;
            }

            var token = argument[0];

            if (token.IsNumber)
            {
                flags = token.Number;
                return true;
            }

            foreach (var part in token.Text.Split('|'))
            {
                var name = part.Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                switch (name.ToUpperInvariant())
                {
                    case "LOF_NOSIGHTCHECK":
                        flags |= LookExFlags.NoSightCheck;
                        break;

                    case "LOF_NOSOUNDCHECK":
                        flags |= LookExFlags.NoSoundCheck;
                        break;

                    case "LOF_DONTCHASEGOAL":
                        flags |= LookExFlags.DontChaseGoal;
                        break;

                    case "LOF_NOSEESOUND":
                        flags |= LookExFlags.NoSeeSound;
                        break;

                    case "LOF_FULLVOLSEESOUND":
                        flags |= LookExFlags.FullVolSeeSound;
                        break;

                    case "LOF_NOJUMP":
                        flags |= LookExFlags.NoJump;
                        break;

                    default:
                        int value;

                        if (!TryReadWholeNumberText(name, out value))
                        {
                            unknown = name;
                            return false;
                        }

                        flags |= value;
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// One numeric argument, which DECORATE may write as a whole number or as a fixed literal.
        ///
        /// "512.0" is how most mods write a distance, and refusing it would turn the common case
        /// into a report line. A non-zero fraction is refused rather than truncated: a fraction of
        /// a map unit means nothing here, and rounding it silently would move a monster's
        /// threshold without saying so.
        /// </summary>
        private static bool TryReadWholeNumber(List<Token> argument, out int value)
        {
            value = 0;

            if (argument.Count != 1)
            {
                return false;
            }

            if (argument[0].IsNumber)
            {
                value = argument[0].Number;
                return true;
            }

            return TryReadWholeNumberText(argument[0].Text, out value);
        }

        private static bool TryReadWholeNumberText(string text, out int value)
        {
            value = 0;

            var dot = text.IndexOf('.');

            if (dot >= 0)
            {
                for (var i = dot + 1; i < text.Length; i++)
                {
                    if (text[i] != '0')
                    {
                        return false;
                    }
                }

                text = text.Substring(0, dot);
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(text.Substring(2), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out value);
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// A_Chase with its arguments.
        ///
        /// The order is the trap, so it is spelled out here: ZDoom's signature is
        /// <c>A_Chase(melee, missile, flags)</c>, which makes flags the **third** argument.
        /// A_Chase(CHF_DONTMOVE) is therefore not a turret - it is a state label of 16, and ZDoom
        /// would go looking for a state of that name and find nothing. A mod that writes it gets
        /// told what it actually means here, instead of being quietly given the wrong thing.
        ///
        /// 0 in either of the first two positions means "the actor's own", which is how mods write
        /// "flags only". That is a reading, not a certainty: ZDoom would take 0 as a state label
        /// and fail to find it. The reading is the only one that makes A_Chase(0, 0, flags) mean
        /// anything, and it is said so in the docs rather than left to be discovered.
        /// </summary>
        private static void ReadChase(List<List<Token>> arguments, DecorateActor actor,
                                      StateFrame frame, IList<string> problems)
        {
            // No arguments at all: leave the plain action in place, so a mod that writes A_Chase()
            // gets exactly the engine's own A_Chase rather than a closure that calls it.
            if (arguments.Count == 0)
            {
                return;
            }

            frame.Action = "";
            frame.HasChase = true;

            frame.ChaseMeleeState = ReadChaseState(arguments, 0, actor, frame, problems, "melee");
            frame.ChaseMissileState = ReadChaseState(arguments, 1, actor, frame, problems, "missile");

            if (arguments.Count < 3 || arguments[2].Count == 0)
            {
                return;
            }

            int flags;
            string unknown;

            if (TryReadChaseFlags(arguments[2], out flags, out unknown))
            {
                frame.ChaseFlags = flags;

                // Named, not swallowed: each of these asks for behaviour this engine does not have,
                // and a modder who wrote one should hear about it once rather than wonder.
                if ((flags & ChaseFlags.FastChase) != 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' CHF_FASTCHASE " +
                                 "is accepted and has no effect - this engine has no boss " +
                                 "strafing to turn on");
                }

                if ((flags & ChaseFlags.Resurrect) != 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' CHF_RESURRECT " +
                                 "is accepted and has no effect - this engine has no resurrection");
                }

                if ((flags & ChaseFlags.DontIdle) != 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' CHF_DONTIDLE " +
                                 "is accepted and has no effect - this engine has no wander, so a " +
                                 "monster without a target returns to its spawn state");
                }
            }
            else
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' flag '" + unknown +
                             "' is not one this engine knows, so the chase runs without it");
            }
        }

        /// <summary>One of A_Chase's two state arguments.</summary>
        private static string ReadChaseState(List<List<Token>> arguments, int index,
                                             DecorateActor actor, StateFrame frame,
                                             IList<string> problems, string which)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return null;
            }

            var token = arguments[index].Count == 1 ? arguments[index][0] : null;

            if (token == null)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' " + which +
                             " state must be one name, so the actor's own is used");
                return null;
            }

            // 0 is how a mod writes "no override" when it only wants flags.
            if (token.IsNumber && token.Number == 0)
            {
                return null;
            }

            // A flag written in a state position. This is the natural mistake - the signature reads
            // "A_Chase(flags)" in a modder's memory - and it deserves the real explanation rather
            // than "there is no state called CHF_DONTMOVE", which is true and useless.
            if (!token.IsNumber)
            {
                int ignored;

                if (TryReadChaseFlags(new List<Token> { token }, out ignored, out _))
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' " + which +
                                 " argument is the flag '" + token.Text + "', but the first two " +
                                 "arguments are state names - the flags go third. Write " +
                                 "A_Chase(0, 0, ...) to keep the actor's own attack states. " +
                                 "The actor's own " + which + " state is used");
                    return null;
                }
            }

            if (token.IsNumber)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' " + which +
                             " argument is the number " + token.Number.ToString(
                                 CultureInfo.InvariantCulture) +
                             ", but the first two arguments are state names - the flags go " +
                             "third. The actor's own " + which + " state is used");
                return null;
            }

            // Kept as written; resolved when the states are emitted, because the chain may be
            // declared further down the same block.
            return token.Text;
        }

        /// <summary>
        /// A_Chase's flag argument, written as CHF_ names joined by bars, a number, or a mix.
        ///
        /// The numbers are ZDoom's own (p_enemy.cpp), not a guess.
        /// </summary>
        private static bool TryReadChaseFlags(List<Token> argument, out int flags, out string unknown)
        {
            flags = 0;
            unknown = null;

            if (argument.Count != 1)
            {
                unknown = "the whole argument";
                return false;
            }

            var token = argument[0];

            if (token.IsNumber)
            {
                flags = token.Number;
                return true;
            }

            foreach (var part in token.Text.Split('|'))
            {
                var name = part.Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                switch (name.ToUpperInvariant())
                {
                    case "CHF_FASTCHASE":
                        flags |= ChaseFlags.FastChase;
                        break;

                    case "CHF_NOPLAYACTIVE":
                        flags |= ChaseFlags.NoPlayActive;
                        break;

                    case "CHF_NIGHTMAREFAST":
                        flags |= ChaseFlags.NightmareFast;
                        break;

                    case "CHF_RESURRECT":
                        flags |= ChaseFlags.Resurrect;
                        break;

                    case "CHF_DONTMOVE":
                        flags |= ChaseFlags.DontMove;
                        break;

                    case "CHF_NORANDOMTURN":
                        flags |= ChaseFlags.NoRandomTurn;
                        break;

                    case "CHF_NODIRECTIONTURN":
                        flags |= ChaseFlags.NoDirectionTurn;
                        break;

                    case "CHF_NOPOSTATTACKTURN":
                        flags |= ChaseFlags.NoPostAttackTurn;
                        break;

                    case "CHF_STOPIFBLOCKED":
                        flags |= ChaseFlags.StopIfBlocked;
                        break;

                    case "CHF_DONTIDLE":
                        flags |= ChaseFlags.DontIdle;
                        break;

                    case "CHF_DONTLOOKALLAROUND":
                        flags |= ChaseFlags.DontLookAllAround;
                        break;

                    default:
                        int value;

                        if (!TryReadWholeNumberText(name, out value))
                        {
                            unknown = name;
                            return false;
                        }

                        flags |= value;
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// One of A_Chase's state arguments as it should be written in the script.
        ///
        /// Same rule as a jump: this actor's own chain wins over the engine's, because a mod that
        /// called its own chain "See" must reach its own. A name that is neither is reported and
        /// becomes "the actor's own", so the chase still works.
        /// </summary>
        private static string ChaseStateName(DecorateActor actor, string label,
                                             IList<string> problems, StateFrame frame)
        {
            if (label == null)
            {
                return "";
            }

            if (HasChain(actor, label))
            {
                return FrameName(label, 1);
            }

            problems.Add(actor.Name + " line " + frame.Line + ": 'A_Chase' names the state '" +
                         label + "', which is not one of this actor's own chains, so the actor's " +
                         "own state is used");

            return "";
        }

        /// <summary>
        /// A_Explode with arguments.
        ///
        /// ZDoom's signature is (damage, distance, flags, alert, fulldamagedistance, nails,
        /// naildamage, pufftype, damagetype). The first three are what a mod usually writes, and
        /// they are the three this engine can honour.
        ///
        /// A bare A_Explode is left as the engine's own action, which is the barrel explosion - the
        /// same thing the IWAD means by it. That is not an accident of the defaults: the barrel is
        /// the only vanilla actor that uses the action, so its behaviour is the right default.
        ///
        /// A negative damage means "from the actor's metadata" in ZDoom (ExplosionDamage and
        /// ExplosionRadius), and this engine has no such properties, so it is reported rather than
        /// turned into an explosion that heals.
        /// </summary>
        private static void ReadExplode(List<List<Token>> arguments, DecorateActor actor,
                                        StateFrame frame, IList<string> problems)
        {
            if (arguments.Count == 0)
            {
                return;
            }

            frame.Action = "";
            frame.HasExplode = true;

            if (arguments.Count > 0 && arguments[0].Count > 0)
            {
                int damage;

                if (!TryReadWholeNumber(arguments[0], out damage))
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Explode' damage must " +
                                 "be a whole number; 128 is used");
                    damage = 128;
                }
                else if (damage < 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Explode' negative " +
                                 "damage means 'from the actor's metadata' in ZDoom, and this " +
                                 "engine has no ExplosionDamage property, so 128 is used");
                    damage = 128;
                }

                frame.ExplodeDamage = damage;
            }

            if (arguments.Count > 1 && arguments[1].Count > 0)
            {
                int radius;

                if (!TryReadWholeNumber(arguments[1], out radius))
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Explode' distance " +
                                 "must be a whole number; the damage is used as the radius");
                    radius = 0;
                }

                frame.ExplodeRadius = radius;
            }
            else
            {
                frame.ExplodeRadius = frame.ExplodeDamage;
            }

            if (arguments.Count > 2 && arguments[2].Count > 0)
            {
                int flags;
                string unknown;

                if (TryReadExplodeFlags(arguments[2], out flags, out unknown))
                {
                    frame.ExplodeFlags = flags;

                    var unsupported = flags & ~(ExplosionFlags.HurtSource | ExplosionFlags.NotMissile |
                                                ExplosionFlags.ExplicitDamageType |
                                                ExplosionFlags.NoSplash);

                    if (unsupported != 0)
                    {
                        problems.Add(actor.Name + " line " + frame.Line + ": 'A_Explode' flags " +
                                     "0x" + unsupported.ToString("x", CultureInfo.InvariantCulture) +
                                     " (thrust, allies, circular) have no counterpart in this " +
                                     "engine, so the blast is the ordinary square one; the rest " +
                                     "of the call still runs");
                    }
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_Explode' flag '" +
                                 unknown + "' is not one this engine knows, so the explosion runs " +
                                 "with the flags it did understand");
                }
            }

            // The arguments past the flags are a nailbomb, a puff type and a damage type. A mod
            // that uses them gets a different explosion, and it should be told so once.
            for (var i = 3; i < arguments.Count; i++)
            {
                if (arguments[i].Count == 0)
                {
                    continue;
                }

                if (arguments[i].Count == 1 && arguments[i][0].IsNumber &&
                    arguments[i][0].Number == 0)
                {
                    continue;
                }

                problems.Add(actor.Name + " line " + frame.Line + ": 'A_Explode' arguments after " +
                             "the flags (alert, full damage distance, nails, puff, damage type) " +
                             "are not supported, so they are ignored; the blast itself still runs");
                break;
            }
        }

        /// <summary>A_Explode's flag argument, written as XF_ names joined by bars or a number.</summary>
        private static bool TryReadExplodeFlags(List<Token> argument, out int flags, out string unknown)
        {
            flags = 0;
            unknown = null;

            if (argument.Count != 1)
            {
                unknown = "the whole argument";
                return false;
            }

            var token = argument[0];

            if (token.IsNumber)
            {
                flags = token.Number;
                return true;
            }

            foreach (var part in token.Text.Split('|'))
            {
                var name = part.Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                switch (name.ToUpperInvariant())
                {
                    case "XF_HURTSOURCE":
                        flags |= ExplosionFlags.HurtSource;
                        break;

                    case "XF_NOTMISSILE":
                        flags |= ExplosionFlags.NotMissile;
                        break;

                    case "XF_EXPLICITDAMAGETYPE":
                        flags |= ExplosionFlags.ExplicitDamageType;
                        break;

                    case "XF_NOSPLASH":
                        flags |= ExplosionFlags.NoSplash;
                        break;

                    case "XF_THRUSTZ":
                        flags |= ExplosionFlags.ThrustZ;
                        break;

                    case "XF_THRUSTLESS":
                        flags |= ExplosionFlags.ThrustLess;
                        break;

                    case "XF_NOALLIES":
                        flags |= ExplosionFlags.NoAllies;
                        break;

                    case "XF_CIRCULAR":
                        flags |= ExplosionFlags.Circular;
                        break;

                    case "XF_CIRCULARTHRUST":
                        flags |= ExplosionFlags.CircularThrust;
                        break;

                    default:
                        int value;

                        if (!TryReadWholeNumberText(name, out value))
                        {
                            unknown = name;
                            return false;
                        }

                        flags |= value;
                        break;
                }
            }

            return true;
        }

        /// <summary>A_NoBlocking: clear SOLID. DropItem metadata is reported by its own property.</summary>
        private static void ReadNoBlocking(List<List<Token>> arguments, DecorateActor actor,
                                           StateFrame frame, IList<string> problems)
        {
            // No arguments means the engine's plain A_NoBlocking, not a second closure for the same
            // thing. The optional drop argument is the only reason to emit frame work.
            if (arguments.Count == 0)
            {
                return;
            }

            frame.Action = "";
            frame.HasNoBlocking = true;
            frame.NoBlockingDrop = ReadActionBoolean(arguments, 0, true, actor, frame, problems,
                "A_NoBlocking drop");
        }

        /// <summary>A_SetHealth: health is an expression, and target/tracer pointers are supported.</summary>
        private static void ReadSetHealth(List<List<Token>> arguments, DecorateActor actor,
                                          StateFrame frame, IList<string> problems)
        {
            frame.Action = "";

            if (arguments.Count == 0 || arguments[0].Count == 0)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_SetHealth' needs a health value, so it is not applied");
                return;
            }

            var pointer = ReadPointer(arguments, 1, actor, frame, problems, "A_SetHealth");

            if (pointer < 0)
            {
                return;
            }

            frame.HasSetHealth = true;
            frame.SetHealthExpression = ReadSpawnExpression(arguments, 0, actor, frame, problems, "1");
            frame.SetHealthPointer = pointer;
        }

        /// <summary>A_SetAngle: angle is a fixed-point degree expression.</summary>
        private static void ReadSetAngle(List<List<Token>> arguments, DecorateActor actor,
                                         StateFrame frame, IList<string> problems)
        {
            frame.Action = "";

            var pointer = ReadPointer(arguments, 2, actor, frame, problems, "A_SetAngle");

            if (pointer < 0)
            {
                return;
            }

            frame.HasSetAngle = true;
            frame.SetAngleExpression = ReadSpawnExpression(arguments, 0, actor, frame, problems, "0");
            frame.SetAngleFlags = ReadActionFlags(arguments, 1, actor, frame, problems, "A_SetAngle");
            frame.SetAnglePointer = pointer;
        }

        /// <summary>A_ChangeVelocity: x/y/z are expressions, flags and pointer are read at load.</summary>
        private static void ReadChangeVelocity(List<List<Token>> arguments, DecorateActor actor,
                                               StateFrame frame, IList<string> problems)
        {
            frame.Action = "";

            var pointer = ReadPointer(arguments, 4, actor, frame, problems, "A_ChangeVelocity");

            if (pointer < 0)
            {
                return;
            }

            frame.HasChangeVelocity = true;
            frame.ChangeVelocityExpressions = new string[3];

            for (var i = 0; i < 3; i++)
            {
                frame.ChangeVelocityExpressions[i] = ReadSpawnExpression(
                    arguments, i, actor, frame, problems, "0");
            }

            frame.ChangeVelocityFlags = ReadActionFlags(arguments, 3, actor, frame, problems,
                "A_ChangeVelocity");
            frame.ChangeVelocityPointer = pointer;
        }

        /// <summary>
        /// A_StartSound: sound name, channel, presentation flags, volume and attenuation.
        ///
        /// This is not the old A_PlaySound: its second argument is a CHAN_* slot, not a small
        /// channel number with a different meaning. Volume and attenuation remain expressions,
        /// because a mod may randomise them. Looping, overlap and local-only policies are reported
        /// rather than promised: this mixer has channels, not those policies.
        /// </summary>
        private static void ReadStartSound(List<List<Token>> arguments, DecorateActor actor,
                                           StateFrame frame, IList<string> problems)
        {
            if (arguments.Count == 0 || arguments[0].Count != 1)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_StartSound' needs a sound name, so the frame stays silent");
                frame.Action = "";
                return;
            }

            frame.Action = "";
            frame.HasStartSound = true;
            frame.StartSoundName = arguments[0][0].Text;
            frame.StartSoundSlot = ReadSoundSlot(arguments, 1, actor, frame, problems);
            frame.StartSoundFlags = ReadStartSoundFlags(arguments, 2, actor, frame, problems);
            frame.StartSoundVolumeExpression = ReadSpawnExpression(arguments, 3, actor, frame,
                problems, "1.0");
            frame.StartSoundAttenuationExpression = ReadSpawnExpression(arguments, 4, actor, frame,
                problems, "1.0");

            ReportUnsupportedSoundArgument(arguments, 5, actor, frame, problems, "pitch");
            ReportUnsupportedSoundArgument(arguments, 6, actor, frame, problems, "startTime");
        }

        private static int ReadSoundSlot(List<List<Token>> arguments, int index,
                                         DecorateActor actor, StateFrame frame,
                                         IList<string> problems)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return 4; // CHAN_BODY, ZDoom's default for A_StartSound.
            }

            var text = JoinTokens(arguments[index]).Replace(" ", "");
            int slot;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
            {
                if (slot >= 0 && slot <= 7)
                {
                    return slot;
                }
            }
            else
            {
                switch (text.ToUpperInvariant())
                {
                    case "CHAN_AUTO": return 0;
                    case "CHAN_WEAPON": return 1;
                    case "CHAN_VOICE": return 2;
                    case "CHAN_ITEM": return 3;
                    case "CHAN_BODY": return 4;
                    case "CHAN_5": return 5;
                    case "CHAN_6": return 6;
                    case "CHAN_7": return 7;
                }
            }

            problems.Add(actor.Name + " line " + frame.Line + ": 'A_StartSound' channel '" + text +
                         "' is not a CHAN_* slot this engine knows; CHAN_BODY is used");
            return 4;
        }

        private static int ReadStartSoundFlags(List<List<Token>> arguments, int index,
                                               DecorateActor actor, StateFrame frame,
                                               IList<string> problems)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return 0;
            }

            var text = JoinTokens(arguments[index]).Replace(" ", "");
            int flags = 0;
            var unsupported = new List<string>();

            foreach (var part in text.Split('|'))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                int number;

                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    flags |= number;
                    continue;
                }

                switch (part.ToUpperInvariant())
                {
                    case "CHANF_DEFAULT":
                    case "CHANF_LISTENERZ":
                    case "CHANF_MAYBE_LOCAL":
                    case "CHANF_UI":
                    case "CHANF_NOPAUSE":
                    case "CHANF_TRANSIENT":
                    case "CHANF_RUMBLE":
                    case "CHANF_NORUMBLE":
                        break;

                    case "CHANF_LOOP":
                    case "CHAN_LOOP":
                    case "CHANF_NOSTOP":
                    case "CHAN_NOSTOP":
                    case "CHANF_OVERLAP":
                    case "CHAN_OVERLAP":
                    case "CHANF_LOCAL":
                    case "CHANF_FORCE":
                    case "CHANF_SINGULAR":
                        if (!unsupported.Contains(part))
                        {
                            unsupported.Add(part);
                        }
                        break;

                    default:
                        problems.Add(actor.Name + " line " + frame.Line + ": 'A_StartSound' flag '" +
                                     part + "' is not known, so it is ignored");
                        break;
                }
            }

            if (unsupported.Count > 0)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_StartSound' flags " +
                             string.Join("|", unsupported.ToArray()) +
                             " (looping, overlap or local policies) are not supported by this " +
                             "mixer; the sound still plays once");
            }

            return flags;
        }

        private static void ReportUnsupportedSoundArgument(List<List<Token>> arguments, int index,
                                                           DecorateActor actor, StateFrame frame,
                                                           IList<string> problems, string what)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return;
            }

            var text = JoinTokens(arguments[index]).Trim();

            if (text == "0" || text == "0.0" || text == "0.00")
            {
                return;
            }

            problems.Add(actor.Name + " line " + frame.Line + ": 'A_StartSound' " + what +
                         " is not supported by this mixer, so it is ignored");
        }

        /// <summary>A_ScaleVelocity: one scale expression and an optional pointer.</summary>
        private static void ReadScaleVelocity(List<List<Token>> arguments, DecorateActor actor,
                                              StateFrame frame, IList<string> problems)
        {
            frame.Action = "";

            if (arguments.Count == 0 || arguments[0].Count == 0)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_ScaleVelocity' needs a scale, so it is not applied");
                return;
            }

            var pointer = ReadPointer(arguments, 1, actor, frame, problems, "A_ScaleVelocity");

            if (pointer < 0)
            {
                return;
            }

            frame.HasScaleVelocity = true;
            frame.ScaleVelocityExpression = ReadSpawnExpression(arguments, 0, actor, frame, problems, "1.0");
            frame.ScaleVelocityPointer = pointer;
        }

        /// <summary>Reads an AAPTR name or number, returning -1 when it cannot be honoured.</summary>
        private static int ReadPointer(List<List<Token>> arguments, int index,
                                       DecorateActor actor, StateFrame frame,
                                       IList<string> problems, string action)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return ActorPointer.Default;
            }

            var text = JoinTokens(arguments[index]).Replace(" ", "");
            int pointer;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out pointer))
            {
                if (ActorPointer.IsSupported(pointer))
                {
                    return pointer;
                }
            }
            else
            {
                switch (text.ToUpperInvariant())
                {
                    case "AAPTR_DEFAULT": return ActorPointer.Default;
                    case "AAPTR_NULL": return ActorPointer.Null;
                    case "AAPTR_TARGET": return ActorPointer.Target;
                    case "AAPTR_TRACER": return ActorPointer.Tracer;
                }
            }

            problems.Add(actor.Name + " line " + frame.Line + ": '" + action + "' pointer '" + text +
                         "' is not supported here (target and tracer are supported, master and " +
                         "player/line pointers are not), so the action is not applied");
            return -1;
        }

        /// <summary>Reads a numeric flags argument without treating it as an expression.</summary>
        private static int ReadActionFlags(List<List<Token>> arguments, int index,
                                           DecorateActor actor, StateFrame frame,
                                           IList<string> problems, string action)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return 0;
            }

            var text = JoinTokens(arguments[index]).Replace(" ", "");
            int value;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                if (action == "A_ChangeVelocity" && (value & ~3) != 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": '" + action + "' flags 0x" +
                                 (value & ~3).ToString("x", CultureInfo.InvariantCulture) +
                                 " are not known; CVF_RELATIVE and CVF_REPLACE are used");
                }
                else if (action == "A_SetAngle" && (value & ~2) != 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": '" + action + "' flags 0x" +
                                 (value & ~2).ToString("x", CultureInfo.InvariantCulture) +
                                 " are not known; SPF_INTERPOLATE is presentation and the angle " +
                                 "is still set");
                }

                return value;
            }

            // CVF and SPF names are tiny closed sets, not arbitrary expressions. Unknown names are
            // reported instead of silently producing a different movement or angle.
            if (action == "A_ChangeVelocity")
            {
                var flags = 0;

                foreach (var part in text.Split('|'))
                {
                    switch (part.ToUpperInvariant())
                    {
                        case "CVF_RELATIVE": flags |= 1; break;
                        case "CVF_REPLACE": flags |= 2; break;
                        case "":
                            break;
                        default:
                            problems.Add(actor.Name + " line " + frame.Line + ": '" + action +
                                         "' flag '" + part + "' is not known, so it is ignored");
                            break;
                    }
                }

                return flags;
            }

            if (action == "A_SetAngle")
            {
                var flags = 0;

                foreach (var part in text.Split('|'))
                {
                    switch (part.ToUpperInvariant())
                    {
                        case "SPF_INTERPOLATE":
                            // Interpolation is presentation, not simulation. It is safe to accept:
                            // the angle itself is still set on this tic.
                            break;
                        case "":
                            break;
                        default:
                            problems.Add(actor.Name + " line " + frame.Line + ": '" + action +
                                         "' flag '" + part + "' is not known, so it is ignored");
                            break;
                    }
                }

                return flags;
            }

            problems.Add(actor.Name + " line " + frame.Line + ": '" + action + "' flags '" + text +
                         "' must be a number or a known flag name; zero is used");
            return 0;
        }

        private static bool ReadActionBoolean(List<List<Token>> arguments, int index, bool fallback,
                                              DecorateActor actor, StateFrame frame,
                                              IList<string> problems, string what)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return fallback;
            }

            if (arguments[index].Count == 1)
            {
                var token = arguments[index][0];

                if (token.IsNumber)
                {
                    return token.Number != 0;
                }

                if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            problems.Add(actor.Name + " line " + frame.Line + ": '" + what +
                         "' must be true/false or 0/1; the default is used");
            return fallback;
        }

        /// <summary>
        /// A_CustomPunch: damage, norandom, flags, puff and range are supported; the remaining
        /// arguments are reported rather than silently approximated.
        ///
        /// In ademo1.wad the second argument is 1, meaning "do not roll the 1..8 multiplier". It is
        /// a boolean in ZDoom, not an ammo count, and reading it as such would change the weapon's
        /// damage by a factor of eight.
        /// </summary>
        private static void ReadCustomPunch(List<List<Token>> arguments, DecorateActor actor,
                                            StateFrame frame, IList<string> problems)
        {
            frame.Action = "";
            frame.HasCustomPunch = true;

            frame.PunchDamageExpression = ReadSpawnExpression(
                arguments, 0, actor, frame, problems, "0");
            frame.PunchNoRandom = ReadPunchBoolean(arguments, 1, actor, frame, problems);
            frame.PunchFlags = ReadPunchFlags(arguments, 2, actor, frame, problems);

            if (arguments.Count > 3 && arguments[3].Count == 1)
            {
                frame.PunchPuffName = arguments[3][0].Text;

                if (ActorNames.TypeOf(frame.PunchPuffName) < 0)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": no such actor '" +
                                 frame.PunchPuffName + "' for 'A_CustomPunch', so BulletPuff is used");
                    frame.PunchPuffName = "BulletPuff";
                }
            }

            frame.PunchRangeExpression = ReadSpawnExpression(
                arguments, 4, actor, frame, problems, "0");

            if (arguments.Count > 5 && arguments[5].Count > 0)
            {
                var lifeSteal = JoinTokens(arguments[5]).Trim();

                if (lifeSteal != "0" && lifeSteal != "0.0" && lifeSteal != "0.00")
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomPunch' " +
                                 "lifesteal is not supported, so it is ignored");
                }
            }

            if (arguments.Count > 6 && arguments[6].Count > 0)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomPunch' " +
                             "lifestealmax and armour bonus are not supported, so they are ignored");
            }

            if (arguments.Count > 7 && arguments[7].Count > 0)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomPunch' " +
                             "armorbonustype is not supported, so it is ignored");
            }

            frame.PunchHitSound = ReadOptionalSound(arguments, 8);
            frame.PunchMissSound = ReadOptionalSound(arguments, 9);
        }

        private static bool ReadPunchBoolean(List<List<Token>> arguments, int index,
                                             DecorateActor actor, StateFrame frame,
                                             IList<string> problems)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return false;
            }

            if (arguments[index].Count == 1)
            {
                var token = arguments[index][0];

                if (token.IsNumber)
                {
                    return token.Number != 0;
                }

                if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomPunch' norandom must " +
                         "be true/false or 0/1; the random multiplier is used");
            return false;
        }

        private static int ReadPunchFlags(List<List<Token>> arguments, int index,
                                          DecorateActor actor, StateFrame frame,
                                          IList<string> problems)
        {
            if (index >= arguments.Count || arguments[index].Count == 0)
            {
                return CustomPunchFlags.UseAmmo;
            }

            var text = JoinTokens(arguments[index]).Replace(" ", "");
            int flags = 0;
            var unsupported = new List<string>();

            foreach (var part in text.Split('|'))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                int value;

                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    flags |= value;
                    continue;
                }

                switch (part.ToUpperInvariant())
                {
                    case "CPF_USEAMMO": flags |= CustomPunchFlags.UseAmmo; break;
                    case "CPF_DAGGER": flags |= CustomPunchFlags.Dagger; break;
                    case "CPF_PULLIN": flags |= CustomPunchFlags.PullIn; break;
                    case "CPF_NORANDOMPUFFZ": flags |= CustomPunchFlags.NoRandomPuffZ; break;
                    case "CPF_NOTURN": flags |= CustomPunchFlags.NoTurn; break;
                    case "CPF_STEALARMOR": flags |= CustomPunchFlags.StealArmor; break;
                    default:
                        problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomPunch' flag '" +
                                     part + "' is not one this engine knows, so it is ignored");
                        break;
                }
            }

            var unsupportedBits = flags & ~CustomPunchFlags.Supported;

            if (unsupportedBits != 0)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomPunch' flags 0x" +
                             unsupportedBits.ToString("x", CultureInfo.InvariantCulture) +
                             " (ammo, dagger, pull-in or armour stealing) are not supported here; " +
                             "the punch still runs without them");
            }

            return flags & CustomPunchFlags.Supported;
        }

        /// <summary>
        /// A_CustomMeleeAttack: damage, hit sound, miss sound, damage type and bleed.
        ///
        /// The first three arguments are what most mods use. Damage is kept as an expression
        /// because random(20,24) is common. Damage types have no counterpart in this engine, so
        /// they are reported when written; bleed=false is also reported rather than ignored,
        /// because the engine's damage path does not have a per-action blood toggle.
        /// </summary>
        private static void ReadCustomMelee(List<List<Token>> arguments, DecorateActor actor,
                                            StateFrame frame, IList<string> problems)
        {
            frame.Action = "";
            frame.HasCustomMelee = true;

            frame.MeleeDamageExpression = ReadSpawnExpression(
                arguments, 0, actor, frame, problems, "0");
            frame.MeleeHitSound = ReadOptionalSound(arguments, 1);
            frame.MeleeMissSound = ReadOptionalSound(arguments, 2);

            if (arguments.Count > 3 && arguments[3].Count > 0)
            {
                var type = JoinTokens(arguments[3]).Trim();

                if (type.Length > 0 && type != "\"\"" &&
                    !string.Equals(type, "none", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomMeleeAttack' " +
                                 "damage type " + type + " is ignored - this engine has no " +
                                 "damage types");
                }
            }

            if (arguments.Count > 4 && arguments[4].Count > 0)
            {
                var token = arguments[4].Count == 1 ? arguments[4][0] : null;

                if (token != null &&
                    ((token.IsNumber && token.Number == 0) ||
                     string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_CustomMeleeAttack' " +
                                 "bleed=false is ignored - there is no per-action blood toggle " +
                                 "in this engine");
                }
            }
        }

        /// <summary>One optional sound argument, without the quotes DECORATE uses.</summary>
        private static string ReadOptionalSound(List<List<Token>> arguments, int index)
        {
            if (index >= arguments.Count || arguments[index].Count != 1)
            {
                return null;
            }

            var text = arguments[index][0].Text;

            return text.Length == 0 ? null : text;
        }

        /// <summary>
        /// A_ChangeFlag: set or clear one flag on the actor.
        ///
        /// The name is normalised the same way a "+FLAG" line is, so "NO_BLOCKMAP" and "NOBLOCKMAP"
        /// both reach the engine's name. An unknown flag is reported and costs the frame's action
        /// rather than the actor: a frame that quietly changes nothing is the failure this is
        /// meant to prevent.
        ///
        /// Link flags are a real limitation and are not pretended away: this engine keeps the
        /// sector and blockmap links it built when the actor was spawned, so clearing NOBLOCKMAP or
        /// NOSECTOR at runtime changes the flag but does not move the actor in the world links.
        /// </summary>
        private static void ReadChangeFlag(List<List<Token>> arguments, DecorateActor actor,
                                           StateFrame frame, IList<string> problems)
        {
            if (arguments.Count < 1 || arguments[0].Count != 1)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_ChangeFlag' needs a flag name, so the frame changes nothing");
                frame.Action = "";
                return;
            }

            var name = NormaliseFlagName(arguments[0][0].Text);

            if (!ScriptActorApi.TryParseFlagName(name, out _))
            {
                problems.Add(actor.Name + " line " + frame.Line + ": there is no flag '" +
                             arguments[0][0].Text + "' in this engine, so 'A_ChangeFlag' changes " +
                             "nothing; the frame itself is kept");
                frame.Action = "";
                return;
            }

            var value = true;

            if (arguments.Count > 1)
            {
                var token = arguments[1].Count == 1 ? arguments[1][0] : null;

                if (token == null)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_ChangeFlag' value must " +
                                 "be 0 or 1; the flag is set");
                }
                else if (token.IsNumber)
                {
                    value = token.Number != 0;
                }
                else if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                }
                else if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                }
                else
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_ChangeFlag' value '" +
                                 token.Text + "' is not a number or true/false; the flag is set");
                }
            }

            frame.ChangeFlagName = name;
            frame.ChangeFlagValue = value;
            frame.Action = "";
        }

        /// <summary>A_SetTics: how long the frame lasts.</summary>
        private static void ReadSetTics(List<List<Token>> arguments, DecorateActor actor,
                                        StateFrame frame, IList<string> problems)
        {
            int tics;

            if (arguments.Count < 1 || !TryReadWholeNumber(arguments[0], out tics))
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_SetTics' needs a whole number of tics, so the frame keeps its own");
                frame.Action = "";
                return;
            }

            if (tics < -1)
            {
                problems.Add(actor.Name + " line " + frame.Line + ": 'A_SetTics' cannot be less " +
                             "than -1, which means never; -1 is used");
                tics = -1;
            }

            frame.SetTicsValue = tics;
            frame.HasSetTics = true;
            frame.Action = "";
        }

        /// <summary>A_Recoil: a push backwards along the actor's own facing.</summary>
        private static void ReadRecoil(List<List<Token>> arguments, DecorateActor actor,
                                       StateFrame frame, IList<string> problems)
        {
            int distance;

            if (arguments.Count < 1 || !TryReadWholeNumber(arguments[0], out distance))
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_Recoil' needs a whole number of map units, so the frame " +
                             "pushes nothing");
                frame.Action = "";
                return;
            }

            frame.RecoilDistance = distance;
            frame.HasRecoil = true;
            frame.Action = "";
        }

        /// <summary>
        /// A_FaceTarget with arguments.
        ///
        /// ZDoom's signature is (max_turn, max_pitch, ang_offset, ...), so the first number is a
        /// **limit on turning**, not an offset. A mod that wrote A_FaceTarget(90) asked for a
        /// gradual turn and would get an instant one if the argument were read as an offset - the
        /// opposite of what it wanted.
        ///
        /// max_pitch is reported when it is used: this engine has no per-actor pitch, so a mod
        /// asking a monster to aim up or down gets a monster that still aims level, and should be
        /// told so rather than left to wonder.
        /// </summary>
        private static void ReadFaceTarget(List<List<Token>> arguments, DecorateActor actor,
                                           StateFrame frame, IList<string> problems)
        {
            int maxTurn;

            if (arguments.Count < 1 || !TryReadWholeNumber(arguments[0], out maxTurn))
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_FaceTarget' turn limit must be a whole number of degrees, " +
                             "so the actor turns all the way");
                frame.Action = "";
                return;
            }

            if (maxTurn < 0)
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_FaceTarget' turn limit is negative; it is read as positive");
                maxTurn = -maxTurn;
            }

            if (arguments.Count > 1 && arguments[1].Count > 0)
            {
                int pitch;

                if (TryReadWholeNumber(arguments[1], out pitch) && pitch != 270)
                {
                    problems.Add(actor.Name + " line " + frame.Line + ": 'A_FaceTarget' pitch " +
                                 "limit is ignored - this engine has no per-actor pitch, so the " +
                                 "actor still aims level");
                }
            }

            var offset = 0;

            if (arguments.Count > 2 && arguments[2].Count > 0 &&
                !TryReadWholeNumber(arguments[2], out offset))
            {
                problems.Add(actor.Name + " line " + frame.Line +
                             ": 'A_FaceTarget' angle offset must be a whole number of degrees; " +
                             "no offset is applied");
                offset = 0;
            }

            frame.FaceMaxTurn = maxTurn;
            frame.FaceOffset = offset;
            frame.HasFaceTarget = true;
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
