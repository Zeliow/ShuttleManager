# ѕуть к exe
$exePath = "$PSScriptRoot\bin\Release\net10.0-windows10.0.19041.0\win-x64\ShuttleManager.exe"

# ѕуть дл€ €рлыка (р€дом с папкой win-x64)
$shortcutPath = "$PSScriptRoot\bin\Release\net10.0-windows10.0.19041.0\ShuttleManager.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $exePath
$Shortcut.WorkingDirectory = Split-Path $exePath
$Shortcut.Save()

Write-Host "ярлык создан: $shortcutPath"