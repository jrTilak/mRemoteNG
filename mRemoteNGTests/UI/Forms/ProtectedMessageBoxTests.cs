using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.UI.CaptureProtection;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.TaskDialog;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms
{
    // Most cases build real controls without a modal UI loop. The monitor case
    // closes itself from Shown; screenshot exclusion still needs capture tests.
    [TestFixture]
    [Platform("Win")]
    [Apartment(ApartmentState.STA)]
    public class ProtectedMessageBoxTests
    {
        [TestCase(MessageBoxButtons.OK, MessageBoxDefaultButton.Button1, DialogResult.OK, DialogResult.OK, true)]
        [TestCase(MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button1, DialogResult.OK, DialogResult.Cancel, true)]
        [TestCase(MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button2, DialogResult.Cancel, DialogResult.Cancel, true)]
        [TestCase(MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button1, DialogResult.Yes, DialogResult.None, false)]
        [TestCase(MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button2, DialogResult.No, DialogResult.None, false)]
        [TestCase(MessageBoxButtons.YesNoCancel, MessageBoxDefaultButton.Button1, DialogResult.Yes, DialogResult.Cancel, true)]
        [TestCase(MessageBoxButtons.YesNoCancel, MessageBoxDefaultButton.Button2, DialogResult.No, DialogResult.Cancel, true)]
        [TestCase(MessageBoxButtons.YesNoCancel, MessageBoxDefaultButton.Button3, DialogResult.Cancel, DialogResult.Cancel, true)]
        public void DefaultAndEscapeButtonsPreserveTheRequestedDecision(MessageBoxButtons buttons,
            MessageBoxDefaultButton defaultButton, DialogResult expectedDefault, DialogResult expectedEscape,
            bool allowClose)
        {
            using frmTaskDialog dialog = ProtectedMessageBox.CreateDialog("Choose an action", "Decision",
                buttons, MessageBoxIcon.Question, defaultButton);

            dialog.BuildForm();

            Assert.That(dialog.AcceptButton, Is.Not.Null);
            Assert.That(dialog.AcceptButton.DialogResult, Is.EqualTo(expectedDefault));
            Assert.That(dialog.CancelButton?.DialogResult ?? DialogResult.None, Is.EqualTo(expectedEscape));
            Assert.That(dialog.ControlBox, Is.EqualTo(allowClose));
            Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.None), "Building must not answer the prompt.");
        }

        [Test]
        public void YesNoDoesNotTreatEscapeAsNo()
        {
            using var dialog = new ClosingProbeDialog { Buttons = ETaskDialogButtons.YesNo };
            dialog.ConfigureMessageBox(1);
            dialog.BuildForm();

            Assert.That(dialog.AcceptButton.DialogResult, Is.EqualTo(DialogResult.No), "Restart keeps its safe default.");
            Assert.That(dialog.CancelButton, Is.Null, "Native Yes/No has no Escape action.");
            Assert.That(dialog.ControlBox, Is.False);
            Assert.That(dialog.PressEscape(), Is.True, "Consume Escape without answering the prompt.");
            Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.None));
        }

        [TestCase(ETaskDialogButtons.Ok, DialogResult.OK)]
        [TestCase(ETaskDialogButtons.OkCancel, DialogResult.Cancel)]
        [TestCase(ETaskDialogButtons.YesNoCancel, DialogResult.Cancel)]
        public void ClosingWithoutASelectionPreservesTheNativeResult(ETaskDialogButtons buttons,
            DialogResult expectedResult)
        {
            using var dialog = new ClosingProbeDialog { Buttons = buttons };
            dialog.ConfigureMessageBox(0);
            dialog.BuildForm();

            // A modal WinForms close supplies Cancel before OnFormClosing.
            // A hidden modeless Close does not initialize that modal result.
            bool canceled = dialog.RequestUserClose(DialogResult.Cancel);

            Assert.That(canceled, Is.False);
            Assert.That(dialog.DialogResult, Is.EqualTo(expectedResult));
        }

        [Test]
        public void YesNoRequiresAnExplicitAnswerBeforeClosing()
        {
            using var dialog = new ClosingProbeDialog { Buttons = ETaskDialogButtons.YesNo };
            dialog.ConfigureMessageBox(0);
            dialog.BuildForm();

            Assert.That(dialog.RequestUserClose(DialogResult.Cancel), Is.True);
            Assert.That(dialog.RequestUserClose(DialogResult.None), Is.True);
            Assert.That(dialog.RequestUserClose(DialogResult.No), Is.False);
            Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.No));
            Assert.That(dialog.RequestUserClose(DialogResult.Yes), Is.False);
            Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.Yes));
        }

        [Test]
        public void RebuildingPreservesTheDefaultAndRemovesStaleCancelActions()
        {
            using frmTaskDialog dialog = Create(MessageBoxButtons.YesNoCancel, MessageBoxDefaultButton.Button2);
            dialog.BuildForm();
            dialog.BuildForm();
            Assert.That(dialog.AcceptButton.DialogResult, Is.EqualTo(DialogResult.No));
            Assert.That(dialog.CancelButton.DialogResult, Is.EqualTo(DialogResult.Cancel));

            dialog.Buttons = ETaskDialogButtons.Ok;
            dialog.ConfigureMessageBox(0);
            dialog.BuildForm();
            Assert.That(dialog.AcceptButton.DialogResult, Is.EqualTo(DialogResult.OK));
            Assert.That(dialog.CancelButton.DialogResult, Is.EqualTo(DialogResult.OK));

            dialog.Buttons = ETaskDialogButtons.YesNo;
            dialog.ConfigureMessageBox(1);
            dialog.BuildForm();
            Assert.That(dialog.AcceptButton.DialogResult, Is.EqualTo(DialogResult.No));
            Assert.That(dialog.CancelButton, Is.Null);
            Assert.That(dialog.ControlBox, Is.False);
        }

        [Test]
        public void RebuildingACommandOnlyDialogKeepsItsDefaultCommand()
        {
            using var dialog = new frmTaskDialog
            {
                Buttons = ETaskDialogButtons.None,
                CommandButtons = "First|Second",
                DefaultButtonIndex = 1
            };
            dialog.BuildForm();
            var originalDefault = (Control)dialog.AcceptButton;

            dialog.BuildForm();

            Assert.That(dialog.AcceptButton, Is.InstanceOf<Control>());
            Assert.That(((Control)dialog.AcceptButton).Tag, Is.EqualTo(1));
            Assert.That(dialog.AcceptButton, Is.Not.SameAs(originalDefault));
            Assert.That(originalDefault.IsDisposed, Is.True);
            Assert.That(dialog.AcceptButton.DialogResult, Is.EqualTo(DialogResult.None));
            Assert.That(dialog.CancelButton, Is.Null);
            Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.None));
        }

        [Test]
        [NonParallelizable]
        public void LongMessageRemainsScrollableWithDecisionsAndStatusInsideTheWorkingArea()
        {
            string text = string.Join(Environment.NewLine, Enumerable.Range(1, 160)
                .Select(line => $"Line {line}: retain this complete decision text, including & characters."));
            using frmTaskDialog dialog = ProtectedMessageBox.CreateDialog(text, "Long decision",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            dialog.BuildForm();

            Assert.That(dialog.Bounds.Height, Is.LessThanOrEqualTo(Screen.FromHandle(dialog.Handle).WorkingArea.Height));
            // Screen-coordinate checks need the real native parent chain. A
            // never-shown child can still belong to a WinForms parking window.
            dialog.Show();
            Application.DoEvents();
            Assert.That(dialog.Bounds.Height, Is.LessThanOrEqualTo(Screen.FromHandle(dialog.Handle).WorkingArea.Height));
            var overflow = (TextBox)dialog.Controls.Find("MessageTextOverflow", true).Single();
            Assert.That(overflow.Visible, Is.True);
            Assert.That(overflow.Text, Is.EqualTo(text));
            Assert.That(overflow.ReadOnly, Is.True);
            Assert.That(overflow.Multiline, Is.True);
            Assert.That(overflow.ScrollBars, Is.EqualTo(ScrollBars.Vertical));
            Assert.That(overflow.Height, Is.GreaterThan(0));
            Rectangle overflowBounds = BoundsWithinDialog(dialog, overflow);
            Assert.That(dialog.ClientRectangle.Contains(overflowBounds), Is.True,
                $"Dialog client={dialog.ClientRectangle}; overflow in dialog={overflowBounds}; " +
                $"overflow bounds={overflow.Bounds}; parent bounds={overflow.Parent.Bounds}; DPI={dialog.DeviceDpi}.");

            // Each choice remains visible outside the scrolling message area.
            foreach (string name in new[] { "bt1", "bt2", "bt3" })
            {
                Control button = dialog.Controls.Find(name, true).Single();
                Assert.That(button.Visible, Is.True, $"{name} must remain visible.");
                Rectangle buttonBounds = BoundsWithinDialog(dialog, button);
                Assert.That(dialog.ClientRectangle.Contains(buttonBounds), Is.True, $"{name} must remain clickable.");
                Assert.That(overflowBounds.IntersectsWith(buttonBounds), Is.False);
            }

            Control status = dialog.Controls.Find("CaptureProtectionStatus", true).Single();
            Rectangle statusBounds = BoundsWithinDialog(dialog, status);
            Assert.That(status.Height, Is.GreaterThan(0));
            Assert.That(dialog.ClientRectangle.Contains(statusBounds), Is.True);
            Assert.That(overflowBounds.IntersectsWith(statusBounds), Is.False);
            Assert.That(dialog.AcceptButton.DialogResult, Is.EqualTo(DialogResult.No));
            Assert.That(dialog.CancelButton.DialogResult, Is.EqualTo(DialogResult.Cancel));
        }

        [TestCase(FormStartPosition.CenterScreen)]
        [TestCase(FormStartPosition.CenterParent)]
        [NonParallelizable]
        public void ModalPresentationRefitsLongTextToTheShorterOwnerMonitor(FormStartPosition startPosition)
        {
            Screen[] screens = Screen.AllScreens.OrderBy(screen => screen.WorkingArea.Height).ToArray();
            if (screens.Length < 2 || screens[0].WorkingArea.Height == screens[^1].WorkingArea.Height)
                Assert.Ignore("Requires two monitors with different working-area heights.");
            Rectangle ownerArea = screens[0].WorkingArea;
            Rectangle initialArea = screens[^1].WorkingArea;
            using var owner = new Form
            {
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Bounds = new Rectangle(ownerArea.Left + ownerArea.Width / 4, ownerArea.Top + ownerArea.Height / 4,
                    ownerArea.Width / 2, ownerArea.Height / 2)
            };
            _ = owner.Handle;
            string text = string.Join(Environment.NewLine, Enumerable.Range(1, 200)
                .Select(line => $"Line {line}: keep the decision buttons accessible on the owner's shorter monitor."));
            using frmTaskDialog dialog = ProtectedMessageBox.CreateDialog(text, "Owner monitor decision",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(initialArea.Left + (initialArea.Width - dialog.Width) / 2, initialArea.Top + 8);
            dialog.BuildForm();

            if (owner.DeviceDpi != dialog.DeviceDpi)
                Assert.Ignore("Requires equal window DPI so a DPI-change rebuild cannot mask the regression.");
            if (Screen.FromHandle(owner.Handle).DeviceName != screens[0].DeviceName ||
                Screen.FromHandle(dialog.Handle).DeviceName != screens[^1].DeviceName)
                Assert.Ignore("Could not place both initial HWNDs on the required monitors.");
            if (dialog.Height <= ownerArea.Height)
                Assert.Ignore("The available monitor geometry does not produce an initially oversized dialog.");

            dialog.StartPosition = startPosition;
            bool shown = false;
            Rectangle shownBounds = Rectangle.Empty, shownClient = Rectangle.Empty, statusBounds = Rectangle.Empty;
            Rectangle[] buttonBounds = Array.Empty<Rectangle>();
            Exception snapshotError = null;
            dialog.Shown += (_, _) =>
            {
                try
                {
                    shown = true;
                    shownBounds = dialog.Bounds;
                    shownClient = dialog.ClientRectangle;
                    buttonBounds = new[] { "bt1", "bt2", "bt3" }.Select(name =>
                        BoundsWithinDialog(dialog, dialog.Controls.Find(name, true).Single())).ToArray();
                    statusBounds = BoundsWithinDialog(dialog,
                        dialog.Controls.Find("CaptureProtectionStatus", true).Single());
                }
                catch (Exception error)
                {
                    snapshotError = error;
                }
                finally
                {
                    dialog.DialogResult = DialogResult.Cancel;
                }
            };

            DialogResult result = ProtectedDialog.Show(dialog, owner, dialog.BuildForm, dialog.SetCaptureProtectionStatus);

            Assert.That(snapshotError, Is.Null, snapshotError?.ToString());
            Assert.That(shown, Is.True);
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(ownerArea.Contains(shownBounds), Is.True, "Refit and recenter on the resolved owner monitor.");
            Assert.That(buttonBounds, Has.Length.EqualTo(3));
            Assert.That(buttonBounds.All(bounds => bounds.Width > 0 && bounds.Height > 0 && shownClient.Contains(bounds)), Is.True);
            Assert.That(statusBounds.Height, Is.GreaterThan(0));
            Assert.That(shownClient.Contains(statusBounds), Is.True);
        }

        [Test]
        [NonParallelizable]
        public void ModalPresentationKeepsTitlesBlankWithoutChangingTheDecisionOrFailureStatus()
        {
            const string decision = "Keep the connection to server.example and the exact error & details?";
            using frmTaskDialog dialog = ProtectedMessageBox.CreateDialog(decision, "Application name",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            string preparedCaption = null, shownCaption = null, shownDecision = null, shownStatus = null;
            Exception snapshotError = null;
            dialog.Load += (_, _) => dialog.Text = "Localized application name";
            dialog.Shown += (_, _) =>
            {
                try
                {
                    dialog.Text = "Late application name";
                    dialog.SetCaptureProtectionStatus(CaptureProtectionStatus.Failed(5, "Test failure"));
                    shownCaption = dialog.Text;
                    shownDecision = dialog.MainInstruction;
                    shownStatus = dialog.Controls.Find("CaptureProtectionStatus", true).Single().Text;
                }
                catch (Exception error)
                {
                    snapshotError = error;
                }
                finally
                {
                    dialog.DialogResult = DialogResult.Cancel;
                }
            };

            DialogResult result = ProtectedDialog.Show(dialog, prepare: () =>
            {
                preparedCaption = dialog.Text;
                dialog.Text = "Prepared application name";
                dialog.BuildForm();
            }, statusChanged: dialog.SetCaptureProtectionStatus);

            Assert.That(snapshotError, Is.Null, snapshotError?.ToString());
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(preparedCaption, Is.Empty, "Clear the caption before preparing the HWND.");
            Assert.That(shownCaption, Is.Empty, "Localization and late title changes must not restore branding.");
            Assert.That(dialog.Text, Is.Empty, "Closing must not restore the old title.");
            Assert.That(shownDecision, Is.EqualTo(decision));
            Assert.That(shownStatus, Is.EqualTo(CaptureProtectionStatus.Failed(5, "Test failure").DisplayText));
        }

        [Test]
        [NonParallelizable]
        public void BlankPasswordCaptionKeepsTheRequestedFileNameVisibleInTheBody()
        {
            const string subject = "production & backup.xml";
            using var dialog = new FrmPassword(subject, newPasswordMode: false);
            string shownSubject = null;
            bool labelVisible = false, useMnemonic = true;
            Exception snapshotError = null;
            dialog.Shown += (_, _) =>
            {
                try
                {
                    var label = (Label)dialog.Controls.Find("lblPassword", true).Single();
                    shownSubject = label.Text;
                    labelVisible = label.Visible;
                    useMnemonic = label.UseMnemonic;
                }
                catch (Exception error)
                {
                    snapshotError = error;
                }
                finally
                {
                    dialog.DialogResult = DialogResult.Cancel;
                }
            };

            DialogResult result = ProtectedDialog.Show(dialog);

            Assert.That(snapshotError, Is.Null, snapshotError?.ToString());
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(dialog.Text, Is.Empty);
            Assert.That(labelVisible, Is.True);
            Assert.That(shownSubject, Does.Contain(subject));
            Assert.That(useMnemonic, Is.False, "Ampersands in file and credential names must be literal.");
        }

        [TestCase(MessageBoxIcon.None, ESysIcons.None)]
        [TestCase(MessageBoxIcon.Information, ESysIcons.Information)]
        [TestCase(MessageBoxIcon.Question, ESysIcons.Question)]
        [TestCase(MessageBoxIcon.Warning, ESysIcons.Warning)]
        [TestCase(MessageBoxIcon.Error, ESysIcons.Error)]
        public void CreatingAMessagePreservesItsTextCaptionAndIcon(MessageBoxIcon icon, ESysIcons expectedIcon)
        {
            const string text = "First line\nSecond line with & literal text";
            using frmTaskDialog dialog = ProtectedMessageBox.CreateDialog(text, "A & B",
                MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1);

            Assert.That(dialog.MainInstruction, Is.EqualTo(text));
            Assert.That(dialog.Title, Is.EqualTo("A & B"));
            Assert.That(dialog.MainIcon, Is.EqualTo(expectedIcon));
            var instruction = (Label)dialog.Controls.Find("lbMainInstruction", true)[0];
            Assert.That(instruction.UseMnemonic, Is.False, "Message text must display literal ampersands.");
        }

        [TestCase(MessageBoxButtons.OK, MessageBoxDefaultButton.Button2)]
        [TestCase(MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button3)]
        [TestCase(MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button3)]
        public void ADefaultThatHasNoButtonIsRejected(MessageBoxButtons buttons,
            MessageBoxDefaultButton defaultButton)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ProtectedMessageBox.CreateDialog("Text", "Caption", buttons, MessageBoxIcon.None, defaultButton));
        }

        private static frmTaskDialog Create(MessageBoxButtons buttons,
            MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1) =>
            ProtectedMessageBox.CreateDialog("Choose an action", "Decision", buttons,
                MessageBoxIcon.Question, defaultButton);

        private static Rectangle BoundsWithinDialog(Form dialog, Control control) =>
            dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));

        private sealed class ClosingProbeDialog : frmTaskDialog
        {
            internal bool PressEscape() => ProcessDialogKey(Keys.Escape);

            internal bool RequestUserClose(DialogResult result)
            {
                DialogResult = result;
                var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
                OnFormClosing(args);
                return args.Cancel;
            }
        }
    }
}
