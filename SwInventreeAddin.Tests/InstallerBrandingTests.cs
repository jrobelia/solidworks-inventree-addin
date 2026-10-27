using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// File-content assertions over the installer package surface
    /// (Setup.iss, Package.ps1, POST-INSTALL.txt, CI/release workflows):
    /// the brand shows "InvenTree Link for SOLIDWORKS" while the install
    /// identity stays on the SwInventreeAddin technical identifier.
    /// </summary>
    [TestFixture]
    public class InstallerBrandingTests
    {
        private const string ProductName = "InvenTree Link for SOLIDWORKS";
        private const string TechnicalName = "SwInventreeAddin";

        // ── Release artifact name ────────────────────────────────────

        [Test]
        public void OutputBaseFilename_TaggedBuild_ProducesInvenTreeLinkSetupExe()
        {
            var exeName = SetupValue("OutputBaseFilename", "2.1.0") + ".exe";

            Assert.That(exeName, Is.EqualTo("InvenTree-Link-2.1.0-Setup.exe"));
        }

        [Test]
        public void OutputBaseFilename_DevBuild_KeepsGitDerivedSuffix()
        {
            var exeName = SetupValue("OutputBaseFilename", "2.1.0-3-g0123abc") + ".exe";

            Assert.That(exeName, Is.EqualTo("InvenTree-Link-2.1.0-3-g0123abc-Setup.exe"));
        }

        // ── Wizard and Windows Apps identity ─────────────────────────

        [Test]
        public void AppName_WhenResolved_IsFullProductName()
        {
            Assert.That(SetupValue("AppName", "0.0.0"), Is.EqualTo(ProductName));
        }

        [Test]
        public void UninstallDisplayName_WhenResolved_IsFullProductName()
        {
            Assert.That(SetupValue("UninstallDisplayName", "0.0.0"), Is.EqualTo(ProductName));
        }

        [Test]
        public void AppPublisher_WhenResolved_IsJonRobelia()
        {
            Assert.That(SetupValue("AppPublisher", "0.0.0"), Is.EqualTo("Jon Robelia"));
        }

        // ── Stable install identity ──────────────────────────────────

        [Test]
        public void AppId_WhenResolved_IsUnchangedClsid()
        {
            Assert.That(SetupValue("AppId", "0.0.0"),
                Is.EqualTo("{{7C3E9A2F-4B61-4D8F-A2E5-1F6C9B3D8E47}}"));
        }

        [Test]
        public void DefaultDirName_WhenResolved_KeepsTechnicalName()
        {
            Assert.That(SetupValue("DefaultDirName", "0.0.0"),
                Is.EqualTo(@"{autopf}\" + TechnicalName));
        }

        [Test]
        public void LegacyUninstallKeyCleanup_TargetsTechnicalName()
        {
            var iss = ResolveDefines(ReadRepoFile("Installer", "Setup.iss"));

            Assert.That(iss, Does.Contain(@"Uninstall\" + TechnicalName));
        }

        [Test]
        public void SetupScript_ComRegistration_UsesRegAsm()
        {
            var iss = ReadRepoFile("Installer", "Setup.iss");

            Assert.That(iss, Does.Contain("RegAsm.exe"));
            Assert.That(iss, Does.Contain("'/codebase /s'"));
        }

        [Test]
        public void SetupScript_ComUnregistration_UsesRegAsm()
        {
            var iss = ReadRepoFile("Installer", "Setup.iss");

            Assert.That(iss, Does.Contain("'/u /s'"));
        }

        [Test]
        public void SetupScript_WizardArtworkAndNotices_AreUnchanged()
        {
            var iss = ReadRepoFile("Installer", "Setup.iss");

            Assert.That(iss, Does.Contain("SetupIconFile=sw-inventree-logo.ico"));
            Assert.That(iss, Does.Contain("WizardImageFile=wizard-image-164x314.bmp,wizard-image-328x628.bmp"));
            Assert.That(iss, Does.Contain("WizardSmallImageFile=wizard-small-55x55.bmp,wizard-small-110x110.bmp"));
            Assert.That(iss, Does.Contain("..\\THIRD-PARTY-NOTICES.txt"));
        }

        // ── Post-install notes ───────────────────────────────────────

        [Test]
        public void PostInstallNotes_TaskPaneAndAddIns_UseShortProductName()
        {
            var notes = ReadRepoFile("Installer", "POST-INSTALL.txt");

            Assert.That(notes, Does.Contain("\"InvenTree Link\""));
        }

        [Test]
        public void PostInstallNotes_WindowsAppsEntry_UsesFullProductName()
        {
            var notes = ReadRepoFile("Installer", "POST-INSTALL.txt");

            Assert.That(notes, Does.Contain("\"InvenTree Link for SOLIDWORKS\""));
        }

        [Test]
        public void PostInstallNotes_Anywhere_DropsTechnicalIdentifier()
        {
            var notes = ReadRepoFile("Installer", "POST-INSTALL.txt");

            Assert.That(notes, Does.Not.Contain(TechnicalName));
        }

        // ── Packaging and workflow selection ─────────────────────────

        [Test]
        public void PackageScript_InstallerPath_NamesInvenTreeLinkSetupExe()
        {
            var script = ReadRepoFile("Installer", "Package.ps1");

            Assert.That(script, Does.Contain("InvenTree-Link-$versionNumber-Setup.exe"));
        }

        [Test]
        public void PackageScript_Anywhere_DropsOldArtifactName()
        {
            var script = ReadRepoFile("Installer", "Package.ps1");

            Assert.That(script, Does.Not.Contain(TechnicalName + "-"));
        }

        [Test]
        public void CiWorkflow_PackageCheck_SelectsInvenTreeLinkSetupExe()
        {
            var ci = ReadRepoFile(".github", "workflows", "ci.yml");

            Assert.That(ci, Does.Contain(@"Installer\InvenTree-Link-*-Setup.exe"));
            Assert.That(ci, Does.Not.Contain(TechnicalName + "-*-Setup"));
        }

        [Test]
        public void ReleaseWorkflow_UploadGlobs_SelectInvenTreeLinkSetupExe()
        {
            var release = ReadRepoFile(".github", "workflows", "release.yml");

            Assert.That(release, Does.Contain("Installer/InvenTree-Link-*-Setup.exe"));
            Assert.That(release, Does.Contain("Installer/InvenTree-Link-*-Setup.exe.sha256"));
            Assert.That(release, Does.Not.Contain(TechnicalName + "-*-Setup"));
        }

        // ── Helpers ──────────────────────────────────────────────────

        // The effective value of a [Setup] directive: {#Define} macros expand,
        // AppVersion comes from the iscc command line so the caller supplies it.
        private static string SetupValue(string directive, string appVersion)
        {
            var iss = ReadRepoFile("Installer", "Setup.iss");
            var line = iss.Split('\n')
                .Select(l => l.Trim())
                .First(l => l.StartsWith(directive + "=", StringComparison.Ordinal));
            return ResolveDefines(iss, line.Substring(directive.Length + 1))
                .Replace("{#AppVersion}", appVersion);
        }

        private static string ResolveDefines(string text) => ResolveDefines(text, text);

        private static string ResolveDefines(string source, string text)
        {
            foreach (Match define in Regex.Matches(source, "#define\\s+(\\w+)\\s+\"([^\"]*)\"", RegexOptions.None))
            {
                if (define.Groups[1].Value != "AppVersion")
                    text = text.Replace("{#" + define.Groups[1].Value + "}", define.Groups[2].Value);
            }
            return text;
        }

        private static string ReadRepoFile(params string[] parts)
        {
            return File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Installer", "Setup.iss")))
                dir = dir.Parent;
            if (dir == null)
                throw new DirectoryNotFoundException(
                    "Repository root containing Installer\\Setup.iss not found above the test directory.");
            return dir.FullName;
        }
    }
}
