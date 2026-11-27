# SupportToolkit - Intune install (Win32 app, user context).
#
# Installs the app for the signed-in user, so no admin rights are needed and
# the settings file can be dropped next to SupportToolkit.exe for
# "Import SupportToolkit settings", the same as with the zip:
#   app        %LOCALAPPDATA%\Programs\SupportToolkit
#   shortcuts  Start menu > SupportToolkit
#   detection  HKCU\Software\SupportToolkit, Version (string) = version.txt
#
# Settings, receipts and stored credentials live elsewhere
# (%LOCALAPPDATA%\SupportToolkit and Windows Credential Manager) and are not
# touched. Exit codes: 0 installed, 1618 SupportToolkit is running (Intune
# retries later), 1 failed. Log: %TEMP%\SupportToolkit-intune.log

$ErrorActionPreference = 'Stop'
$exitCode = 1

Start-Transcript -Path (Join-Path $env:TEMP 'SupportToolkit-intune.log') -Append | Out-Null

try {
    $version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
    $source = Join-Path $PSScriptRoot 'app'
    $target = Join-Path $env:LOCALAPPDATA 'Programs\SupportToolkit'
    $exe = Join-Path $target 'SupportToolkit.exe'

    Write-Output "Installing SupportToolkit $version to $target"

    $running = Get-Process -Name 'SupportToolkit' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path -ieq $exe) }

    if ($running) {
        Write-Output 'SupportToolkit is running; asking Intune to retry later.'
        $exitCode = 1618
    }
    else {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
            throw 'SupportToolkit.exe is missing after the copy.'
        }

        # Start menu shortcuts for the user.
        $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'SupportToolkit'
        New-Item -ItemType Directory -Path $menu -Force | Out-Null

        $shell = New-Object -ComObject WScript.Shell

        $shortcuts = @(
            @{ Name = 'SupportToolkit'; Target = (Join-Path $target 'Start SupportToolkit.cmd');
               Description = 'Start SupportToolkit; a page opens in your browser.' },
            @{ Name = 'Import SupportToolkit settings'; Target = (Join-Path $target 'Import config.cmd');
               Description = 'Import the settings file put in the SupportToolkit folder.' },
            @{ Name = 'SupportToolkit folder'; Target = $target;
               Description = 'Put the settings file you were sent here, then run Import SupportToolkit settings.' }
        )

        foreach ($item in $shortcuts) {
            $link = $shell.CreateShortcut((Join-Path $menu ($item.Name + '.lnk')))
            $link.TargetPath = $item.Target
            $link.WorkingDirectory = $target
            $link.IconLocation = "$exe,0"
            $link.Description = $item.Description
            $link.Save()
        }

        # Written last, so detection only succeeds once everything is in place.
        $key = 'HKCU:\Software\SupportToolkit'
        New-Item -Path $key -Force | Out-Null
        New-ItemProperty -Path $key -Name 'InstallPath' -Value $target -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $key -Name 'Version' -Value $version -PropertyType String -Force | Out-Null

        Write-Output "SupportToolkit $version installed."
        $exitCode = 0
    }
}
catch {
    Write-Output "Install failed: $_"
    $exitCode = 1
}
finally {
    Stop-Transcript | Out-Null
}

exit $exitCode
