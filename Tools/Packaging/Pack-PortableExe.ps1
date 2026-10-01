#requires -Version 5.1
<#
.SYNOPSIS
Packages an already-built Release Self-Contained application into one launcher EXE.
.DESCRIPTION
Run on Windows with the .NET 10 SDK. Supply a clean publish directory that has
never been used to run the client. The complete tree is copied to temporary
staging; the source directory is not modified and its executable is never run.
The output directory must not already exist. The resulting EXE opens the client
using the bundled launcher; no separate installer or extraction command is needed.
.EXAMPLE
.\Tools\Packaging\Pack-PortableExe.ps1 -PublishDirectory '.\mRemoteNG\bin\x64\Publish Self-Contained' -OutputDirectory '.\artifacts\portable-x64'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PublishDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'Packaging requires Windows and the .NET 10 SDK; it does not build the application itself.'
}

function Test-WithinDirectory([string] $Candidate, [string] $Directory) {
    $prefix = $Directory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    return $Candidate.Equals($Directory, [StringComparison]::OrdinalIgnoreCase) -or
        $Candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoLinkedAncestors([string] $Path) {
    $current = $Path
    while (-not [string]::IsNullOrEmpty($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Symbolic links, junctions and other reparse points are not accepted: $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Get-PeMachine([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) {
            throw "Not a Windows PE executable: $Path"
        }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 64 -or $peOffset -gt ($stream.Length - 6)) {
            throw "Invalid Windows PE header: $Path"
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "Invalid Windows PE signature: $Path"
        }
        return $reader.ReadUInt16()
    }
    finally {
        $reader.Dispose()
    }
}

function ConvertTo-MSBuildValue([string] $Value) {
    # Property-list separators and MSBuild expression characters in Windows paths
    # must remain literal, even though PowerShell already passes each argument whole.
    return $Value.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C').Replace('$', '%24').Replace('@', '%40').Replace("'", '%27').Replace('(', '%28').Replace(')', '%29')
}

$sourcePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishDirectory)
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$repositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$launcherProject = Join-Path $PSScriptRoot 'Launcher\Launcher.csproj'
if (-not [IO.Directory]::Exists($sourcePath)) {
    throw "Publish directory does not exist: $sourcePath"
}
if (Test-Path -LiteralPath $outputPath) {
    throw "OutputDirectory must be a new directory, not an existing file or folder: $outputPath"
}
if (Test-WithinDirectory $outputPath $sourcePath) {
    throw 'OutputDirectory must be outside PublishDirectory so the source remains unchanged.'
}
Assert-NoLinkedAncestors $sourcePath
Assert-NoLinkedAncestors $outputPath
if (-not [IO.File]::Exists($launcherProject)) {
    throw "Launcher project is missing: $launcherProject"
}
$dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop).Source

# Walk one directory at a time. Do not recurse into reparse points while checking.
$directories = [Collections.Generic.Queue[string]]::new()
$directories.Enqueue($sourcePath)
$sourceFiles = [Collections.Generic.List[string]]::new()
$sourceDirectories = [Collections.Generic.List[string]]::new()
while ($directories.Count -gt 0) {
    $directory = $directories.Dequeue()
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Publish tree contains a symbolic link, junction or other reparse point: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            $sourceDirectories.Add($item.FullName)
            $directories.Enqueue($item.FullName)
        }
        else {
            # A previously launched portable folder can contain personal connection
            # and credential data. Never silently include or discard those files.
            if ($item.Name -like 'confCons*' -or $item.Name -like 'confCreds*' -or
                $item.Name -eq 'user.config' -or $item.Extension -eq '.rdp' -or
                $item.Name -like 'mRemoteNG.log*' -or $item.Name -like 'mremoteng.settings*' -or
                $item.Name -eq 'mremoteng.keystore.json' -or $item.Name -eq 'mremoteng.dpapi.bin' -or
                $item.Name -eq 'extApps.xml' -or $item.Name -eq 'LocalConnectionProperties.xml' -or
                $item.Name -eq 'pnlLayout.xml') {
                throw "Publish tree contains runtime/user data. Supply a clean, never-launched publish folder: $($item.FullName)"
            }
            $sourceFiles.Add($item.FullName)
        }
    }
}

foreach ($fileName in @('Capture2Text.exe', 'mRemoteNG.dll', 'mRemoteNG.deps.json', 'mRemoteNG.runtimeconfig.json',
        'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll')) {
    if (-not [IO.File]::Exists((Join-Path $sourcePath $fileName))) {
        throw "The self-contained publish directory is missing $fileName. Publish Release Self-Contained first."
    }
}

$runtimeConfig = Get-Content -LiteralPath (Join-Path $sourcePath 'mRemoteNG.runtimeconfig.json') -Raw | ConvertFrom-Json
$optionsProperty = $runtimeConfig.PSObject.Properties['runtimeOptions']
if ($null -eq $optionsProperty -or $null -eq $optionsProperty.Value) {
    throw 'mRemoteNG.runtimeconfig.json has no runtimeOptions.'
}
$runtimeOptions = $optionsProperty.Value
if ($null -ne $runtimeOptions.PSObject.Properties['framework'] -or
    $null -ne $runtimeOptions.PSObject.Properties['frameworks']) {
    throw 'The input requires an installed .NET framework. Supply Release Self-Contained output, not regular Release.'
}
$includedProperty = $runtimeOptions.PSObject.Properties['includedFrameworks']
if ($null -eq $includedProperty) {
    throw 'The runtime configuration does not identify bundled .NET frameworks.'
}
$includedNames = @($includedProperty.Value | ForEach-Object { $_.name })
if ('Microsoft.NETCore.App' -notin $includedNames -or 'Microsoft.WindowsDesktop.App' -notin $includedNames) {
    throw 'The publish output must bundle both .NET and Windows Desktop runtimes.'
}

