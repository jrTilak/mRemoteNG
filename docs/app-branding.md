# Blank titles, executable name, icon and Start menu visibility

Open **File → Options → Appearance** to change the Start menu shortcut label, select a Windows `.ico` file, or enable **Hide this app's shortcut from my Start menu**. Save with **Apply** or **OK**. When a branding preference changes, the app offers to restart so the new preferences take effect. Declining keeps the current session running; the saved preferences apply on the next launch. Accepting restart uses the normal exit path and disconnects active sessions without a second exit confirmation. Declining the restart keeps the current process running.

Leave the shortcut label or icon empty to use its build default. The reset button restores the branding defaults. Shortcut labels are limited to 80 characters and must be valid Windows shortcut filenames; surrounding whitespace is trimmed. Icon files must be valid, nonempty `.ico` files no larger than 4 MiB. Selected icons are copied into the settings directory's `Branding` subfolder, so moving the original icon later does not break the preference. The settings directory must be writable, including for portable installations.

The main and floating RDP windows have empty native titles and empty custom title labels. App-owned managed prompts also clear their captions before presentation and after language changes; necessary question text and choices remain inside the dialog. A capture-protection failure can still appear in a generic dialog's caption so the failure is visible. The frame and document tabs draw no app-icon badge, and the startup splash is removed.

The application executable and the transferable launcher are named **`Capture2Text.exe`**. The build emits empty title and description attributes for both executables; changing the shortcut label does not restore a descriptive executable name. The actual process still appears in Task Manager. Its Details view can show the filename, and other views can fall back to it: **blank descriptions do not guarantee a blank Task Manager entry**. Spaces or invisible characters are not used as filenames.

The `BrandingDisplayName` build value and saved name preference now describe the Start menu shortcut label. They do not change window captions, executable descriptions or the process filename. The icon is assigned to the executable/window and managed shortcut. The compiled managed assembly remains `mRemoteNG.dll`, the internal product/settings identity remains stable, and portable XML settings remain `mRemoteNG.settings`. Existing product/legal metadata and upstream attribution are retained; this is not removal of every internal product identifier.

Loading a connections file, using a database, switching sessions or detaching a panel keeps the protected window titles empty. The former full-connections-path title option is hidden and its saved value has no effect on these titles. Saved icon and shortcut preferences apply after restarting. A new build and a new package are required to change an existing EXE's filename or compiled description.

Close the previous client before launching the renamed build. With single-instance mode enabled, an already running `mRemoteNG.exe` holds the same application mutex, so a second launch under the new filename can exit without bringing the old window forward.

## Start menu behavior

The toggle controls shortcuts in the current user's **Programs\mRemoteNG** folder that point to this installation without launch arguments. It recognizes both `Capture2Text.exe` and the previous `mRemoteNG.exe` in that same directory, so upgrading in place updates the existing shortcut. Enabling visibility creates or updates the named shortcut; hiding removes matching shortcuts. Changing the shortcut label removes the old matching entry after the replacement has been written. Unrelated targets and other installation directories are preserved. If Windows refuses a shortcut operation, the app reports the problem.

The toggle does not remove other users' shortcuts, pinned Start entries, desktop shortcuts, shortcuts with custom launch arguments, manually copied shortcuts elsewhere, or old all-users entries from another installer. Those entries are managed separately in Windows. It does not affect process visibility, application startup, the taskbar or the protected frame. Keep the executable or an existing desktop shortcut accessible when hiding the Start entry.

New MSI builds place the initial Start shortcut in the installing user's roaming Start menu, using the build default below. The installer uses a fixed `mRemoteNG` folder and stable executable target, so normal runtime changes can manage its shortcut without elevation. The documentation shortcuts have been removed; their files remain in the installation directory. An upgrade removes the previous MSI inside the installation transaction before creating new shortcuts, so changing the build title does not leave its old installer-managed shortcut names behind. A failed upgrade can roll back the previous installation. The MSI's existing installation scope and requirements otherwise remain unchanged. System-account deployment and redirected Start menu folders require deployment-specific verification. Remove runtime-created custom shortcuts using the toggle before uninstalling; MSI tracks only the shortcut it originally installed, so renamed entries can otherwise remain after uninstall.

