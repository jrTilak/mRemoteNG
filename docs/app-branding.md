# Application name, icon and Start menu visibility

Open **File → Options → Appearance** to change the application name, select a Windows `.ico` file, or enable **Hide this app's shortcut from my Start menu**. Save with **Apply** or **OK**. When a branding preference changes, the app offers to restart so the new preferences take effect. Declining keeps the current session running; the saved preferences apply on the next launch. Accepting restart uses the normal exit path and disconnects active sessions without a second exit confirmation. Declining the restart keeps the current process running.

Leave the name or icon empty to use its build default. The reset button restores the branding defaults. Names are limited to 80 characters and must be valid Windows shortcut filenames; surrounding whitespace is trimmed. Icon files must be valid, nonempty `.ico` files no larger than 4 MiB. Selected icons are copied into the settings directory's `Branding` subfolder, so moving the original icon later does not break the preference. The settings directory must be writable, including for portable installations.

The runtime name is used for the managed Start menu shortcut and application dialogs. The runtime icon is assigned to the window and managed shortcut, but the custom frame and document tabs draw no application-icon badge. These preferences do not rewrite the running executable or its version resources. Task Manager can show window titles, executable file descriptions, icons or process filenames in different views. The filename remains **mRemoteNG.exe**, and the process remains listed under its executable identity; an empty window title does not make every Task Manager label empty. Windows may cache shortcut icons and search results.

The protected main window and detached/floating RDP windows always have empty native window titles and empty custom title labels. This is the default behavior with no title-visibility setting. Capture-protection status, the dragging area and window buttons remain available, and connection tabs retain their text labels. There is no splash image or app-icon badge in the frame or document tabs. The configured application name remains available for Start menu shortcuts and dialogs; the build name still supplies executable metadata. Leaving the application-name field empty selects the build default (**Capture2Text**).

Loading a connections file, using a database, switching sessions or detaching a panel keeps the protected window titles empty. The former full-connections-path title option is hidden and its saved value has no effect on these titles. Changing the application name still takes effect after restarting, without adding a title to the protected windows.

## Start menu behavior

The toggle controls only shortcuts in the current user's **Programs\mRemoteNG** folder that point to this exact executable without launch arguments. Enabling visibility creates or updates the named shortcut; hiding removes matching shortcuts. Renaming removes the old matching entry after the replacement has been written. A shortcut for another executable is never overwritten or removed. If Windows refuses a shortcut operation, the app reports the problem.

The toggle does not remove other users' shortcuts, pinned Start entries, desktop shortcuts, shortcuts with custom launch arguments, manually copied shortcuts elsewhere, or old all-users entries from another installer. Those entries are managed separately in Windows. It does not affect process visibility, application startup, the taskbar or the protected frame. Keep the executable or an existing desktop shortcut accessible when hiding the Start entry.

New MSI builds place the initial Start shortcut in the installing user's roaming Start menu, using the build default below. The installer uses a fixed `mRemoteNG` folder and stable executable target, so normal runtime changes can manage its shortcut without elevation. The documentation shortcuts have been removed; their files remain in the installation directory. An upgrade removes the previous MSI inside the installation transaction before creating new shortcuts, so changing the build title does not leave its old installer-managed shortcut names behind. A failed upgrade can roll back the previous installation. The MSI's existing installation scope and requirements otherwise remain unchanged. System-account deployment and redirected Start menu folders require deployment-specific verification. Remove runtime-created custom shortcuts using the toggle before uninstalling; MSI tracks only the shortcut it originally installed, so renamed entries can otherwise remain after uninstall.

The installer supplies the profile-directory cleanup records required by Windows Installer's ICE64 validation. Those records remove empty directories only; they never recursively delete files, other applications' shortcuts or populated Windows profile folders.

## Build defaults without source-code edits

Edit the three values in [`Branding.props`](../Branding.props), then rebuild the app and, if used, the installer:

| Property | Default | Effect |
| --- | --- | --- |
| `BrandingDisplayName` | `Capture2Text` | Default runtime app name for dialogs and shortcuts, executable title/file description, installer display name and installer shortcut label. Protected window titles stay empty. |
| `BrandingIconPath` | `mRemoteNG/Icons/Capture2Text.ico` | Icon embedded in the executable and MSI. Runtime uses this icon unless the user selects another. |
| `BrandingShowInStartMenu` | `true` | Initial current-user Start shortcut visibility; set `false` to hide it by default. |

The icon path can be absolute or relative to the repository root. Supply an `.ico` containing 16, 32, 48 and 256 pixel images for useful Windows scaling. Build validation rejects invalid names, missing/non-ICO icon paths and invalid boolean values; the compiler validates the icon data. Use ordinary XML escaping in the file, such as `&amp;` for `&`.

This fork defaults to the Capture2Text name and the icon extracted from the supplied `Capture2Text.exe`. The checked-in icon preserves its original 16, 32, 48, 64 and 128 pixel images. The portable launcher's default title/icon also follow `Branding.props`; the packaging script takes its icon from the published client. Existing saved branding preferences still take priority: reset to build defaults and restart to use the new defaults.

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

The build generates title, description and runtime-default assembly metadata separately from the T4 version file, using MSBuild's [WriteCodeFragment task](https://learn.microsoft.com/en-us/visualstudio/msbuild/writecodefragment-task). The assembly name, executable filename, `AssemblyProduct`, settings identity, installation directory, registry roots and MSI upgrade code stay stable. Name/icon customization is not a separate side-by-side product identity. Existing license notices and upstream attribution remain intact.

## Verification