$expectedMachine = if ($Runtime -eq 'win-x64') { 0x8664 } else { 0xAA64 }
foreach ($fileName in @('Capture2Text.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
    $machine = Get-PeMachine (Join-Path $sourcePath $fileName)
    if ($machine -ne $expectedMachine) {
        throw "$fileName has PE machine 0x$('{0:X4}' -f $machine), which does not match $Runtime."
    }
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if ((Test-WithinDirectory $temporaryRoot $repositoryPath) -or
    (Test-WithinDirectory $temporaryRoot $sourcePath) -or
    (Test-WithinDirectory $temporaryRoot $outputPath)) {
    throw 'The Windows temporary directory must be outside the repository, publish tree and output directory.'
}
Assert-NoLinkedAncestors $temporaryRoot
$stagingRoot = Join-Path $temporaryRoot ('mRemoteNG-package-' + [Guid]::NewGuid().ToString('N'))
$payloadPath = Join-Path $stagingRoot 'app'
$payloadZip = Join-Path $stagingRoot 'payload.zip'
$iconPath = Join-Path $stagingRoot 'launcher.ico'
$createdOutput = $false
$succeeded = $false

try {
    [void][IO.Directory]::CreateDirectory($payloadPath)
    $sourcePrefix = $sourcePath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($directory in $sourceDirectories) {
        [void][IO.Directory]::CreateDirectory((Join-Path $payloadPath $directory.Substring($sourcePrefix.Length)))
    }
    foreach ($file in $sourceFiles) {
        $stagedFile = Join-Path $payloadPath $file.Substring($sourcePrefix.Length)
        [IO.File]::Copy($file, $stagedFile)
        [IO.File]::SetAttributes($stagedFile, [IO.FileAttributes]::Normal)
    }
    foreach ($license in @('COPYING.txt', 'CREDITS.md')) {
        [IO.File]::Copy((Join-Path $repositoryPath $license), (Join-Path $payloadPath $license), $true)
    }

    # Keep feature guidance alongside the extracted app without changing its build output.
    $guidesDirectory = Join-Path $payloadPath 'Documentation'
    [void][IO.Directory]::CreateDirectory($guidesDirectory)
    foreach ($guide in @('capture-protection.md', 'app-branding.md', 'production-packaging.md')) {
        $guidePath = Join-Path (Join-Path $repositoryPath 'docs') $guide
        if ([IO.File]::Exists($guidePath)) {
            [IO.File]::Copy($guidePath, (Join-Path $guidesDirectory $guide), $true)
        }
    }

    Add-Type -AssemblyName System.Drawing
    $icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $payloadPath 'Capture2Text.exe'))
    if ($null -eq $icon) { throw 'Could not extract the application icon for the portable launcher.' }
    try {
        $iconStream = [IO.File]::Create($iconPath)
        try { $icon.Save($iconStream) }
        finally { $iconStream.Dispose() }
    }
    finally { $icon.Dispose() }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payloadPath, $payloadZip, [IO.Compression.CompressionLevel]::Optimal, $false)

    # Recheck immediately before creating our output; never publish into stale files.
    if (Test-Path -LiteralPath $outputPath) { throw "OutputDirectory already exists: $outputPath" }
    [void][IO.Directory]::CreateDirectory($outputPath)
    $createdOutput = $true
    $publishArguments = @(
        'publish', $launcherProject, '-c', 'Release', '-r', $Runtime, '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false',
        ('-p:PayloadZip=' + (ConvertTo-MSBuildValue $payloadZip)),
        ('-p:LauncherIcon=' + (ConvertTo-MSBuildValue $iconPath)),
        ('-p:BaseIntermediateOutputPath=' + (ConvertTo-MSBuildValue (Join-Path $stagingRoot 'obj/'))),
        ('-p:MSBuildProjectExtensionsPath=' + (ConvertTo-MSBuildValue (Join-Path $stagingRoot 'obj/'))),
        ('-p:BaseOutputPath=' + (ConvertTo-MSBuildValue (Join-Path $stagingRoot 'bin/'))),
        '-o', $outputPath
    )
    & $dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "Launcher publishing failed with exit code $LASTEXITCODE." }

    $outputs = @(Get-ChildItem -LiteralPath $outputPath -Force)
    if ($outputs.Count -ne 1 -or $outputs[0].PSIsContainer -or $outputs[0].Name -ne 'Capture2Text.exe') {
        throw 'Launcher publishing did not produce exactly one Capture2Text.exe. No distributable has been accepted.'
    }
    if ((Get-PeMachine $outputs[0].FullName) -ne $expectedMachine) {
        throw 'The launcher architecture does not match the requested runtime.'
    }
    $succeeded = $true
    Write-Output "Created $($outputs[0].FullName)"
}
finally {
    # Delete only staging and a failed output directory created by this invocation.
    try {
        if ([IO.Directory]::Exists($stagingRoot)) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
    }
    finally {
        if (-not $succeeded -and $createdOutput -and [IO.Directory]::Exists($outputPath)) {
            Remove-Item -LiteralPath $outputPath -Recurse -Force
        }
    }
}
