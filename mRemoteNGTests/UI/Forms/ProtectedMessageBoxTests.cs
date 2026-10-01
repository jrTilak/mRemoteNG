using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.TaskDialog;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms
{
    // Build real WinForms controls without running a modal UI loop. These tests
    // verify result semantics; screenshot exclusion still needs Windows capture tests.
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
        public void LongMessageRemainsScrollableWithDecisionsAndStatusInsideTheWorkingArea()
        {
            string text = string.Join(Environment.NewLine, Enumerable.Range(1, 160)
                .Select(line => $"Line {line}: retain this complete decision text, including & characters."));
            using frmTaskDialog dialog = ProtectedMessageBox.CreateDialog(text, "Long decision",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            dialog.BuildForm();

            Assert.That(dialog.Bounds.Height, Is.LessThanOrEqualTo(Screen.FromHandle(dialog.Handle).WorkingArea.Height));
            var overflow = (TextBox)dialog.Controls.Find("MessageTextOverflow", true).Single();
            Assert.That(overflow.Text, Is.EqualTo(text));
            Assert.That(overflow.ReadOnly, Is.True);
            Assert.That(overflow.Multiline, Is.True);
            Assert.That(overflow.ScrollBars, Is.EqualTo(ScrollBars.Vertical));
            Assert.That(overflow.Height, Is.GreaterThan(0));
            Rectangle overflowBounds = BoundsWithinDialog(dialog, overflow);
            Assert.That(dialog.ClientRectangle.Contains(overflowBounds), Is.True);

            // Each choice remains outside the scrolling message area. Do not
            // inspect Visible: this test intentionally never shows the parent.
            foreach (string name in new[] { "bt1", "bt2", "bt3" })
            {
                Control button = dialog.Controls.Find(name, true).Single();
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
