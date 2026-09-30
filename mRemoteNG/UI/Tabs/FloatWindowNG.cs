using System;
using System.Drawing;
using System.Windows.Forms;
using mRemoteNG.UI.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace mRemoteNG.UI.Tabs
{
    class FloatWindowNG : FloatWindow
    {
        private ProtectedWindowChrome _chrome;

        public FloatWindowNG(DockPanel dockPanel, DockPane pane)
            : base(dockPanel, pane)
        {
            ConfigureProtectedWindow();
        }

        public FloatWindowNG(DockPanel dockPanel, DockPane pane, Rectangle bounds)
            : base(dockPanel, pane, bounds)
        {
            ConfigureProtectedWindow();
        }

        private void ConfigureProtectedWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Owner = FrmMain.Default;

            // DockPanelSuite's float-title drag temporarily adds WS_EX_LAYERED to
            // this HWND. Use the opaque custom title drag; tabs can still be docked.
            AllowEndUserDocking = false;
            DoubleClickTitleBarToDock = false;
            _chrome = new ProtectedWindowChrome(this, FrmMain.Default.CaptureProtection,
                () => FrmMain.Default.Close());
        }

        protected override CreateParams CreateParams =>
            ProtectedWindowChrome.AdjustCreateParams(base.CreateParams);

        // DockPanelSuite uses DisplayingRectangle instead of the form's normal
        // layout area, so explicitly reserve the custom title bar and resize border.
        public override Rectangle DisplayingRectangle => new(
            Padding.Left, Padding.Top,
            Math.Max(0, ClientSize.Width - Padding.Horizontal),
            Math.Max(0, ClientSize.Height - Padding.Vertical));

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // The base constructor can create a handle before _chrome exists.
            var main = FrmMain.Default;
            if (!main.IsClosing && !main.IsDisposed && !main.Disposing)
                main.CaptureProtection?.Register(this, null);
        }

        protected override void WndProc(ref Message m)
        {
            if (ProtectedWindowChrome.ProcessNonClientMessage(ref m)) return;
            const int WM_CLOSE = 0x0010;
            if (m.Msg == WM_CLOSE && !Disposing && NestedPanes.Count > 0 &&
                DockPanel is { IsDisposed: false, Disposing: false } && !FrmMain.Default.IsClosing)
            {
                // System close, Alt+F4 and the custom X use the same application exit.
                // Empty/disposing docking hosts still follow DockPanelSuite cleanup.
                FrmMain.Default.Close();
                return;
            }

            if (_chrome?.ProcessWindowMessage(ref m) == true) return;
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _chrome?.Dispose();
            base.Dispose(disposing);
        }
    }

    public class CustomFloatWindowFactory : DockPanelExtender.IFloatWindowFactory
    {
        public FloatWindow CreateFloatWindow(DockPanel dockPanel, DockPane pane, Rectangle bounds) =>
            new FloatWindowNG(dockPanel, pane, bounds);

        public FloatWindow CreateFloatWindow(DockPanel dockPanel, DockPane pane) =>
            new FloatWindowNG(dockPanel, pane);
    }
}
