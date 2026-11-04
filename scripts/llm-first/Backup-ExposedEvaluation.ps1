# Owner-only preparation. This script never removes files or changes Git.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BackupDirectory,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..')
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$backup = [IO.Path]::GetFullPath($BackupDirectory)
$separator = [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::IsPathFullyQualified($BackupDirectory) -or $backup.StartsWith('\\') -or
    $backup.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
    $backup.StartsWith($repository.TrimEnd($separator) + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backup must be an absolute private local directory outside the repository.'
}
# Reject existing linked ancestors: the destination must not resolve into Git.
$ancestor = $backup
while ($ancestor) {
    if (Test-Path -LiteralPath $ancestor) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Linked backup paths require independent private custody; no copy made.'
        }
    }
    $parent = [IO.Path]::GetDirectoryName($ancestor)
    if ($parent -eq $ancestor) { break }; $ancestor = $parent
}
$names = @('NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json', 'NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md')
# Check all sources before touching custody storage. No content is printed.
foreach ($name in $names) {
    $source = Get-Item -LiteralPath (Join-Path $repository $name) -Force
    if ($source.PSIsContainer -or ($source.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid evaluation source.' }
}
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($name in $names) {
    $source = Join-Path $repository $name; $target = Join-Path $backup $name
    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target -Force
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid custody target.' }
    } else { [IO.File]::Copy($source, $target, $false) }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw 'Custody verification failed. Preserve originals; do not retire Git copies.'
    }
}
Write-Output 'Both private copies verified; originals and Git remain unchanged.'
Write-Output 'Owner: verify private-directory ACLs and approve a normal removal commit of the two exact paths. Public history remains exposed.'
