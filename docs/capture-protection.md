# Protected frameless RDP client

This patch extends mRemoteNG. Microsoft Remote Desktop ActiveX still handles RDP networking, authentication, graphics, keyboard and mouse input, and the remote host uses Windows' built-in Remote Desktop server. No new RDP engine or server is introduced.

## Build on Windows

Use the repository's Windows build environment: Visual Studio 2026 with .NET desktop development and full MSBuild, .NET 10 SDK, Windows SDK 10.0.26100.0, and the registered Microsoft Remote Desktop ActiveX component (`mstscax.dll`). The existing [build workflow](../.github/workflows/Build_mR-NB.yml) uses these tools and transforms the assembly-info T4 template. Windows 11 24H2 or newer is the appropriate test host for the existing test project's `SupportedOSPlatformVersion` of 10.0.26100.0.

From Developer PowerShell at the repository root, build the application project without requiring the MSI installer toolchain:

```powershell
dotnet tool install --global dotnet-t4
t4 .\mRemoteNG\Properties\AssemblyInfo.tt -P platformType=x64 -r:.\mRemoteNG\libs\Microsoft.VisualStudio.Interop.dll
dotnet restore .\mRemoteNG\mRemoteNG.csproj -p:Configuration=Debug -p:Platform=x64
msbuild .\mRemoteNG\mRemoteNG.csproj /p:Configuration=Debug /p:Platform=x64 /p:MSBuildEnableWorkloadResolver=false /verbosity:minimal
```

If `dotnet-t4` is already installed, omit its install command. The executable is `mRemoteNG\bin\x64\Debug\mRemoteNG.exe`. Build ARM64 on a matching Windows development environment using the platform configuration in the existing workflow. Building the entire `mRemoteNG.sln` also requires its existing WiX installer and custom-action dependencies.

Use full Visual Studio `msbuild`, rather than `dotnet build`, for the application and tests: the project resolves a COM/ActiveX reference using `ResolveComReference`, which is unsupported by .NET-hosted MSBuild. Microsoft's [MSB4803 diagnostic](https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/msb4803) describes this distinction.

Build and run the existing NUnit suite on Windows:

```powershell
msbuild .\mRemoteNGTests\mRemoteNGTests.csproj /restore /p:Configuration=Debug /p:Platform=x64 /p:MSBuildEnableWorkloadResolver=false /verbosity:minimal
dotnet vstest .\mRemoteNGTests\bin\x64\Debug\mRemoteNGTests.dll /Logger:trx
```

The separate `mRemoteNGSpecs` project is not in the main solution and currently targets .NET 9 while referencing the .NET 10 application. Record this existing mismatch separately if attempting that suite; it is outside this focused patch.

## Operation

Launch the executable and open connections with the existing mRemoteNG UI. The application remains visible and interactive. Its opaque custom title bar contains the title, protection status, maximize/restore control and a close **X**. Drag its unused area to move the window, and resize with its borders. Maximizing keeps the local title bar available; ActiveX-controlled fullscreen is disabled.

The custom **X**, a local Alt+F4, and normal Windows close messages use the form's close path. Existing confirmation prompts still apply. Confirming exit closes connections and disposes the ActiveX controls and the capture timer. Canceling exit must leave the client visible and usable. There is no separate Stop/Disconnect control in the custom frame. Existing connection management continues to support reconnecting and closing individual sessions.

The window uses `FormBorderStyle.None`, `ShowInTaskbar = false` and `WS_EX_TOOLWINDOW`, without `WS_EX_APPWINDOW`. Native `WS_THICKFRAME` sizing remains enabled; an early `WM_NCCALCSIZE` handler removes the visible system frame and keeps the full client area available to the opaque custom chrome. This handler runs even before the chrome instance exists, including floating-window base-constructor handle creation. It should be absent from the normal taskbar and Alt+Tab list where Windows permits. There is no tray icon, and old start-minimized, minimize-to-tray and close-to-tray preferences cannot hide the client indefinitely. Other application dialogs and Windows shell behavior need their own checks; this does not conceal process or window enumeration.

