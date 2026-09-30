using System;
using System.Collections.Generic;
using mRemoteNG.UI.CaptureProtection;
using NUnit.Framework;

namespace mRemoteNGTests.UI.CaptureProtection
{
    [TestFixture]
    public class CaptureProtectionPolicyTests
    {
        private static readonly IntPtr Window = new(123);
        private FakeAffinityApi _api;
        private CaptureProtectionPolicy _policy;

        [SetUp]
        public void SetUp()
        {
            _api = new FakeAffinityApi();
            _policy = new CaptureProtectionPolicy(_api);
        }

        [Test]
        public void IntegrityCheckDoesNotSetAlreadyCorrectAffinity()
        {
            _api.Affinity = 0x11;
            for (int tick = 0; tick < 100; tick++)
                Assert.That(_policy.EnsureProtected(Window, false).Enabled, Is.True);

            Assert.That(_api.GetCalls, Is.EqualTo(100));
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [TestCase(0u)]
        [TestCase(1u)]
        public void MissingOrWeakenedAffinityIsRestoredAndVerified(uint previousAffinity)
        {
            _api.Affinity = previousAffinity;
            CaptureProtectionStatus result = _policy.EnsureProtected(Window, false);

            Assert.That(result.Enabled, Is.True);
            Assert.That(_api.SetCalls, Is.EqualTo(1));
            Assert.That(_api.GetCalls, Is.EqualTo(2));
            Assert.That(_api.Affinity, Is.EqualTo(0x11));
        }

        [Test]
        public void EventDrivenReapplicationAlwaysSetsAndVerifies()
        {
            _api.Affinity = 0x11;
            Assert.That(_policy.EnsureProtected(Window, true).Enabled, Is.True);
            Assert.That(_api.SetCalls, Is.EqualTo(1));
            Assert.That(_api.GetCalls, Is.EqualTo(1));
        }

        [Test]
        public void FailedInitialReadAttemptsRecovery()
        {
            _api.ReadErrors.Enqueue(5);
            Assert.That(_policy.EnsureProtected(Window, false).Enabled, Is.True);
            Assert.That(_api.SetCalls, Is.EqualTo(1));
            Assert.That(_api.GetCalls, Is.EqualTo(2));
        }

        [Test]
        public void FailedSetPreservesActualWin32Error()
        {
            _api.SetError = 8;
            CaptureProtectionStatus result = _policy.EnsureProtected(Window, false);

            Assert.That(result.Enabled, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(8));
            Assert.That(result.DisplayText, Is.EqualTo("Capture protection: failed — error 8"));
        }

        [Test]
        public void SuccessfulSetWithFailedReadbackNeverReportsEnabled()
        {
            _api.ReadErrors.Enqueue(87);
            CaptureProtectionStatus result = _policy.EnsureProtected(Window, true);

            Assert.That(result.Enabled, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(87));
        }

        [Test]
        public void SuccessfulSetWithUnexpectedReadbackNeverReportsEnabled()
        {
            _api.IgnoreSet = true;
            _api.Affinity = 1;
            CaptureProtectionStatus result = _policy.EnsureProtected(Window, true);

            Assert.That(result.Enabled, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(13));
            Assert.That(result.Detail, Does.Contain("0x1, expected 0x11"));
        }

        [Test]
        public void OlderWindowsCannotClaimExclusionEvenIfReadbackWouldReturnEleven()
        {
            _api.SupportsCaptureExclusion = false;
            _api.Affinity = 0x11;
            CaptureProtectionStatus result = _policy.EnsureProtected(Window, false);

            Assert.That(result.Enabled, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(50));
            Assert.That(_api.GetCalls, Is.Zero);
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [TestCase(5)] // Foreign process.
        [TestCase(87)] // Child or layered HWND.
        [TestCase(1400)] // Destroyed HWND.
        public void IneligibleWindowNeverReachesAffinityApi(int errorCode)
        {
            _api.ValidationError = errorCode;
            CaptureProtectionStatus result = _policy.EnsureProtected(Window, true);

            Assert.That(result.ErrorCode, Is.EqualTo(errorCode));
            Assert.That(_api.GetCalls, Is.Zero);
            Assert.That(_api.SetCalls, Is.Zero);
        }

        [Test]
        public void FailureCanRecoverOnNextTick()
        {
            _api.SetError = 5;
            Assert.That(_policy.EnsureProtected(Window, false).Enabled, Is.False);
            _api.SetError = 0;
            Assert.That(_policy.EnsureProtected(Window, false).Enabled, Is.True);
        }

        [Test]
        public void PendingStatusDoesNotClaimEnabled()
        {
            Assert.That(CaptureProtectionStatus.Pending.Enabled, Is.False);
            Assert.That(CaptureProtectionStatus.Pending.DisplayText, Is.EqualTo("Capture protection: pending"));
        }

        [Test]
        public void RepeatedIdenticalFailureIsNotLoggedAgain()
        {
            var gate = new CaptureProtectionFailureLogGate();
            var status = CaptureProtectionStatus.Failed(5, "Access denied");
            var now = DateTimeOffset.UtcNow;
            Assert.That(gate.ShouldLog(status, now), Is.True);
            Assert.That(gate.ShouldLog(status, now.AddMinutes(10)), Is.False);
        }

        [Test]
        public void RapidlyChangingFailuresAreRateLimitedWithoutHidingTheirState()
        {
            var gate = new CaptureProtectionFailureLogGate();
            var now = DateTimeOffset.UtcNow;
            Assert.That(gate.ShouldLog(CaptureProtectionStatus.Failed(5, "Denied"), now), Is.True);
            var changed = CaptureProtectionStatus.Failed(87, "Invalid");
            Assert.That(gate.ShouldLog(changed, now.AddSeconds(1)), Is.False);
            Assert.That(gate.ShouldLog(changed, now.AddSeconds(30)), Is.True);
            Assert.That(changed.DisplayText, Does.EndWith("error 87"));
        }

        [Test]
        public void RecoveryDoesNotPermitRapidFailureLogFlooding()
        {
            var gate = new CaptureProtectionFailureLogGate();
            var now = DateTimeOffset.UtcNow;
            var failure = CaptureProtectionStatus.Failed(5, "Denied");
            Assert.That(gate.ShouldLog(failure, now), Is.True);
            Assert.That(gate.ShouldLog(CaptureProtectionStatus.Verified, now.AddSeconds(1)), Is.False);
            Assert.That(gate.ShouldLog(failure, now.AddSeconds(2)), Is.False);
            Assert.That(gate.ShouldLog(failure, now.AddSeconds(30)), Is.True);
        }

        private sealed class FakeAffinityApi : IDisplayAffinityApi
        {
            public bool SupportsCaptureExclusion { get; set; } = true;
            internal uint Affinity { get; set; }
            internal int ValidationError { get; set; }
            internal int SetError { get; set; }
            internal bool IgnoreSet { get; set; }
            internal int GetCalls { get; private set; }
            internal int SetCalls { get; private set; }
            internal Queue<int> ReadErrors { get; } = new();

            public bool TryValidateWindow(IntPtr window, out int errorCode, out string detail)
            {
                Assert.That(window, Is.EqualTo(Window));
                errorCode = ValidationError;
                detail = "Window precondition failed.";
                return errorCode == 0;
            }

            public bool TryGetAffinity(IntPtr window, out uint affinity, out int errorCode)
            {
                GetCalls++;
                affinity = Affinity;
                errorCode = ReadErrors.Count > 0 ? ReadErrors.Dequeue() : 0;
                return errorCode == 0;
            }

            public bool TrySetAffinity(IntPtr window, uint affinity, out int errorCode)
            {
                Assert.That(affinity, Is.EqualTo(0x11));
                SetCalls++;
                errorCode = SetError;
                if (errorCode == 0 && !IgnoreSet)
                    Affinity = affinity;
                return errorCode == 0;
            }
        }
    }
}
