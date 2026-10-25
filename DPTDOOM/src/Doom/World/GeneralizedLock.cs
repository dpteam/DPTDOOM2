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
    /// <summary>
    /// Which key a Boom generalized locked door needs, and what to say when the
    /// player has none.
    ///
    /// This is separate from <see cref="BoomGeneralized"/> on purpose. Decoding
    /// answers "what does this number mean"; this answers "may this player open
    /// it", which is the part a mod can change and the part a test can pin
    /// without a map. The rules are Boom's, taken from
    /// `refs/prboom-plus/prboom2/src/p_spec.c`, `P_CanUnlockGenDoor`.
    ///
    /// The one piece of Boom behaviour that looks like a bug and is not: the
    /// `LockedNKeys` bit does not mean "how many keys". It means **skulls count
    /// as cards**. With it set, a red skull opens a red card lock and vice
    /// versa, and "all keys" means one key of each colour rather than all six.
    /// Without it, the type matters: a card lock wants a card, a skull lock
    /// wants a skull, and "all keys" wants every one of the six.
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

        /// <summary>
        /// True when the player may open the door. When false, <paramref name="message"/>
        /// is the line to show, and is never null.
        /// </summary>
        public static bool CanOpen(int key, bool skullsCountAsCards, bool[] cards,
                                   out DoomString message)
        {
            message = null;

            if (cards == null)
            {
                return false;
            }

            switch (key)
            {
                case AnyKey:
                    for (var i = 0; i < (int)CardType.Count; i++)
                    {
                        if (cards[i])
                        {
                            return true;
                        }
                    }

                    message = DoomInfo.Strings.PD_ANY;
                    return false;

                case RedCard:
                    return Need(cards, CardType.RedCard, CardType.RedSkull, skullsCountAsCards,
                                DoomInfo.Strings.PD_REDC, DoomInfo.Strings.PD_REDK, out message);

                case BlueCard:
                    return Need(cards, CardType.BlueCard, CardType.BlueSkull, skullsCountAsCards,
                                DoomInfo.Strings.PD_BLUEC, DoomInfo.Strings.PD_BLUEK, out message);

                case YellowCard:
                    return Need(cards, CardType.YellowCard, CardType.YellowSkull, skullsCountAsCards,
                                DoomInfo.Strings.PD_YELLOWC, DoomInfo.Strings.PD_YELLOWK, out message);

                case RedSkull:
                    return Need(cards, CardType.RedSkull, CardType.RedCard, skullsCountAsCards,
                                DoomInfo.Strings.PD_REDS, DoomInfo.Strings.PD_REDK, out message);

                case BlueSkull:
                    return Need(cards, CardType.BlueSkull, CardType.BlueCard, skullsCountAsCards,
                                DoomInfo.Strings.PD_BLUES, DoomInfo.Strings.PD_BLUEK, out message);

                case YellowSkull:
                    return Need(cards, CardType.YellowSkull, CardType.YellowCard, skullsCountAsCards,
                                DoomInfo.Strings.PD_YELLOWS, DoomInfo.Strings.PD_YELLOWK, out message);

                case AllKeys:
                    if (skullsCountAsCards)
                    {
                        // One key of each colour, card or skull.
                        if (HasAny(cards, CardType.RedCard, CardType.RedSkull) &&
                            HasAny(cards, CardType.BlueCard, CardType.BlueSkull) &&
                            HasAny(cards, CardType.YellowCard, CardType.YellowSkull))
                        {
                            return true;
                        }

                        message = DoomInfo.Strings.PD_ALL3;
                        return false;
                    }

                    // Every one of the six.
                    for (var i = 0; i < (int)CardType.Count; i++)
                    {
                        if (!cards[i])
                        {
                            message = DoomInfo.Strings.PD_ALL6;
                            return false;
                        }
                    }

                    return true;
            }

            // A key number this port does not know. Refusing is the safe answer: opening
            // the door would be a lock that does not lock.
            message = DoomInfo.Strings.PD_ANY;
            return false;
        }

        private static bool Need(bool[] cards, CardType kind, CardType alternative,
                                 bool skullsCountAsCards, DoomString specific, DoomString generic,
                                 out DoomString message)
        {
            if (cards[(int)kind] || (skullsCountAsCards && cards[(int)alternative]))
            {
                message = null;
                return true;
            }

            // The specific line names the item the player has to find ("a red card"),
            // the generic one does not ("a red key"). Boom picks the same way.
            message = skullsCountAsCards ? generic : specific;
            return false;
        }

        private static bool HasAny(bool[] cards, CardType first, CardType second)
        {
            return cards[(int)first] || cards[(int)second];
        }
    }
}
