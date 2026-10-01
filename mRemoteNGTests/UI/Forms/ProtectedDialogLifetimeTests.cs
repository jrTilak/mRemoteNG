using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.Themes;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.TaskDialog;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms
{
    [TestFixture]
    [Platform("Win")]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class ProtectedDialogLifetimeTests
    {
        [Test]
        public void RepeatedMessageBoxesReleaseTheirControlsFromTheThemeSingleton()
        {
            ThemeManager theme = ThemeManager.getInstance();
            Delegate[] baseline = ThemeCallbacks(theme);

            for (int iteration = 0; iteration < 5; iteration++)
            {
                Control[] registeredControls;
                using (frmTaskDialog dialog = ProtectedMessageBox.CreateDialog("A message to dismiss", "Decision",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2))
                {
                    dialog.BuildForm();
                    registeredControls = ThemeCallbacks(theme).Except(baseline)
                        .Select(callback => callback.Target).OfType<Control>().Distinct().ToArray();
                    string[] controlTypes = registeredControls.Select(control => control.GetType().Name).ToArray();
                    Assert.That(controlTypes, Does.Contain("MrngLabel"));
                    Assert.That(controlTypes, Does.Contain("MrngButton"));
                    Assert.That(controlTypes, Does.Contain("MrngCheckBox"));
                }

                Assert.That(registeredControls.All(control => control.IsDisposed), Is.True);
                Assert.That(ThemeCallbacks(theme), Is.EqualTo(baseline),
                    $"Dialog {iteration + 1} left callbacks or changed pre-existing subscriptions after disposal.");
            }
        }

        [Test]
        public void RebuildingRadioChoicesReleasesOldControlsAndDisposalRestoresTheThemeCallbacks()
        {
            ThemeManager theme = ThemeManager.getInstance();
            Delegate[] baseline = ThemeCallbacks(theme);

            using (var dialog = new frmTaskDialog
            {
                Buttons = ETaskDialogButtons.OkCancel,
                RadioButtons = "First|Second",
                DefaultButtonIndex = 1
            })
            {
                dialog.BuildForm();
                for (int rebuild = 0; rebuild < 3; rebuild++)
                {
                    RadioButton[] oldChoices = ThemeCallbacks(theme).Except(baseline)
                        .Select(callback => callback.Target).OfType<RadioButton>().Distinct().ToArray();
                    Assert.That(oldChoices, Has.Length.EqualTo(rebuild == 0 ? 2 : 3));

                    dialog.RadioButtons = "First|Second|Third";
                    dialog.BuildForm();

                    Delegate[] afterRebuild = ThemeCallbacks(theme);
                    Assert.That(oldChoices.All(choice => choice.IsDisposed), Is.True);
                    Assert.That(afterRebuild.Any(callback => oldChoices.Any(choice => ReferenceEquals(callback.Target, choice))),
                        Is.False, "A replaced radio button must not receive future theme changes.");
                    RadioButton[] currentChoices = afterRebuild.Except(baseline)
                        .Select(callback => callback.Target).OfType<RadioButton>().Distinct().ToArray();
                    Assert.That(currentChoices, Has.Length.EqualTo(3));
                    Assert.That(currentChoices.All(choice => !choice.IsDisposed), Is.True);
                }
            }

            Assert.That(ThemeCallbacks(theme), Is.EqualTo(baseline),
                "The complete callback list and order must return to its pre-dialog state.");
        }

        private static Delegate[] ThemeCallbacks(ThemeManager theme)
        {
            // Inspect the strong-reference root directly instead of depending on
            // garbage-collection timing or invoking callbacks on disposed controls.
            FieldInfo callbacks = typeof(ThemeManager).GetField("ThemeChangedEvent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(callbacks, Is.Not.Null);
            return ((Delegate)callbacks.GetValue(theme))?.GetInvocationList() ?? Array.Empty<Delegate>();
        }
    }
}
