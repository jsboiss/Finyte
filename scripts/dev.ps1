param([ValidateSet('start', 'status', 'stop', 'restart')][string]$Action = 'start')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $workspace
$compose = @('compose', '-p', 'finyte-sample-dev', '-f', (Join-Path $workspace 'docker-compose.dev.yml'))
function Docker-Run([string[]]$Arguments) {
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed: $($Arguments -join ' ')" }
}
function Http-Ready([string]$url) {
    try { return (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 2).StatusCode -eq 200 } catch { return $false }
}
function Docker-Ready {
    try {
        & docker info --format '{{.ServerVersion}}' *> $null
        return $LASTEXITCODE -eq 0
    } catch { return $false }
}
function Wait-Ready([string]$name, [scriptblock]$check, [int]$seconds = 60) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        try { if (& $check) { return } } catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$name did not become ready. Inspect: docker compose -p finyte-sample-dev -f docker-compose.dev.yml logs --tail 50"
}
if ($Action -eq 'status') {
    Docker-Run ($compose + @('ps', '-a'))
    Write-Host "API health: $(Http-Ready 'http://127.0.0.1:5086/health')"
    Write-Host "Client: $(Http-Ready 'http://127.0.0.1:5186/')"
    exit 0
}
if ($Action -eq 'stop') {
    Docker-Run ($compose + @('stop'))
    Write-Host 'Application stopped. PostgreSQL, Temporal and imported data are preserved.'
    exit 0
}
if (!(Docker-Ready)) {
    $dockerDesktop = Join-Path $env:ProgramFiles 'Docker/Docker/Docker Desktop.exe'
    if (!(Test-Path -LiteralPath $dockerDesktop)) { throw 'Install/start Docker Desktop, then run this command again.' }
    Start-Process -FilePath $dockerDesktop -WindowStyle Hidden
    Wait-Ready 'Docker Desktop' { Docker-Ready } 120
}
foreach ($container in @('finyte-postgres', 'finyte-temporal')) {
    & docker inspect $container *> $null
    if ($LASTEXITCODE -ne 0) { throw "Missing $container. Inspect the existing infrastructure and sample data before setting it up; no replacement volume was created." }
    Docker-Run @('start', $container)
}
Wait-Ready 'PostgreSQL' { & docker exec finyte-postgres pg_isready -U finyte -d finyte_recurring_samples *> $null; $LASTEXITCODE -eq 0 }
Wait-Ready 'Temporal' { & docker exec finyte-temporal temporal operator cluster health --address localhost:7233 *> $null; $LASTEXITCODE -eq 0 }
if ($Action -eq 'restart') { Docker-Run ($compose + @('stop')) }
Docker-Run ($compose + @('build', '--provenance=false'))
Docker-Run ($compose + @('up', '-d', '--no-build'))
Wait-Ready 'API' { Http-Ready 'http://127.0.0.1:5086/health' }
Wait-Ready 'Client' { Http-Ready 'http://127.0.0.1:5186/' }
Wait-Ready 'Worker' {
    $workerId = & docker @compose ps -q worker
    if (!$workerId) { return $false }
    $running = & docker inspect --format '{{.State.Running}}' $workerId
    $logs = & docker @compose logs --no-color --tail 100 worker 2>&1 | Out-String
    return $running -eq 'true' -and $logs.Contains('Temporal worker is polling task queue finyte-recurring-samples')
}
Docker-Run ($compose + @('ps'))
Write-Host "Workspace: $workspace"
Write-Host 'Database: finyte_recurring_samples (preserved between starts)'
Write-Host 'Ready: http://127.0.0.1:5186/'
