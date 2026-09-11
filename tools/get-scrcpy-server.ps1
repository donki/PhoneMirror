<#
.SYNOPSIS
    Baja el servidor de scrcpy de la release oficial y lo deja en Assets\scrcpy-server.

.DESCRIPTION
    La version tiene que coincidir con ScrcpySession.ServerVersion: el servidor comprueba la que le
    manda el cliente y el formato de los mensajes cambia entre versiones. Se comprueba el SHA-256
    contra el que consta en THIRD-PARTY-NOTICES.md.
#>
param(
    [string] $Version = "4.1",
    [string] $Sha256 = "deacb991ed2509715160ffdc7907e47b4160eb30d1566217e9047fd5b8850cae"
)
$ErrorActionPreference = "Stop"
$destination = Join-Path (Split-Path -Parent $PSScriptRoot) "Assets\scrcpy-server"
$url = "https://github.com/Genymobile/scrcpy/releases/download/v$Version/scrcpy-server-v$Version"
Write-Host "Bajando $url"
Invoke-WebRequest -Uri $url -OutFile $destination
$hash = (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $Sha256) { Remove-Item $destination; throw "SHA-256 distinto: $hash" }
Write-Host "OK: $destination"
