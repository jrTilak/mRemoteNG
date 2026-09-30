namespace mRemoteNG.UI.CaptureProtection
{
    /// <summary>A successful status always means that the affinity was read back as 0x11.</summary>
    public sealed record CaptureProtectionStatus
    {
        private CaptureProtectionStatus(bool enabled, int? errorCode, string detail)
        {
            Enabled = enabled;
            ErrorCode = errorCode;
            Detail = detail;
        }

        public bool Enabled { get; }
        public int? ErrorCode { get; }
        public string Detail { get; }
        public string DisplayText => Enabled
            ? "Capture protection: enabled"
            : ErrorCode.HasValue
                ? $"Capture protection: failed — error {ErrorCode.Value}"
                : "Capture protection: pending";

        public static CaptureProtectionStatus Pending { get; } = new(false, null, "Awaiting a window handle and verification.");
        internal static CaptureProtectionStatus Verified { get; } = new(true, null, "GetWindowDisplayAffinity returned 0x11.");
        internal static CaptureProtectionStatus Failed(int errorCode, string detail) => new(false, errorCode, detail);
    }
}
