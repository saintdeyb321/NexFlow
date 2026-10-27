[CmdletBinding()]
param(
    [switch]$WithEvolution
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$environmentFile = Join-Path $repoRoot '.env.staging'
if (-not (Test-Path -LiteralPath $environmentFile)) { throw 'No local staging .env.staging file exists.' }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker Desktop with Docker Compose v2 is required.' }

$composeArguments = @('compose', '--env-file', $environmentFile, '-f', (Join-Path $repoRoot 'docker-compose.staging.yml'), '--project-name', 'nexflow-staging')
if ($WithEvolution) { $composeArguments += @('-f', (Join-Path $repoRoot 'docker-compose.staging.evolution.yml'), '--profile', 'whatsapp') }

Push-Location $repoRoot
try {
    # Stop preserves every staging volume. It never contacts or changes production.
    & docker @composeArguments stop
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose could not stop local staging.' }
}
finally {
    Pop-Location
}
