using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.UI.CaptureProtection;
using mRemoteNG.UI.Forms;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms
{
    [TestFixture]
    [Platform("Win")]
    [Apartment(ApartmentState.STA)]
    public class ProtectedWindowChromeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void FirstHandleHasNativeSizingWithoutAnInsetClientArea(bool createHandleBeforeChrome)
        {
            using var form = new ChromeForm(createHandleBeforeChrome);
            IntPtr handle = form.Handle;

            Assert.That(GetWindowLong(handle, -16) & 0x00040000, Is.Not.Zero, "Native sizing must stay enabled.");
            Assert.That(GetWindowLong(handle, -16) & 0x00C00000, Is.Zero, "The system caption must stay hidden.");
            Assert.That(GetWindowLong(handle, -20) & 0x80, Is.Not.Zero);
            Assert.That(GetWindowLong(handle, -20) & (0x40000 | 0x80000), Is.Zero);
            Assert.That(GetWindowRect(handle, out NativeRectangle window), Is.True);
            Assert.That(GetClientRect(handle, out NativeRectangle client), Is.True);
            Assert.That(client.Right - client.Left, Is.EqualTo(window.Right - window.Left));
            Assert.That(client.Bottom - client.Top, Is.EqualTo(window.Bottom - window.Top));
        }

        [Test]
        public void MaximizeFitsTheWorkingAreaAndKeepsTheLocalTitleBar()
        {
            using var form = new ChromeForm(false);
            form.Show();
            Rectangle workingArea = Screen.FromHandle(form.Handle).WorkingArea;
            form.WindowState = FormWindowState.Maximized;
            Application.DoEvents();

            Assert.That(form.Bounds, Is.EqualTo(workingArea));
            Control caption = form.Controls["ProtectedTitleBar"];
            Assert.That(caption.Visible, Is.True);
            Assert.That(form.ClientRectangle.Contains(caption.Bounds), Is.True);
            Assert.That(form.Padding.Top, Is.GreaterThanOrEqualTo(caption.Bottom));
        }

        [Test]
        public void VerifiedProtectionHidesSuccessTextAndFailuresRemainVisibleUntilRecovery()
        {
            var api = new FakeAffinityApi();
            using var form = new ChromeForm(false, api);
            form.Show();
            Control status = form.Controls["ProtectedTitleBar"].Controls["CaptureProtectionStatus"];

            Assert.That(status.Text, Is.Empty);
            Assert.That(status.Visible, Is.False);

            api.ErrorCode = 5;
            form.CheckProtection();
            Assert.That(status.Text, Is.EqualTo("Capture protection: failed — error 5"));
            Assert.That(status.Visible, Is.True);

            api.ErrorCode = 0;
            form.CheckProtection();
            Assert.That(status.Text, Is.Empty);
            Assert.That(status.Visible, Is.False);
        }

        private sealed class ChromeForm : Form
        {
            private readonly CaptureProtectionManager _manager;
            private ProtectedWindowChrome _chrome;

            internal ChromeForm(bool createHandleBeforeChrome, FakeAffinityApi api = null)
            {
                _manager = new CaptureProtectionManager(api ?? new FakeAffinityApi());
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(Screen.PrimaryScreen.WorkingArea.Location, new Size(800, 500));
                if (createHandleBeforeChrome) _ = Handle;
                _chrome = new ProtectedWindowChrome(this, _manager);
            }

            internal void CheckProtection() => _manager.CheckRegisteredForms();

            protected override CreateParams CreateParams => ProtectedWindowChrome.AdjustCreateParams(base.CreateParams);

            protected override void WndProc(ref Message message)
            {
                if (ProtectedWindowChrome.ProcessNonClientMessage(ref message) ||
                    _chrome?.ProcessWindowMessage(ref message) == true) return;
                base.WndProc(ref message);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _chrome?.Dispose();
                    _manager.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private sealed class FakeAffinityApi : IDisplayAffinityApi
        {
            internal int ErrorCode { get; set; }
            public bool SupportsCaptureExclusion => true;
            public bool TryValidateWindow(IntPtr window, out int errorCode, out string detail)
            {
                errorCode = 0;
                detail = null;
                return true;
            }
            public bool TryGetAffinity(IntPtr window, out uint affinity, out int errorCode)
            {
                affinity = CaptureProtectionPolicy.WDA_EXCLUDEFROMCAPTURE;
                errorCode = ErrorCode;
                return errorCode == 0;
            }
            public bool TrySetAffinity(IntPtr window, uint affinity, out int errorCode)
            {
                errorCode = ErrorCode;
                return errorCode == 0;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern uint GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out NativeRectangle rectangle);
    }
}
