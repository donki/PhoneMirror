<#
.SYNOPSIS
    Baja las platform-tools de Android de Google y deja adb (y sus dos DLL) en Assets\platform-tools.

.DESCRIPTION
    adb, AdbWinApi.dll y AdbWinUsbApi.dll son componentes de codigo abierto del SDK (Apache 2.0, en
    AOSP). La clausula 3.6 del Android SDK License Agreement dice que la distribucion de los
    componentes con licencia de codigo abierto se rige por esa licencia, no por el acuerdo: por eso
    se pueden redistribuir sin modificar, con su NOTICE (es lo mismo que hace scrcpy en Windows).

    Se baja el zip de una revision concreta y se comprueba su SHA-256 (los dos constan en
    THIRD-PARTY-NOTICES.md). Los binarios no van al repositorio: este script los deja en Assets y el
    csproj los copia junto al ejecutable y dentro del MSIX.

.EXAMPLE
    .\tools\get-platform-tools.ps1
#>
param(
    [string] $Revision = "37.0.1",
    [string] $Sha256 = "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99"
)
$ErrorActionPreference = "Stop"
$destination = Join-Path (Split-Path -Parent $PSScriptRoot) "Assets\platform-tools"
$url = "https://dl.google.com/android/repository/platform-tools_r$Revision-win.zip"
$zip = Join-Path $env:TEMP "platform-tools_r$Revision-win.zip"

Write-Host "Bajando $url"
Invoke-WebRequest -Uri $url -OutFile $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $Sha256) { Remove-Item $zip; throw "SHA-256 distinto: $hash" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($name in "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "NOTICE.txt", "source.properties") {
        $entry = $archive.Entries | Where-Object { $_.FullName -eq "platform-tools/$name" }
        if (-not $entry) { throw "El zip no trae $name" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $name), $true)
    }
}
finally { $archive.Dispose() }
Remove-Item $zip
Write-Host "OK: $destination (platform-tools r$Revision)"
