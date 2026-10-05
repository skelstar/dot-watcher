# Cleans up after a merged feature branch: switches to staging, pulls the latest from the
# remote, then deletes the branch you were on (after asking you to confirm).
#
# Usage: scripts/staging-delete-old-branch.ps1
$ErrorActionPreference = 'Stop'

$baseBranch = 'staging'

function Confirm-Yes([string]$prompt) {
    $answer = Read-Host "$prompt [y/N]"
    return $answer -match '^(y|yes)$'
}

$featureBranch = (git rev-parse --abbrev-ref HEAD).Trim()

if ($featureBranch -eq $baseBranch) {
    Write-Error "Already on $baseBranch; run this from the feature branch you want to delete."
}

if ($featureBranch -eq 'HEAD') {
    Write-Error 'Detached HEAD; check out the feature branch first.'
}

if (git status --porcelain) {
    Write-Error 'Working tree has uncommitted changes; commit or stash them first.'
}

Write-Host "Feature branch: $featureBranch"
git checkout $baseBranch
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git pull
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
if (-not (Confirm-Yes "Delete local branch '$featureBranch'?")) {
    Write-Host "Kept '$featureBranch'."
    exit 0
}

# -d refuses to delete a branch git doesn't see as merged, which is the case for squash-merged PRs.
# Offer a force delete in that case rather than silently using -D.
git branch -d $featureBranch
if ($LASTEXITCODE -eq 0) { exit 0 }

Write-Host ''
if (Confirm-Yes "'$featureBranch' is not fully merged into $baseBranch. Force delete anyway?") {
    git branch -D $featureBranch
} else {
    Write-Host "Kept '$featureBranch'."
}
