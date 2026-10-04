using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using mRemoteNG.Themes;
using mRemoteNG.UI.CaptureProtection;
using mRemoteNG.App.Branding;

namespace mRemoteNG.UI.Forms
{
    /// <summary>Opaque local controls outside the area occupied by the RDP ActiveX surface.</summary>
    internal sealed class ProtectedWindowChrome : IDisposable
    {
        private readonly Form _form;
        private readonly CaptureProtectionManager _manager;
        private readonly AlwaysOnTopManager _alwaysOnTop;
        private readonly Panel _caption;
        private readonly Label _title;
        private Icon _windowIcon;
        private readonly Label _status;
        private string _protectionStatus = "Capture protection: pending";
        private readonly Button _maximize;
        private readonly Button _close;
        private readonly ThemeManager _theme = ThemeManager.getInstance();
        private FormWindowState _lastVisibleState = FormWindowState.Normal;
        private bool _disposed;
        private bool _layingOut;
        private Point? _dragStart;

        public ProtectedWindowChrome(Form form, CaptureProtectionManager manager, Action close = null,
            AlwaysOnTopManager alwaysOnTop = null)
        {
            _form = form;
            _manager = manager;
            _alwaysOnTop = alwaysOnTop;
            form.FormBorderStyle = FormBorderStyle.None;
            form.ShowInTaskbar = false;
            form.ShowIcon = false;
            form.MinimizeBox = false;
            form.Opacity = 1;
            form.TransparencyKey = Color.Empty;
            form.AllowTransparency = false;

            _caption = new Panel { Name = "ProtectedTitleBar", TabStop = false };
            _title = new Label { TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
            _status = new Label
            {
                Name = "CaptureProtectionStatus", Text = _protectionStatus, TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true, UseMnemonic = false, AccessibleName = "Capture protection status"
            };
            _maximize = CreateButton("□", "Maximize or restore window");
            _close = CreateButton("X", "Close application");
            _maximize.Click += (_, _) => ToggleMaximize();
            _close.Click += (_, _) => (close ?? form.Close)();
            _caption.Controls.AddRange(new Control[] { _title, _status, _maximize, _close });
            form.Controls.Add(_caption);
            foreach (Control control in new Control[] { _caption, _title, _status })
            {
                control.MouseDown += CaptionMouseDown;
                control.MouseMove += CaptionMouseMove;
                control.MouseUp += CaptionMouseUp;
                control.MouseDoubleClick += CaptionDoubleClick;
            }

            form.TextChanged += UpdateTitle;
            if (_alwaysOnTop != null)
            {
                _alwaysOnTop.Register(form);
                _alwaysOnTop.FailureChanged += HotKeyFailureChanged;
            }
            manager.Register(form, status =>
            {
                // Keep checking the native flag, but reserve the frame text for
                // pending protection and errors that need the user's attention.
                _protectionStatus = status.Enabled ? string.Empty : status.DisplayText;
                RefreshStatus();
            });
            form.Resize += Resize;
            form.Layout += Layout;
            form.DpiChanged += DpiChanged;
            form.Disposed += FormDisposed;
            _theme.ThemeChanged += ApplyTheme;
            ApplicationBranding.Initialized += RefreshBranding;
            RefreshBranding();
            ApplyTheme();
            UpdateMetrics();
        }

        public static CreateParams AdjustCreateParams(CreateParams parameters)
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_APPWINDOW = 0x40000;
            const int WS_EX_LAYERED = 0x80000;
            parameters.Caption = string.Empty;
            parameters.ExStyle = (parameters.ExStyle | WS_EX_TOOLWINDOW) & ~(WS_EX_APPWINDOW | WS_EX_LAYERED);
            // DockPanelSuite can create a float handle in its base constructor, before
            // the derived constructor sets FormBorderStyle.None.
            // Native edge sizing needs WS_THICKFRAME even with a custom frame.
            // WM_NCCALCSIZE below removes its visible non-client border.
            parameters.Style = (parameters.Style | 0x00040000 /* WS_THICKFRAME */) &
                ~(0x00C00000 /* WS_CAPTION */ | 0x00020000 /* WS_MINIMIZEBOX */);
            return parameters;
        }

