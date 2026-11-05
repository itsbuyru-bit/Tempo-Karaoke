<#
.SYNOPSIS
    Создаёт самоподписанный сертификат для MSIX и экспортирует публичную часть (.cer).

.DESCRIPTION
    Publisher сертификата берётся из Package.appxmanifest, поэтому подпись
    совпадает с identity пакета. В репозиторий сертификат не попадает (certs/ в .gitignore).

    Распространять получателям нужно только .cer: .pfx содержит закрытый ключ.

.EXAMPLE
    .\make-cert.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$Password = 'password',
    [string]$Manifest
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutputDir) { $OutputDir = Join-Path $scriptRoot '..\certs' }
if (-not $Manifest) { $Manifest = Join-Path $scriptRoot '..\src\TempoKaraoke\Package.appxmanifest' }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$pfx = Join-Path $outputDir 'TempoKaraoke.pfx'
$cer = Join-Path $outputDir 'TempoKaraoke.cer'

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw 'Не найден winapp. Установите WinApp CLI 0.7+: winget install Microsoft.WinAppCLI'
}

winapp cert generate --manifest $Manifest --output $pfx --password $Password --if-exists overwrite
if ($LASTEXITCODE -ne 0) {
    throw "winapp cert generate завершился с кодом $LASTEXITCODE"
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $Password)
$certBytes = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
[System.IO.File]::WriteAllBytes($cer, $certBytes)

winapp cert info $pfx

Write-Host ''
Write-Host "Сертификат (с ключом, только для сборки): $pfx"
Write-Host "Публичный сертификат (раздавать вместе с .msix): $cer"
Write-Host "Пароль PFX: $Password"
