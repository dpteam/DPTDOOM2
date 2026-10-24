using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The chat input line.
    ///
    /// Tested without a network on purpose: the interesting behaviour is what
    /// happens to a keystroke, and that has to be right before anything is sent.
    /// </summary>
    [TestClass]
    public class ChatTest
    {
        private static DoomEvent Key(DoomKey key)
        {
            return new DoomEvent(EventType.KeyDown, key);
        }

        private static void Type(Chat chat, string text)
        {
            foreach (var ch in text)
            {
                chat.Handle(Key(CharToKey(ch)));
            }
        }

        private static DoomKey CharToKey(char ch)
        {
            if (ch >= 'a' && ch <= 'z')
            {
                return DoomKey.A + (ch - 'a');
            }

            if (ch >= '0' && ch <= '9')
            {
                return DoomKey.Num0 + (ch - '0');
            }

            if (ch == ' ')
            {
                return DoomKey.Space;
            }

            throw new ArgumentException("test helper does not map '" + ch + "'");
        }

        [TestMethod]
        public void InactiveChatIgnoresEverything()
        {
            var chat = new Chat();

            Assert.IsFalse(chat.Handle(Key(DoomKey.A)),
                "an inactive chat must not consume events, or the game stops responding");
            Assert.AreEqual("", chat.Text);
        }

        [TestMethod]
        public void TypingBuildsTheLine()
        {
            var chat = new Chat();
            chat.Open();

            Type(chat, "hello there");

            Assert.AreEqual("hello there", chat.Text);
            Assert.IsTrue(chat.Active);
        }

        [TestMethod]
        public void EveryKeyIsSwallowedWhileTyping()
        {
            // The whole point: typing a cheat code into the chat must not fire it,
            // and Tab must not open the automap mid-sentence.
            var chat = new Chat();
            chat.Open();

            Assert.IsTrue(chat.Handle(Key(DoomKey.A)));
            Assert.IsTrue(chat.Handle(Key(DoomKey.Tab)));
            Assert.IsTrue(chat.Handle(Key(DoomKey.B)));
        }

        [TestMethod]
        public void BackspaceRemovesTheLastCharacter()
        {
            var chat = new Chat();
            chat.Open();

            Type(chat, "abc");
            chat.Handle(Key(DoomKey.Backspace));

            Assert.AreEqual("ab", chat.Text);

            // Backspace on an empty line must not underflow.
            chat.Handle(Key(DoomKey.Backspace));
            chat.Handle(Key(DoomKey.Backspace));
            chat.Handle(Key(DoomKey.Backspace));

            Assert.AreEqual("", chat.Text);
        }

        [TestMethod]
        public void EscapeCancelsWithoutSubmitting()
        {
            var chat = new Chat();
            chat.Open();
            Type(chat, "never mind");

            chat.Handle(Key(DoomKey.Escape));

            Assert.IsFalse(chat.Active);
            Assert.IsFalse(chat.Submitted, "escape must not send anything");
            Assert.AreEqual("", chat.Text);
        }

        [TestMethod]
        public void EnterSubmitsAndCloses()
        {
            var chat = new Chat();
            chat.Open();
            Type(chat, "gg");

            chat.Handle(Key(DoomKey.Enter));

            Assert.IsTrue(chat.Submitted);
            Assert.IsFalse(chat.Active);
            Assert.AreEqual("gg", chat.Text, "the text is still there for the caller to send");
        }

        [TestMethod]
        public void TheLineIsCapped()
        {
            var chat = new Chat();
            chat.Open();

            for (var i = 0; i < Chat.MaxLength + 40; i++)
            {
                chat.Handle(Key(DoomKey.A));
            }

            Assert.AreEqual(Chat.MaxLength, chat.Text.Length,
                "a message must not exceed the limit the protocol can frame");
        }

        [TestMethod]
        public void UnprintableKeysAreNotInserted()
        {
            var chat = new Chat();
            chat.Open();

            chat.Handle(Key(DoomKey.F1));
            chat.Handle(Key(DoomKey.Left));
            chat.Handle(Key(DoomKey.Enter));
            chat.Handle(Key(DoomKey.Backspace));

            Assert.AreEqual("", chat.Text);
        }

        [TestMethod]
        public void TheSameKeystrokesDoNotTypeTwice()
        {
            // KeyDown and KeyUp both arrive for every key. Only KeyDown may add a
            // character, or every letter appears twice - which looks like a broken
            // keyboard rather than a chat bug.
            var chat = new Chat();
            chat.Open();

            chat.Handle(new DoomEvent(EventType.KeyDown, DoomKey.A));
            chat.Handle(new DoomEvent(EventType.KeyUp, DoomKey.A));

            Assert.AreEqual("a", chat.Text);
        }
    }
}