        // Call before the instance handler, including during base construction.
        // Otherwise an early-created floating HWND retains the system's border
        // until its next non-client layout, potentially covering the local frame.
        internal static bool ProcessNonClientMessage(ref Message message)
        {
            if (message.Msg != 0x0083 /* WM_NCCALCSIZE */) return false;
            // Leave the supplied window rectangle unchanged as the client area
            // for both RECT and NCCALCSIZE_PARAMS forms of this message.
            message.Result = IntPtr.Zero;
            return true;
        }

        private static Button CreateButton(string text, string accessibleName) => new Button
        {
            Text = text, AccessibleName = accessibleName, FlatStyle = FlatStyle.Flat,
            TabStop = true, UseVisualStyleBackColor = false
        };

        private int Scale(int value) => (int)Math.Round(value * _form.DeviceDpi / 96.0);

        private void UpdateMetrics()
        {
            _form.Padding = new Padding(Scale(6), Scale(42), Scale(6), Scale(6));
            _form.MinimumSize = new Size(Scale(660), Scale(300));
            LayoutCaption();
        }

        private void DpiChanged(object sender, DpiChangedEventArgs e) => UpdateMetrics();
        private void Layout(object sender, LayoutEventArgs e) => LayoutCaption();
        private void UpdateTitle(object sender, EventArgs e)
        {
            // Main and floating hosts always have an empty native and custom title.
            // DockPanelSuite can assign another caption when the active pane changes.
            _title.Text = string.Empty;
            if (_form.Text.Length != 0)
                _form.Text = string.Empty;
        }

        private void RefreshBranding()
        {
            Icon icon = ApplicationBranding.CreateWindowIcon();
            if (icon != null)
            {
                Icon oldIcon = _windowIcon;
                _windowIcon = icon;
                _form.Icon = icon;
                oldIcon?.Dispose();
            }
            UpdateTitle(_form, EventArgs.Empty);
        }

        private void HotKeyFailureChanged(string error) => RefreshStatus();

        private void RefreshStatus()
        {
            string hotKeyError = _alwaysOnTop?.StatusError;
            _status.Text = string.IsNullOrEmpty(hotKeyError)
                ? _protectionStatus
                : string.IsNullOrEmpty(_protectionStatus) ? hotKeyError : $"{_protectionStatus} | {hotKeyError}";
            _status.Visible = _status.Text.Length > 0;
            LayoutCaption();
        }

        private void LayoutCaption()
        {
            if (_disposed || _layingOut) return;
            _layingOut = true;
            try
            {
                int border = Scale(6), height = Scale(36), buttonWidth = Scale(42), gap = Scale(12);
                _caption.SetBounds(border, border, Math.Max(0, _form.ClientSize.Width - border * 2), height);
                _close.SetBounds(_caption.Width - buttonWidth, 0, buttonWidth, height);
                _maximize.SetBounds(_close.Left - buttonWidth, 0, buttonWidth, height);
                int statusWidth = _status.Text.Length == 0 ? 0 :
                    Math.Min(Math.Max(0, _maximize.Left - gap * 2),
                        TextRenderer.MeasureText(_status.Text, _status.Font).Width + gap);
                _status.SetBounds(Math.Max(0, _maximize.Left - gap - statusWidth), 0, statusWidth, height);
                int titleLeft = gap;
                _title.SetBounds(titleLeft, 0, Math.Max(0, _status.Left - titleLeft - gap), height);
                _caption.BringToFront();
            }
            finally
            {
                _layingOut = false;
            }
        }

        private void ApplyTheme()
        {
            Color background = SystemColors.Control, foreground = SystemColors.ControlText;
            if (_theme.ActiveAndExtended)
            {
                background = _theme.ActiveTheme.ExtendedPalette.getColor("Dialog_Background");
                foreground = _theme.ActiveTheme.ExtendedPalette.getColor("Dialog_Foreground");
            }
            // Force solid colors even if a palette supplies an alpha component.
            _caption.BackColor = Color.FromArgb(255, background);
            _caption.ForeColor = Color.FromArgb(255, foreground);
            _form.BackColor = _caption.BackColor;
            foreach (Button button in new[] { _maximize, _close })
            {
                button.BackColor = _caption.BackColor;
                button.ForeColor = _caption.ForeColor;
                button.FlatAppearance.BorderSize = 0;
            }
            _close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
        }

