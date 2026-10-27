[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$OutputDirectory = 'release'
)

$ErrorActionPreference = 'Stop'
$projectDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
$recordedVersion = [System.IO.File]::ReadAllText((Join-Path $projectDirectory 'VERSION')).Trim()
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $recordedVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $recordedVersion) {
    throw 'Version must match the three-part version recorded in VERSION.'
}
$metadata = [System.IO.File]::ReadAllText((Join-Path $projectDirectory 'AssemblyInfo.cs'))
if ($metadata -notmatch ('AssemblyFileVersion\("' + [regex]::Escape($Version) + '\.0"\)')) {
    throw 'AssemblyInfo.cs and VERSION do not match.'
}

if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { $outputDirectoryPath = $OutputDirectory }
else { $outputDirectoryPath = Join-Path $projectDirectory $OutputDirectory }
$outputDirectoryPath = [System.IO.Path]::GetFullPath($outputDirectoryPath).TrimEnd('\')
$projectPrefix = $projectDirectory.TrimEnd('\') + '\'
if (-not $outputDirectoryPath.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The release directory must be a child of this project directory.'
}
[void][System.IO.Directory]::CreateDirectory($outputDirectoryPath)
$outputPrefix = $outputDirectoryPath + '\'

# Explicit source allowlist. Never package the working directory or live dist.
$sourceFiles = @(
    '.gitignore', '.gitattributes', 'LICENSE', 'VERSION', 'README.md', 'CHANGELOG.md',
    'AssemblyInfo.cs', 'Build.ps1', 'Package.ps1', 'app.manifest', 'CornerTodo.exe.config',
    'AppearanceSettings.cs', 'AppearanceEditor.cs',
    'DesktopHost.cs', 'Diagnostics.cs', 'GrowthLayout.cs', 'MainWindow.cs', 'Models.cs',
    'Program.cs', 'StartupManager.cs', 'LaunchOptions.cs', 'TaskEditor.cs', 'Theme.cs', 'TodoStore.cs',
    'tests/StoreTests.cs', 'tests/Run-StoreTests.ps1',
    'tests/GrowthLayoutTests.cs', 'tests/Run-GrowthLayoutTests.ps1',
    'tests/OffscreenUiTests.cs', 'tests/Run-OffscreenUiTests.ps1', 'tests/Inspect-Windows.ps1',
    'tests/AppearanceUiTests.cs', 'tests/Run-AppearanceUiTests.ps1',
    'tests/StartupTests.cs', 'tests/LaunchOptionsTests.cs', 'tests/StartupUiTests.cs', 'tests/Run-StartupTests.ps1',
    'docs/PUBLISHING.md', 'docs/PORTABLE-README.txt', 'docs/RELEASE_NOTES.md', 'docs/STARTUP.md',
    'docs/images/normal.png', 'docs/images/urgent.png', 'docs/images/overdue.png'
)
foreach ($relativePath in $sourceFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectDirectory $relativePath) -PathType Leaf)) {
        throw ('A required public source file is missing: ' + $relativePath)
    }
}

function Copy-SourceFiles([string]$From, [string]$To) {
    foreach ($relativePath in $sourceFiles) {
        $targetFile = Join-Path $To $relativePath
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $targetFile))
        Copy-Item -LiteralPath (Join-Path $From $relativePath) -Destination $targetFile
    }
}

function New-PublicArchive([string]$ArchivePath, [string]$RootName, [string]$SourceRoot, [string[]]$Files) {
    $output = [System.IO.File]::Open($ArchivePath, [System.IO.FileMode]::CreateNew)
    $archive = $null
    try {
        $archive = New-Object System.IO.Compression.ZipArchive -ArgumentList @($output, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        foreach ($relativePath in $Files) {
            # Explicit entries retain dotfiles and use portable ZIP separators.
            $entry = $archive.CreateEntry($RootName + '/' + $relativePath.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal)
            $input = [System.IO.File]::OpenRead((Join-Path $SourceRoot $relativePath))
            $entryOutput = $null
            try { $entryOutput = $entry.Open(); $input.CopyTo($entryOutput) }
            finally { if ($null -ne $entryOutput) { $entryOutput.Dispose() }; $input.Dispose() }
        }
    }
    finally { if ($null -ne $archive) { $archive.Dispose() }; $output.Dispose() }
}

function Assert-Archive([string]$ArchivePath, [string]$RootName, [string]$SourceRoot, [string[]]$ExpectedFiles) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if ($name.Contains('\')) { throw ('Nonportable archive separator: ' + $name) }
            if ($name.EndsWith('/')) { continue }
            if (-not $name.StartsWith($RootName + '/', [System.StringComparison]::Ordinal)) {
                throw ('Unexpected archive root: ' + $name)
            }
            $relativePath = $name.Substring($RootName.Length + 1)
            if ($ExpectedFiles -cnotcontains $relativePath -or $seen.ContainsKey($relativePath)) {
                throw ('Unexpected or duplicate archive entry: ' + $name)
            }
            $seen[$relativePath] = $true
            $stream = $entry.Open()
            $hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
            try { $actualHash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $hashAlgorithm.Dispose() }
            $expectedHash = (Get-FileHash -LiteralPath (Join-Path $SourceRoot $relativePath) -Algorithm SHA256).Hash
            if ($actualHash -ne $expectedHash) { throw ('Archive hash mismatch: ' + $name) }
        }
        if ($seen.Count -ne $ExpectedFiles.Count) { throw 'The archive is missing required files.' }
    }
    finally { $archive.Dispose() }
}

