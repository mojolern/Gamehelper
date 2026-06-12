param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "GenerateUpdateSigningKey\GenerateUpdateSigningKey.csproj"
if (-not (Test-Path $project)) {
    throw "GenerateUpdateSigningKey-Projekt fehlt: $project"
}

dotnet run --project $project -c Release -- sign $ManifestPath
if ($LASTEXITCODE -ne 0) {
    throw "Manifest-Signierung fehlgeschlagen."
}