Windows runtime verification is still required. In addition to the [capture-protection checklist](capture-protection.md#windows-verification-checklist), check:

1. Save a different application name and valid icon, decline restart, and confirm the current window retains its empty title without an icon badge. Launch again and check the new icon on the named Start shortcut. Verify that both the native window title and custom title label stay empty for the main window and detached/floating RDP windows.
   Load another connections file, switch between sessions, detach/redock panels and recreate window handles. Confirm protected titles stay empty and active-connection tracking in the Connections tree still works when enabled. Check capture status, dragging, maximize/restore and close, and confirm Start menu and executable labels remain named.
2. Accept restart with active RDP sessions. Confirm there is no second exit prompt and that restart leaves exactly one client process with the saved settings applied. Also close the app with unsaved Appearance edits: application exit discards unapplied control edits without asking. Apply/OK still validates a name/icon before saving; a failed save keeps the Options page and sessions open.
3. Hide the Start shortcut, restart, and check current-user Programs. Show it again and check its label, target, working directory and icon. Repeat application-name changes and confirm old matching entries disappear.
4. Put a shortcut to an unrelated program in the managed folder, including one with the requested title. Confirm it is preserved and a naming conflict is reported.
5. Try an invalid name, invalid/corrupt icon, oversized icon and unwritable settings/shortcut directory. Check that errors are visible and the existing working shortcut is preserved. Remove the original selected icon and confirm the managed copy survives restart.
6. Reset to build defaults. Build with a custom name/icon and `BrandingShowInStartMenu=false`, then test a fresh profile. Check Explorer file properties, the executable icon, empty protected title labels, Start menu and Task Manager's different views. The executable name must remain `mRemoteNG.exe`, and the process must remain listed.
7. Test fresh MSI installation, upgrades from the previous installer, repair and uninstall. For an upgrade, increase the MSI product version and change the build title: verify both old installer-managed desktop/Start shortcut names are removed before the new names appear. Check both Start-menu defaults, old common shortcuts, custom runtime names, rollback after an interrupted upgrade, and Windows icon caching. Verify user preference is reapplied after repair/upgrade and launch. Run MSI validation, including ICE64, and confirm uninstall preserves unrelated entries in the profile-directory chain. Test any supported redirected-folder or deployment-account configuration explicitly.

Linux checks performed for this change: MSBuild branding metadata generation and validation succeeded; an isolated .NET harness compiled the generated metadata with the existing assembly-version file and verified default/custom title, file description, Start-menu metadata and stable product name. The installer project evaluated the branding properties successfully. A full MSI build restored dependencies but stopped because WiX's `MakeSfxCA.exe` requires Windows. These checks do not establish Windows shell, MSI, Task Manager, restart or RDP runtime behavior.

All 62 automated name-validation and shortcut-management tests passed on Linux, including unrelated-link preservation, collisions, write failures and rename ordering. Fifteen targeted MSBuild configuration scenarios passed. The runtime branding service, native shortcut adapter and updated chrome compiled against Windows Forms references in an isolated harness with application dependency stubs. A second harness compiled the Appearance controls, settings, restart code and five Windows UI tests; the UI tests have not been executed. The 17 existing capture-policy tests also passed. A full application build still requires Windows MSBuild for the RDP COM reference.

The Options document retains its save/discard/cancel flow when closed explicitly; application exit now deliberately skips it and discards unapplied edits. Six additional confirmation-decision tests passed on Linux using the exact extracted production method with only the WinForms `DialogResult` enum substituted; the same method and tests also compiled against real WinForms references. This verifies the save/discard/cancel decision logic, not the complete main-form shutdown event sequence, which still requires Windows testing.

## Modified files

| Files | Purpose |
| --- | --- |
| [`Branding.props`](../Branding.props), [`build/Branding.targets`](../build/Branding.targets) | Editable build defaults, validation and generated metadata. |
| [`mRemoteNG.csproj`](../mRemoteNG/mRemoteNG.csproj), [`AssemblyInfo.tt`](../mRemoteNG/Properties/AssemblyInfo.tt), [`AssemblyInfo.cs`](../mRemoteNG/Properties/AssemblyInfo.cs) | Embed the chosen icon and avoid conflicting T4 branding attributes. |
| [`App/Branding`](../mRemoteNG/App/Branding/) | Saved preferences, copied icon assets, per-user shell shortcuts and validation. |
| [`AppearancePage.Branding.cs`](../mRemoteNG/UI/Forms/OptionsPages/AppearancePage.Branding.cs) | Settings controls in the existing Appearance page. |
| [`frmOptions.cs`](../mRemoteNG/UI/Forms/frmOptions.cs), [`OptionsWindow.cs`](../mRemoteNG/UI/Window/OptionsWindow.cs), [`Shutdown.cs`](../mRemoteNG/App/Shutdown.cs), [`ProgramRoot.cs`](../mRemoteNG/App/ProgramRoot.cs) | Save validation and restart prompting; relaunch after accepted shutdown and mutex release. |
| [`ProtectedWindowChrome.cs`](../mRemoteNG/UI/Forms/ProtectedWindowChrome.cs), [`frmMain.cs`](../mRemoteNG/UI/Forms/frmMain.cs) | Keep protected native/custom titles empty and apply the launch's icon and current-user shortcut preference. |
| [`App/Branding tests`](../mRemoteNGTests/App/Branding/), [`ApplicationBrandingOptionsTests.cs`](../mRemoteNGTests/UI/Forms/OptionsPages/ApplicationBrandingOptionsTests.cs), [`OptionsCloseConfirmationTests.cs`](../mRemoteNGTests/UI/Forms/OptionsCloseConfirmationTests.cs) | Name/shortcut policy tests, Windows Appearance-page checks and save/discard/cancel decisions. |
| [`mRemoteNGInstaller/Installer`](../mRemoteNGInstaller/Installer/) | Shared build defaults, branded package/icon/shortcut and per-user Start menu integration. |
