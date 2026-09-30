using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using mRemoteNG.UI.CaptureProtection;
using NUnit.Framework;

namespace mRemoteNGTests.UI.CaptureProtection
{
    // These exercise real WinForms lifecycle events on Windows, but deliberately fake display affinity.
    // They cannot establish that Windows capture APIs honor the policy; that requires the manual matrix.
    [TestFixture]
    [Platform("Win")]
    [Apartment(ApartmentState.STA)]
    public class CaptureProtectionManagerTests
    {
        private TestForm _form;
        private FakeAffinityApi _api;
        private CaptureProtectionManager _manager;
        private List<CaptureProtectionStatus> _statuses;

        [SetUp]
        public void SetUp()
        {
            _form = new TestForm { ShowInTaskbar = false, Size = new Size(300, 200) };
            _api = new FakeAffinityApi();
            _manager = new CaptureProtectionManager(_api);
            _statuses = new List<CaptureProtectionStatus>();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Dispose();
            _form.Dispose();
        }

        [Test]
        public void RegistrationAndIntegrityChecksDoNotCreateHandles()
        {
            _manager.Register(_form, _statuses.Add);
            _manager.CheckRegisteredForms();

            Assert.That(_form.IsHandleCreated, Is.False);
            Assert.That(_api.SetCalls, Is.Zero);
            Assert.That(_api.GetCalls, Is.Zero);
            Assert.That(_statuses, Is.EqualTo(new[] { CaptureProtectionStatus.Pending }));
        }

        [Test]
        public void HandleCreationAndShownReapplyAndVerify()
        {
            _manager.Register(_form, _statuses.Add);
            _form.CreateNativeHandle();
            Assert.That(_api.SetCalls, Is.EqualTo(1));
            Assert.That(_statuses[^1].Enabled, Is.True);

            _form.RaiseShown();
            Assert.That(_api.SetCalls, Is.EqualTo(2));
            Assert.That(_api.GetCalls, Is.EqualTo(2));
        }

