# Production build and one-EXE packaging

This guide builds an optimized **Release Self-Contained** Windows client and packages it as **`mRemoteNG-Portable.exe`**. The recipient can double-click that file or launch it from a terminal; it extracts the application's supporting files into a persistent user folder and opens the client. There is no separate extraction step for the recipient.

The EXE is one file to transfer, but the application still uses files on disk when running. The launcher preserves the existing RDP ActiveX integration and folder layout. It does not change the RDP engine or capture-protection policy.

Use each command block separately, in order. Linux commands belong on the host; PowerShell commands belong inside Windows. Stop if a command fails.

## Requirements

- A **Windows 11 x64, version 24H2 or newer** build environment, either a physical PC or a full virtual machine.
- The [Windows build requirements](capture-protection.md#requirements): Visual Studio 2026 with .NET desktop development and full MSBuild, .NET 10 SDK, Windows SDK 10.0.26100.0, Git, and Windows' registered RDP ActiveX component.
- Internet access for the initial SDK/NuGet restore and sufficient disk space for the self-contained application, temporary packaging files, and final EXE.

Recipients need compatible Windows x64 and Windows' RDP components, plus any native prerequisites used by their selected protocols. The package includes .NET; Visual Studio, the .NET SDK, Docker, and an archive utility are not needed on the receiving PC. A self-contained deployment does not bundle Windows or every native prerequisite. See Microsoft's [deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/).

These commands target **x64**, also called **x86_64** or **amd64**, including Intel processors. ARM64 is a separate target; do not select it merely because Docker uses the name `amd64` for an Intel machine.

## Optional: build in a Windows VM through Docker on Linux

Skip this section if you already have a Windows build machine. A regular Linux container cannot provide this application's Windows COM build environment. [Dockur Windows](https://github.com/dockur/windows) runs a full Windows VM through QEMU/KVM inside Docker, where the Windows build tools can be installed.

Check the Linux host's architecture. `x86_64` is suitable for the Windows x64 guest used here:

```bash
uname -m
```

Check Docker:

```bash
docker info --format 'OS={{.OSType}} architecture={{.Architecture}}'
```

Check that the KVM device exists and is accessible to Docker:

```bash
ls -l /dev/kvm
```

Allow at least 8 GB of RAM for the VM and enough host disk space for a 96 GB virtual disk and build output. These are suggested development allocations, not the application's runtime requirements.

Create a VM directory outside the source checkout:

```bash
mkdir -p "$HOME/rdp-build-vm/shared"
```

Enter that directory:

```bash
cd "$HOME/rdp-build-vm"
```

Save the following configuration as `compose.yaml` in that directory:

```yaml
services:
  windows:
    image: dockurr/windows
    environment:
      VERSION: "11"
      RAM_SIZE: "8G"
      CPU_CORES: "4"
      DISK_SIZE: "96G"
    devices:
      - /dev/kvm
      - /dev/net/tun
    cap_add:
      - NET_ADMIN
    ports:
      - "127.0.0.1:8006:8006"
    volumes:
      - ./storage:/storage
      - ./shared:/shared
    stop_grace_period: 2m
```

Start the VM:

```bash
docker compose up -d
```

Open **http://localhost:8006** in the Linux host's browser, wait for Windows installation, and install the Windows build requirements inside the guest. Keep `storage` to retain Windows and its installed tools. The `shared` folder is exposed inside Windows as the desktop's **Shared** folder and drive **Z:**. Build from a checkout on the guest's Windows disk, then copy the finished package to that shared drive.

When finished, shut down Windows from its Start menu, then stop the container:

```bash
docker compose stop
```

The browser console is bound to the local host. This example does not publish an RDP port. Capture-protection testing must happen inside the Windows guest; the guest's display affinity cannot prevent Linux from capturing the VM's displayed framebuffer.

## Prepare and test the Windows checkout

Open **Developer PowerShell for Visual Studio 2026** inside Windows. Follow [Build and launch](capture-protection.md#build-and-launch) to clone the feature branch, verify full MSBuild, install `dotnet-t4`, resolve its DLL reference to an absolute path, and generate `AssemblyInfo.cs`. That guide now uses ordinary **Release**, not Debug. Its ordinary Release build requires the separately installed .NET Desktop Runtime when distributed.

For this one-EXE package, continue below with **Release Self-Contained** after completing the [Release test commands](capture-protection.md#run-the-tests). Run tests before the final self-contained restore/build so a different configuration's restore does not replace the application assets needed for publishing.

Stay in the repository root, containing `mRemoteNG.sln`. Set build-time name, icon, and Start menu defaults in [`Branding.props`](../Branding.props) before building; see [app branding](app-branding.md). A Release configuration enables optimization; it does not establish that Windows/RDP behavior has passed testing.

## Build the self-contained Release application

Create a unique path for this build. Nothing below reuses a folder from a previously run client:

```powershell
$rdpPackageRoot = Join-Path (Get-Location).Path ("mRemoteNG\bin\x64\Packages\" + [Guid]::NewGuid().ToString('N'))
```

Choose the publish directory. Leave this directory nonexistent until the build creates it:

```powershell
$rdpPublish = Join-Path $rdpPackageRoot 'payload'
```

Restore dependencies for the exact self-contained configuration:

```powershell
dotnet restore .\mRemoteNG\mRemoteNG.csproj "-p:Configuration=Release Self-Contained" -p:Platform=x64 -p:SelfContained=true -p:RuntimeIdentifier=win-x64 -p:PublishReadyToRun=true
```

Build using full Visual Studio MSBuild. Keep the trailing `/` in the publish-directory argument:

```powershell
MSBuild.exe .\mRemoteNG\mRemoteNG.csproj "/p:Configuration=Release Self-Contained" /p:Platform=x64 /p:SelfContained=true /p:RuntimeIdentifier=win-x64 "/p:PublishDir=$rdpPublish/" /p:MSBuildEnableWorkloadResolver=false /verbosity:minimal
```

This configuration already publishes after building. Do not run a separate `dotnet publish` for the main application; its COM reference needs full Windows MSBuild. The project deletes intermediate build output after publishing, so use **`$rdpPublish`**, not the temporary `Release Self-Contained` build directory.

Confirm that the published executable exists:

```powershell
Get-Item -LiteralPath (Join-Path $rdpPublish 'mRemoteNG.exe')
```

Keep the entire publish folder intact, including DLLs, runtime files, `Assemblies`, language resources, schemas, themes, and helper executables. **Package this fresh output before launching it or entering credentials.** The self-contained configuration uses portable settings; packaging a folder you have already used can include personal connections, credentials, logs, or settings.

## Package the directly launchable EXE

The [packaging script](../Tools/Packaging/Pack-PortableExe.ps1) embeds the complete publish folder in a small [launcher](../Tools/Packaging/Launcher/Launcher.csproj). It adds the repository's license and credits, copies the client's executable icon, and publishes the launcher in Release as a self-contained single file. This step uses the .NET SDK because the launcher itself has no COM reference.

Run the packager from the repository root in the same PowerShell session:

```powershell
.\Tools\Packaging\Pack-PortableExe.ps1 -PublishDirectory $rdpPublish -OutputDirectory (Join-Path $rdpPackageRoot 'single-exe') -Runtime win-x64
```

Locate the result:

```powershell
$rdpPackageExe = Join-Path $rdpPackageRoot 'single-exe\mRemoteNG-Portable.exe'
```

Inspect the finished file:

```powershell
Get-Item -LiteralPath $rdpPackageExe
```

Record a checksum for the exact file being transferred:

```powershell
Get-FileHash -LiteralPath $rdpPackageExe -Algorithm SHA256
```

For a Dockur VM, copy the EXE to the host's shared folder:

```powershell
Copy-Item -LiteralPath $rdpPackageExe -Destination Z:\
```

The host receives it at `~/rdp-build-vm/shared/mRemoteNG-Portable.exe`. Otherwise, transfer the EXE using your normal file-transfer method. No manual ZIP extraction is needed on the receiving PC.

The packaging command refuses an existing output directory. For another build, start with a new `$rdpPackageRoot`; do not publish over an app folder containing user data. The script packages an existing self-contained application build; it does not compile or validate the main application for you.

## Launch, settings, and updates

In Windows Explorer, **double-click `mRemoteNG-Portable.exe`**. To launch the build you just created from PowerShell:

```powershell
& $rdpPackageExe
```

On a receiving PC, open PowerShell in the folder containing the transferred EXE:

```powershell
.\mRemoteNG-Portable.exe
```

From Command Prompt in that folder:

```bat
mRemoteNG-Portable.exe
```

The launcher extracts once to **`%LOCALAPPDATA%\mRemoteNG\Packaged\<payload-hash>\app`**, starts `mRemoteNG.exe`, and exits. Subsequent launches of the same package reuse that directory, preserving portable settings and connections. The application still closes through its normal custom **X** or Windows close path; there is no launcher background service. The actual application remains visible in Task Manager.

Command-line arguments are forwarded to the client. Use absolute paths for connection/configuration file arguments: the client's working directory is the extracted application folder, not the terminal's original directory.

The first launch takes longer and needs disk space for the extracted application. The launcher's own bundled native .NET files can also be extracted to .NET's normal cache. See Microsoft's [single-file deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#native-libraries).

Moving or renaming the transferred EXE does not change its payload hash. A package with changed contents gets a different directory and therefore a separate set of portable settings. Before replacing a package, export or back up your connections/settings using the existing application facilities; restore them in the new version. Old package directories are retained, and deleting one also deletes the portable data it contains. Existing Start menu shortcuts can continue to point at an older extracted version until updated through the app's branding settings.

For connection instructions, see [Connect to a remote RDP desktop](capture-protection.md#connect-to-a-remote-rdp-desktop). Packaging does not bundle remote credentials, enable a remote server, or change network access requirements.

## Verify before distributing

The Windows application build and runtime checks remain pending in this Linux development environment. A successful launcher cross-build or archive check is not verification of Windows launch, ActiveX, or capture exclusion.

Run the launcher's extraction checks from the repository root with .NET 10, on Windows or Linux:

```powershell
dotnet run --project .\Tools\Packaging\Launcher\Tests\Launcher.Tests.csproj --configuration Release
```

On Linux, use forward slashes for that project path:

```bash
dotnet run --project ./Tools/Packaging/Launcher/Tests/Launcher.Tests.csproj --configuration Release
```

These checks cover extraction, reuse without overwriting settings, concurrent first launches, incomplete caches, and unsafe archive paths. They do not launch the Windows executable.

1. On a separate compatible Windows x64 PC or clean VM, transfer only the final EXE and confirm it opens the client by double-click and from both PowerShell and Command Prompt.
2. Confirm the client starts without a separately installed .NET runtime, subject to its remaining Windows/native prerequisites. Check the icon and confirm that the main and floating window titles are blank.
3. Close and reopen the same package; settings must persist. Try two launches close together during first extraction, and a path containing spaces.
4. Exercise the [Windows verification checklist](capture-protection.md#windows-verification-checklist), including a real RDP connection, screenshot comparisons, keyboard/mouse input, and every close path.
5. Confirm the launcher exits after starting the client and that closing the client leaves neither application nor launcher running.
6. Verify unavailable storage and a missing extraction marker or `mRemoteNG.exe` produce a visible launcher failure. Reused folders are not checked file-by-file; missing or modified dependency files still need normal application troubleshooting. Test a changed package separately, including the documented settings migration.

Linux validation for this change: **34 extraction checks passed**. Cross-publishing the launcher for Windows x64 with a synthetic payload produced exactly one EXE, both with normal build paths and with the packager's isolated build paths. Its manifest requests `asInvoker`. PowerShell 7 parsed the packaging script successfully, and **28 input-rejection checks passed** without publishing or executing a payload. Actual Windows PowerShell 5.1 packaging, icon extraction, Windows launch, and the full application's Windows/RDP checks remain pending.

These checks cannot establish RDP capture protection. Do not describe this build as Windows-verified until the Windows checks have actually passed.
