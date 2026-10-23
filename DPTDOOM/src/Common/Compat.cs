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
    /// Small helpers that fill gaps between .NET Framework 4.0 and the modern
    /// BCL. Keeping them in one place is what lets the game code stay close to
    /// the original Managed Doom sources while still targeting Windows XP.
    /// </summary>
    internal static class MathEx
    {
        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }

#if NET40
    /// <summary>
    /// System.MathF does not exist on .NET Framework 4.0. Only the members the
    /// port actually uses are provided.
    /// </summary>
    internal static class MathF
    {
        public static float Round(float value)
        {
            return (float)Math.Round(value);
        }

        public static float Sqrt(float value)
        {
            return (float)Math.Sqrt(value);
        }

        public static float Atan2(float y, float x)
        {
            return (float)Math.Atan2(y, x);
        }

        public static float Sin(float value)
        {
            return (float)Math.Sin(value);
        }

        public static float Cos(float value)
        {
            return (float)Math.Cos(value);
        }
    }

    internal static class DictionaryExtensions
    {
        public static bool TryAdd<TKey, TValue>(
            this System.Collections.Generic.IDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }
    }
#endif
}

#if NET40
namespace System.Runtime.ExceptionServices
{
    /// <summary>
    /// .NET Framework 4.0 has no ExceptionDispatchInfo. This shim keeps the
    /// original code shape; the stack trace of a rethrown exception is not
    /// preserved, which is acceptable for a game.
    /// </summary>
    public sealed class ExceptionDispatchInfo
    {
        private readonly Exception exception;

        private ExceptionDispatchInfo(Exception exception)
        {
            this.exception = exception;
        }

        public static ExceptionDispatchInfo Capture(Exception source)
        {
            return new ExceptionDispatchInfo(source);
        }

        public void Throw()
        {
            throw exception;
        }

        public static void Throw(Exception source)
        {
            throw source;
        }
    }
}
#endif
