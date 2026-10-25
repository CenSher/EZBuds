param([switch]$Run, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-shell.ps1') -Run:$Run -NoRestore:$NoRestore
