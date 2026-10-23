using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Guards the P/Invoke declarations against the mistake that has bitten this
    /// project four times now: DefWindowProc, LoadIcon, SendMessage and
    /// GetWindowLong were each declared in a way that resolved to a function
    /// Windows does not export, and each failed at run time with
    /// EntryPointNotFoundException — never at compile time.
    ///
    /// Two checks:
    ///
    ///  1. Static: with CharSet.Unicode and ExactSpelling the runtime looks for
    ///     the exact declared name, so it must end in "W". This runs everywhere,
    ///     including CI on Linux.
    ///
    ///  2. Runtime: every declared entry point is actually looked up in the real
    ///     DLL. This catches names that look right but do not exist at all, and
    ///     it is the check that would have caught all four bugs. It only runs on
    ///     Windows, where those DLLs are present.
    /// </summary>
    [TestClass]
    public class PInvokeDeclarationTest
    {
        private static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        [TestMethod]
        public void UnicodeImportsNameTheWideEntryPoint()
        {
            var problems = new List<string>();

            foreach (var method in Imports())
            {
                var attribute = Attribute(method);

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
                        method.DeclaringType.Name + "." + method.Name + " -> '" + entryPoint + "'");
                }
            }

            Assert.AreEqual(
                0,
                problems.Count,
                "These would fail at run time (ExactSpelling + Unicode needs the W name):\n  " +
                string.Join("\n  ", problems.ToArray()));
        }

        [TestMethod]
        public void EveryEntryPointExistsInItsDll()
        {
            if (!IsWindows)
            {
                Assert.Inconclusive("The Win32 DLLs only exist on Windows.");
            }

            var problems = new List<string>();

            foreach (var method in Imports())
            {
                var attribute = Attribute(method);

                foreach (var candidate in CandidateNames(method, attribute))
                {
                    if (EntryPointExists(attribute.Value, candidate))
                    {
                        goto found;
                    }
                }

                problems.Add(
                    method.DeclaringType.Name + "." + method.Name +
                    " -> " + attribute.Value + "!" + string.Join(" / ", CandidateNames(method, attribute).ToArray()));

                found: ;
            }

            Assert.AreEqual(
                0,
                problems.Count,
                "These entry points do not exist in the DLL and would throw at run time:\n  " +
                string.Join("\n  ", problems.ToArray()));
        }

        /// <summary>
        /// The names the runtime may look up for this declaration.
        ///
        /// With ExactSpelling the name is used as written, and nothing else is
        /// acceptable — that is the strict case, and the one this project keeps
        /// getting wrong.
        ///
        /// Without ExactSpelling the runtime resolves the ANSI or wide variant
        /// and falls back to the plain name, so any of the three is valid. That
        /// still catches a declaration pointing at the wrong DLL, which is how
        /// SetTextColor, SetBkMode and TextOut were found in user32 instead of
        /// gdi32.
        /// </summary>
        private static IEnumerable<string> CandidateNames(MethodInfo method, DllImportAttribute attribute)
        {
            var entryPoint = string.IsNullOrEmpty(attribute.EntryPoint)
                ? method.Name
                : attribute.EntryPoint;

            if (attribute.ExactSpelling)
            {
                yield return entryPoint;
                yield break;
            }

            yield return entryPoint;
            yield return entryPoint + "W";
            yield return entryPoint + "A";
        }

        [TestMethod]
        public void ExternMethodsHaveAttributes()
        {
            var type = typeof(DPTDOOM.Win32.NativeMethods);

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetMethodBody() != null)
                {
                    continue;
                }

                Assert.IsTrue(
                    method.GetCustomAttributes(typeof(DllImportAttribute), false).Length > 0,
                    method.Name + " is extern but has no DllImport");
            }
        }

        ////////////////////////////////////////////////////////////
        // Helpers
        ////////////////////////////////////////////////////////////

        private static IEnumerable<MethodInfo> Imports()
        {
            var assembly = typeof(DPTDOOM.Win32.NativeMethods).Assembly;

            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.GetCustomAttributes(typeof(DllImportAttribute), false).Length > 0)
                    {
                        yield return method;
                    }
                }
            }
        }

        private static DllImportAttribute Attribute(MethodInfo method)
        {
            return method
                .GetCustomAttributes(typeof(DllImportAttribute), false)
                .Cast<DllImportAttribute>()
                .First();
        }

        private static bool EntryPointExists(string library, string entryPoint)
        {
            IntPtr module;
            if (!LoadLibraryEx(library, IntPtr.Zero, 0, out module))
            {
                // The DLL itself is missing; that is a different failure and the
                // game could not run at all.
                return false;
            }

            try
            {
                return GetProcAddress(module, entryPoint) != IntPtr.Zero;
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        private static bool LoadLibraryEx(string name, IntPtr file, uint flags, out IntPtr module)
        {
            module = LoadLibrary(name);
            return module != IntPtr.Zero;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}
