param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not (Get-Command git -ErrorAction SilentlyContinue) -or -not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'Install Git and GitHub CLI, then run gh auth login.'
}
if (Test-Path '.git') { throw 'A repository already exists here. Review and push it explicitly instead.' }
gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub sign-in is required.' }
git init -b main
if ($LASTEXITCODE -ne 0) { throw 'Git initialization failed.' }
git add .gitignore README.md src tests infra scripts .github .vscode
if ($LASTEXITCODE -ne 0) { throw 'Git staging failed.' }
git diff --cached --stat
Write-Host 'Review staged files for secrets before publishing. The repository will be PRIVATE.'
$confirmation = Read-Host 'Type PUSH to create the private repository and push'
if ($confirmation -ne 'PUSH') { throw 'Publishing cancelled. Files remain staged locally.' }
git commit -m 'Add real assets analytics demo with Foundry, Fabric IQ and Bicep' -m 'Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>'
if ($LASTEXITCODE -ne 0) { throw 'Git commit failed. Configure your Git identity and retry manually.' }
gh repo create $Repository --private --source . --remote origin --push
if ($LASTEXITCODE -ne 0) { throw 'GitHub publication failed. Inspect the repository state before retrying.' }
