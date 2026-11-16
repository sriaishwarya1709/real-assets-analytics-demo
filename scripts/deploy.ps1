param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$Location
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
foreach ($tool in @('az', 'dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is required." }
}
foreach ($variable in @('AZURE_TENANT_ID', 'AZURE_AD_CLIENT_ID', 'AZURE_AD_CLIENT_SECRET')) {
    if (-not [Environment]::GetEnvironmentVariable($variable)) { throw "Set $variable without committing it." }
}
az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) { throw 'Azure subscription selection failed. Run az login first.' }
$env:AZURE_LOCATION = $Location
az group create --name $ResourceGroup --location $Location --output none
if ($LASTEXITCODE -ne 0) { throw 'Resource group creation failed.' }
az deployment group what-if --resource-group $ResourceGroup --parameters infra\main.bicepparam
if ($LASTEXITCODE -ne 0) { throw 'Bicep validation failed.' }
$confirmation = Read-Host 'Create these billable resources? Type DEPLOY to continue'
if ($confirmation -ne 'DEPLOY') { throw 'Deployment cancelled.' }
$raw = az deployment group create --name portfolio-lens --resource-group $ResourceGroup --parameters infra\main.bicepparam --output json
if ($LASTEXITCODE -ne 0) { throw 'Infrastructure deployment failed.' }
$deployment = ($raw -join "`n") | ConvertFrom-Json
$name = $deployment.properties.outputs.webAppName.value
$redirect = $deployment.properties.outputs.redirectUri.value
Write-Host "Add this Web redirect URI to your Entra application before opening the site: $redirect"
dotnet publish src\PortfolioLens\PortfolioLens.csproj -c Release -o artifacts\publish
if ($LASTEXITCODE -ne 0) { throw '.NET publish failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = Join-Path $PWD 'artifacts\app.zip'
if (Test-Path $archivePath) { Remove-Item $archivePath }
$publishPath = (Resolve-Path artifacts\publish).Path
$archive = [System.IO.Compression.ZipFile]::Open($archivePath, 'Create')
try {
    foreach ($file in Get-ChildItem $publishPath -Recurse -File) {
        $entryName = $file.FullName.Substring($publishPath.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName) | Out-Null
    }
}
finally { $archive.Dispose() }
az webapp deploy --resource-group $ResourceGroup --name $name --src-path artifacts\app.zip --type zip --output none
if ($LASTEXITCODE -ne 0) { throw 'Web app deployment failed.' }
az webapp restart --resource-group $ResourceGroup --name $name
if ($LASTEXITCODE -ne 0) { throw 'Web app restart failed.' }
$url = $deployment.properties.outputs.webUrl.value
$healthy = $false
for ($attempt = 0; $attempt -lt 12; $attempt++) {
    try {
        $health = Invoke-RestMethod "$url/health"
        if ($health.status -eq 'healthy') { $healthy = $true; break }
    }
    catch [System.Net.WebException], [System.Net.Http.HttpRequestException] {
        Write-Warning "Health check not ready (attempt $($attempt + 1))."
    }
    Start-Sleep -Seconds 10
}
if (-not $healthy) { throw "Deployment completed but $url/health is not healthy. Inspect App Service logs and Key Vault reference resolution." }
Write-Host "Healthy deployment: $url"
Write-Host 'Next: verify sign-in, a permitted Fabric query, a denied-user query, and PDF export.'
