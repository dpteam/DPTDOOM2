using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The chat log: how many lines, how long they live, and what goes when it is full.
    ///
    /// Before this, a message went through the pickup message line: the next medikit erased it and
    /// eight seconds was the whole memory of a conversation. A player who looked up a second late
    /// had no way to know anything had been said.
    /// </summary>
    [TestClass]
    public class ChatLogTest
    {
        [TestMethod]
        public void ALinesAreAddedOldestFirst()
        {
            var log = new ChatLog();

            log.Add(0, "alpha", "first");
            log.Add(1, "bravo", "second");

            Assert.AreEqual(2, log.Count);
            Assert.AreEqual("alpha", log.Entries[0].Name);
            Assert.AreEqual("second", log.Entries[1].Text);
        }

        [TestMethod]
        public void TheOldestLineGoesWhenTheLogIsFull()
        {
            // The newest message is the one somebody is waiting for, so it survives.
            var log = new ChatLog();

            for (var i = 0; i < ChatLog.MaxLines + 2; i++)
            {
                log.Add(0, "player", "line " + i);
            }

            Assert.AreEqual(ChatLog.MaxLines, log.Count);

            var last = log.Entries[log.Count - 1];

            Assert.AreEqual("line " + (ChatLog.MaxLines + 1), last.Text, "the newest is still there");
            Assert.IsFalse(Contains(log, "line 0"), "the oldest is gone");
        }

        [TestMethod]
        public void ALineExpiresAfterItsLifetime()
        {
            var log = new ChatLog();

            log.Add(0, "player", "hello");

            for (var i = 0; i < ChatLog.Lifetime - 1; i++)
            {
                log.Update();
            }

            Assert.AreEqual(1, log.Count, "still on screen one tic before the end");

            log.Update();

            Assert.AreEqual(0, log.Count, "and gone after it");
        }

        [TestMethod]
        public void AnEmptyMessageIsNotStored()
        {
            // Something that would draw as "name: " with nothing after it is not a message.
            var log = new ChatLog();

            log.Add(0, "player", null);
            log.Add(0, "player", "");

            Assert.AreEqual(0, log.Count);
        }

        [TestMethod]
        public void AMissingNameFallsBackToTheSlotNumber()
        {
            // A welcome without names, or a message from a slot the welcome did not describe. The
            // line still has to say who it was, and "player 2" is better than a blank.
            var log = new ChatLog();

            log.Add(1, null, "hello");

            Assert.AreEqual("player 2", log.Entries[0].Name);
        }

        [TestMethod]
        public void TheLineIsTheNameAndTheText()
        {
            var log = new ChatLog();

            log.Add(0, "alpha", "hello there");

            // The name is kept separately, so a name containing a colon cannot be split wrongly -
            // but the drawn form is still one sentence.
            Assert.AreEqual("alpha: hello there", ChatLog.LineOf(log.Entries[0]));

            log.Clear();
            log.Add(0, "a: b", "hello");

            Assert.AreEqual("a: b: hello", ChatLog.LineOf(log.Entries[0]));
            Assert.AreEqual("a: b", log.Entries[0].Name);
        }

        private static bool Contains(ChatLog log, string text)
        {
            foreach (var entry in log.Entries)
            {
                if (entry.Text == text)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
