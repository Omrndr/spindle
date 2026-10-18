# Spindle'ı derleyip geçerli kullanıcı için kurar (yönetici izni gerekmez).
#   powershell -File scripts\install.ps1
# Kurulum yeri: %LOCALAPPDATA%\Programs\Spindle  ·  Başlat menüsüne ve "Uygulamalar" listesine eklenir.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $env:LOCALAPPDATA 'Programs\Spindle'

Write-Output "Derleniyor..."
$out = Join-Path $root 'publish'
dotnet publish (Join-Path $root 'src\Spindle\Spindle.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız." }

# Derlenen Spindle.exe ilk açılışta kendini kurar: çalışan eski kopyayı kapatır, dosyayı
# %LOCALAPPDATA%\Programs\Spindle'a kopyalar, Başlat menüsüne ve "Uygulamalar" listesine ekler.
Write-Output "Kuruluyor: $target"
# -Wait kullanılmaz: kurulu kopya bu işlemin alt işlemi olarak tepside çalışmaya devam eder.
(Start-Process (Join-Path $out 'Spindle.exe') -PassThru).WaitForExit()
Write-Output "Kuruldu."
