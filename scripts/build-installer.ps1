<#
.SYNOPSIS
    Собирает установщик Inno Setup: портативная публикация + компиляция .iss.

.DESCRIPTION
    Версия берётся из <Version> в TempoKaraoke.csproj (она же попадает в exe и в
    сравнение обновлений). Компилятор ищется в Inno Setup 7/6; можно передать путь
    явно через -InnoCompiler.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -InnoCompiler "D:\Program Files\Inno Setup 7\ISCC.exe"
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version,
    [string]$InnoCompiler,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\TempoKaraoke\TempoKaraoke.csproj'
$script = Join-Path $root 'installer\TempoKaraoke.iss'

if (-not $Version) {
    $match = Select-String -LiteralPath $project -Pattern '<Version>([^<]+)</Version>'
    if (-not $match) { throw 'В TempoKaraoke.csproj не найден <Version>.' }
    $Version = $match.Matches[0].Groups[1].Value
}

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        'D:\Program Files\Inno Setup 7\ISCC.exe'
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Не найден ISCC.exe. Установите Inno Setup 6/7 или передайте -InnoCompiler.'
}

if (-not $SkipPublish) {
    dotnet publish $project -c $Configuration -p:PublishProfile=portable-x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }
}

& $InnoCompiler $script "/DMyAppVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с кодом $LASTEXITCODE" }

$installer = Join-Path $root "artifacts\TempoKaraoke-Setup-$Version.exe"
Write-Host "Установщик: $installer"