The entire RDP-hosting top-level form receives display affinity, including the custom frame. Protection is not applied to the ActiveX child HWND. The window stays opaque: no `WS_EX_LAYERED`, transparency key, partial form opacity or per-pixel transparency.

Floating DockPanelSuite windows use the same chrome and manager. Their title-bar close button closes the application through the main form's normal exit path. Move a floating window using its custom title bar; use session-tab docking commands to dock its contents. DockPanelSuite's original floating-title drag is disabled because it temporarily makes the window layered. RDP host registration follows ancestor reparenting when tabs move. ActiveX multi-monitor presentation is disabled; moving or maximizing a protected host on another monitor still requires the checks below. These integrations have not yet been validated on Windows.

## Policy verification

The manager accepts only top-level HWNDs owned by this process. It applies `WDA_EXCLUDEFROMCAPTURE` (`0x00000011`) after handle creation and on relevant visibility, show, restore and handle-recreation events. Every `SetWindowDisplayAffinity` failure records `Marshal.GetLastWin32Error()` immediately. A successful set is followed by `GetWindowDisplayAffinity`; only a verified `0x11` produces `Capture protection: enabled`.

The UI-thread `System.Windows.Forms.Timer` defaults to 500 ms; `CaptureProtectionManager` accepts an interval of 250–5000 ms in its constructor. Each check skips disposed, invisible or handleless forms, validates HWND ownership and top-level status, then reads affinity. A verified `0x11` causes no set call. A missing, changed or unverifiable flag triggers repair and re-verification. Failures appear in the title bar as `Capture protection: failed — error <number>`, with duplicate failures suppressed in logging. The timer stops and is disposed during application shutdown.

Native call failures preserve their actual Win32 error. Local precondition failures use corresponding Win32 codes with a log explanation: for example, `50` for unsupported Windows, `87` for an ineligible window, or `13` when affinity read-back differs from `0x11`. These validation codes do not imply that a native set call was attempted.

The timer is a recovery mechanism, not a stronger security boundary. The documented API does not allow another process to set affinity on windows it does not own. A verified flag establishes the current policy; it does not prove that an arbitrary capture tool obeys it. Windows 10 version 2004 (build 19041) introduced full exclusion; older versions interpret this value differently and must not be reported as fully protected. See [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).

## Windows verification checklist

**All Windows runtime checks below remain pending.** Run them in an interactive Windows session with a disposable test RDP account and non-sensitive remote content. Record the client commit/build, Windows version/build, CPU architecture, graphics driver, monitor arrangement and DPI, capture-tool version, result and any error code. Repeat screenshot checks after lifecycle transitions and for every RDP-bearing top-level window. Do not claim detached-window or fullscreen protection from a main-window result.