$workDirectory = Join-Path $outputDirectoryPath ('.package-' + [Guid]::NewGuid().ToString('N'))
$sourceName = 'CornerTodo-v' + $Version + '-source'
$portableName = 'CornerTodo-v' + $Version + '-windows-portable'
$sourceRoot = Join-Path $workDirectory $sourceName
$portableRoot = Join-Path $workDirectory 'CornerTodo'
$verificationRoot = Join-Path $workDirectory 'verify-source'
$preserveWorkDirectory = $false
try {
    Copy-SourceFiles $projectDirectory $sourceRoot
    Copy-SourceFiles $sourceRoot $verificationRoot

    # Compile and test the exact public source set in an isolated copy.
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $verificationRoot 'Build.ps1') -Test
    if ($LASTEXITCODE -ne 0) { throw 'The clean source build or tests failed; no release was replaced.' }
    $builtDirectory = Join-Path $verificationRoot 'dist'
    $builtExecutable = Join-Path $builtDirectory 'CornerTodo.exe'
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($builtExecutable).FileVersion
    if ($fileVersion -ne ($Version + '.0')) { throw 'The built executable has an unexpected version.' }

    [void][System.IO.Directory]::CreateDirectory($portableRoot)
    foreach ($file in @('CornerTodo.exe', 'CornerTodo.exe.config', 'CornerTodo.ico')) {
        Copy-Item -LiteralPath (Join-Path $builtDirectory $file) -Destination (Join-Path $portableRoot $file)
    }
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'LICENSE') -Destination (Join-Path $portableRoot 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'docs/PORTABLE-README.txt') -Destination (Join-Path $portableRoot 'README.txt')
    $portableFiles = @('CornerTodo.exe', 'CornerTodo.exe.config', 'CornerTodo.ico', 'LICENSE.txt', 'README.txt')

    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $sourceZip = Join-Path $workDirectory ($sourceName + '.zip')
    $portableZip = Join-Path $workDirectory ($portableName + '.zip')
    New-PublicArchive $sourceZip $sourceName $sourceRoot $sourceFiles
    New-PublicArchive $portableZip 'CornerTodo' $portableRoot $portableFiles
    Assert-Archive $sourceZip $sourceName $sourceRoot $sourceFiles
    Assert-Archive $portableZip 'CornerTodo' $portableRoot $portableFiles

    $checksums = foreach ($zip in @($sourceZip, $portableZip)) {
        (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($zip)
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllLines((Join-Path $workDirectory 'SHA256SUMS.txt'), [string[]]$checksums, $utf8)
    $report = @(
        ('CornerTodo v' + $Version),
        'Clean public source build and all five test suites: PASS',
        ('Source archive allowlist and per-file hashes: PASS (' + $sourceFiles.Count + ' files)'),
        ('Portable archive allowlist and per-file hashes: PASS (' + $portableFiles.Count + ' files)'),
        ('Executable file version: ' + $fileVersion),
        'Runtime Data, logs, backups, local test data, and previous builds: excluded',
        'No repository was created and no files were uploaded.'
    )
    [System.IO.File]::WriteAllLines((Join-Path $workDirectory 'PACKAGE-REPORT.txt'), [string[]]$report, $utf8)

    # Publish locally only after every check succeeds. Preserve previous bundles.
    $archiveDirectory = Join-Path $outputDirectoryPath ('previous-bundles-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
    $published = @()
    $preserved = @()
    try {
        foreach ($name in @($sourceName, ($sourceName + '.zip'), ($portableName + '.zip'), 'SHA256SUMS.txt', 'PACKAGE-REPORT.txt')) {
            $destination = [System.IO.Path]::GetFullPath((Join-Path $outputDirectoryPath $name))
            $stagedPath = [System.IO.Path]::GetFullPath((Join-Path $workDirectory $name))
            if (-not $destination.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not $stagedPath.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release destination.' }
            if (Test-Path -LiteralPath $destination) {
                [void][System.IO.Directory]::CreateDirectory($archiveDirectory)
                $preservedPath = [System.IO.Path]::GetFullPath((Join-Path $archiveDirectory $name))
                if (-not $preservedPath.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid backup destination.' }
                Move-Item -LiteralPath $destination -Destination $preservedPath
                $preserved += @{ Original = $destination; Backup = $preservedPath }
            }
            Move-Item -LiteralPath $stagedPath -Destination $destination
            $published += @{ Current = $destination; Staged = $stagedPath }
        }
    }
    catch {
        $publishError = $_
        try {
            for ($index = $published.Count - 1; $index -ge 0; $index--) {
                Move-Item -LiteralPath $published[$index].Current -Destination $published[$index].Staged
            }
            for ($index = $preserved.Count - 1; $index -ge 0; $index--) {
                Move-Item -LiteralPath $preserved[$index].Backup -Destination $preserved[$index].Original
            }
        }
        catch {
            $preserveWorkDirectory = $true
            throw ('Release replacement and rollback failed. Recovery files were preserved in: ' + $workDirectory + ' and ' + $archiveDirectory)
        }
        throw $publishError
    }
    Write-Output ('Release files: ' + $outputDirectoryPath)
    Write-Output ('Source files: ' + $sourceFiles.Count + '; portable files: ' + $portableFiles.Count)
    foreach ($line in $checksums) { Write-Output $line }
}
finally {
    $resolvedWorkDirectory = [System.IO.Path]::GetFullPath($workDirectory)
    if (-not $preserveWorkDirectory -and $resolvedWorkDirectory.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedWorkDirectory) -match '^\.package-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolvedWorkDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
