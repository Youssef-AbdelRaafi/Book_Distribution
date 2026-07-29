[CmdletBinding()]
param(
    [string]$PublishProfile = 'cambridge.local'
)

$ErrorActionPreference = 'Stop'

$projectDirectory = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $projectDirectory
$dotenvPath = Join-Path $repositoryRoot '.env'
$temporarySettingsPath = Join-Path $projectDirectory 'appsettings.Production.json'

if (-not (Test-Path -LiteralPath $dotenvPath)) {
    throw 'Missing .env. Create it from .env.example and set JWT_SIGNING_KEY before publishing.'
}

$jwtLine = Get-Content -LiteralPath $dotenvPath |
    Where-Object { $_ -match '^JWT_SIGNING_KEY=' } |
    Select-Object -First 1
$jwtSigningKey = $jwtLine -replace '^JWT_SIGNING_KEY=', ''

if ([string]::IsNullOrWhiteSpace($jwtSigningKey)) {
    throw 'JWT_SIGNING_KEY is required in .env before publishing.'
}

if (Test-Path -LiteralPath $temporarySettingsPath) {
    throw 'Refusing to overwrite an existing appsettings.Production.json.'
}

$temporarySettings = @{ Auth = @{ JwtSigningKey = $jwtSigningKey } } |
    ConvertTo-Json -Depth 3

try {
    [System.IO.File]::WriteAllText(
        $temporarySettingsPath,
        $temporarySettings,
        [System.Text.UTF8Encoding]::new($false))

    & dotnet publish (Join-Path $projectDirectory 'BookDistributionAPI.csproj') `
        --configuration Release `
        "-p:PublishProfile=$PublishProfile" `
        '-p:LaunchSiteAfterPublish=false'

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $temporarySettingsPath) {
        Remove-Item -LiteralPath $temporarySettingsPath -Force
    }
}
