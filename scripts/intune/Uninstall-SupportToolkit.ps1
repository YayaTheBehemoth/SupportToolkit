# SupportToolkit - Intune uninstall (Win32 app, user context).
#
# Removes what Install-SupportToolkit.ps1 put in place: the app folder, the
# Start menu shortcuts and the detection key. The user's settings, receipts
# (%LOCALAPPDATA%\SupportToolkit) and stored credentials are kept, so a
# reinstall picks up where the user left off.
# Exit codes: 0 removed, 1618 SupportToolkit is running (Intune retries
# later), 1 failed. Log: %TEMP%\SupportToolkit-intune.log

$ErrorActionPreference = 'Stop'
$exitCode = 1

Start-Transcript -Path (Join-Path $env:TEMP 'SupportToolkit-intune.log') -Append | Out-Null

try {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\SupportToolkit'
    $exe = Join-Path $target 'SupportToolkit.exe'

    $running = Get-Process -Name 'SupportToolkit' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path -ieq $exe) }

    if ($running) {
        Write-Output 'SupportToolkit is running; asking Intune to retry later.'
        $exitCode = 1618
    }
    else {
        # The detection key first, so a half-finished removal is not reported as installed.
        Remove-Item -Path 'HKCU:\Software\SupportToolkit' -Recurse -Force -ErrorAction SilentlyContinue

        $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'SupportToolkit'
        Remove-Item -LiteralPath $menu -Recurse -Force -ErrorAction SilentlyContinue

        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }

        Write-Output 'SupportToolkit removed; settings and credentials kept.'
        $exitCode = 0
    }
}
catch {
    Write-Output "Uninstall failed: $_"
    $exitCode = 1
}
finally {
    Stop-Transcript | Out-Null
}

exit $exitCode
