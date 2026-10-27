[CmdletBinding()]
param(
    [switch]$WithEvolution,
    [int]$MaximumAttempts = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$environmentFile = Join-Path $repoRoot '.env.staging'
if (-not (Test-Path -LiteralPath $environmentFile)) { throw 'No local staging .env.staging file exists.' }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker Desktop with Docker Compose v2 is required.' }

$settings = @{}
foreach ($line in Get-Content -LiteralPath $environmentFile) {
    if ($line -match '^([^#=]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
}
$port = if ($settings.ContainsKey('STAGING_API_PORT') -and $settings['STAGING_API_PORT']) { $settings['STAGING_API_PORT'] } else { '58081' }
$composeArguments = @('compose', '--env-file', $environmentFile, '-f', (Join-Path $repoRoot 'docker-compose.staging.yml'), '--project-name', 'nexflow-staging')
if ($WithEvolution) { $composeArguments += @('-f', (Join-Path $repoRoot 'docker-compose.staging.evolution.yml'), '--profile', 'whatsapp') }

Push-Location $repoRoot
try {
    & docker @composeArguments ps
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read local staging service state.' }

    $live = $null
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            $live = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health/live" -TimeoutSec 5
            break
        }
        catch {
            if ($attempt -eq $MaximumAttempts) { throw 'API liveness did not become reachable.' }
            Start-Sleep -Seconds 2
        }
    }
    if ($live.status -ne 'Healthy') { throw "API liveness returned '$($live.status)'." }
    $ready = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health/ready" -TimeoutSec 10
    if ($ready.status -ne 'Healthy') { throw "API readiness returned '$($ready.status)'." }
    $components = @($ready.checks | ForEach-Object { "$($_.component)=$($_.status)" }) -join ', '
    Write-Output "NexFlow staging health is Healthy ($components)."
    Write-Output 'No provider or WhatsApp message is sent by this health check.'
}
finally {
    Pop-Location
}