1. Launch the modified executable on Windows 10 version 2004 or newer with a compatible .NET runtime.
2. Confirm the custom opaque title bar, title, capture status and local close button appear without a standard frame. Inspect extended styles: `WS_EX_TOOLWINDOW` set, `WS_EX_APPWINDOW` and `WS_EX_LAYERED` clear.
3. Confirm no normal taskbar or Alt+Tab entry and no tray icon. Repeat with old start-minimized/tray settings in the user profile. Test shell minimize commands and Show Desktop: the client must remain recoverable.
4. Drag the title bar, resize every edge/corner, maximize and restore. Confirm actual bounds change during border dragging, not only the resize cursor. Inspect `WS_THICKFRAME` and confirm the first HWND has no extra system border; maximize must fit the monitor working area and preserve the local caption. Repeat for an initially floating window. Test negative monitor coordinates and mixed DPI; the frame and close button must remain accessible on every monitor.
5. Close with the custom **X**, then separately with local Alt+F4 and a Windows close message. Confirm they run the same cleanup. Test Alt+F4 both with a local title control focused and with ActiveX focused under each existing keyboard-redirection setting; record if the remote desktop receives the shortcut. Test both accepting and canceling existing exit confirmation, including with multiple sessions.
6. Reopen and connect to a Windows RDP host through normal mRemoteNG connection settings. Keep NLA and certificate checks enabled.
7. Verify normal keyboard, pointer, focus and resize behavior in the remote desktop. Ensure **X** remains locally clickable above the ActiveX surface. Repeatedly use the RDP fullscreen keyboard shortcut (Ctrl+Alt+Break); it should maximize/restore the containing window and must not open an ActiveX fullscreen window.
8. Confirm the title bar shows exactly `Capture protection: enabled` after verification, or a failed status with its actual error number.
9. Break in the manager's post-set `GetWindowDisplayAffinity` path and inspect the returned value: it must be `0x11` on the current top-level HWND. Also check `GetWindowThreadProcessId` matches this process and `GetAncestor(hwnd, GA_ROOT)` equals that HWND.
10. Capture with Snipping Tool in region, window and full-screen modes. The protected content must be omitted or blank while remaining visible to the local user.
11. Run the ordinary GDI/BitBlt capture below. Compare the saved pixels against the visible test desktop; do not infer this result from Snipping Tool.
12. Repeat with another ordinary screenshot application; record its name, version and capture mode.
13. In a temporary development build only, disable the manager's lifecycle/timer enforcement and set the current form's affinity to `WDA_NONE` (`0`) from the application's UI thread; see the development-only comparison below. Verify with `GetWindowDisplayAffinity`, then repeat captures: the same RDP content must now be capturable. Restart with enforcement restored and verify `0x11` and exclusion again. Do not commit or distribute a disabled build.
14. Repeatedly restore, resize and maximize the client, move it between monitors, and hide/show it through a development-only UI-thread action. Verify affinity and captures after each transition. External minimization must not leave an unreachable window.
15. Disconnect and reconnect using existing connection management. Open multiple RDP sessions; repeat affinity and input checks for their containing top-level forms.
16. Force WinForms handle recreation from a temporary development-only UI-thread action (`RecreateHandle()` on the form), or a debugger invocation at a UI-thread breakpoint. Confirm the HWND changes, the new HWND is re-registered and verified as `0x11`, the title status updates, and input/close still work. Never invoke WinForms handles from a worker thread.
17. Put tracepoints/counters on native `GetWindowDisplayAffinity` and `SetWindowDisplayAffinity` wrappers. During stable idle operation, reads occur about every 500 ms per eligible form and writes remain zero after initial/event application. Clear the flag once from the application's UI thread: the next check must repair and verify it. Duplicate failures must not flood logs.
18. Measure idle CPU for at least one minute with one and several sessions. Compare to an unmodified build under the same RDP workload; watch for a busy loop or continuous set calls.
19. Confirm exiting with active sessions disconnects them and disposes ActiveX controls and the timer. Verify no mRemoteNG process remains in Task Manager after each close path. A disconnected remote Windows user session may remain on the server according to normal RDP policy.
20. Use the fake native adapter in unit tests to force set/read failures with known codes. In a development build, exercise the same failure path while displaying the frame and compare its number to the native error captured immediately after the failed call. Exercise an unsupported OS or the version-check seam: it must never show enabled. Remove temporary fault injection before distributing.

Also exercise connection-panel float/detach commands, restored floating layouts, moving session tabs between panels, additional RDP windows and display changes. Inventory every top-level HWND that actually contains RDP pixels. Each window must have a protected opaque frame, verified `0x11`, locally accessible close control and passing captures. Include docking tab contents back into the main form, not just floating them, and verify no layered style is introduced during a drag.

### Development-only control comparison

