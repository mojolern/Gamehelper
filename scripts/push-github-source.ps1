# Quellcode + CREDITS.md + README.md nach GitHub pushen (separat von Release-Binaries).
# Releases enthalten nur vorkompilierte Dateien; der Code-Tab wird ueber dieses Script befuellt.
param(
    [string]$Repository = "MordWraith/Gamehelper",
    [string]$Branch = "main",
    [string]$GitExe = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent

function Resolve-GitExecutable {
    param([string]$OverridePath)

    if (-not [string]::IsNullOrWhiteSpace($OverridePath)) {
        if (Test-Path $OverridePath) { return $OverridePath }
        throw "Git nicht gefunden: $OverridePath"
    }

    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "C:\Program Files\Git\cmd\git.exe",
        "C:\Program Files\Git\bin\git.exe",
        "C:\Program Files (x86)\Git\cmd\git.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }

    throw @"
Git ist nicht installiert oder nicht im PATH.

Install (empfohlen):
  winget install --id Git.Git -e --source winget

Danach PowerShell neu oeffnen und erneut ausfuehren:
  powershell -ExecutionPolicy Bypass -File scripts\push-github-source.ps1

Oder: https://git-scm.com/download/win
"@
}

function Invoke-Git {
    param([string[]]$GitArguments)
    & $script:GitExe @GitArguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArguments -join ' ') fehlgeschlagen (exit $LASTEXITCODE)"
    }
}

function Get-GitCommitIdentity {
    $user = gh api user 2>$null | ConvertFrom-Json
    if (-not $user) {
        return @{ Name = "MordWraith"; Email = "MordWraith@users.noreply.github.com" }
    }

    $email = $user.email
    if ([string]::IsNullOrWhiteSpace($email)) {
        $email = "{0}+{1}@users.noreply.github.com" -f $user.id, $user.login
    }

    return @{ Name = $user.login; Email = $email }
}

$configPath = Join-Path $Root "github.config.json"
if (Test-Path $configPath) {
    $cfg = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($cfg.repository) { $Repository = $cfg.repository }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) fehlt. Install: winget install GitHub.cli"
}

$null = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "gh nicht eingeloggt: gh auth login"
}

$script:GitExe = Resolve-GitExecutable -OverridePath $GitExe
Write-Host "Git: $GitExe" -ForegroundColor DarkGray

Push-Location $Root
try {
    if (-not (Test-Path (Join-Path $Root ".git"))) {
        Invoke-Git @("init", "-b", $Branch)
        Write-Host "Git-Repo initialisiert." -ForegroundColor Green
    }

    $remoteUrl = "https://github.com/$Repository.git"
    $remotes = & $GitExe remote 2>$null
    if ($remotes -notcontains "origin") {
        Invoke-Git @("remote", "add", "origin", $remoteUrl)
    }
    else {
        Invoke-Git @("remote", "set-url", "origin", $remoteUrl)
    }

    Invoke-Git @("add", "-A")
    $status = & $GitExe status --porcelain
    if (-not $status) {
        Write-Host "Keine Aenderungen zum Pushen." -ForegroundColor Yellow
        return
    }

    $identity = Get-GitCommitIdentity
    Invoke-Git @(
        "-c", "user.name=$($identity.Name)",
        "-c", "user.email=$($identity.Email)",
        "commit", "-m", "Publish open-source tree ($((Get-Date).ToString('yyyy-MM-dd')))"
    )
    Invoke-Git @("push", "-u", "origin", $Branch)
    Write-Host ""
    Write-Host "Quellcode gepusht: https://github.com/$Repository" -ForegroundColor Green
    Write-Host "Pruefen:       https://github.com/$Repository/tree/$Branch/GameHelper" -ForegroundColor DarkGray
    Write-Host "Credits:       https://github.com/$Repository/blob/$Branch/CREDITS.md" -ForegroundColor DarkGray
    Write-Host "Binaries:      https://github.com/$Repository/releases" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
