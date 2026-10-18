# Spindle'ı ve yaptığı tüm kullanıcı ayarlarını kaldırır.
#   powershell -File scripts\uninstall.ps1
Get-Process Spindle -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-Item 'HKCU:\Software\Classes\AllFilesystemObjects\shell\Spindle' -Recurse -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Spindle -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Spindle.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\Spindle') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'Spindle') -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "Spindle kaldırıldı."