At a debugger breakpoint on the main form's UI thread, `FrmMain.Default.CaptureProtection.Dispose()` stops the integrity timer and unregisters lifecycle handlers. The native adapter's `TrySetAffinity` can then set `FrmMain.Default.Handle` to `0u`, followed by `TryGetAffinity` to confirm the returned value is zero. Use the `mRemoteNG.UI.Forms` and `mRemoteNG.UI.CaptureProtection` namespaces in debugger expressions, and inspect the returned `bool` and error outputs. Test only a disposable development session; no production disable switch is provided. The old title text is not an authoritative status after manually disposing its manager. Restart the application before trusting status or resuming normal use. For a floating form, repeat the set/read against that specific top-level form's HWND as well.

To test the recovery timer, leave the manager running and clear affinity once from that same UI-thread breakpoint. The next eligible timer check should set and verify `0x11`; this is a separate test from the deliberately unprotected capture comparison.

### Ordinary GDI/BitBlt capture

Open Windows PowerShell 5.1 on the same interactive desktop, keep the test RDP window visible, and save screenshots outside the repository:

```powershell
powershell.exe -NoProfile -File .\Tools\CaptureProtection\Capture-DesktopBitBlt.ps1 -OutputPath "$env:TEMP\rdp-protected-bitblt.png"
powershell.exe -NoProfile -File .\Tools\CaptureProtection\Capture-DesktopBitBlt.ps1 -OutputPath "$env:TEMP\rdp-protected-bitblt-layered.png" -IncludeLayeredWindows
```

The [diagnostic script](../Tools/CaptureProtection/Capture-DesktopBitBlt.ps1) performs a desktop `GetDC`/`BitBlt` using `SRCCOPY`, optionally adding `CAPTUREBLT`, across the virtual desktop. It does not change affinity, inject code or require elevation. It refuses to overwrite existing files. Use fresh filenames for the unprotected control comparison and restore test. Inspect the PNGs manually; successful PNG creation alone does not establish exclusion. The script itself has not been executed on Windows in this development environment. See Microsoft's [BitBlt documentation](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt).

## Limitations and RDP security

Display affinity is a Windows capture policy, not DRM. It relies on Desktop Window Manager and capture implementations honoring the policy. It does not protect against physical cameras, injected code, administrator modifications, kernel drivers, hypervisors or unsupported capture paths. It does not make the client completely invisible.

The local monitor still displays the client. Task Manager, process/window enumeration, accessibility and diagnostic tools, network monitoring, Windows/RDP logs and the remote server can still observe it or its connection. No process concealment, monitoring evasion, code injection, elevation, persistence, automatic startup, kernel driver, anti-analysis behavior or interference with security tools is introduced.

Keep normal Microsoft RDP security behavior and existing mRemoteNG credential handling. Do not disable Network Level Authentication or certificate validation, hardcode credentials, log passwords/tokens, or commit personal connection files, screenshots containing private data or secrets. Use a VPN or RD Gateway for access outside the local network; do not expose TCP 3389 publicly without appropriate network protection.

## Validation record

Implementation took place on Linux. The existing repository build and test commands were attempted before changes but Windows targeting prevented execution (`NETSDK1100`). Retrying the application build with `-p:EnableWindowsTargeting=true` restored dependencies and reached `MSB4803` at `ResolveComReference`, which requires full Windows MSBuild. Neither result is a successful application build or repository test run. Windows, ActiveX, screenshot behavior, Alt+Tab/taskbar behavior, shutdown and the manual checklist above remain unverified until executed on Windows. Baseline infrastructure failures must be recorded separately from failures introduced by this patch.

The 17 pure-policy NUnit cases passed on Linux in a temporary .NET 10 harness linking the exact policy, status and test source files. This verifies the fake-native state logic, not user32 behavior. A separate isolated compile of the manager, native adapter, chrome, floating window and fullscreen handler passed with zero warnings/errors against the real DockPanelSuite 3.1.1 assembly and minimal main-form/theme stubs. This checks those source interfaces; it is not a full-application build. Local documentation links resolved. The PowerShell capture diagnostic was reviewed but could not be executed because this environment has neither Windows nor PowerShell.

The 16 Windows STA manager-lifecycle cases also compiled with the production affinity files and policy tests in a temporary Windows-targeted WinForms harness, with zero warnings/errors. Their Windows execution is pending. They use actual WinForms lifecycle events with a fake native adapter; even a future passing run will not establish screenshot exclusion without the manual capture checks.

