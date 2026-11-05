<#
.SYNOPSIS
    Собирает MSIX: для боковой установки (подписанный) или для Microsoft Store (без подписи).

.DESCRIPTION
    sideload — пакет подписывается сертификатом из certs/TempoKaraoke.pfx; его можно
    поставить скриптом install-msix.ps1 или двойным кликом после доверия сертификату.

    store    — пакет без подписи для загрузки в Partner Center. Identity в
    Package.appxmanifest должна совпадать с Partner Center (Package/Identity/Name,
    Package/Identity/Publisher), иначе Store отклонит пакет.

.EXAMPLE
    .\build-msix.ps1 -Mode sideload
    .\build-msix.ps1 -Mode store
#>
[CmdletBinding()]
param(
    [ValidateSet('sideload', 'store')]
    [string]$Mode = 'sideload',
    [string]$Arch = 'x64',
    [string]$CertPath,
    [string]$CertPassword = 'password',
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $CertPath) { $CertPath = Join-Path $root 'certs\TempoKaraoke.pfx' }
if (-not $Output) { $Output = Join-Path $root 'artifacts\msix' }
$project = Join-Path $root 'src\TempoKaraoke\TempoKaraoke.csproj'
New-Item -ItemType Directory -Force -Path $Output | Out-Null

if ($Mode -eq 'sideload') {
    if (-not (Test-Path $CertPath)) {
        throw "Нет сертификата $CertPath. Сначала запустите scripts\make-cert.ps1"
    }

    # Self-contained: .NET и Windows App SDK внутрь пакета — на чистом ПК ничего не нужно.
    winapp package $project --arch $Arch --self-contained -p SelfContained=true --cert $CertPath --cert-password $CertPassword --output $Output
}
else {
    $manifest = Join-Path $PSScriptRoot '..\src\TempoKaraoke\Package.appxmanifest'
    [xml]$identity = Get-Content -LiteralPath $manifest
    $publisher = $identity.Package.Identity.Publisher

    if ($publisher -eq 'CN=AppPublisher') {
        Write-Warning ('В манифесте остался тестовый Publisher "CN=AppPublisher". ' +
            'Для Store подставьте Package/Identity/Publisher из Partner Center.')
    }

    # Store подписывает пакет сам, поэтому загружается неподписанный self-contained пакет.
    winapp package $project --arch $Arch --self-contained -p SelfContained=true --no-sign --output $Output
}

if ($LASTEXITCODE -ne 0) {
    throw "winapp package завершился с кодом $LASTEXITCODE"
}

Write-Host "Пакеты: $Output"
