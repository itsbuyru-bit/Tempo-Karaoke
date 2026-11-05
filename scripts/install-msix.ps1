<#
.SYNOPSIS
    Ставит MSIX на ПК: доверяет сертификат и устанавливает пакет.

.DESCRIPTION
    Сертификат нужен только для самоподписанных пакетов (сборка sideload).
    Для импорта в хранилище машины требуются права администратора — скрипт
    сам запросит UAC и перезапустится.

    Если пакет не указан, берётся самый свежий .msix рядом со скриптом
    или в artifacts\msix.

.EXAMPLE
    .\install-msix.ps1 -Package .\TempoKaraoke_1.8.21.0_x64.msix -Certificate .\certs\TempoKaraoke.cer
#>
[CmdletBinding()]
param(
    [string]$Package,
    [string]$Certificate,
    [ValidateSet('TrustedPeople', 'Root')]
    [string]$Store = 'TrustedPeople',
    [switch]$SkipCertificate,
    [switch]$Force,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

function Resolve-Package {
    param([string]$Path)

    $candidates = @()
    if ($Path) { $candidates += $Path }
    $candidates += (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.msix' -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
    $candidates += (Get-ChildItem -LiteralPath (Join-Path $root 'artifacts\msix') -Filter '*.msix' -File -ErrorAction SilentlyContinue | ForEach-Object FullName)

    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $found) {
        throw 'Не найден .msix. Укажите -Package путь\к\пакету.msix'
    }

    return (Resolve-Path -LiteralPath $found).Path
}

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$Package = Resolve-Package -Path $Package
if ($Certificate) { $Certificate = (Resolve-Path -LiteralPath $Certificate).Path }
elseif (-not $SkipCertificate) {
    $default = Join-Path $root 'certs\TempoKaraoke.cer'
    if (Test-Path -LiteralPath $default) { $Certificate = $default }
}

if (-not $SkipCertificate -and -not (Test-Admin)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Package', $Package, '-Store', $Store)
    if ($Certificate) { $arguments += @('-Certificate', $Certificate) }
    if ($Force) { $arguments += '-Force' }
    if ($NoLaunch) { $arguments += '-NoLaunch' }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments | Out-Null
    return
}

if (-not $SkipCertificate) {
    if (-not $Certificate) {
        throw 'Не найден сертификат: запустите scripts\make-cert.ps1 или укажите -Certificate.'
    }

    Import-Certificate -FilePath $Certificate -CertStoreLocation "Cert:\LocalMachine\$Store" | Out-Null
    Write-Host "Сертификат добавлен в Cert:\LocalMachine\$Store"
}

$installParams = @{ Path = $Package }
if ($Force) { $installParams['ForceUpdateFromAnyVersion'] = $true }
Add-AppxPackage @installParams
Write-Host "Пакет установлен: $Package"

if (-not $NoLaunch) {
    $app = Get-StartApps | Where-Object { $_.Name -eq 'Tempo Karaoke' } | Select-Object -First 1
    if ($app) {
        Start-Process "shell:AppsFolder\$($app.AppID)"
    }
}
