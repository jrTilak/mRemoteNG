#requires -Version 5.1
<#
.SYNOPSIS
Captures the virtual desktop using ordinary GDI BitBlt for manual Windows testing.
.DESCRIPTION
Run from Windows PowerShell 5.1 in the interactive desktop session. This writes
one PNG and never changes another window's display affinity. Keep test images
outside the repository and use non-sensitive RDP content.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [switch] $IncludeLayeredWindows
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'This diagnostic requires Windows and an interactive desktop session.'
}

Add-Type -AssemblyName System.Drawing
if (-not ('CaptureProtectionDiagnostic.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CaptureProtectionDiagnostic
{
    public static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr destination, int x, int y,
            int width, int height, IntPtr source, int sourceX, int sourceY,
            uint rasterOperation);

        public static void Copy(IntPtr destination, int width, int height,
            IntPtr source, int sourceX, int sourceY, bool includeLayeredWindows)
        {
            const uint SRCCOPY = 0x00CC0020;
            const uint CAPTUREBLT = 0x40000000;
            uint operation = SRCCOPY | (includeLayeredWindows ? CAPTUREBLT : 0);
            if (!BitBlt(destination, 0, 0, width, height, source, sourceX, sourceY, operation))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt failed");
        }
    }
}
'@
}

$destination = [System.IO.Path]::GetFullPath($OutputPath)
if ([System.IO.File]::Exists($destination)) {
    throw "Output already exists: $destination"
}
if (-not [System.IO.Directory]::Exists([System.IO.Path]::GetDirectoryName($destination))) {
    throw 'Create the output directory before running the capture.'
}

$bitmap = $null
$graphics = $null
$screenDc = [IntPtr]::Zero
$bitmapDc = [IntPtr]::Zero
# Scope physical-pixel coordinates to this diagnostic thread, including mixed-DPI monitors.
$previousDpiContext = [CaptureProtectionDiagnostic.Native]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
if ($previousDpiContext -eq [IntPtr]::Zero) {
    throw 'Could not enter per-monitor DPI awareness; run on Windows 10 version 2004 or newer.'
}

try {
    $left = [CaptureProtectionDiagnostic.Native]::GetSystemMetrics(76) # SM_XVIRTUALSCREEN
    $top = [CaptureProtectionDiagnostic.Native]::GetSystemMetrics(77) # SM_YVIRTUALSCREEN
    $width = [CaptureProtectionDiagnostic.Native]::GetSystemMetrics(78) # SM_CXVIRTUALSCREEN
    $height = [CaptureProtectionDiagnostic.Native]::GetSystemMetrics(79) # SM_CYVIRTUALSCREEN
    if ($width -le 0 -or $height -le 0) {
        throw 'No virtual desktop is available to capture.'
    }

    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $screenDc = [CaptureProtectionDiagnostic.Native]::GetDC([IntPtr]::Zero)
    if ($screenDc -eq [IntPtr]::Zero) {
        throw 'Could not acquire the desktop device context.'
    }
    $bitmapDc = $graphics.GetHdc()
    [CaptureProtectionDiagnostic.Native]::Copy($bitmapDc, $width, $height, $screenDc, $left, $top, $IncludeLayeredWindows.IsPresent)
    $graphics.ReleaseHdc($bitmapDc)
    $bitmapDc = [IntPtr]::Zero
    $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Saved $width x $height desktop capture to $destination"
}
finally {
    if ($bitmapDc -ne [IntPtr]::Zero) { $graphics.ReleaseHdc($bitmapDc) }
    if ($screenDc -ne [IntPtr]::Zero) { [void][CaptureProtectionDiagnostic.Native]::ReleaseDC([IntPtr]::Zero, $screenDc) }
    if ($null -ne $graphics) { $graphics.Dispose() }
    if ($null -ne $bitmap) { $bitmap.Dispose() }
    [void][CaptureProtectionDiagnostic.Native]::SetThreadDpiAwarenessContext($previousDpiContext)
}
