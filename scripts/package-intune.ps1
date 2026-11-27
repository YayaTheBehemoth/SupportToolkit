<#
.SYNOPSIS
    Packages the published Windows build as an Intune Win32 app.

.DESCRIPTION
    Run after scripts\publish-windows.cmd. Produces, in artifacts\:
      SupportToolkit-<version>.intunewin    the file to upload in Intune
      SupportToolkit-<version>-intune.md    the settings to enter there
    The package installs per user (see scripts\intune\Install-SupportToolkit.ps1).

    The Microsoft Win32 Content Prep Tool is downloaded at a pinned version
    and checked against its SHA-256 before it runs, unless -ContentPrepTool
    names a copy to use instead.

.EXAMPLE
    scripts\publish-windows.cmd
    powershell -ExecutionPolicy Bypass -File scripts\package-intune.ps1 -Version 0.6.1
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $PublishDir = 'artifacts\publish\win-x64',

    [string] $OutputDir = 'artifacts',

    [string] $ContentPrepTool
)

$ErrorActionPreference = 'Stop'

$PrepToolVersion = 'v1.8.7'
$PrepToolSha256 = 'C1BA45B5CB939E84AF064BB7FF4B38FB3DFE33C8DC1078FD9B157672EAE671F6'
$PrepToolUrl = "https://raw.githubusercontent.com/microsoft/Microsoft-Win32-Content-Prep-Tool/$PrepToolVersion/IntuneWinAppUtil.exe"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $Version = $Version.Trim().TrimStart('v', 'V')

    if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
        throw "Version '$Version' is not of the form 1.2.3."
    }

    Write-Host "Packaging SupportToolkit $Version for Intune"

    # 1. The published build, with its launchers.
    foreach ($required in 'SupportToolkit.exe', 'Start SupportToolkit.cmd', 'Import config.cmd') {
        if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $required) -PathType Leaf)) {
            throw "$required is missing from $PublishDir. Run scripts\publish-windows.cmd first."
        }
    }

    # 2. The package source: the install scripts, the version, the app.
    $staging = Join-Path $OutputDir 'intune'
    $source = Join-Path $staging 'source'
    $built = Join-Path $staging 'out'

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    New-Item -ItemType Directory -Path (Join-Path $source 'app'), $built -Force | Out-Null

    Copy-Item -Path 'scripts\intune\Install-SupportToolkit.ps1', 'scripts\intune\Uninstall-SupportToolkit.ps1' -Destination $source
    Set-Content -LiteralPath (Join-Path $source 'version.txt') -Value $Version -Encoding Ascii
    Copy-Item -Path (Join-Path $PublishDir '*') -Destination (Join-Path $source 'app') -Recurse

    # 3. The Content Prep Tool, pinned and verified.
    if (-not $ContentPrepTool) {
        $tools = Join-Path $OutputDir 'tools'
        New-Item -ItemType Directory -Path $tools -Force | Out-Null
        $ContentPrepTool = Join-Path $tools "IntuneWinAppUtil-$PrepToolVersion.exe"

        if (-not (Test-Path -LiteralPath $ContentPrepTool -PathType Leaf)) {
            Write-Host "Downloading the Win32 Content Prep Tool $PrepToolVersion"
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $PrepToolUrl -OutFile $ContentPrepTool -UseBasicParsing
        }

        $hash = (Get-FileHash -LiteralPath $ContentPrepTool -Algorithm SHA256).Hash

        if ($hash -ne $PrepToolSha256) {
            Remove-Item -LiteralPath $ContentPrepTool -Force
            throw "The Content Prep Tool does not match its pinned SHA-256 ($hash)."
        }
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $ContentPrepTool

    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "The Content Prep Tool is not validly signed by Microsoft ($($signature.Status))."
    }

    # 4. The .intunewin.
    & $ContentPrepTool -c $source -s 'Install-SupportToolkit.ps1' -o $built -q

    if ($LASTEXITCODE -ne 0) {
        throw "The Content Prep Tool failed (exit code $LASTEXITCODE)."
    }

    $produced = Join-Path $built 'Install-SupportToolkit.intunewin'

    if (-not (Test-Path -LiteralPath $produced -PathType Leaf) -or (Get-Item -LiteralPath $produced).Length -eq 0) {
        throw 'The .intunewin file was not created.'
    }

    $package = Join-Path $OutputDir "SupportToolkit-$Version.intunewin"
    Move-Item -LiteralPath $produced -Destination $package -Force

    # 5. The settings to enter in Intune, for this version.
    $sheet = Join-Path $OutputDir "SupportToolkit-$Version-intune.md"
    $powershell = '%windir%\sysnative\WindowsPowerShell\v1.0\powershell.exe -ExecutionPolicy Bypass -NoProfile -File'

    @"
# SupportToolkit $Version - Intune settings

Upload ``SupportToolkit-$Version.intunewin`` as a **Windows app (Win32)**.
A new version: open the existing app, **Properties > App information > Edit**,
select the new file, then change the detection rule's value to ``$Version``.

## App information
- Name: SupportToolkit
- Publisher: (your organisation)
- App version: $Version

## Program
- Install command: ``$powershell .\Install-SupportToolkit.ps1``
- Uninstall command: ``$powershell .\Uninstall-SupportToolkit.ps1``
- Installation time required: 15 minutes
- Allow available uninstall: Yes
- Install behavior: **User**
- Device restart behavior: No specific action
- Return codes: keep the defaults (0 success, 1618 retry: SupportToolkit was running)

## Requirements
- Operating system architecture: x64
- Minimum operating system: Windows 10 1809 or later

## Detection rules
- Rules format: Manually configure detection rules
- Rule type: Registry
- Key path: ``HKEY_CURRENT_USER\Software\SupportToolkit``
- Value name: ``Version``
- Detection method: String comparison, Equals ``$Version``
- Associated with a 32-bit app on 64-bit clients: No

## Assignments
- Available for enrolled devices: the group of people who should be able to install it
  from Company Portal (start with a test group of one).

## What the user gets
- Start menu > SupportToolkit > **SupportToolkit** (starts the web UI),
  **Import SupportToolkit settings** and **SupportToolkit folder**.
- The app in ``%LOCALAPPDATA%\Programs\SupportToolkit``; install log in ``%TEMP%\SupportToolkit-intune.log``.
- Uninstalling keeps the user's settings and stored credentials.
"@ | Set-Content -LiteralPath $sheet -Encoding UTF8

    Write-Host ''
    Write-Host "Intune package: $package"
    Write-Host "Intune settings: $sheet"
}
finally {
    Pop-Location
}