        [Test]
        public void TimerSkipsInvisibleFormsAndOnlyReadsCorrectVisibleForms()
        {
            _manager.Register(_form, _statuses.Add);
            _form.CreateNativeHandle();
            _api.ResetCounts();
            _manager.CheckRegisteredForms();
            Assert.That(_api.GetCalls, Is.Zero);
            Assert.That(_api.SetCalls, Is.Zero);

            _form.Show();
            _api.ResetCounts();
            _manager.CheckRegisteredForms();
            Assert.That(_api.GetCalls, Is.EqualTo(1));
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [Test]
        public void BecomingVisibleReappliesProtection()
        {
            _manager.Register(_form, _statuses.Add);
            _form.Show();
            _form.Hide();
            _api.ResetCounts();
            _form.Show();

            Assert.That(_api.SetCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(_statuses[^1].Enabled, Is.True);
        }

        [Test]
        public void RestoringFromMinimizedReappliesProtection()
        {
            _manager.Register(_form, _statuses.Add);
            _form.Show();
            _form.WindowState = FormWindowState.Minimized;
            _api.ResetCounts();
            _form.WindowState = FormWindowState.Normal;

            Assert.That(_api.SetCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(_statuses[^1].Enabled, Is.True);
        }

        [Test]
        public void HandleRecreationClearsStatusAndReapplies()
        {
            _manager.Register(_form, _statuses.Add);
            _form.CreateNativeHandle();
            _statuses.Clear();
            _api.ResetCounts();
            _form.RecreateNativeHandle();

            Assert.That(_statuses[0], Is.EqualTo(CaptureProtectionStatus.Pending));
            Assert.That(_statuses[^1].Enabled, Is.True);
            Assert.That(_api.SetCalls, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void EachAdditionalTopLevelFormIsProtected()
        {
            using var second = new TestForm { ShowInTaskbar = false };
            _manager.Register(_form, _statuses.Add);
            _manager.Register(second, null);
            _form.CreateNativeHandle();
            second.CreateNativeHandle();

            Assert.That(_api.SetCalls, Is.EqualTo(2));
            Assert.That(_api.ProtectedWindows, Does.Contain(_form.Handle));
            Assert.That(_api.ProtectedWindows, Does.Contain(second.Handle));
        }

        [Test]
        public void ReregisteringWithNullCallbackPreservesStatusDisplay()
        {
            _manager.Register(_form, _statuses.Add);
            _manager.Register(_form, null);
            _form.CreateNativeHandle();

            Assert.That(_statuses[^1].Enabled, Is.True);
            Assert.That(_api.SetCalls, Is.EqualTo(1));
        }

        [Test]
        public void UnregisterDetachesEventsAndIntegrityChecks()
        {
            _manager.Register(_form, _statuses.Add);
            _form.Show();
            _manager.Unregister(_form);
            _api.ResetCounts();
            _form.RecreateNativeHandle();
            _manager.CheckRegisteredForms();

            Assert.That(_api.GetCalls, Is.Zero);
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [Test]
        public void DisposedFormsAreNotChecked()
        {
            _manager.Register(_form, _statuses.Add);
            _form.Show();
            _form.Dispose();
            _api.ResetCounts();
            _manager.CheckRegisteredForms();

            Assert.That(_api.GetCalls, Is.Zero);
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [Test]
        public void ManagerDisposalDetachesEventsAndStopsChecks()
        {
            _manager.Register(_form, _statuses.Add);
            _form.Show();
            _manager.Dispose();
            _api.ResetCounts();
            _form.RecreateNativeHandle();
            _manager.CheckRegisteredForms();

            Assert.That(_api.GetCalls, Is.Zero);
            Assert.That(_api.SetCalls, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => _manager.Register(_form, null));
        }

        [Test]
        public void ChildFormsAreRejectedBeforeNativeCalls()
        {
            _form.TopLevel = false;
            Assert.Throws<ArgumentException>(() => _manager.Register(_form, null));
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [Test]
        public void TransparentFormsCannotReportProtectionEnabled()
        {
            _form.Opacity = 0.9;
            _manager.Register(_form, _statuses.Add);
            _form.CreateNativeHandle();

            Assert.That(_statuses[^1].ErrorCode, Is.EqualTo(87));
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [Test]
        public void BackgroundThreadAccessIsRejectedBeforeTouchingForms()
        {
            Exception failure = Task.Run(() =>
            {
                try { _manager.CheckRegisteredForms(); }
                catch (Exception exception) { return exception; }
                return null;
            }).GetAwaiter().GetResult();

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_api.GetCalls, Is.Zero);
        }

        [TestCase(249)]
        [TestCase(5001)]
        public void InvalidTimerIntervalsAreRejected(int interval)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureProtectionManager(_api, intervalMilliseconds: interval));
        }

        private sealed class TestForm : Form
        {
            internal void CreateNativeHandle() => CreateHandle();
            internal void RecreateNativeHandle() => RecreateHandle();
            internal void RaiseShown() => OnShown(EventArgs.Empty);
        }

        private sealed class FakeAffinityApi : IDisplayAffinityApi
        {
            public bool SupportsCaptureExclusion => true;
            internal int GetCalls { get; private set; }
            internal int SetCalls { get; private set; }
            internal HashSet<IntPtr> ProtectedWindows { get; } = new();

            public bool TryValidateWindow(IntPtr window, out int errorCode, out string detail)
            {
                errorCode = 0;
                detail = null;
                return true;
            }

            public bool TryGetAffinity(IntPtr window, out uint affinity, out int errorCode)
            {
                GetCalls++;
                affinity = ProtectedWindows.Contains(window) ? 0x11u : 0u;
                errorCode = 0;
                return true;
            }

            public bool TrySetAffinity(IntPtr window, uint affinity, out int errorCode)
            {
                SetCalls++;
                ProtectedWindows.Add(window);
                errorCode = 0;
                return true;
            }

            internal void ResetCounts()
            {
                GetCalls = 0;
                SetCalls = 0;
            }
        }
    }
}