The installer supplies the profile-directory cleanup records required by Windows Installer's ICE64 validation. Those records remove empty directories only; they never recursively delete files, other applications' shortcuts or populated Windows profile folders.

## Build defaults without source-code edits

Edit the three values in [`Branding.props`](../Branding.props), then rebuild the app and, if used, the installer:

| Property | Default | Effect |
| --- | --- | --- |
| `BrandingDisplayName` | `Capture2Text` | Default Start menu shortcut label, installer display name and installer shortcut label. Window titles and executable descriptions stay blank. |
| `BrandingIconPath` | `mRemoteNG/Icons/Capture2Text.ico` | Icon embedded in the executable and MSI. Runtime uses this icon unless the user selects another. |
| `BrandingShowInStartMenu` | `true` | Initial current-user Start shortcut visibility; set `false` to hide it by default. |

The icon path can be absolute or relative to the repository root. Supply an `.ico` containing 16, 32, 48 and 256 pixel images for useful Windows scaling. Build validation rejects invalid names, missing/non-ICO icon paths and invalid boolean values; the compiler validates the icon data. Use ordinary XML escaping in the file, such as `&amp;` for `&`.

This fork defaults to the Capture2Text shortcut label and the icon extracted from the supplied `Capture2Text.exe`. The checked-in icon preserves its original 16, 32, 48, 64 and 128 pixel images. The portable launcher's icon also follows `Branding.props`; the packaging script takes its icon from the published client. Both executable descriptions stay blank. Existing saved branding preferences still take priority: reset to build defaults and restart to use the new defaults.

From a Windows Visual Studio Developer PowerShell with the repository's .NET/Windows/COM build requirements installed:

```powershell
MSBuild.exe .\mRemoteNG\mRemoteNG.csproj -restore -t:Build -p:Configuration=Release -p:Platform=x64
```

If building the MSI as well, run its build separately:

```powershell
MSBuild.exe .\mRemoteNGInstaller\Installer\Installer.wixproj -restore -t:Build -p:Configuration=Release -p:Platform=x64
```

For a one-off build, the same properties can be passed on the command line instead of editing the file. Use the same overrides for both app and installer builds:

```powershell
MSBuild.exe .\mRemoteNG\mRemoteNG.csproj -restore -t:Build -p:Configuration=Release -p:Platform=x64 '-p:BrandingDisplayName=Team Remote Desktop' '-p:BrandingIconPath=C:\Branding\team.ico' -p:BrandingShowInStartMenu=false
```

Saved user overrides take priority over runtime build defaults. Reset branding preferences and restart when checking a new build's defaults. An installer build does not rebuild the main app; build the app with the same configuration first. For the repository's complete build requirements and test commands, see [the capture-protection build guide](capture-protection.md).

For an optimized self-contained build packaged as one directly launchable EXE, use the [production packaging guide](production-packaging.md). Set the branding defaults before building the application payload.

The build generates empty title/description attributes and shortcut-default assembly metadata separately from the T4 version file, using MSBuild's [WriteCodeFragment task](https://learn.microsoft.com/en-us/visualstudio/msbuild/writecodefragment-task). The managed assembly name, `AssemblyProduct`, settings identity, installation directory, registry roots and MSI upgrade code stay stable. Build/publish outputs use `Capture2Text.exe` as the apphost filename. Name/icon customization is not a separate side-by-side product identity. Existing license notices and upstream attribution remain intact.

## Verification

