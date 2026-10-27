[CmdletBinding()]
param(
    [switch]$WithEvolution
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$composeFile = Join-Path $repoRoot 'docker-compose.staging.yml'
$evolutionComposeFile = Join-Path $repoRoot 'docker-compose.staging.evolution.yml'
$templateFile = Join-Path $repoRoot '.env.staging.example'
$environmentFile = Join-Path $repoRoot '.env.staging'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker Desktop with Docker Compose v2 is required for local staging.'
}

function New-LocalSecret {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToHexString($bytes).ToLowerInvariant()
}

if (-not (Test-Path -LiteralPath $environmentFile)) {
    Copy-Item -LiteralPath $templateFile -Destination $environmentFile
}

$requiredLocalSecrets = @(
    'STAGING_POSTGRES_PASSWORD',
    'STAGING_EVOLUTION_POSTGRES_PASSWORD',
    'STAGING_EVOLUTION_API_KEY',
    'STAGING_EVOLUTION_WEBHOOK_KEY',
    'STAGING_N8N_WEBHOOK_SECRET'
)
$environmentLines = Get-Content -LiteralPath $environmentFile
foreach ($key in $requiredLocalSecrets) {
    $lineIndex = [Array]::FindIndex([string[]]$environmentLines, [Predicate[string]] { param($line) $line -match ('^' + [regex]::Escape($key) + '=') })
    if ($lineIndex -lt 0) { throw "Missing $key in .env.staging." }
    $value = $environmentLines[$lineIndex].Substring($key.Length + 1).Trim()
    if ([string]::IsNullOrWhiteSpace($value) -or $value -eq 'CHANGE_ME') {
        $environmentLines[$lineIndex] = "$key=$(New-LocalSecret)"
    }
}
Set-Content -LiteralPath $environmentFile -Value $environmentLines -Encoding utf8NoBOM

$composeArguments = @('compose', '--env-file', $environmentFile, '-f', $composeFile, '--project-name', 'nexflow-staging')
if ($WithEvolution) {
    $requiredEvolutionValues = @('STAGING_EVOLUTION_IMAGE', 'STAGING_EVOLUTION_RUNTIME_ENV_FILE', 'STAGING_EVOLUTION_DATA_PATH')
    $values = @{}
    foreach ($line in $environmentLines) {
        if ($line -match '^([^#=]+)=(.*)$') { $values[$matches[1]] = $matches[2] }
    }
    foreach ($key in $requiredEvolutionValues) {
        if ([string]::IsNullOrWhiteSpace($values[$key])) { throw "Set $key in the ignored .env.staging after verifying the provider contract." }
    }
    $vendorFile = Join-Path $repoRoot $values['STAGING_EVOLUTION_RUNTIME_ENV_FILE']
    if (-not (Test-Path -LiteralPath $vendorFile)) { throw 'The Evolution runtime env file does not exist.' }
    $composeArguments += @('-f', $evolutionComposeFile, '--profile', 'whatsapp')
}

Push-Location $repoRoot
try {
    & docker @composeArguments config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose validation failed.' }
    & docker @composeArguments up --build --detach
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose could not start local staging.' }
    & (Join-Path $PSScriptRoot 'Test-LocalStaging.ps1') -WithEvolution:$WithEvolution
}
finally {
    Pop-Location
}
