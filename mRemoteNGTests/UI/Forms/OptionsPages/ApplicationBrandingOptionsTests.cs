using System.Linq;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.App.Branding;
using mRemoteNG.Properties;
using mRemoteNG.UI.Forms.OptionsPages;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms.OptionsPages
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Platform("Win")]
    public class ApplicationBrandingOptionsTests
    {
        private string _name;
        private string _icon;
        private bool _show;
        private bool _hasPreference;

        [SetUp]
        public void SaveOriginalPreferences()
        {
            var settings = OptionsAppearancePage.Default;
            _name = settings.ApplicationDisplayName;
            _icon = settings.ApplicationIconPath;
            _show = settings.ShowInStartMenu;
            _hasPreference = settings.HasCustomStartMenuPreference;
        }

        [TearDown]
        public void RestoreOriginalPreferences()
        {
            var settings = OptionsAppearancePage.Default;
            settings.ApplicationDisplayName = _name;
            settings.ApplicationIconPath = _icon;
            settings.ShowInStartMenu = _show;
            settings.HasCustomStartMenuPreference = _hasPreference;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LoadSettingsShowsSavedStartMenuPreference(bool show)
        {
            OptionsAppearancePage.Default.HasCustomStartMenuPreference = true;
            OptionsAppearancePage.Default.ShowInStartMenu = show;
            using var page = new AppearancePage();
            page.LoadSettings();

            Assert.That(Find<CheckBox>(page, "chkHideFromStartMenu").Checked, Is.EqualTo(!show));
        }

        [Test]
        public void UnsetStartMenuPreferenceUsesBuildDefault()
        {
            OptionsAppearancePage.Default.HasCustomStartMenuPreference = false;
            OptionsAppearancePage.Default.ShowInStartMenu = !ApplicationBranding.DefaultShowInStartMenu;
            using var page = new AppearancePage();
            page.LoadSettings();

            Assert.That(Find<CheckBox>(page, "chkHideFromStartMenu").Checked,
                Is.EqualTo(!ApplicationBranding.DefaultShowInStartMenu));
        }

        [Test]
        public void ReloadDiscardsBrandingEditsWithoutChangingStoredSettings()
        {
            OptionsAppearancePage.Default.ApplicationDisplayName = "Support Desk";
            OptionsAppearancePage.Default.ApplicationIconPath = @"C:\Icons\support.ico";
            using var page = new AppearancePage();
            page.LoadSettings();
            Find<TextBox>(page, "txtApplicationDisplayName").Text = "Unsaved name";
            Find<TextBox>(page, "txtApplicationIconPath").Text = @"C:\Icons\unsaved.ico";

            page.LoadSettings();

            Assert.Multiple(() =>
            {
                Assert.That(Find<TextBox>(page, "txtApplicationDisplayName").Text, Is.EqualTo("Support Desk"));
                Assert.That(Find<TextBox>(page, "txtApplicationIconPath").Text, Is.EqualTo(@"C:\Icons\support.ico"));
                Assert.That(OptionsAppearancePage.Default.ApplicationDisplayName, Is.EqualTo("Support Desk"));
            });
        }

        [Test]
        public void ResetRestoresBuildDefaultsInControlsWithoutSaving()
        {
            OptionsAppearancePage.Default.ApplicationDisplayName = "Support Desk";
            OptionsAppearancePage.Default.ApplicationIconPath = @"C:\Icons\support.ico";
            OptionsAppearancePage.Default.HasCustomStartMenuPreference = true;
            OptionsAppearancePage.Default.ShowInStartMenu = !ApplicationBranding.DefaultShowInStartMenu;
            using var page = new AppearancePage { Dock = DockStyle.Fill };
            using var host = new Form();
            host.Controls.Add(page);
            page.LoadSettings();
            host.Show();

            Find<Button>(page, "btnResetApplicationBranding").PerformClick();

            Assert.Multiple(() =>
            {
                Assert.That(Find<TextBox>(page, "txtApplicationDisplayName").Text, Is.Empty);
                Assert.That(Find<TextBox>(page, "txtApplicationDisplayName").PlaceholderText,
                    Is.EqualTo(ApplicationBranding.DefaultDisplayName));
                Assert.That(Find<TextBox>(page, "txtApplicationIconPath").Text, Is.Empty);
                Assert.That(Find<CheckBox>(page, "chkHideFromStartMenu").Checked,
                    Is.EqualTo(!ApplicationBranding.DefaultShowInStartMenu));
                Assert.That(OptionsAppearancePage.Default.ApplicationDisplayName, Is.EqualTo("Support Desk"));
            });
        }

        private static T Find<T>(Control parent, string name) where T : Control =>
            parent.Controls.Find(name, true).OfType<T>().Single();
    }
}