        private void Resize(object sender, EventArgs e)
        {
            // A window without a taskbar or tray entry must remain reachable.
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = _lastVisibleState;
            else
                _lastVisibleState = _form.WindowState;
            _maximize.Text = _form.WindowState == FormWindowState.Maximized ? "❐" : "□";
            LayoutCaption();
        }

        private void ToggleMaximize() => _form.WindowState =
            _form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

        private void CaptionDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ToggleMaximize();
        }

        private void CaptionMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks != 1) return;
            _dragStart = Control.MousePosition;
            ((Control)sender).Capture = true;
        }

        private void CaptionMouseUp(object sender, MouseEventArgs e)
        {
            _dragStart = null;
            ((Control)sender).Capture = false;
        }

        private void CaptionMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart is not Point start || e.Button != MouseButtons.Left) return;
            Point cursor = Control.MousePosition;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(cursor.X - start.X) < threshold.Width / 2 &&
                Math.Abs(cursor.Y - start.Y) < threshold.Height / 2) return;
            _dragStart = null;
            ReleaseCapture();
            if (_form.WindowState == FormWindowState.Maximized)
            {
                double fraction = Math.Clamp((double)(cursor.X - _form.Left) / _form.Width, 0, 1);
                _form.WindowState = FormWindowState.Normal;
                _form.Location = new Point(cursor.X - (int)(_form.Width * fraction), cursor.Y - Scale(20));
            }
            // DockPanelSuite also hit-tests this point before forwarding to
            // DefWindowProc. Preserve signed screen coordinates on all monitors.
            int coordinates = unchecked((cursor.Y << 16) | (cursor.X & 0xFFFF));
            SendMessage(_form.Handle, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, (IntPtr)coordinates);
        }

        public bool ProcessWindowMessage(ref Message message)
        {
            if (message.Msg == 0x0112 && (message.WParam.ToInt64() & 0xFFF0) == 0xF020 /* SC_MINIMIZE */)
                return true;
            if (message.Msg == 0x0084 /* WM_NCHITTEST */ && _form.WindowState == FormWindowState.Normal)
            {
                long coordinates = message.LParam.ToInt64();
                Point point = _form.PointToClient(new Point((short)(coordinates & 0xFFFF), (short)((coordinates >> 16) & 0xFFFF)));
                int border = Scale(6);
                bool left = point.X < border, right = point.X >= _form.ClientSize.Width - border;
                bool top = point.Y < border, bottom = point.Y >= _form.ClientSize.Height - border;
                int hit = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 0;
                if (hit != 0)
                {
                    message.Result = (IntPtr)hit;
                    return true;
                }
            }
            if (message.Msg == 0x0024 /* WM_GETMINMAXINFO */)
            {
                Screen screen = Screen.FromHandle(message.HWnd);
                MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
                info.MaxPosition = new Point(screen.WorkingArea.Left - screen.Bounds.Left, screen.WorkingArea.Top - screen.Bounds.Top);
                info.MaxSize = screen.WorkingArea.Size;
                info.MinTrackSize = _form.MinimumSize;
                info.MaxTrackSize = new Size(Math.Max(info.MaxTrackSize.Width, info.MinTrackSize.Width),
                    Math.Max(info.MaxTrackSize.Height, info.MinTrackSize.Height));
                Marshal.StructureToPtr(info, message.LParam, false);
                message.Result = IntPtr.Zero;
                return true;
            }
            return false;
        }

        private void FormDisposed(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manager.Unregister(_form);
            _theme.ThemeChanged -= ApplyTheme;
            ApplicationBranding.Initialized -= RefreshBranding;
            if (_alwaysOnTop != null)
                _alwaysOnTop.FailureChanged -= HotKeyFailureChanged;
            _form.TextChanged -= UpdateTitle;
            _form.Resize -= Resize;
            _form.Layout -= Layout;
            _form.DpiChanged -= DpiChanged;
            _form.Disposed -= FormDisposed;
            _windowIcon?.Dispose();
            _caption.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Size MaxSize;
            public Point MaxPosition;
            public Size MinTrackSize;
            public Size MaxTrackSize;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}
