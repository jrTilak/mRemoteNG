# Protected frameless RDP client

This patch extends mRemoteNG. Microsoft Remote Desktop ActiveX still handles RDP networking, authentication, graphics, keyboard and mouse input, and the remote host uses Windows' built-in Remote Desktop server. No new RDP engine or server is introduced.

## Build on Windows

### Requirements

Build and run the complete application on Windows. Use **Windows 11 x64, version 24H2 or newer**, for the build and test environment described below; the existing test project requires Windows build 26100 or newer.

- **Git for Windows** and internet access to restore NuGet dependencies.
- **Visual Studio 2026**, updated, with the **.NET desktop development** workload and full MSBuild.
- **.NET 10 SDK**.
- **Windows SDK 10.0.26100.0**, selectable in Visual Studio Installer's individual components.
- Windows' Remote Desktop client and its registered **`mstscax.dll` ActiveX component**.

The existing [build workflow](../.github/workflows/Build_mR-NB.yml) uses these tools and transforms the assembly-info T4 template. Building the application project alone does not require the MSI installer toolchain. Building the MSI additionally requires its WiX and .NET Framework 4.8.1 custom-action dependencies; see [the branding build guide](app-branding.md#build-defaults-without-source-code-edits).

### Build and launch

Use the `Release` configuration for the production build. The commands below produce an optimized, framework-dependent application; Windows runtime verification remains required before distribution.

Open **Developer PowerShell for Visual Studio 2026** from the Windows Start menu, or through Visual Studio's **Tools → Command Line → Developer PowerShell**. Run each command below separately, in order, in the same PowerShell session. Stop and resolve any error before continuing.

Confirm that Visual Studio's MSBuild is available:

```powershell
MSBuild.exe -version
```

If `MSBuild.exe` is not recognized, open the Developer PowerShell shortcut associated with your Visual Studio installation. If the shortcut is missing, install or modify Visual Studio with the .NET desktop development workload. The standalone .NET SDK does not provide the full MSBuild required by this project's COM reference.

For a fresh checkout, clone this feature branch. If you already cloned it, skip the next two commands and open your existing repository folder:

```powershell
git clone --branch feat/protected-frameless-rdp https://github.com/jrTilak/mRemoteNG.git
```

Enter the newly cloned repository:

```powershell
cd .\mRemoteNG
```

Stay in the repository root (the folder containing `mRemoteNG.sln`). The source project is in a second `mRemoteNG` folder inside that root. To customize the build's default shortcut label, icon or Start menu visibility, edit [`Branding.props`](../Branding.props) before building.

Check the current folder. This must print `True`; if it prints `False`, change into the repository folder before continuing:

```powershell
Test-Path .\mRemoteNG.sln
```

Install the T4 tool once. Skip this command if `dotnet-t4` is already installed:

```powershell
dotnet tool install --global dotnet-t4
```

Make the installed tool available in this PowerShell session:

```powershell
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
```

Resolve the interop DLL to an absolute path:

```powershell
$rdpInterop = (Resolve-Path -LiteralPath .\mRemoteNG\libs\Microsoft.VisualStudio.Interop.dll -ErrorAction Stop).Path
```

Generate assembly version information:

```powershell
t4 .\mRemoteNG\Properties\AssemblyInfo.tt -P platformType=x64 "-r:$rdpInterop"
```

Restore application dependencies:

```powershell
dotnet restore .\mRemoteNG\mRemoteNG.csproj -p:Configuration=Release -p:Platform=x64
```

Build the application with Visual Studio's full MSBuild:

```powershell
MSBuild.exe .\mRemoteNG\mRemoteNG.csproj /p:Configuration=Release /p:Platform=x64 /p:MSBuildEnableWorkloadResolver=false /verbosity:minimal
```

After a successful build, launch the application:

```powershell
.\mRemoteNG\bin\x64\Release\Capture2Text.exe
```

Keep the **entire output folder** together when copying the application to another PC. Regular `Release` requires **.NET 10 Desktop Runtime** and the matching **Visual C++ Redistributable** listed in the [runtime requirements](../README.md#minimum-requirements). The separate `Release Self-Contained` configuration includes the .NET runtime in its published output. For Windows VM/Docker guidance, self-contained publishing and a single EXE that opens the client directly, see the [production packaging guide](production-packaging.md).

Build ARM64 on a matching Windows development environment using the platform configuration in the existing workflow. A successful dependency restore alone does not mean the application has been compiled.

Pass the DLL reference as an absolute path, as above. A relative `-r:.\...` path can cause `dotnet-t4` to throw `System.IO.FileLoadException: The given assembly name was invalid`. `Resolve-Path` also confirms the DLL exists before transformation, and quoting the complete reference argument handles folder names containing spaces. If T4 instead says its input file does not exist, check that PowerShell is in the repository root; the source project is in the nested `mRemoteNG` folder.

Use full Visual Studio `msbuild`, rather than `dotnet build`, for the application and tests: the project resolves a COM/ActiveX reference using `ResolveComReference`, which is unsupported by .NET-hosted MSBuild. Microsoft's [MSB4803 diagnostic](https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/msb4803) describes this distinction.

### Run the tests

Build the existing NUnit suite on Windows:

```powershell
MSBuild.exe .\mRemoteNGTests\mRemoteNGTests.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:MSBuildEnableWorkloadResolver=false /verbosity:minimal
```

After a successful test build, run the suite:

```powershell
dotnet vstest .\mRemoteNGTests\bin\x64\Release\mRemoteNGTests.dll /Logger:trx
```

The separate `mRemoteNGSpecs` project is not in the main solution and currently targets .NET 9 while referencing the .NET 10 application. Record this existing mismatch separately if attempting that suite; it is outside this focused patch.

## Operation

### Connect to a remote RDP desktop

Use the remote server's existing **IP/hostname, port, username and password**. Remote Desktop must already be enabled on that server, the account must have permission to connect, and the server must be reachable from your PC over your network or VPN. The remote computer uses Windows' built-in RDP server; this client does not need to be installed there.

1. Launch the application and find the **Connections** panel.
2. Right-click its root or a folder and select **New Connection**.
3. Give the connection a name, then select it.
4. In **Config → Properties**, enter the values below.

| Field | What to enter |
| --- | --- |
| **Hostname/IP** | The remote server's hostname or IP address, without the port. |
| **Protocol** | `RDP`. |
| **Port** | The supplied RDP port; normally `3389`. |
| **Username** | Your supplied remote Windows username. |
| **Password** | Your supplied remote Windows password. |
| **Domain** | The supplied domain, if required; otherwise leave it blank. |

Right-click the saved connection and select **Connect**. The remote desktop opens in a tab; click inside it to use your keyboard and mouse. For another session, create another connection or reconnect to an existing entry.

If the **Connections** or **Config** panel is missing, select **View → Reset layout** and confirm. Keep Network Level Authentication and certificate checks enabled. For connectivity errors, first verify the supplied address, port and network/VPN access; for authentication errors, verify the remote account credentials and permissions.

### Window controls

The application opens visible and interactive. Its opaque custom title bar contains a maximize/restore control and a close **X**. The frame has no branding image or app icon. Successful capture protection adds no status text; pending verification and failures remain visible. The native window title and custom title text are always empty for the main and floating windows. Drag the unused title-bar area to move the window, and resize with its borders. Maximizing keeps the local title bar available; ActiveX-controlled fullscreen is disabled.

The custom **X**, **F9**, a local Alt+F4, and normal Windows close messages use the form's close path without an application-exit confirmation. F9 also works while the client is hidden. Closing disconnects active sessions, disposes ActiveX controls, stops the capture timer and releases both shortcuts. Save pending Options edits with **Apply** or **OK** before closing; unapplied control edits are discarded on application exit. Existing connection-file saving and backup preferences still apply. There is no separate Stop/Disconnect control in the custom frame. Existing connection management continues to support reconnecting and closing individual sessions.

Use the global shortcuts as follows:

| Key | Action |
| --- | --- |
| **F8**, first press after launch | Bring the main window and detached RDP windows forward and keep them always on top. |
| **F8**, next press | Hide those windows without disconnecting their RDP sessions. |
| **F8**, while hidden | Show the windows again, still always on top. Further presses alternate between hidden and visible. |
| **F9** | Close the application through normal cleanup, including while hidden, without an exit confirmation. |

Hiding uses normal Windows window visibility. The client process and RDP sessions keep running, and the process remains listed in Task Manager. With no taskbar or tray entry, **press F8 again to return to your desktop sessions**. New detached windows follow the current state. Each launch starts visible with always on top off.

While an application-owned required modal prompt is open, F8 brings it forward instead of hiding the client so you can answer it. F9 closes the application and cancels its owned prompts. Password and certificate decisions are not automatically accepted by a shortcut.

F8 and F9 are reserved globally while this client runs, including when another app has focus. They are local commands, so verify delivery with RDP ActiveX focus on Windows. If another app owns either shortcut, the protected frame shows the actual Win32 registration error and records it in the log. Release the conflicting shortcut and restart this client to retry. If F8 registration fails after handle recreation while the client is hidden, its windows are restored so they remain reachable. No keyboard hook or busy loop is used. These shortcuts do not change capture protection or override Windows' secure desktop.

Startup opens the client directly without the splash image or update-preference popup. Routine notification messages, including idle disconnection, stay in the log and the manually opened **Notifications** panel; they do not spawn message boxes or steal focus. Restart failures are logged. Unexpected UI errors are logged and attempt normal application shutdown; fatal background errors retain normal runtime termination without an app exception dialog. Legacy popup/focus-switch options are hidden and no longer enable those behaviors. Application branding icons are absent from the custom frame and document-tab strips; executable and shortcut icons remain unchanged.

Required prompts remain visible and interactive locally. Application message boxes now use protected WinForms dialogs; task dialogs, password entry, panel/input dialogs, export choices and the prerequisite-download dialog register capture protection before building/showing their HWNDs. Their protection timers are disposed when each dialog returns. Password input, Yes/No/Cancel results, safe default buttons and certificate/NLA behavior are preserved. A protection failure is shown inside the dialog (or its caption) with the actual error number; it is never reported as enabled. Each prompt type needs its own capture verification; the tested app password prompt is recorded in the [October 4 results](#windows-validation-on-2026-10-04).

Managed prompt captions are empty, including after localization changes; their question text, remote hostnames and choices remain visible inside the dialog. A generic dialog without a status label uses its caption to report a capture-protection failure. The executable is `Capture2Text.exe`, and its process remains visible in Task Manager; see [blank titles and executable branding](app-branding.md).

Microsoft RDP credential/certificate dialogs, Windows file/color/folder pickers and separate connector UI are not covered by that application-dialog presenter. In particular, a credential broker or secure-desktop dialog owned by another process cannot be protected with this app's display-affinity API. Do not assume the main window's policy protects those prompts or that they have passed capture testing.

The window uses `FormBorderStyle.None`, `ShowInTaskbar = false` and `WS_EX_TOOLWINDOW`, without `WS_EX_APPWINDOW`. Native `WS_THICKFRAME` sizing remains enabled; an early `WM_NCCALCSIZE` handler removes the visible system frame and keeps the full client area available to the opaque custom chrome. This handler runs even before the chrome instance exists, including floating-window base-constructor handle creation. It should be absent from the normal taskbar and Alt+Tab list where Windows permits. There is no tray icon. Old start-minimized, minimize-to-tray and close-to-tray preferences cannot hide the client indefinitely; deliberate F8 hiding is reversed with F8. Other application dialogs and Windows shell behavior need their own checks; this does not conceal process or window enumeration.

The entire RDP-hosting top-level form receives display affinity, including the custom frame. Protection is not applied to the ActiveX child HWND. The window stays opaque: no `WS_EX_LAYERED`, transparency key, partial form opacity or per-pixel transparency.

Floating DockPanelSuite windows use the same chrome and manager. Their title-bar close button closes the application through the main form's normal exit path. Move a floating window using its custom title bar; use session-tab docking commands to dock its contents. DockPanelSuite's original floating-title drag is disabled because it temporarily makes the window layered. RDP host registration follows ancestor reparenting when tabs move. ActiveX multi-monitor presentation is disabled; moving or maximizing a protected host on another monitor still requires the checks below. Floating/reparented RDP-host behavior remains unverified on Windows.

## Policy verification

The manager accepts only top-level HWNDs owned by this process. It applies `WDA_EXCLUDEFROMCAPTURE` (`0x00000011`) after handle creation and on relevant visibility, show, restore and handle-recreation events. Every `SetWindowDisplayAffinity` failure records `Marshal.GetLastWin32Error()` immediately. A successful set is followed by `GetWindowDisplayAffinity`; only a verified `0x11` establishes the enabled state. The main and floating custom frames suppress the successful `Capture protection: enabled` text. A blank status area is not proof of protection: use the verified affinity value for testing. Pending verification and failures remain visible, and protection checks continue unchanged.

The UI-thread `System.Windows.Forms.Timer` defaults to 500 ms; `CaptureProtectionManager` accepts an interval of 250–5000 ms in its constructor. Each check skips disposed, invisible or handleless forms, validates HWND ownership and top-level status, then reads affinity. A verified `0x11` causes no set call. A missing, changed or unverifiable flag triggers repair and re-verification. Failures appear in the title bar as `Capture protection: failed — error <number>`, with duplicate failures suppressed in logging. The timer stops and is disposed during application shutdown.

Native call failures preserve their actual Win32 error. Local precondition failures use corresponding Win32 codes with a log explanation: for example, `50` for unsupported Windows, `87` for an ineligible window, or `13` when affinity read-back differs from `0x11`. These validation codes do not imply that a native set call was attempted.

The timer is a recovery mechanism, not a stronger security boundary. The documented API does not allow another process to set affinity on windows it does not own. A verified flag establishes the current policy; it does not prove that an arbitrary capture tool obeys it. Windows 10 version 2004 (build 19041) introduced full exclusion; older versions interpret this value differently and must not be reported as fully protected. See [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).

## Windows verification checklist

**Partial Windows results are recorded in the [validation record](#validation-record).** Complete the remaining checks in an interactive Windows session with a disposable test RDP account and non-sensitive remote content. Record the client commit/build, Windows version/build, CPU architecture, graphics driver, monitor arrangement and DPI, capture-tool version, result and any error code. Repeat screenshot checks after lifecycle transitions and for every RDP-bearing top-level window. Do not claim detached-window or fullscreen protection from a main-window result.

1. Launch the modified executable on Windows 10 version 2004 or newer with a compatible .NET runtime.
2. Confirm the opaque custom title bar and local close button appear, with no splash, frame icon or standard Windows frame. Verify that both native window titles and custom title text stay empty for main and floating windows, including after switching sessions and recreating handles. Inspect extended styles: `WS_EX_TOOLWINDOW` set, `WS_EX_APPWINDOW` and `WS_EX_LAYERED` clear.
3. Confirm no normal taskbar or Alt+Tab entry and no tray icon. Repeat with old start-minimized/tray settings in the user profile. Test shell minimize commands and Show Desktop: the client must remain recoverable.
4. Drag the title bar, resize every edge/corner, maximize and restore. Confirm actual bounds change during border dragging, not only the resize cursor. Inspect `WS_THICKFRAME` and confirm the first HWND has no extra system border; maximize must fit the monitor working area and preserve the local caption. Repeat for an initially floating window. Test negative monitor coordinates and mixed DPI; the frame and close button must remain accessible on every monitor.
5. Close with the custom **X**, then separately with **F9** while visible and hidden, local Alt+F4 and a Windows close message. Confirm they run the same cleanup. Test Alt+F4 both with a local title control focused and with ActiveX focused under each existing keyboard-redirection setting; record if the remote desktop receives the shortcut. Verify immediate exit without confirmation, including with multiple sessions and an open Options tab. Confirm unsaved Options control edits are discarded while changes already saved with Apply/OK persist. Trigger a routine logged warning and confirm it produces neither a popup nor automatic panel activation.
6. Reopen and connect to a Windows RDP host through normal mRemoteNG connection settings. Keep NLA and certificate checks enabled.
7. Verify normal keyboard, pointer, focus and resize behavior in the remote desktop. Ensure **X** remains locally clickable above the ActiveX surface. Repeatedly use the RDP fullscreen keyboard shortcut (Ctrl+Alt+Break); it should maximize/restore the containing window and must not open an ActiveX fullscreen window.
8. Confirm the main and floating title bars omit `Capture protection: enabled` after verification. Pending verification and a failed status with its actual error number must still appear. Confirm a shortcut-registration error stays visible even when capture protection is verified.
9. Break in the manager's post-set `GetWindowDisplayAffinity` path and inspect the returned value: it must be `0x11` on the current top-level HWND. Also check `GetWindowThreadProcessId` matches this process and `GetAncestor(hwnd, GA_ROOT)` equals that HWND.
10. Capture with Snipping Tool in region, window and full-screen modes. The protected content must be omitted or blank while remaining visible to the local user.
11. Run the ordinary GDI/BitBlt capture below. Compare the saved pixels against the visible test desktop; do not infer this result from Snipping Tool.
12. Repeat with another ordinary screenshot application; record its name, version and capture mode.
13. In a temporary development build only, disable the manager's lifecycle/timer enforcement and set the current form's affinity to `WDA_NONE` (`0`) from the application's UI thread; see the development-only comparison below. Verify with `GetWindowDisplayAffinity`, then repeat captures: the same RDP content must now be capturable. Restart with enforcement restored and verify `0x11` and exclusion again. Do not commit or distribute a disabled build.
14. Repeatedly restore, resize and maximize the client, move it between monitors, and hide/show it with F8. Verify `0x11` and captures after each return to visible state; hidden forms are skipped by the integrity timer. External minimization must not leave an unreachable window.
15. Disconnect and reconnect using existing connection management. Open multiple RDP sessions; repeat affinity and input checks for their containing top-level forms.
16. Force WinForms handle recreation from a temporary development-only UI-thread action (`RecreateHandle()` on the form), or a debugger invocation at a UI-thread breakpoint. Confirm the HWND changes, the new HWND is re-registered and verified as `0x11`, pending/error status updates without displaying the success string, and input/close still work. Never invoke WinForms handles from a worker thread.
17. Put tracepoints/counters on native `GetWindowDisplayAffinity` and `SetWindowDisplayAffinity` wrappers. During stable idle operation, reads occur about every 500 ms per eligible form and writes remain zero after initial/event application. Clear the flag once from the application's UI thread: the next check must repair and verify it. Duplicate failures must not flood logs.
18. Measure idle CPU for at least one minute with one and several sessions. Compare to an unmodified build under the same RDP workload; watch for a busy loop or continuous set calls.
19. Confirm exiting with active sessions disconnects them and disposes ActiveX controls and the timer. Verify no mRemoteNG process remains in Task Manager after each close path. A disconnected remote Windows user session may remain on the server according to normal RDP policy.
20. Use the fake native adapter in unit tests to force set/read failures with known codes. In a development build, exercise the same failure path while displaying the frame and compare its number to the native error captured immediately after the failed call. Exercise an unsupported OS or the version-check seam: it must never show enabled. Remove temporary fault injection before distributing.

Also exercise connection-panel float/detach commands, restored floating layouts, moving session tabs between panels, additional RDP windows and display changes. Inventory every top-level HWND that actually contains RDP pixels. Each window must have a protected opaque frame, verified `0x11`, locally accessible close control and passing captures. Include docking tab contents back into the main form, not just floating them, and verify no layered style is introduced during a drag.

Also verify F8 and F9 while the main menu, RDP ActiveX control, another application and each detached window have focus. Check the F8 sequence: first raise/pin, then hide, then restore pinned, then hide again. Confirm sessions stay connected while hidden, new detached windows follow the current state, handle recreation preserves recovery, and holding either key acts only once. Reserve each shortcut in a separate test app to exercise registration failure, confirm the error stays inside the protected frame without covering close/maximize, and verify a failed F8 re-registration restores hidden windows. Close with F9 both while visible and hidden, then verify both keys are released. Test the password dialog with the same capture tools and confirm cancel/success both dispose its protection timer.

Exercise required protected prompts with capture tools: password entry, connection-file recovery, save/discard/cancel and Settings restart. Verify the default choice and Escape/X behavior, no silent approval, visible protection errors, correct z-order when F8 is enabled, and timer disposal after each dialog closes. Include an overlapping detached window when pressing F8 with a prompt open: the prompt must be brought forward and remain answerable, and the client must not hide it. Confirm F9 never silently accepts a required password or certificate decision. Test long messages: all text must remain readable by scrolling while buttons and protection status stay on screen. Repeat with an owner on a shorter monitor and the pointer on a taller monitor, including when both monitors have the same DPI. Inventory native Microsoft/Windows/connector prompts separately; this checklist does not claim protection for them.

### Development-only control comparison

At a debugger breakpoint on the main form's UI thread, `FrmMain.Default.CaptureProtection.Dispose()` stops the integrity timer and unregisters lifecycle handlers. The native adapter's `TrySetAffinity` can then set `FrmMain.Default.Handle` to `0u`, followed by `TryGetAffinity` to confirm the returned value is zero. Use the `mRemoteNG.UI.Forms` and `mRemoteNG.UI.CaptureProtection` namespaces in debugger expressions, and inspect the returned `bool` and error outputs. Test only a disposable development session; no production disable switch is provided. The blank success-status area is not an authoritative status after manually disposing its manager. Restart the application before trusting status or resuming normal use. For a floating form, repeat the set/read against that specific top-level form's HWND as well.

To test the recovery timer, leave the manager running and clear affinity once from that same UI-thread breakpoint. The next eligible timer check should set and verify `0x11`; this is a separate test from the deliberately unprotected capture comparison.

### Ordinary GDI/BitBlt capture

Open Windows PowerShell 5.1 on the same interactive desktop, keep the test RDP window visible, and save screenshots outside the repository:

```powershell
powershell.exe -NoProfile -File .\Tools\CaptureProtection\Capture-DesktopBitBlt.ps1 -OutputPath "$env:TEMP\rdp-protected-bitblt.png"
```

Then repeat with `CAPTUREBLT` enabled:

```powershell
powershell.exe -NoProfile -File .\Tools\CaptureProtection\Capture-DesktopBitBlt.ps1 -OutputPath "$env:TEMP\rdp-protected-bitblt-layered.png" -IncludeLayeredWindows
```

The [diagnostic script](../Tools/CaptureProtection/Capture-DesktopBitBlt.ps1) performs a desktop `GetDC`/`BitBlt` using `SRCCOPY`, optionally adding `CAPTUREBLT`, across the virtual desktop. It does not change affinity, inject code or require elevation. It refuses to overwrite existing files. Use fresh filenames for the unprotected control comparison and restore test. Inspect the PNGs manually; successful PNG creation alone does not establish exclusion. The script itself has not been executed on Windows in this development environment. See Microsoft's [BitBlt documentation](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt).

## Limitations and RDP security

Display affinity is a Windows capture policy, not DRM. It relies on Desktop Window Manager and capture implementations honoring the policy. It does not protect against physical cameras, injected code, administrator modifications, kernel drivers, hypervisors or unsupported capture paths. It does not make the client completely invisible.

When the client is visible, the local monitor still displays it. Task Manager, process/window enumeration, accessibility and diagnostic tools, network monitoring, Windows/RDP logs and the remote server can still observe it or its connection. No process concealment, monitoring evasion, code injection, elevation, persistence, automatic startup, kernel driver, anti-analysis behavior or interference with security tools is introduced.

Keep normal Microsoft RDP security behavior and existing mRemoteNG credential handling. Do not disable Network Level Authentication or certificate validation, hardcode credentials, log passwords/tokens, or commit personal connection files, screenshots containing private data or secrets. Use a VPN or RD Gateway for access outside the local network; do not expose TCP 3389 publicly without appropriate network protection.

## Validation record

### Windows validation on 2026-10-04

The current changes remove successful capture-status text from the main/floating custom frames, change F8 to raise/pin → hide → restore pinned, and add F9 for normal shutdown without confirmation. Shutdown during a required password prompt also stops connection-file recovery and the remaining startup work, preventing further dialogs after the main form closes.

Full Visual Studio MSBuild successfully built the application in **Release x64** and built the complete test project in an isolated Windows VM workspace. The selected regression run reported **218 passed, 0 failed and 2 skipped out of 220**, including a startup-password shutdown regression under the actual `Application.Run` message loop. The two monitor-dependent cases require multiple monitors and were skipped on this single-monitor VM. The full repository suite was not executed; the selection avoids tests that change the user's existing settings. The tested source archive's SHA-256 was `12E8B3D3C31813BA07EA8765A69C6A82C3DC4526873379EBDF339465DE7C0018`.

The **Release Self-Contained** build and publish passed. Windows packaging checks passed **33/33**, producing one **`Capture2Text.exe`**, **192,898,823 bytes**, with SHA-256 `3052D1A4C3951EBD9086D1B583666AB313BD5E9664C05FDA0EA737FD52C74666`. The application payload and launcher each reported File Description and Comments lengths of **0**. The source payload's file-hash inventory was unchanged after packaging.

The actual client passed **46/46 runtime checks**. Successive F8 presses raised/pinned the window, hid it, restored it pinned, and hid it again. The successful capture-status label was absent. Visible main-window affinity was **`0x11`** before hiding and after restoration. Windows returned `0` for the invisible HWND in this test; the manager skips invisible forms and reapplies verification when the window becomes visible. F9 while hidden exited with **code 0** and left no client HWNDs.

A separate required-password fixture displayed a visible, owned app password prompt with verified affinity **`0x11`**. Pressing only F9, without answering the prompt, exited with **code 0** and left no client HWNDs. Visual inspection of in-guest GDI/BitBlt images confirmed omission of the visible main window before hiding and after restoration, and of the visible password prompt; native visibility snapshots confirmed the tested windows were on screen when captured.

The standalone package passed **21/21 transfer-and-launch checks**. Only the EXE was copied into a fresh Windows folder containing spaces, and its SHA-256 matched the packaged artifact. Two shell launches opened the client with an empty native title, opaque tool-window styles and verified affinity **`0x11`**. F9 exited the first visible client with **code 0**. The second launch passed F8 raise/pin → hide → restore pinned → hide, with affinity **`0x11`** after restoration; F9 while hidden also exited with **code 0**. Neither a launcher nor client process remained. The second run used an empty connection-file fixture, without an RDP session. The verified EXE was copied to the test VM's shared folder at **`tested-release-20261004/Capture2Text.exe`**.

No live RDP session, native certificate prompt, detached RDP session or multi-monitor behavior was tested in this run. Snipping Tool, another screenshot application, physical double-click launch and a fresh receiving PC without build tools or a separately installed .NET runtime remain on the manual checklist. The October 1 results below are historical and describe the earlier F8 topmost toggle.

### Windows validation on 2026-10-01, commit `5256eb10`

Windows validation ran in an isolated x64 VM on Windows build **10.0.26200**, using Visual Studio **18.10**, .NET SDK **10.0.401**, .NET Desktop Runtime **10.0.12** and Windows SDK **10.0.26100.0**. Full Visual Studio MSBuild successfully built the application in **Release x64**, built the complete test project, and published **Release Self-Contained** to a fresh portable directory. The copied source files were verified against a hash manifest before building. The existing user checkout and client settings were preserved.

The final selected Windows regression run reported **200 passed, 0 failed and 2 skipped out of 202**. The complete repository suite was not executed because some existing Options tests save user settings; the run selected profile-safe regressions instead. The two owner-monitor layout cases were skipped because this VM has one monitor. A successful test-project build is separate from execution of the complete suite.

The actual application's main HWND was confirmed to have an empty native title, opaque tool-window styles, no native caption, and verified display affinity **`0x11`**. Ordinary GDI/BitBlt capture inside the guest omitted the visible protected application, confirmed by comparing the captured image with the live VM framebuffer. The framebuffer itself still showed the app, as expected for a hypervisor capture path. No RDP session was connected during this check.

A separate Windows harness linked the real capture manager and native adapter. With the **500 ms** timer, an intact flag produced two affinity reads and zero writes during the measured interval; clearing the flag produced one repair write. Handle recreation, restoration/visibility events and disposal also passed. The harness's GDI comparison showed its test window while protection was disabled and excluded it after protection was restored. These lifecycle and control-comparison results apply to the harness; they do not establish detached RDP-window behavior.

After startup completed, **F8 toggled the actual application's topmost state** through both the QEMU virtual keyboard and `SendInput`. Two successive automated presses changed it from false to true and back to false; verified capture affinity stayed **`0x11`** throughout. A separate diagnostic `WM_HOTKEY` message also exercised the dispatch path, independently of keyboard delivery. These checks used an empty session; ActiveX keyboard focus, held-key repeat behavior and physical-PC keyboard testing remain pending.

The actual application exited cleanly with **exit code 0** after a normal **`WM_CLOSE`** request, with no RDP session open. A foreground **Alt+F4** interaction through the QEMU keyboard also exited the client; no exit code was recorded for that observation.

Windows launcher extraction tests passed **33/33**. The actual Windows PowerShell 5.1 packager successfully extracted the client icon and produced exactly one **`Capture2Text.exe`**, **192,890,631 bytes** for the validated artifact. Windows file-version metadata reported File Description and Comments lengths of **0** for both the application payload and launcher. File hashes confirmed that packaging left the source payload unchanged.

The single EXE was copied alone into a fresh local folder containing a space in its name and launched from PowerShell. It extracted to a new payload-hash cache, started the client, and exited with code 0. The loaded client retained an empty title, opaque tool-window styles and affinity `0x11`; F8 switched topmost on and off while preserving affinity. A pointer click on its custom **X** closed the client, leaving neither launcher nor client running. These were empty-session checks; no remote-session disconnect was exercised. The process recorded **0 ms of CPU time over a 5,002 ms idle sample**, a short observation rather than a general performance guarantee. See the [packaging verification checklist](production-packaging.md#verify-before-distributing) for receiving-PC checks.

Still required: a real RDP connection with keyboard/mouse input, Snipping Tool and another screenshot application, actual taskbar/Alt+Tab inspection, close paths with active sessions, detached/floating RDP windows, multiple monitors, MSI installation/upgrade, physical double-click launch, a fresh receiving PC without build tools or a separately installed .NET runtime, and the remaining manual checklist above. Native Windows/RDP security prompts are not covered by the managed-dialog tests. Results on this VM do not establish that every Windows version or capture path respects the policy.

Earlier Linux checks passed 17 pure capture-policy cases, 17 hotkey scenarios, 84 dialog-mapping checks and 12 exception-routing scenarios in isolated harnesses. Windows-targeted source compilation also covered chrome, dialogs and shutdown. Linux full-application attempts stopped at Windows targeting/COM-reference requirements (`NETSDK1100`/`MSB4803`); these historical limitations were not successful builds and are distinct from the later successful Windows builds.

## Modified-file report

| Files | Change |
| --- | --- |
| [`UI/CaptureProtection`](../mRemoteNG/UI/CaptureProtection/) | Central UI-thread manager, lifecycle registration, native display-affinity declarations, ownership/top-level/opacity checks, verified status, repair policy and failure-log suppression. |
| [`ProtectedWindowChrome.cs`](../mRemoteNG/UI/Forms/ProtectedWindowChrome.cs), [`frmMain.cs`](../mRemoteNG/UI/Forms/frmMain.cs), [`frmMain.Designer.cs`](../mRemoteNG/UI/Forms/frmMain.Designer.cs) | Opaque custom frame, close/maximize/drag/resize controls, tool-window styles, status, normal shutdown integration and minimized-state recovery. |
| [`AlwaysOnTopManager.cs`](../mRemoteNG/UI/Forms/AlwaysOnTopManager.cs), [`AlwaysOnTopManagerTests.cs`](../mRemoteNGTests/UI/Forms/AlwaysOnTopManagerTests.cs) | UI-thread F8/F9 registration, shared main/floating topmost and visibility state, normal quit, failure reporting and shortcut cleanup. |
| [`ProtectedDialog.cs`](../mRemoteNG/UI/Forms/ProtectedDialog.cs), [`ProtectedMessageBox.cs`](../mRemoteNG/UI/Forms/ProtectedMessageBox.cs), [`GlobalUsings.cs`](../mRemoteNG/GlobalUsings.cs), [`UI/TaskDialog`](../mRemoteNG/UI/TaskDialog/), [`ProtectedMessageBoxTests.cs`](../mRemoteNGTests/UI/Forms/ProtectedMessageBoxTests.cs) | Register app-owned managed prompts before showing them, preserve decisions, keep long messages usable and dispose dialog protection timers. |
| [`ProgramRoot.cs`](../mRemoteNG/App/ProgramRoot.cs), [`MessageCollectorSetup.cs`](../mRemoteNG/App/Initialization/MessageCollectorSetup.cs), [`Shutdown.cs`](../mRemoteNG/App/Shutdown.cs), [`NotificationsPage.cs`](../mRemoteNG/UI/Forms/OptionsPages/NotificationsPage.cs) | Remove the splash and automatic notification popups; log unhandled failures and use normal shutdown cleanup. |
| [`Runtime.cs`](../mRemoteNG/App/Runtime.cs), [`DialogFactory.cs`](../mRemoteNG/UI/DialogFactory.cs), [`ProgramRootShutdownTests.cs`](../mRemoteNGTests/App/ProgramRootShutdownTests.cs) | Stop connection-file recovery and further startup dialogs once the main form closes, including during password entry. |
| [`ProtectedDialogLifetimeTests.cs`](../mRemoteNGTests/UI/Forms/ProtectedDialogLifetimeTests.cs), [`ProgramRootShutdownTests.cs`](../mRemoteNGTests/App/ProgramRootShutdownTests.cs) | Windows regressions for releasing dialog theme subscriptions and requesting thread exit when normal close does not finish. |
| [`FloatWindowNG.cs`](../mRemoteNG/UI/Tabs/FloatWindowNG.cs), [`InterfaceControl.cs`](../mRemoteNG/Connection/InterfaceControl.cs), [`ConnectionWindow.cs`](../mRemoteNG/UI/Window/ConnectionWindow.cs) | Register RDP-bearing main/floating hosts and reparented controls; reserve space for local floating-window chrome. |
| [`RdpProtocol.cs`](../mRemoteNG/Connection/Protocol/RDP/RdpProtocol.cs), [`RdpProtocol8.cs`](../mRemoteNG/Connection/Protocol/RDP/RdpProtocol8.cs), [`FullscreenHandler.cs`](../mRemoteNG/UI/FullscreenHandler.cs), [`ViewMenu.cs`](../mRemoteNG/UI/Menu/msMain/ViewMenu.cs), [`ConnectionContextMenu.cs`](../mRemoteNG/UI/Controls/ConnectionContextMenu.cs) | Replace ActiveX fullscreen/multi-monitor presentation with maximized protected hosts; retain content-area sizing and update command labels. |
| [`SettingsLoader.cs`](../mRemoteNG/Config/Settings/SettingsLoader.cs), [`SettingsSaver.cs`](../mRemoteNG/Config/Settings/SettingsSaver.cs), [`AppearancePage.cs`](../mRemoteNG/UI/Forms/OptionsPages/AppearancePage.cs), [`StartupExitPage.cs`](../mRemoteNG/UI/Forms/OptionsPages/StartupExitPage.cs) | Disable tray and unreachable-minimization paths and remove the shutdown opacity change. |
| [`mRemoteNG.csproj`](../mRemoteNG/mRemoteNG.csproj) | Declare the capture policy's minimum Windows platform version, build 19041. |
| [`CaptureProtectionPolicyTests.cs`](../mRemoteNGTests/UI/CaptureProtection/CaptureProtectionPolicyTests.cs), [`CaptureProtectionManagerTests.cs`](../mRemoteNGTests/UI/CaptureProtection/CaptureProtectionManagerTests.cs) | Fake-native tests for verification, recovery, no redundant timer writes, eligibility, error preservation, old Windows and log suppression; Windows STA lifecycle tests for registration, visibility, handle recreation and disposal. |
| [`README.md`](../README.md), this guide, [`Capture-DesktopBitBlt.ps1`](../Tools/CaptureProtection/Capture-DesktopBitBlt.ps1) | Build and operation guidance, limitations, pending Windows tests and a manual GDI capture diagnostic. |
