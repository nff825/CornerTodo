[CmdletBinding()]
param()

# This is an independent integration check. Do not add it to the functional suites
# invoked by Package.ps1 -> Build.ps1 -Test, which this script itself exercises.
# Session, panel-controller and view checks belong to those registered functional suites.
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectDirectory 'build\Common.ps1')
$script:buildCheckCount = 0
$testOutputRoot = Join-Path $projectDirectory 'output'
$testDirectory = $null
# Short fixture paths leave room for Package.ps1's nested clean-build copy on
# Windows PowerShell 5.1, while the space still exercises native argument quoting.
$sandboxProject = $null
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'

function New-BuildTestDirectory {
    if (-not (Test-Path -LiteralPath $testOutputRoot -PathType Container)) {
        try { [void](New-Item -ItemType Directory -Path $testOutputRoot -ErrorAction Stop) }
        catch {
            if (-not (Test-Path -LiteralPath $testOutputRoot -PathType Container)) { throw }
        }
    }
    for ($attempt = 0; $attempt -lt 32; $attempt++) {
        $shortId = [Guid]::NewGuid().ToString('N').Substring(0, 8)
        $candidate = Join-Path $testOutputRoot ('b-' + $shortId)
        try {
            # No -Force: an existing directory is a collision, never our fixture.
            $created = New-Item -ItemType Directory -Path $candidate -ErrorAction Stop
            return $created.FullName
        }
        catch {
            if (Test-Path -LiteralPath $candidate) { continue }
            throw
        }
    }
    throw 'Could not create an unused short build-regression directory.'
}

function Assert-BuildCheck {
    param([bool]$Condition, [string]$Message)
    $script:buildCheckCount++
    if (-not $Condition) { throw ('Build regression failed: ' + $Message) }
}

function Resolve-BuildTestPath {
    param([string]$RelativePath)
    $prefix = [IO.Path]::GetFullPath($sandboxProject).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath((Join-Path $sandboxProject $RelativePath))
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('The test path leaves the isolated project: ' + $RelativePath)
    }
    return $resolved
}

function Write-BuildTestFile {
    param([string]$RelativePath, [string]$Contents)
    $path = Resolve-BuildTestPath $RelativePath
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, $Contents, (New-Object Text.UTF8Encoding $false))
}

