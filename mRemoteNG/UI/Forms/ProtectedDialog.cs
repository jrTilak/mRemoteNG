using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.UI.CaptureProtection;

namespace mRemoteNG.UI.Forms
{
    /// <summary>Protects a caller-owned WinForms dialog on its current UI thread before presentation.</summary>
    internal static class ProtectedDialog
    {
        internal static DialogResult Show(Form form, IWin32Window owner = null, Action prepare = null,
            Action<CaptureProtectionStatus> statusChanged = null)
        {
            ArgumentNullException.ThrowIfNull(form);
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Protected dialogs require the current STA UI thread.");
            if (form.IsHandleCreated && form.InvokeRequired)
                throw new InvalidOperationException("Protected dialogs must be shown on their owning UI thread.");

            form.Opacity = 1;
            form.TransparencyKey = Color.Empty;
            form.AllowTransparency = false;
            form.ShowIcon = false;
            string failure = null;
            bool updatingCaption = false;

            void UpdateCaption()
            {
                if (form.IsDisposed || form.Disposing || updatingCaption) return;
                updatingCaption = true;
                try
                {
                    // Keep application-owned captions blank, including titles
                    // assigned by localization while the dialog is loading.
                    // Forms without a status control still expose actual failures.
                    form.Text = failure ?? string.Empty;
                }
                finally
                {
                    updatingCaption = false;
                }
            }

            void CaptionChanged(object sender, EventArgs e)
            {
                if (updatingCaption) return;
                UpdateCaption();
            }

            void ProtectionChanged(CaptureProtectionStatus status)
            {
                if (statusChanged != null)
                {
                    statusChanged(status);
                    return;
                }
                failure = status.ErrorCode.HasValue ? status.DisplayText : null;
                UpdateCaption();
            }

            using var protection = new CaptureProtectionManager(message => Logger.Instance.Log?.Warn(message));
            form.TextChanged += CaptionChanged;
            try
            {
                UpdateCaption();
                protection.Register(form, ProtectionChanged);
                prepare?.Invoke();
                owner ??= form.Owner;
                if (owner == null)
                {
                    // ActiveForm can refer to a foreground form on another UI
                    // thread. Never read that form's HWND through ShowDialog.
                    Form active = Form.ActiveForm;
                    if (active != form && active is { IsDisposed: false, Disposing: false, IsHandleCreated: true } &&
                        !active.InvokeRequired)
                        owner = active;
                }
                return owner == null ? form.ShowDialog() : form.ShowDialog(owner);
            }
            finally
            {
                form.TextChanged -= CaptionChanged;
                failure = null;
                UpdateCaption();
            }
        }
    }
}
