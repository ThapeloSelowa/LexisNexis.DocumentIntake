# Place at the repository root

param(
    [Parameter(Position=0)]
    [string]$Command = "help"
)

# Ensure Docker CLI is on the PATH (Docker Desktop installs here on Windows)
$dockerBin = "C:\Program Files\Docker\Docker\resources\bin"
if ((Test-Path $dockerBin) -and ($env:PATH -notlike "*$dockerBin*")) {
    $env:PATH += ";$dockerBin"
}

if ($Command -eq "run") {
    Write-Host "## Starting full stack (LocalStack + API)..." -ForegroundColor Green
    docker-compose up --build
}
elseif ($Command -eq "run-detached") {
    Write-Host "## Starting full stack in background..." -ForegroundColor Green
    docker-compose up --build -d
}
elseif ($Command -eq "stop") {
    Write-Host "## Stopping all containers..." -ForegroundColor Green
    docker-compose down
}
elseif ($Command -eq "test") {
    Write-Host "## Running unit tests..." -ForegroundColor Green
    dotnet test --verbosity normal
}
elseif ($Command -eq "build") {
    Write-Host "## Building release binaries..." -ForegroundColor Green
    dotnet build --configuration Release
}
elseif ($Command -eq "clean") {
    Write-Host "## Cleaning build artifacts and stopping containers..." -ForegroundColor Green
    docker-compose down -v
    dotnet clean
    Write-Host "Removing bin/obj folders..." -ForegroundColor Yellow
    Get-ChildItem -Path . -Include "bin","obj" -Recurse -Directory | Remove-Item -Recurse -Force
    Write-Host "## Clean complete!" -ForegroundColor Green
}
elseif ($Command -eq "logs") {
    Write-Host "## Tailing API logs..." -ForegroundColor Green
    docker-compose logs -f api
}
else {
    Write-Host ""
    Write-Host "Usage: .\run.ps1 <command>" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Commands:" -ForegroundColor Yellow
    Write-Host "  run           Start the full stack (LocalStack + API) - most common command"
    Write-Host "  run-detached  Run in background"
    Write-Host "  stop          Stop all containers"
    Write-Host "  test          Run unit tests"
    Write-Host "  build         Build release binaries only"
    Write-Host "  clean         Clean build artifacts and stop containers"
    Write-Host "  logs          Tail API logs"
    Write-Host "  help          Show this help"
    Write-Host ""
}