function Invoke-BuildTestChild {
    param([string]$ScriptName, [string[]]$Arguments, [string]$Description)
    $scriptPath = Resolve-BuildTestPath $ScriptName
    $previousPreference = $ErrorActionPreference
    try {
        # Preserve complete native diagnostics, including stderr, before deciding
        # whether the isolated child failed. The caller still fails on nonzero.
        $ErrorActionPreference = 'Continue'
        $output = & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($exitCode -ne 0) {
        foreach ($line in $output) { Write-Output $line }
        throw ($Description + ' failed with exit code ' + $exitCode + '.')
    }
    Write-Output ('PASS: ' + $Description)
}

function Assert-RejectedBuildDeclaration {
    param([string]$Description, [scriptblock]$Mutate)
    $path = Resolve-BuildTestPath 'CornerTodo.Build.props'
    $before = Get-CornerTodoBuildDefinition -ProjectDirectory $sandboxProject
    $originalBytes = [IO.File]::ReadAllBytes($path)
    try {
        $document = New-Object Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($path)
        & $Mutate $document
        $document.Save($path)
        $rejected = $false
        try { $null = Get-CornerTodoBuildDefinition -ProjectDirectory $sandboxProject }
        catch { $rejected = $true }
        Assert-BuildCheck $rejected ('The shared definition accepted an unsupported declaration: ' + $Description)
    }
    finally { [IO.File]::WriteAllBytes($path, $originalBytes) }
    $restored = Get-CornerTodoBuildDefinition -ProjectDirectory $sandboxProject
    Assert-BuildCheck ($restored.SourceFiles.Count -eq $before.SourceFiles.Count) ('The declaration rejection check failed to restore the valid manifest: ' + $Description)
}

try {
    $testDirectory = New-BuildTestDirectory
    $sandboxProject = Join-Path $testDirectory 'p s'
    $originalDefinition = Get-CornerTodoBuildDefinition -ProjectDirectory $projectDirectory
    $publicPaths = @(Get-CornerTodoPublicSourceFiles -Definition $originalDefinition)
    $originalManifestPath = Join-Path $projectDirectory 'CornerTodo.Build.props'
    $originalManifestHash = (Get-FileHash -LiteralPath $originalManifestPath -Algorithm SHA256).Hash

    # Copy exactly the public inventory, never a whole checkout or runtime folder.
    [void][IO.Directory]::CreateDirectory($sandboxProject)
    foreach ($relativePath in $publicPaths) {
        $sourcePath = Resolve-CornerTodoPath $projectDirectory $relativePath
        $destination = Resolve-BuildTestPath $relativePath
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
        Copy-Item -LiteralPath $sourcePath -Destination $destination
    }

    # The shared contract must reject declarations that MSBuild could apply but
    # the script compiler does not implement, even if their values are literals.
    Assert-RejectedBuildDeclaration 'literal OutputPath property' {
        param($document)
        $property = $document.CreateElement('OutputPath', $document.DocumentElement.NamespaceURI)
        $property.InnerText = 'dist/'
        [void]$document.SelectSingleNode('/*[local-name()="Project"]/*[local-name()="PropertyGroup"]').AppendChild($property)
    }
    Assert-RejectedBuildDeclaration 'Reference HintPath metadata' {
        param($document)
        $hint = $document.CreateElement('HintPath', $document.DocumentElement.NamespaceURI)
        $hint.InnerText = 'Other.dll'
        [void]$document.SelectSingleNode('//*[local-name()="Reference"]').AppendChild($hint)
    }
    Assert-RejectedBuildDeclaration 'conditional Compile item' {
        param($document)
        $document.SelectSingleNode('//*[local-name()="Compile"]').SetAttribute('Condition', "'true' == 'true'")
    }
    Assert-RejectedBuildDeclaration 'wildcard Compile Include' {
        param($document)
        $document.SelectSingleNode('//*[local-name()="Compile"]').SetAttribute('Include', '*.cs')
    }
    Write-Output 'PASS: unsupported literal properties, reference metadata, conditions and source globs are rejected'

    $poisonFiles = @(
        'RootNoise.cs',
        'tests/nested/NestedNoise.cs',
        'release/old-source/OldReleaseNoise.cs',
        'dist/old-source/OldDistNoise.cs'
    )
    foreach ($relativePath in $poisonFiles) {
        Write-BuildTestFile $relativePath '#error UNDECLARED_SOURCE_MUST_NEVER_BE_COMPILED'
    }
    Write-BuildTestFile 'Data/build-regression-sentinel.txt' 'Synthetic test content. This is not personal task data.'
    $sentinelPath = Resolve-BuildTestPath 'Data/build-regression-sentinel.txt'
    $sentinelHash = (Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash

    $isolatedDefinition = Get-CornerTodoBuildDefinition -ProjectDirectory $sandboxProject
    Assert-BuildCheck ($isolatedDefinition.SourceFiles.Count -eq $originalDefinition.SourceFiles.Count) 'Adding undeclared files changed the application source count.'
    $sandboxPrefix = [IO.Path]::GetFullPath($sandboxProject).TrimEnd('\') + '\'
    foreach ($sourcePath in $isolatedDefinition.SourceFiles) {
        Assert-BuildCheck ($sourcePath.StartsWith($sandboxPrefix, [StringComparison]::OrdinalIgnoreCase)) 'A clone build resolved a source from outside its own project.'
    }
    foreach ($relativePath in $poisonFiles) {
        Assert-BuildCheck ($isolatedDefinition.RelativeSourceFiles -notcontains $relativePath) ('An undeclared file entered the compile list: ' + $relativePath)
    }
    Invoke-BuildTestChild -ScriptName 'Build.ps1' -Arguments @('-OutputDirectory', 'o') -Description 'shared compile list excludes root, nested tests, old release and dist sources'
    $compiledExe = Resolve-BuildTestPath 'o/CornerTodo.exe'
    Assert-BuildCheck (Test-Path -LiteralPath $compiledExe -PathType Leaf) 'The isolated application build did not produce an executable.'

    # The default developer build must not overwrite a daily-use dist program.
    Write-BuildTestFile 'dist/CornerTodo.exe' 'Synthetic daily-use executable sentinel.'
    $dailyExecutable = Resolve-BuildTestPath 'dist/CornerTodo.exe'
    $dailyExecutableHash = (Get-FileHash -LiteralPath $dailyExecutable -Algorithm SHA256).Hash
    Invoke-BuildTestChild -ScriptName 'Build.ps1' -Arguments @() -Description 'default development build uses output/build and preserves the daily-use executable'
    Assert-BuildCheck (Test-Path -LiteralPath (Resolve-BuildTestPath 'output/build/CornerTodo.exe') -PathType Leaf) 'The default build did not use output/build.'
    Assert-BuildCheck ((Get-FileHash -LiteralPath $dailyExecutable -Algorithm SHA256).Hash -eq $dailyExecutableHash) 'The default build changed the daily-use executable sentinel.'

    # Move one declared production source and change only its shared Include.
    # Group metadata is retained; Build, runners, Package and csproj stay intact.
    $growthSources = @($isolatedDefinition.Sources | Where-Object { [IO.Path]::GetFileName($_.Path) -eq 'GrowthLayout.cs' })
    Assert-BuildCheck ($growthSources.Count -eq 1) 'Expected exactly one GrowthLayout source in the shared definition.'
    $growthSource = $growthSources[0]
    $oldRelativePath = $growthSource.RelativePath.Replace('\', '/')
    $newRelativePath = 'src/m s/GrowthLayout.cs'
    $newSourcePath = Resolve-BuildTestPath $newRelativePath
    $oldSourcePath = Resolve-BuildTestPath $oldRelativePath
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $newSourcePath))
    Move-Item -LiteralPath $oldSourcePath -Destination $newSourcePath

    $manifestPath = Resolve-BuildTestPath 'CornerTodo.Build.props'
    $manifest = New-Object Xml.XmlDocument
    $manifest.XmlResolver = $null
    $manifest.Load($manifestPath)
    $matchingItems = @($manifest.SelectNodes('//*[local-name()="Compile"]') | Where-Object {
        $_.GetAttribute('Include').Replace('\', '/') -eq $oldRelativePath
    })
    Assert-BuildCheck ($matchingItems.Count -eq 1) 'The moved source does not have a unique shared Compile declaration.'
    $matchingItems[0].SetAttribute('Include', $newRelativePath)
    $manifest.Save($manifestPath)

    $movedDefinition = Get-CornerTodoBuildDefinition -ProjectDirectory $sandboxProject
    Assert-BuildCheck ($movedDefinition.RelativeSourceFiles -contains $newRelativePath) 'The shared definition did not pick up the moved source.'
    Assert-BuildCheck ($movedDefinition.RelativeSourceFiles -notcontains $oldRelativePath) 'The shared definition retained the old source path.'
    Assert-BuildCheck (@(Get-CornerTodoSourceFiles -Definition $movedDefinition -Group 'Growth') -contains $newSourcePath) 'Moving a source broke its test-group membership.'
    $movedPublicPaths = @(Get-CornerTodoPublicSourceFiles -Definition $movedDefinition)
    Assert-BuildCheck ($movedPublicPaths -contains $newRelativePath) 'The public inventory omitted the moved source.'
    Assert-BuildCheck ($movedPublicPaths -notcontains $oldRelativePath) 'The public inventory retained the old source path.'
    foreach ($relativePath in @($poisonFiles) + @('Data/build-regression-sentinel.txt')) {
        Assert-BuildCheck ($movedPublicPaths -notcontains $relativePath) ('The public inventory included undeclared or runtime content: ' + $relativePath)
    }

    $version = [IO.File]::ReadAllText((Resolve-BuildTestPath 'VERSION')).Trim()
    Invoke-BuildTestChild -ScriptName 'Package.ps1' -Arguments @('-Version', $version, '-OutputDirectory', 'pkg') -Description 'moved source survives a clean application build, registered functional suites and packaging'
    $archiveRoot = 'CornerTodo-v' + $version + '-source'
    $sourceZip = Resolve-BuildTestPath ('pkg/' + $archiveRoot + '.zip')
    Assert-BuildCheck (Test-Path -LiteralPath $sourceZip -PathType Leaf) 'The clean source package was not produced.'

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
    try {
        $relativeEntries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | ForEach-Object {
            $prefix = $archiveRoot + '/'
            if (-not $_.FullName.StartsWith($prefix, [StringComparison]::Ordinal)) { throw ('Unexpected source archive entry: ' + $_.FullName) }
            $_.FullName.Substring($prefix.Length)
        })
        Assert-BuildCheck ($relativeEntries.Count -eq $movedPublicPaths.Count) 'The source archive does not match the shared public inventory count.'
        foreach ($relativePath in $movedPublicPaths) {
            Assert-BuildCheck ($relativeEntries -contains $relativePath) ('A declared public file is missing from the source package: ' + $relativePath)
        }
        Assert-BuildCheck ($relativeEntries -notcontains $oldRelativePath) 'The source archive still contains the pre-move source path.'
        foreach ($relativePath in @($poisonFiles) + @('Data/build-regression-sentinel.txt')) {
            Assert-BuildCheck ($relativeEntries -notcontains $relativePath) ('The source archive included noise or runtime content: ' + $relativePath)
        }
        $movedEntry = $archive.GetEntry($archiveRoot + '/' + $newRelativePath)
        Assert-BuildCheck ($null -ne $movedEntry) 'The moved source entry is absent from the package.'
        $stream = $movedEntry.Open()
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $archivedHash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $algorithm.Dispose() }
        Assert-BuildCheck ($archivedHash -eq (Get-FileHash -LiteralPath $newSourcePath -Algorithm SHA256).Hash) 'The moved source bytes differ from the packaged source.'
    }
    finally { $archive.Dispose() }

    # Seed an older generation in the default version folder. One normal package
    # run must publish the new generation and preserve all older bytes centrally.
    $versionDirectory = 'release/v' + $version
    $olderFiles = @(
        ($archiveRoot + '/old-generation.txt'),
        ($archiveRoot + '.zip'),
        ('CornerTodo-v' + $version + '-windows-portable.zip'),
        'SHA256SUMS.txt',
        'PACKAGE-REPORT.txt'
    )
    $olderHashes = @{}
    foreach ($relativePath in $olderFiles) {
        $olderRelativePath = $versionDirectory + '/' + $relativePath
        Write-BuildTestFile $olderRelativePath ('Synthetic previous package: ' + $relativePath)
        $olderHashes[$relativePath] = (Get-FileHash -LiteralPath (Resolve-BuildTestPath $olderRelativePath) -Algorithm SHA256).Hash
    }
    Invoke-BuildTestChild -ScriptName 'Package.ps1' -Arguments @() -Description 'default package groups versions and archives the previous generation outside release'
    Assert-BuildCheck (Test-Path -LiteralPath (Resolve-BuildTestPath ($versionDirectory + '/' + $archiveRoot + '.zip')) -PathType Leaf) 'The default source package was not placed in its version directory.'
    Assert-BuildCheck (Test-Path -LiteralPath (Resolve-BuildTestPath ($versionDirectory + '/SHA256SUMS.txt')) -PathType Leaf) 'The default package checksums were not placed in the version directory.'
    Assert-BuildCheck (-not (Test-Path -LiteralPath (Resolve-BuildTestPath 'release/SHA256SUMS.txt'))) 'The default package left unversioned checksums in release.'
    $backupBase = Resolve-BuildTestPath ('archive/packages/v' + $version)
    $backupDirectories = @(Get-ChildItem -LiteralPath $backupBase -Directory | Where-Object { $_.Name -match '^previous-bundles-\d{8}-\d{6}-[a-f0-9]{6}$' })
    Assert-BuildCheck ($backupDirectories.Count -eq 1) 'The previous generation was not archived exactly once.'
    $backupDirectory = $backupDirectories[0].FullName
    foreach ($relativePath in $olderFiles) {
        $preservedFile = [IO.Path]::GetFullPath((Join-Path $backupDirectory $relativePath))
        Assert-BuildCheck ($preservedFile.StartsWith($sandboxPrefix, [StringComparison]::OrdinalIgnoreCase)) 'A previous-generation path escaped the isolated project.'
        Assert-BuildCheck ((Get-FileHash -LiteralPath $preservedFile -Algorithm SHA256).Hash -eq $olderHashes[$relativePath]) ('The old package bytes were not preserved: ' + $relativePath)
    }
    $origin = Get-Content -LiteralPath (Join-Path $backupDirectory 'BACKUP-ORIGIN.json') -Raw | ConvertFrom-Json
    Assert-BuildCheck ($origin.Version -eq $version -and $origin.OriginalOutputDirectory -eq (Resolve-BuildTestPath $versionDirectory)) 'The package backup does not identify its original output directory.'
    Assert-BuildCheck (@(Get-ChildItem -LiteralPath (Resolve-BuildTestPath 'release') -Directory -Recurse | Where-Object { $_.Name -like 'previous-bundles-*' }).Count -eq 0) 'Package history is still scattered underneath release.'
    Assert-BuildCheck (-not (Test-Path -LiteralPath (Resolve-BuildTestPath ($versionDirectory + '/' + $archiveRoot + '/old-generation.txt')))) 'An old package marker leaked into the new public source folder.'
    Assert-BuildCheck ((Get-FileHash -LiteralPath $dailyExecutable -Algorithm SHA256).Hash -eq $dailyExecutableHash) 'Packaging changed the daily-use executable sentinel.'

    Assert-BuildCheck ((Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash -eq $sentinelHash) 'The isolated build changed its synthetic runtime-data sentinel.'
    Assert-BuildCheck ((Get-FileHash -LiteralPath $originalManifestPath -Algorithm SHA256).Hash -eq $originalManifestHash) 'The integration check changed the real shared definition.'
    Write-Output ('PASS: ' + $script:buildCheckCount + ' isolated build/packaging regression checks.')
    Write-Output 'No production executable was run; all builds, packages, sample test data and source moves used the test-owned project copy.'
}
finally {
    # Assigned only after New-Item created a new directory successfully.
    if ($null -ne $testDirectory) {
        $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
        $testOutputPrefix = [IO.Path]::GetFullPath($testOutputRoot).TrimEnd('\') + '\'
        if ($resolvedTestDirectory.StartsWith($testOutputPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($resolvedTestDirectory) -match '^b-[a-f0-9]{8}$') {
            Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
