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

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// Anything wrong with a script, at any stage, with the place it happened.
    ///
    /// One exception type for the whole language, because the caller's decision is the
    /// same in every case: a broken script must not crash the game and must not be
    /// silently ignored either. It is reported, the actor keeps its previous behaviour,
    /// and the load report says which line was wrong.
    ///
    /// The line is the important part. "Unexpected token" without a line number turns a
    /// five-line script into a guessing game, and a five-hundred-line one into an
    /// afternoon.
    /// </summary>
    public sealed class ScriptException : Exception
    {
        public ScriptException(string message)
            : base(message)
        {
            Line = 0;
        }

        public ScriptException(string message, int line)
            : base(line > 0 ? "line " + line + ": " + message : message)
        {
            Line = line;
        }

        public ScriptException(string message, int line, Exception inner)
            : base(line > 0 ? "line " + line + ": " + message : message, inner)
        {
            Line = line;
        }

        /// <summary>The 1-based source line, or 0 when it is not known.</summary>
        public int Line { get; private set; }
    }
}