The application and portable output have been built on Windows, and an actual main window was verified to have an empty native title. Windows packaging and icon extraction passed; both the payload and launcher reported File Description and Comments lengths of **0**. The final selected Windows regression run passed **200 tests**, with **2 monitor-dependent skips** and **no failures**. The single EXE also launched after being copied alone to a fresh local folder. Its loaded client kept the empty title and tool-window style, F8 toggled topmost while affinity remained `0x11`, and clicking custom **X** exited without a remaining launcher or client process. These checks opened no RDP session. See the [Windows validation record](capture-protection.md#validation-record) for the exact test results and unresolved checks. Shortcut changes, restart behavior, floating RDP windows and MSI operations still need the following manual verification:

1. Save a different shortcut label and valid icon, decline restart, and confirm the current window retains its empty title without an icon badge. Launch again and check the new icon on the named Start shortcut. Verify that both the native window title and custom title label stay empty for the main window and detached/floating RDP windows.
   Load another connections file, switch between sessions, detach/redock panels and recreate window handles. Confirm protected titles stay empty and active-connection tracking in the Connections tree still works when enabled. Check capture status, dragging, maximize/restore and close, and confirm Start menu and executable labels remain named.
2. Accept restart with active RDP sessions. Confirm there is no second exit prompt and that restart leaves exactly one client process with the saved settings applied. Also close the app with unsaved Appearance edits: application exit discards unapplied control edits without asking. Apply/OK still validates a name/icon before saving; a failed save keeps the Options page and sessions open.
3. Hide the Start shortcut, restart, and check current-user Programs. Show it again and check its label, target, working directory and icon. Repeat shortcut-label changes and confirm old matching entries disappear. Upgrade an existing installation in place: its shortcut must move from `mRemoteNG.exe` to `Capture2Text.exe`, and its existing settings must load. Preserve a shortcut pointing to `mRemoteNG.exe` in another directory.
4. Put a shortcut to an unrelated program in the managed folder, including one with the requested title. Confirm it is preserved and a naming conflict is reported.
5. Try an invalid name, invalid/corrupt icon, oversized icon and unwritable settings/shortcut directory. Check that errors are visible and the existing working shortcut is preserved. Remove the original selected icon and confirm the managed copy survives restart.
6. Reset to build defaults. Build with a custom shortcut label/icon and `BrandingShowInStartMenu=false`, then test a fresh profile. Check Explorer file properties, the executable icon, empty protected title labels, Start menu and Task Manager's different views. The executable name must be `Capture2Text.exe`, and the process must remain listed. Verify blank File Description/Comments independently from the filename; Task Manager can still display that filename.
7. Test fresh MSI installation, upgrades from the previous installer, repair and uninstall. For an upgrade, increase the MSI product version and change the build title: verify both old installer-managed desktop/Start shortcut names are removed before the new names appear. Check both Start-menu defaults, old common shortcuts, custom runtime names, rollback after an interrupted upgrade, and Windows icon caching. Verify user preference is reapplied after repair/upgrade and launch. Run MSI validation, including ICE64, and confirm uninstall preserves unrelated entries in the profile-directory chain. Test any supported redirected-folder or deployment-account configuration explicitly.

Linux checks for the blank-display/EXE rename: the actual application's branding metadata target succeeded, and the installer branding properties evaluated successfully. A small SDK project using the shared build targets verified framework-dependent, self-contained and no-build publish paths, the renamed run command, stable managed DLL/dependency/runtime-config filenames and cleanup of only previously tracked outputs. PE resources in that fixture and the real portable launcher contained empty File Description and Comments values, without whitespace characters. The real launcher cross-published for Windows x64 and ARM64 as one `Capture2Text.exe`; an x64 no-build/no-restore republish also passed. This is build-output verification, not Windows execution.

All 66 automated name-validation and shortcut-management tests passed on Linux, including same-directory legacy-target migration, unrelated-link preservation, collisions, write failures and rename ordering. The 34 launcher extraction checks passed. The earlier protected-dialog source check compiled 29 Windows cases against WinForms references with surrounding application dependencies stubbed. Later Windows execution is recorded separately in the linked validation record.

Earlier validation covered 15 targeted MSBuild configuration scenarios, compilation of the runtime branding service/native shortcut adapter/chrome and Appearance/restart controls, five Windows Appearance tests, and 17 passing capture-policy tests. A full MSI build previously stopped because WiX's `MakeSfxCA.exe` requires Windows. The full application has since built successfully with Windows MSBuild, including its COM reference. MSI builds and installation, Windows shell/Task Manager labels, branding restart and real RDP behavior remain unverified. Actual main-window title and GDI capture results are recorded in the Windows validation record.

The Options document retains its save/discard/cancel flow when closed explicitly; application exit now deliberately skips it and discards unapplied edits. Six additional confirmation-decision tests passed on Linux using the exact extracted production method with only the WinForms `DialogResult` enum substituted; the same method and tests also compiled against real WinForms references. These checks verify the save/discard/cancel decision logic. Actual empty-session shutdown through `WM_CLOSE` has since passed on Windows with exit code 0; unsaved Options, active-session shutdown and restart scenarios still require their manual checks.

## Modified files

| Files | Purpose |
| --- | --- |
| [`Branding.props`](../Branding.props), [`build/Branding.targets`](../build/Branding.targets) | Editable build defaults, validation and generated metadata. |
| [`build/BrandedAppHost.targets`](../build/BrandedAppHost.targets), [`Tools/Packaging`](../Tools/Packaging/) | Name the apphost and finished portable bundle `Capture2Text.exe` while preserving managed assembly names. |
| [`mRemoteNG.csproj`](../mRemoteNG/mRemoteNG.csproj), [`AssemblyInfo.tt`](../mRemoteNG/Properties/AssemblyInfo.tt), [`AssemblyInfo.cs`](../mRemoteNG/Properties/AssemblyInfo.cs) | Embed the chosen icon and avoid conflicting T4 branding attributes. |
| [`App/Branding`](../mRemoteNG/App/Branding/) | Saved preferences, copied icon assets, per-user shell shortcuts and validation. |
| [`PortableSettingsProvider.cs`](../mRemoteNG/Config/Settings/Providers/PortableSettingsProvider.cs) | Keep the existing `mRemoteNG.settings` filename after renaming the executable. |
| [`AppearancePage.Branding.cs`](../mRemoteNG/UI/Forms/OptionsPages/AppearancePage.Branding.cs) | Settings controls in the existing Appearance page. |
| [`frmOptions.cs`](../mRemoteNG/UI/Forms/frmOptions.cs), [`OptionsWindow.cs`](../mRemoteNG/UI/Window/OptionsWindow.cs), [`Shutdown.cs`](../mRemoteNG/App/Shutdown.cs), [`ProgramRoot.cs`](../mRemoteNG/App/ProgramRoot.cs) | Save validation and restart prompting; relaunch after accepted shutdown and mutex release. |
| [`ProtectedWindowChrome.cs`](../mRemoteNG/UI/Forms/ProtectedWindowChrome.cs), [`frmMain.cs`](../mRemoteNG/UI/Forms/frmMain.cs) | Keep protected native/custom titles empty and apply the launch's icon and current-user shortcut preference. |
| [`ProtectedDialog.cs`](../mRemoteNG/UI/Forms/ProtectedDialog.cs), [`ProtectedMessageBoxTests.cs`](../mRemoteNGTests/UI/Forms/ProtectedMessageBoxTests.cs) | Clear managed prompt captions while preserving required decisions and capture-failure status. |
| [`App/Branding tests`](../mRemoteNGTests/App/Branding/), [`ApplicationBrandingOptionsTests.cs`](../mRemoteNGTests/UI/Forms/OptionsPages/ApplicationBrandingOptionsTests.cs), [`OptionsCloseConfirmationTests.cs`](../mRemoteNGTests/UI/Forms/OptionsCloseConfirmationTests.cs) | Name/shortcut policy tests, Windows Appearance-page checks and save/discard/cancel decisions. |
| [`mRemoteNGInstaller/Installer`](../mRemoteNGInstaller/Installer/) | Shared build defaults, branded package/icon/shortcut and per-user Start menu integration. |
