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

namespace DPTDOOM
{
    /// <summary>What a numbered lock decided: whether it opens, what to say, what to play.</summary>
    public struct LockVerdict
    {
        public bool Open;

        /// <summary>The refusal line. Null when the door opens.</summary>
        public string Message;

        /// <summary>The sound that goes with the refusal.</summary>
        public Sfx Sound;

        public static LockVerdict Opened
        {
            get { return new LockVerdict { Open = true }; }
        }

        public static LockVerdict Refused(string message, Sfx sound)
        {
            return new LockVerdict { Open = false, Message = message, Sound = sound };
        }
    }

    /// <summary>
    /// Which key a numbered lock needs, and what to say when the player has none.
    ///
    /// Two sources, in this order:
    ///
    ///  1. **a LOCKDEFS file**, if it defined this number - see <see cref="LockDefs"/>;
    ///  2. **Boom's built-in rules**, which is what a map with no LOCKDEFS gets.
    ///
    /// The split matters. Boom's rules are a fixed table of eight shapes and are checked
    /// here without a map, because a door that opens without its key is indistinguishable
    /// from a map that was built without a lock. A file can say something Boom cannot -
    /// "a blue card or a red skull", a message in the mod's own words, a different refusal
    /// sound - and that is why the table exists at all.
    ///
    /// The one piece of Boom behaviour that looks like a bug and is not: the `LockedNKeys`
    /// bit does not mean "how many keys". It means **skulls count as cards**. With it set,
    /// a red skull opens a red card lock and vice versa, and "all keys" means one key of
    /// each colour rather than all six. Without it, the type matters.
    /// </summary>
    public static class GeneralizedLock
    {
        // Boom's keykind_e, in its order. The numbers are what a map's linedef
        // carries, so they are not ours to renumber.
        public const int AnyKey = 0;
        public const int RedCard = 1;
        public const int BlueCard = 2;
        public const int YellowCard = 3;
        public const int RedSkull = 4;
        public const int BlueSkull = 5;
        public const int YellowSkull = 6;
        public const int AllKeys = 7;

        /// <summary>Decides whether the player may open the door.</summary>
        public static LockVerdict Check(int key, bool skullsCountAsCards, bool[] cards)
        {
            var definition = LockDefs.Find(key);

            if (definition != null)
            {
                if (definition.IsSatisfied(cards))
                {
                    return LockVerdict.Opened;
                }

                return LockVerdict.Refused(
                    definition.Message != null ? definition.Message : DoomInfo.Strings.PD_LOCKED,
                    definition.Sound);
            }

            if (LockDefs.BuiltInsRemoved)
            {
                // `clearlocks` took the built-in rules away and this number was not given a
                // new one. Nothing is left to require, so the door opens. The load report
                // says so once; refusing here would make the door impassable for ever.
                return LockVerdict.Opened;
            }

            return CheckBuiltIn(key, skullsCountAsCards, cards);
        }

        private static LockVerdict CheckBuiltIn(int key, bool skullsCountAsCards, bool[] cards)
        {
            if (cards == null)
            {
                return LockVerdict.Refused(DoomInfo.Strings.PD_LOCKED, Sfx.OOF);
            }

            switch (key)
            {
                case AnyKey:
                    for (var i = 0; i < (int)CardType.Count; i++)
                    {
                        if (cards[i])
                        {
                            return LockVerdict.Opened;
                        }
                    }

                    return LockVerdict.Refused(DoomInfo.Strings.PD_ANY, Sfx.OOF);

                case RedCard:
                    return Need(cards, CardType.RedCard, CardType.RedSkull, skullsCountAsCards,
                                DoomInfo.Strings.PD_REDC, DoomInfo.Strings.PD_REDK);

                case BlueCard:
                    return Need(cards, CardType.BlueCard, CardType.BlueSkull, skullsCountAsCards,
                                DoomInfo.Strings.PD_BLUEC, DoomInfo.Strings.PD_BLUEK);

                case YellowCard:
                    return Need(cards, CardType.YellowCard, CardType.YellowSkull, skullsCountAsCards,
                                DoomInfo.Strings.PD_YELLOWC, DoomInfo.Strings.PD_YELLOWK);

                case RedSkull:
                    return Need(cards, CardType.RedSkull, CardType.RedCard, skullsCountAsCards,
                                DoomInfo.Strings.PD_REDS, DoomInfo.Strings.PD_REDK);

                case BlueSkull:
                    return Need(cards, CardType.BlueSkull, CardType.BlueCard, skullsCountAsCards,
                                DoomInfo.Strings.PD_BLUES, DoomInfo.Strings.PD_BLUEK);

                case YellowSkull:
                    return Need(cards, CardType.YellowSkull, CardType.YellowCard, skullsCountAsCards,
                                DoomInfo.Strings.PD_YELLOWS, DoomInfo.Strings.PD_YELLOWK);

                case AllKeys:
                    if (skullsCountAsCards)
                    {
                        // One key of each colour, card or skull.
                        if (HasAny(cards, CardType.RedCard, CardType.RedSkull) &&
                            HasAny(cards, CardType.BlueCard, CardType.BlueSkull) &&
                            HasAny(cards, CardType.YellowCard, CardType.YellowSkull))
                        {
                            return LockVerdict.Opened;
                        }

                        return LockVerdict.Refused(DoomInfo.Strings.PD_ALL3, Sfx.OOF);
                    }

                    // Every one of the six.
                    for (var i = 0; i < (int)CardType.Count; i++)
                    {
                        if (!cards[i])
                        {
                            return LockVerdict.Refused(DoomInfo.Strings.PD_ALL6, Sfx.OOF);
                        }
                    }

                    return LockVerdict.Opened;
            }

            // A key number this port does not know. Refusing is the safe answer: opening
            // the door would be a lock that does not lock.
            return LockVerdict.Refused(DoomInfo.Strings.PD_LOCKED, Sfx.OOF);
        }

        private static LockVerdict Need(bool[] cards, CardType kind, CardType alternative,
                                        bool skullsCountAsCards, DoomString specific,
                                        DoomString generic)
        {
            if (cards[(int)kind] || (skullsCountAsCards && cards[(int)alternative]))
            {
                return LockVerdict.Opened;
            }

            // The specific line names the item the player has to find ("a red card"),
            // the generic one does not ("a red key"). Boom picks the same way.
            return LockVerdict.Refused(skullsCountAsCards ? generic : specific, Sfx.OOF);
        }

        private static bool HasAny(bool[] cards, CardType first, CardType second)
        {
            return cards[(int)first] || cards[(int)second];
        }
    }
}
