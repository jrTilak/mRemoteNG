using System;
using System.Runtime.InteropServices;

namespace mRemoteNG.UI.CaptureProtection
{
    internal sealed class WindowsDisplayAffinityApi : IDisplayAffinityApi
    {
        private const uint GA_ROOT = 2;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_EX_LAYERED = 0x00080000;

        public bool SupportsCaptureExclusion => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

        public bool TryValidateWindow(IntPtr window, out int errorCode, out string detail)
        {
            errorCode = 0;
            detail = null;
            if (window == IntPtr.Zero || !IsWindow(window))
            {
                errorCode = 1400; // ERROR_INVALID_WINDOW_HANDLE; this is a precondition failure.
                detail = "The window handle is no longer valid.";
                return false;
            }

            if (GetWindowThreadProcessId(window, out uint processId) == 0)
            {
                errorCode = Marshal.GetLastWin32Error();
                detail = "GetWindowThreadProcessId failed.";
                return false;
            }

            if (processId != (uint)Environment.ProcessId)
            {
                errorCode = 5; // ERROR_ACCESS_DENIED; never set another process's HWND.
                detail = "Capture protection requires a window owned by the current process.";
                return false;
            }

            if (!TryGetStyle(window, GWL_STYLE, out uint style, out errorCode) ||
                !TryGetStyle(window, GWL_EXSTYLE, out uint extendedStyle, out errorCode))
            {
                detail = "GetWindowLong could not verify the window styles.";
                return false;
            }

            // GA_ROOT allows owned top-level forms; GetParent alone incorrectly rejects owned windows.
            if (GetAncestor(window, GA_ROOT) != window || (style & WS_CHILD) != 0)
            {
                errorCode = 87; // ERROR_INVALID_PARAMETER
                detail = "Capture protection requires a top-level window, not a child ActiveX HWND.";
                return false;
            }

            if ((extendedStyle & WS_EX_LAYERED) != 0)
            {
                errorCode = 87;
                detail = "The protected window must be opaque and must not have WS_EX_LAYERED.";
                return false;
            }

            return true;
        }

        private static bool TryGetStyle(IntPtr window, int index, out uint style, out int errorCode)
        {
            // A style value of zero is valid. Clear the error before disambiguating it.
            Marshal.SetLastPInvokeError(0);
            style = GetWindowLong(window, index);
            errorCode = Marshal.GetLastWin32Error();
            return style != 0 || errorCode == 0;
        }

        public bool TryGetAffinity(IntPtr window, out uint affinity, out int errorCode)
        {
            bool success = GetWindowDisplayAffinity(window, out affinity);
            errorCode = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }

        public bool TrySetAffinity(IntPtr window, uint affinity, out int errorCode)
        {
            bool success = SetWindowDisplayAffinity(window, affinity);
            errorCode = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        // Window styles are 32-bit values on both supported architectures (x64 and ARM64).
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
    }
}
