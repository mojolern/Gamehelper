# Erzeugt update-signing.key (gitignored) und aktualisiert Shared/UpdateSigningPublicKey.cs
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "GenerateUpdateSigningKey\GenerateUpdateSigningKey.csproj"
if (-not (Test-Path $project)) {
    throw "GenerateUpdateSigningKey-Projekt fehlt: $project"
}

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Schlüssel-Erzeugung fehlgeschlagen."
}
