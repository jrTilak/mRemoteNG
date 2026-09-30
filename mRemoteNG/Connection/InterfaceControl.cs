using mRemoteNG.App;
using mRemoteNG.Connection.Protocol;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.Tabs;
using WeifenLuo.WinFormsUI.Docking;
using System.Runtime.Versioning;

namespace mRemoteNG.Connection
{
    [SupportedOSPlatform("windows")]
    public sealed partial class InterfaceControl
    {
        private readonly List<Control> _captureHostAncestors = new();
        public ProtocolBase Protocol { get; set; }
        public ConnectionInfo Info { get; set; }
        // in case the connection is through a SSH tunnel the Info is a copy of original info with hostname and port number overwritten with localhost and local tunnel port
        // and the original Info is saved in the following variable
        public ConnectionInfo OriginalInfo { get; set; }
        // in case the connection is through a SSH tunnel the Info of the SSHTunnelConnection is also saved for reference in log messages etc.
        public ConnectionInfo SSHTunnelInfo { get; set; }


        public InterfaceControl(Control parent, ProtocolBase protocol, ConnectionInfo info)
        {
            try
            {
                Protocol = protocol;
                Info = info;
                Parent = parent;
                Location = new Point(0, 0);
                Size = Parent.Size;
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                InitializeComponent();
                Disposed += (_, _) => DetachCaptureHostAncestors();
                
                // Enable custom painting for border
                this.Paint += InterfaceControl_Paint;
                
                // Set padding to prevent content from covering the frame border
                UpdatePaddingForFrameColor();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                    "Couldn\'t create new InterfaceControl" + Environment.NewLine +
                                                    ex.Message);
            }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            CaptureHostParentChanged(this, e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterCaptureHost();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) RegisterCaptureHost();
        }

        private void CaptureHostParentChanged(object sender, EventArgs e)
        {
            DetachCaptureHostAncestors();
            if (IsDisposed || Disposing || Protocol is not RdpProtocol) return;

            // Docking can reparent any ancestor without reparenting this panel.
            // Track the chain so a moved tab/panel registers its actual outer HWND.
            for (Control ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                _captureHostAncestors.Add(ancestor);
                ancestor.ParentChanged += CaptureHostParentChanged;
            }
            RegisterCaptureHost();
        }

        private void DetachCaptureHostAncestors()
        {
            foreach (var ancestor in _captureHostAncestors)
                ancestor.ParentChanged -= CaptureHostParentChanged;
            _captureHostAncestors.Clear();
        }

        private void RegisterCaptureHost()
        {
            if (IsDisposed || Disposing || Protocol is not RdpProtocol) return;
            // FindForm() may be a child DockContent; affinity must target the
            // process-owned top-level form, never the ActiveX or a docking child.
            if (TopLevelControl is Form { TopLevel: true } host)
            {
                var main = FrmMain.Default;
                if (!main.IsClosing && !main.IsDisposed && !main.Disposing)
                    main.CaptureProtection?.Register(host, null);
            }
        }

        private void InterfaceControl_Paint(object sender, PaintEventArgs e)
        {
            // Draw colored border based on ConnectionFrameColor property
            if (Info?.ConnectionFrameColor != null && Info.ConnectionFrameColor != ConnectionFrameColor.None)
            {
                Color frameColor = GetFrameColor(Info.ConnectionFrameColor);
                int borderWidth = 4; // 4 pixel border for visibility
                
                using (Pen pen = new Pen(frameColor, borderWidth))
                {
                    // Draw border inside the control bounds
                    Rectangle rect = new Rectangle(
                        borderWidth / 2,
                        borderWidth / 2,
                        this.Width - borderWidth,
                        this.Height - borderWidth
                    );
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }

        private void UpdatePaddingForFrameColor()
        {
            // Add padding to prevent content from covering the frame border
            if (Info?.ConnectionFrameColor != null && Info.ConnectionFrameColor != ConnectionFrameColor.None)
            {
                int borderWidth = 4; // Must match the border width in InterfaceControl_Paint
                // Add 2px margin so the border is fully visible and not covered by child controls
                int padding = borderWidth / 2 + 2;
                this.Padding = new Padding(padding);
            }
            else
            {
                this.Padding = new Padding(0);
            }
        }

        private Color GetFrameColor(ConnectionFrameColor frameColor)
        {
            return frameColor switch
            {
                ConnectionFrameColor.Red => Color.FromArgb(220, 53, 69),      // Bootstrap danger red
                ConnectionFrameColor.Yellow => Color.FromArgb(255, 193, 7),   // Warning yellow
                ConnectionFrameColor.Green => Color.FromArgb(40, 167, 69),    // Success green
                ConnectionFrameColor.Blue => Color.FromArgb(0, 123, 255),     // Primary blue
                ConnectionFrameColor.Purple => Color.FromArgb(111, 66, 193),  // Purple
                _ => Color.Transparent
            };
        }

        public static InterfaceControl FindInterfaceControl(DockPanel DockPnl)
        {
            // instead of repeating the code, call the routine using ConnectionTab if called by DockPanel
            if (DockPnl.ActiveDocument is ConnectionTab ct)
                return FindInterfaceControl(ct);
            return null;
        }

        public static InterfaceControl FindInterfaceControl(ConnectionTab tab)
        {
            if (tab.Controls.Count < 1) return null;
            // if the tab has more than one controls and the second is an InterfaceControl than it must be a connection through SSH tunnel
            // and the first Control is the SSH tunnel connection and thus the second control must be returned.
            if (tab.Controls.Count > 1)
            {
                if (tab.Controls[1] is InterfaceControl ic1)
                    return ic1;
            }
            if (tab.Controls[0] is InterfaceControl ic0)
                return ic0;

            return null;
        }
    }
}