The native resize review added three Windows chrome cases covering the first handle's sizing style/client area (including a handle created before the chrome instance) and maximization to the working area with an accessible caption. These tests and the revised chrome/floating-window sources compiled in an isolated Windows-targeted harness against DockPanelSuite 3.1.1 with zero warnings/errors; execution remains pending on Windows. DockPanelSuite's floating-window caption path forwards to native movement when `AllowEndUserDocking` is false; the custom drag now supplies the actual signed screen coordinates to that path.

## Modified-file report

| Files | Change |
| --- | --- |
| [`UI/CaptureProtection`](../mRemoteNG/UI/CaptureProtection/) | Central UI-thread manager, lifecycle registration, native display-affinity declarations, ownership/top-level/opacity checks, verified status, repair policy and failure-log suppression. |
| [`ProtectedWindowChrome.cs`](../mRemoteNG/UI/Forms/ProtectedWindowChrome.cs), [`frmMain.cs`](../mRemoteNG/UI/Forms/frmMain.cs), [`frmMain.Designer.cs`](../mRemoteNG/UI/Forms/frmMain.Designer.cs) | Opaque custom frame, close/maximize/drag/resize controls, tool-window styles, status, normal shutdown integration and minimized-state recovery. |
| [`FloatWindowNG.cs`](../mRemoteNG/UI/Tabs/FloatWindowNG.cs), [`InterfaceControl.cs`](../mRemoteNG/Connection/InterfaceControl.cs), [`ConnectionWindow.cs`](../mRemoteNG/UI/Window/ConnectionWindow.cs) | Register RDP-bearing main/floating hosts and reparented controls; reserve space for local floating-window chrome. |
| [`RdpProtocol.cs`](../mRemoteNG/Connection/Protocol/RDP/RdpProtocol.cs), [`RdpProtocol8.cs`](../mRemoteNG/Connection/Protocol/RDP/RdpProtocol8.cs), [`FullscreenHandler.cs`](../mRemoteNG/UI/FullscreenHandler.cs), [`ViewMenu.cs`](../mRemoteNG/UI/Menu/msMain/ViewMenu.cs), [`ConnectionContextMenu.cs`](../mRemoteNG/UI/Controls/ConnectionContextMenu.cs) | Replace ActiveX fullscreen/multi-monitor presentation with maximized protected hosts; retain content-area sizing and update command labels. |
| [`SettingsLoader.cs`](../mRemoteNG/Config/Settings/SettingsLoader.cs), [`SettingsSaver.cs`](../mRemoteNG/Config/Settings/SettingsSaver.cs), [`AppearancePage.cs`](../mRemoteNG/UI/Forms/OptionsPages/AppearancePage.cs), [`StartupExitPage.cs`](../mRemoteNG/UI/Forms/OptionsPages/StartupExitPage.cs) | Disable tray and unreachable-minimization paths and remove the shutdown opacity change. |
| [`mRemoteNG.csproj`](../mRemoteNG/mRemoteNG.csproj) | Declare the capture policy's minimum Windows platform version, build 19041. |
| [`CaptureProtectionPolicyTests.cs`](../mRemoteNGTests/UI/CaptureProtection/CaptureProtectionPolicyTests.cs), [`CaptureProtectionManagerTests.cs`](../mRemoteNGTests/UI/CaptureProtection/CaptureProtectionManagerTests.cs) | Fake-native tests for verification, recovery, no redundant timer writes, eligibility, error preservation, old Windows and log suppression; Windows STA lifecycle tests for registration, visibility, handle recreation and disposal. |
| [`README.md`](../README.md), this guide, [`Capture-DesktopBitBlt.ps1`](../Tools/CaptureProtection/Capture-DesktopBitBlt.ps1) | Build and operation guidance, limitations, pending Windows tests and a manual GDI capture diagnostic. |
