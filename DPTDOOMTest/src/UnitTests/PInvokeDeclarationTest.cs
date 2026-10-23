using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Guards the P/Invoke declarations against the mistake that has now bitten
    /// this project twice.
    ///
    /// With <c>CharSet.Unicode</c> and <c>ExactSpelling = true</c>, the runtime
    /// looks for a function with exactly the declared name. But Windows exports
    /// "SendMessageW" and "SendMessageA", not "SendMessage", so the call fails at
    /// run time with EntryPointNotFoundException — not at compile time.
    ///
    /// Concretely: <c>DefWindowProc</c> silently bound the ANSI variant (window
    /// titles came out as "D"), and <c>SendMessage</c> and <c>LoadIcon</c>
    /// crashed on startup.
    ///
    /// The rule enforced here: a Unicode declaration must either leave
    /// ExactSpelling false (the runtime appends "W" itself) or name the W entry
    /// point explicitly.
    /// </summary>
    [TestClass]
    public class PInvokeDeclarationTest
    {
        [TestMethod]
        public void UnicodeImportsResolveToTheWideEntryPoint()
        {
            var assembly = typeof(DPTDOOM.Win32.NativeMethods).Assembly;
            var problems = new System.Collections.Generic.List<string>();

            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var attribute = method
                        .GetCustomAttributes(typeof(DllImportAttribute), false)
                        .Cast<DllImportAttribute>()
                        .FirstOrDefault();

                    if (attribute == null)
                    {
                        continue;
                    }

                    if (attribute.CharSet != CharSet.Unicode || !attribute.ExactSpelling)
                    {
                        continue;
                    }

                    var entryPoint = string.IsNullOrEmpty(attribute.EntryPoint)
                        ? method.Name
                        : attribute.EntryPoint;

                    if (!entryPoint.EndsWith("W", StringComparison.Ordinal))
                    {
                        problems.Add(
                            type.Name + "." + method.Name + " -> '" + entryPoint +
                            "' (ExactSpelling with CharSet.Unicode needs the W entry point)");
                    }
                }
            }

            Assert.AreEqual(
                0,
                problems.Count,
                "These P/Invoke declarations would fail at run time:\n  " +
                string.Join("\n  ", problems.ToArray()));
        }

        [TestMethod]
        public void EveryDeclaredEntryPointHasAnAttribute()
        {
            // A method that looks like a Win32 call but lost its attribute would
            // be a managed method and fail in a much more confusing way.
            var assembly = typeof(DPTDOOM.Win32.NativeMethods).Assembly;
            var type = assembly.GetType("DPTDOOM.Win32.NativeMethods");

            Assert.IsNotNull(type, "NativeMethods is missing");

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!method.IsAbstract && method.GetMethodBody() != null)
                {
                    continue;
                }

                Assert.IsTrue(
                    method.GetCustomAttributes(typeof(DllImportAttribute), false).Length > 0,
                    method.Name + " is extern but has no DllImport");
            }
        }
    }
}
