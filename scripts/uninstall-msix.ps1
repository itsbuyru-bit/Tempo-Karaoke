<#
.SYNOPSIS
    Удаляет установленный MSIX Tempo Karaoke.

.EXAMPLE
    .\uninstall-msix.ps1
    .\uninstall-msix.ps1 -PreserveData:$false
#>
[CmdletBinding()]
param(
    [string]$IdentityName = 'CappuccinoDev.TempoKaraoke',
    [bool]$PreserveData = $true
)

$ErrorActionPreference = 'Stop'

$package = Get-AppxPackage -Name $IdentityName -ErrorAction SilentlyContinue
if (-not $package) {
    Write-Host "Пакет $IdentityName не установлен."
    return
}

$package | Remove-AppxPackage -PreserveApplicationData:$PreserveData
Write-Host "Пакет удалён: $($package.PackageFullName)"
