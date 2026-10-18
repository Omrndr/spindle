# Spindle'ı derleyip geçerli kullanıcı için kurar (yönetici izni gerekmez).
#   powershell -File scripts\install.ps1
# Kurulum yeri: %LOCALAPPDATA%\Programs\Spindle  ·  Başlat menüsüne kısayol eklenir.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $env:LOCALAPPDATA 'Programs\Spindle'

Write-Output "Derleniyor..."
dotnet publish (Join-Path $root 'src\Spindle\Spindle.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $root 'publish') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız." }

# Çalışan kopyayı kapat ki dosyalar güncellenebilsin.
Get-Process Spindle -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Output "Kopyalanıyor: $target"
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $root 'publish\*') $target -Recurse -Force

$exe = Join-Path $target 'Spindle.exe'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Spindle.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($startMenu)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.Description = 'Sürükle, Shift''e bas, bırak.'
$link.Save()

Write-Output "Kuruldu. Başlatılıyor..."
Start-Process $exe
