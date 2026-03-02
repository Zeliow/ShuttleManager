# Папка с exe (win-x64)
$exeDir = Join-Path $PSScriptRoot "bin\Release\net10.0-windows10.0.19041.0\win-x64"
$exePath = Join-Path $exeDir "ShuttleManager.exe"

# Папка, где будет ярлык (рядом с win-x64)
$shortcutDir = Split-Path $exeDir -Parent
$shortcutPath = Join-Path $shortcutDir "ShuttleManager.lnk"

# Создание ярлыка
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $exePath
$Shortcut.WorkingDirectory = $exeDir
$Shortcut.Save()

Write-Host "Ярлык создан: $shortcutPath"