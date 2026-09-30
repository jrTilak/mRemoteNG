using System;

namespace mRemoteNG.UI.CaptureProtection
{
    // Kept independent of WinForms and user32 so the recovery policy can be tested without a desktop.
    internal interface IDisplayAffinityApi
    {
        bool SupportsCaptureExclusion { get; }
        bool TryValidateWindow(IntPtr window, out int errorCode, out string detail);
        bool TryGetAffinity(IntPtr window, out uint affinity, out int errorCode);
        bool TrySetAffinity(IntPtr window, uint affinity, out int errorCode);
    }

    internal sealed class CaptureProtectionPolicy
    {
        internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        private readonly IDisplayAffinityApi _api;

        internal CaptureProtectionPolicy(IDisplayAffinityApi api) => _api = api;

        internal CaptureProtectionStatus EnsureProtected(IntPtr window, bool reapply)
        {
            // Older Windows accepts 0x11 but treats it as WDA_MONITOR. Never call that exclusion.
            if (!_api.SupportsCaptureExclusion)
                return CaptureProtectionStatus.Failed(50, "Capture exclusion requires Windows 10 version 2004 (build 19041) or newer.");

            if (!_api.TryValidateWindow(window, out int errorCode, out string detail))
                return CaptureProtectionStatus.Failed(errorCode, detail);

            if (!reapply && _api.TryGetAffinity(window, out uint currentAffinity, out _) && currentAffinity == WDA_EXCLUDEFROMCAPTURE)
                return CaptureProtectionStatus.Verified;

            if (!_api.TrySetAffinity(window, WDA_EXCLUDEFROMCAPTURE, out errorCode))
                return CaptureProtectionStatus.Failed(errorCode, "SetWindowDisplayAffinity failed.");

            if (!_api.TryGetAffinity(window, out uint verifiedAffinity, out errorCode))
                return CaptureProtectionStatus.Failed(errorCode, "GetWindowDisplayAffinity could not verify the applied policy.");

            if (verifiedAffinity != WDA_EXCLUDEFROMCAPTURE)
                return CaptureProtectionStatus.Failed(13, $"Affinity read-back was 0x{verifiedAffinity:X}, expected 0x11 (ERROR_INVALID_DATA).");

            return CaptureProtectionStatus.Verified;
        }
    }

    internal sealed class CaptureProtectionFailureLogGate
    {
        private CaptureProtectionStatus _lastLogged;
        private DateTimeOffset _nextLogAt = DateTimeOffset.MinValue;

        internal bool ShouldLog(CaptureProtectionStatus status, DateTimeOffset now)
        {
            if (!status.ErrorCode.HasValue)
            {
                _lastLogged = null;
                return false;
            }

            if (status == _lastLogged || now < _nextLogAt)
                return false;

            _lastLogged = status;
            _nextLogAt = now.AddSeconds(30);
            return true;
        }
    }
}
