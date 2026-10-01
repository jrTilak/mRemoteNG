using System;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.UI.TaskDialog;

namespace mRemoteNG.UI.Forms
{
    /// <summary>Application MessageBox calls use a protected WinForms dialog instead of a native HWND.</summary>
    internal static class ProtectedMessageBox
    {
        internal static DialogResult Show(string text, string caption = "",
            MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
            MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1) =>
            Show(null, text, caption, buttons, icon, defaultButton);

        internal static DialogResult Show(IWin32Window owner, string text, string caption = "",
            MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
            MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            if (owner is Control control && control.IsHandleCreated && control.InvokeRequired)
                return (DialogResult)control.Invoke(new Func<DialogResult>(() => Show(owner, text, caption, buttons, icon, defaultButton)));
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Protected message boxes require an STA UI thread or an existing UI owner.");
            using frmTaskDialog dialog = CreateDialog(text, caption, buttons, icon, defaultButton);
            return ProtectedDialog.Show(dialog, owner, dialog.BuildForm, dialog.SetCaptureProtectionStatus);
        }

        internal static frmTaskDialog CreateDialog(string text, string caption, MessageBoxButtons buttons,
            MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            ETaskDialogButtons dialogButtons = buttons switch
            {
                MessageBoxButtons.OK => ETaskDialogButtons.Ok,
                MessageBoxButtons.OKCancel => ETaskDialogButtons.OkCancel,
                MessageBoxButtons.YesNo => ETaskDialogButtons.YesNo,
                MessageBoxButtons.YesNoCancel => ETaskDialogButtons.YesNoCancel,
                _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons,
                    "This button set is not used by the application's protected message boxes.")
            };
            ESysIcons dialogIcon = icon switch
            {
                MessageBoxIcon.None => ESysIcons.None,
                MessageBoxIcon.Information => ESysIcons.Information,
                MessageBoxIcon.Question => ESysIcons.Question,
                MessageBoxIcon.Warning => ESysIcons.Warning,
                MessageBoxIcon.Error => ESysIcons.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(icon))
            };
            int defaultIndex = defaultButton switch
            {
                MessageBoxDefaultButton.Button1 => 0,
                MessageBoxDefaultButton.Button2 => 1,
                MessageBoxDefaultButton.Button3 => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(defaultButton))
            };
            int buttonCount = buttons == MessageBoxButtons.OK ? 1 : buttons == MessageBoxButtons.YesNoCancel ? 3 : 2;
            if (defaultIndex >= buttonCount)
                throw new ArgumentOutOfRangeException(nameof(defaultButton), "The default must name an existing button.");

            var dialog = new frmTaskDialog
            {
                Title = caption ?? string.Empty,
                MainInstruction = text ?? string.Empty,
                MainIcon = dialogIcon,
                Buttons = dialogButtons,
                Width = CTaskDialog.EmulatedFormWidth
            };
            dialog.ConfigureMessageBox(defaultIndex);
            return dialog;
        }
    }
}
