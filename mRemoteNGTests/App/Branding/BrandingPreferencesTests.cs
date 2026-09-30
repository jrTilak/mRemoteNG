using System;
using System.IO;
using mRemoteNG.App.Branding;
using NUnit.Framework;

namespace mRemoteNGTests.App.Branding
{
    [TestFixture]
    public class BrandingPreferencesTests
    {
        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        [TestCase("  Team Desktop  ", "Team Desktop")]
        [TestCase("डेस्कटप 日本語", "डेस्कटप 日本語")]
        [TestCase("Team.Desktop", "Team.Desktop")]
        [TestCase("Console", "Console")]
        [TestCase("COM10", "COM10")]
        public void NormalizesValidDisplayNamesWithoutLosingUnicode(string input, string expected)
        {
            Assert.That(BrandingPreferences.NormalizeDisplayName(input), Is.EqualTo(expected));
        }

        [TestCase("CON")]
        [TestCase("con.txt")]
        [TestCase(" PRN ")]
        [TestCase("AUX")]
        [TestCase("NUL")]
        [TestCase("CONIN$")]
        [TestCase("CONOUT$")]
        [TestCase("COM1")]
        [TestCase("com9.ico")]
        [TestCase("LPT1")]
        [TestCase("lpt9.txt")]
        [TestCase("COM¹")]
        [TestCase("LPT²")]
        [TestCase("COM³.txt")]
        public void RejectsWindowsDeviceNamesIncludingExtensions(string input)
        {
            Assert.That(() => BrandingPreferences.NormalizeDisplayName(input), Throws.ArgumentException);
        }

        [TestCase("../Other App")]
        [TestCase("..\\Other App")]
        [TestCase("..")]
        [TestCase(".")]
        [TestCase("C:\\Other App")]
        [TestCase("/Other App")]
        [TestCase("Team<App")]
        [TestCase("Team>App")]
        [TestCase("Team:App")]
        [TestCase("Team\"App")]
        [TestCase("Team|App")]
        [TestCase("Team?App")]
        [TestCase("Team*App")]
        [TestCase("Team App.")]
        [TestCase("Team App.  ")]
        [TestCase("Team\nApp")]
        [TestCase("Team\tApp")]
        [TestCase("Team\0App")]
        public void RejectsTraversalAndInvalidWindowsFilenameCharactersOnEveryPlatform(string input)
        {
            Assert.That(() => BrandingPreferences.NormalizeDisplayName(input), Throws.ArgumentException);
        }

        [Test]
        public void EnforcesLengthLimitAfterTrimming()
        {
            string maximum = new('x', 80);
            Assert.That(BrandingPreferences.NormalizeDisplayName(" " + maximum + " "), Is.EqualTo(maximum));
            Assert.That(() => BrandingPreferences.NormalizeDisplayName(maximum + "x"), Throws.ArgumentException);
        }

        [Test]
        public void TargetComparisonUsesCanonicalPathsAndWindowsCaseRules()
        {
            string executable = Path.Combine(Path.GetTempPath(), "BrandingTests", "Client.exe");
            string equivalent = Path.Combine(Path.GetTempPath(), "BrandingTests", "child", "..", "CLIENT.EXE");
            Assert.That(BrandingPreferences.SameTarget(executable, equivalent), Is.True);
            Assert.That(BrandingPreferences.SameTarget(executable, executable.ToUpperInvariant()), Is.True);
        }

        [Test]
        public void SameExecutableFilenameInAnotherInstallationIsNotOwned()
        {
            string first = Path.Combine(Path.GetTempPath(), "FirstInstall", "Client.exe");
            string second = Path.Combine(Path.GetTempPath(), "SecondInstall", "Client.exe");
            Assert.That(BrandingPreferences.SameTarget(first, second), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void MissingOrUnreadableTargetIsNeverOwned(string missing)
        {
            string executable = Path.Combine(Path.GetTempPath(), "BrandingTests", "Client.exe");
            Assert.That(BrandingPreferences.SameTarget(missing, executable), Is.False);
            Assert.That(BrandingPreferences.SameTarget(executable, missing), Is.False);
        }
    }
}
