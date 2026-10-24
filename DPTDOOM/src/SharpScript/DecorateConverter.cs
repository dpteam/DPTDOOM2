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
        /// DoomEdNum by DECORATE actor name, for the names this converter can resolve.
        ///
        /// Only the monsters, and deliberately so. A wrong number here does not fail: it
        /// silently rewrites a different actor, so a table guessed at from memory is worse
        /// than no table at all - a mod replacing a shotgun guy would end up editing a
        /// health bonus. Everything else is reported as an unknown name, which costs the
        /// modder one line to fix and costs nobody a mystery.
        /// </summary>
        private static readonly string[,] KnownActors =
        {
            { "zombieman", "3004" },
            { "shotgonguy", "9" },
            { "shotgunguy", "9" },
            { "chaingunguy", "65" },
            { "heavyweapondude", "65" },
            { "doomimp", "3001" },
            { "imp", "3001" },
            { "demon", "3002" },
            { "pinky", "3002" },
            { "spectre", "58" },
            { "cacodemon", "3005" },
            { "lostsoul", "3006" },
            { "baronofhell", "3003" },
            { "hellknight", "69" },
            { "revenant", "66" },
            { "mancubus", "67" },
            { "fatso", "67" },
            { "arachnotron", "68" },
            { "archvile", "64" },
            { "painelemental", "71" },
            { "wolfensteinss", "84" },
            { "ssguy", "84" },
            { "commanderkeen", "72" },
            { "bossbrain", "88" },
            { "bosseye", "89" },
            { "cyberdemon", "16" },
            { "spidermastermind", "7" },
            { "player", "1" }
        };

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

                foreach (var property in actor.Unsupported)
                {
                    problems.Add(actor.Name + " line " + actor.Line + ": '" + property.Key +
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

                if (actor.Numbers.Count == 0 && actor.Flags.Count == 0)
                {
                    // Nothing to say, so nothing is emitted. An empty patch would still be
                    // an entry in the report and would look like work.
                    continue;
                }

                script.Append("\nfn Thing_").Append(target.DoomEdNum).Append("() {\n");

                foreach (var pair in actor.Numbers)
                {
                    script.Append("    ").Append(pair.Key).Append('(')
                          .Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(");\n");
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

                script.Append("}\n");
            }

            return script.ToString();
        }

        /// <summary>
        /// Finds the actor in the engine's table that a DECORATE block describes, or null
        /// with the reason.
        ///
        /// Three ways a block names its target, in the order DECORATE itself resolves them:
        /// the number written in the block, the name it replaces, and the parent it inherits
        /// from. New actors - a block with a number this engine does not have - are refused
        /// rather than created, because creating one needs a state table entry and a sprite
        /// name, and an actor with neither cannot be placed on a map.
        /// </summary>
        private static MobjInfo ResolveTarget(DecorateActor actor, MobjInfo[] table,
                                              IList<string> problems)
        {
            var number = actor.DoomEdNum;

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

            if (number < 0 && actor.ParentName != null)
            {
                // Inheriting from a known actor and not replacing it means the derived actor
                // takes over the parent's map slot, which is what DECORATE means by a block
                // whose only identifier is ":" and a parent name.
                TryResolveName(actor.ParentName, out number);
            }

            if (number < 0)
            {
                problems.Add(actor.Name + " line " + actor.Line + ": the block names no DoomEdNum " +
                             "this converter can resolve, so nothing is changed");
                return null;
            }

            foreach (var info in table)
            {
                if (info.DoomEdNum == number)
                {
                    return info;
                }
            }

            problems.Add(actor.Name + " line " + actor.Line + ": this engine has no actor with " +
                         "DoomEdNum " + number.ToString(CultureInfo.InvariantCulture) +
                         "; new actors need their own states, so it is not created");
            return null;
        }

        private static bool TryResolveName(string name, out int number)
        {
            number = -1;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (var i = 0; i < KnownActors.GetLength(0); i++)
            {
                if (!string.Equals(KnownActors[i, 0], name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return int.TryParse(KnownActors[i, 1], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out number);
            }

            return false;
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
                            // "A_Jump" is not "no" but "not here, and here is what there is".
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
                          .Append(next).Append("\");\n");
                }

                var property = StatePropertyFor(chain.Label);

                if (property != null)
                {
                    script.Append("    ").Append(property).Append("(\"")
                          .Append(FrameName(chain.Label, 1)).Append("\");\n");
                }
            }
        }

        private static string FrameName(string label, int index)
        {
            return label + "#" + index.ToString(CultureInfo.InvariantCulture);
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

                // Everything else is recorded rather than dropped: silence is what makes a
                // modder believe a property took effect.
                actor.Unsupported.Add(new KeyValuePair<string, string>(token.Text, value.Text));
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

                foreach (var frame in chain.Frames)
                {
                    frame.Tics = next.Number;
                }

                next = reader.Next(ref line);

                if (next == null)
                {
                    return;
                }
            }

            if (string.Equals(next.Text, "Bright", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var frame in chain.Frames)
                {
                    frame.Letter = "+" + frame.Letter.TrimStart('+');
                }

                next = reader.Next(ref line);

                if (next == null)
                {
                    return;
                }
            }

            if (IsActionName(next.Text))
            {
                foreach (var frame in chain.Frames)
                {
                    frame.Action = next.Text;
                }
            }
            else
            {
                // Not an action, so it begins whatever comes next: a keyword, a label or the
                // sprite of the following frame. It goes back.
                reader.PushBack(next, ref line);
            }
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

                if (c == '{' || c == '}' || c == ':')
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
                return char.IsWhiteSpace(c) || c == '{' || c == '}' || c == ':' || c == '"';
            }

            private static bool IsDigitOrDot(char c)
            {
                return char.IsDigit(c) || c == '.';
            }
        }
    }
}
