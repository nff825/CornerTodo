param(
    [ValidateSet('all','arm64','x64')][string]$Architecture = 'all',
    [string]$DotNetPath = '',
    [string]$PythonPath = '',
    [string]$OutputDirectory = '',
    [string]$NuGetConfigPath = '',
    [string]$ToolingDirectory = 'output/macOS/tooling'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$separator = [IO.Path]::DirectorySeparatorChar
$pathComparison = if ($separator -eq '\') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$projectPrefix = $projectRoot.TrimEnd($separator) + $separator
$version = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid macOS version.' }
function ProjectPath([string]$Path) {
    $resolved = if ([IO.Path]::IsPathRooted($Path)) { [IO.Path]::GetFullPath($Path) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $Path)) }
    if (-not $resolved.StartsWith($projectPrefix,$pathComparison)) { throw 'Output and tooling paths must be inside CornerTodo.' }
    $ancestor = $resolved
    while ($ancestor.StartsWith($projectPrefix,$pathComparison)) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Source, output and tooling paths must not cross symlinks or junctions.'
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $resolved
}
function ProjectProperty([string]$File, [string]$Name) {
    $document = New-Object Xml.XmlDocument
    $document.XmlResolver = $null
    $document.Load((Join-Path $projectRoot $File))
    $nodes = @($document.SelectNodes('/*[local-name()="Project"]/*[local-name()="PropertyGroup"]/*[local-name()="' + $Name + '"]'))
    if ($nodes.Count -ne 1) { throw ('Expected one literal project property: ' + $File + ' / ' + $Name) }
    return $nodes[0].InnerText.Trim()
}
$programSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Desktop/Program.cs'),[Text.Encoding]::UTF8)
$programVersions = [regex]::Matches($programSource, '\bconst\s+string\s+Version\s*=\s*"([^"]+)"')
$programBuilds = [regex]::Matches($programSource, '\bconst\s+string\s+BuildNumber\s*=\s*"([^"]+)"')
if ($programVersions.Count -ne 1 -or $programVersions[0].Groups[1].Value -ne $version -or $programBuilds.Count -ne 1) {
    throw 'Program version/build constants differ from macOS/VERSION or are missing.'
}
$buildNumber = $programBuilds[0].Groups[1].Value
foreach ($projectFile in @('macOS/Core/CornerTodo.MacCore.csproj','macOS/Desktop/CornerTodo.MacDesktop.csproj')) {
    if ((ProjectProperty $projectFile 'Version') -ne $version -or (ProjectProperty $projectFile 'FileVersion') -ne ($buildNumber + '.0')) {
        throw ('Mac project version/build differs from Program and VERSION: ' + $projectFile)
    }
}
if (-not $DotNetPath) { $DotNetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $PythonPath) {
    foreach ($name in @('python3','python')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command -and $command.Source -notmatch 'WindowsApps') { $PythonPath = $command.Source; break }
    }
}
if (-not $PythonPath) { throw 'Python 3.9+ is required. Supply -PythonPath.' }
$DotNetPath = (Resolve-Path -LiteralPath $DotNetPath).Path
$PythonPath = (Resolve-Path -LiteralPath $PythonPath).Path
$identityOutput = & $PythonPath (Join-Path $PSScriptRoot 'Packaging/build_bundle.py') --version $version --build-number $buildNumber --version-info
if ($LASTEXITCODE -ne 0) { throw 'The release version/build is invalid.' }
$identity = ($identityOutput -join [Environment]::NewLine) | ConvertFrom-Json
if ($identity.version -ne $version -or $identity.buildNumber -ne $buildNumber) { throw 'Release identity validation failed.' }
if (-not $NuGetConfigPath) { $NuGetConfigPath = Join-Path $PSScriptRoot 'NuGet.Config' }
$NuGetConfigPath = (Resolve-Path -LiteralPath $NuGetConfigPath).Path
if (-not $OutputDirectory) { $OutputDirectory = 'release/macos/v' + $version }
$outputRoot = ProjectPath $OutputDirectory
$toolingRoot = ProjectPath $ToolingDirectory
$macReleasePrefix = (Join-Path $projectRoot 'release/macos').TrimEnd($separator) + $separator
$developmentPrefix = (Join-Path $projectRoot 'output').TrimEnd($separator) + $separator
if (-not $outputRoot.StartsWith($macReleasePrefix,$pathComparison) -and
    -not $outputRoot.StartsWith($developmentPrefix,$pathComparison)) { throw 'Mac release output must be under release/macos or output, never a Windows runtime directory.' }
if (-not $toolingRoot.StartsWith($developmentPrefix,$pathComparison)) { throw 'Mac tooling caches must be under output.' }
if ((Test-Path -LiteralPath $outputRoot) -and @(Get-ChildItem -LiteralPath $outputRoot -Force).Count -gt 0) {
    throw 'Output directory is not empty; choose a fresh version-specific directory. Existing packages were not changed.'
}
$releaseHistory = Join-Path $projectRoot 'release/macos'
if (Test-Path -LiteralPath $releaseHistory) {
    foreach ($previousManifest in @(Get-ChildItem -LiteralPath $releaseHistory -Directory | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -File -Filter 'CornerTodo-macos-v*.manifest.json'
    })) {
        $previous = [IO.File]::ReadAllText($previousManifest.FullName,[Text.Encoding]::UTF8) | ConvertFrom-Json
        if ($previous.product -eq 'CornerTodo' -and $previous.platform -eq 'macOS' -and $previous.buildNumber) {
            if ($previous.version -eq $version -and $previous.buildNumber -ne $buildNumber) {
                throw 'A previously packaged public version cannot use a different bundle build number.'
            }
            if ($previous.version -ne $version -and [version]$previous.buildNumber -ge [version]$buildNumber) {
                throw ('Bundle build must increase for a different release; already used build ' + $previous.buildNumber + ' in ' + $previous.version)
            }
        }
    }
}
$workRoot = ProjectPath ('output/macOS/build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$cleanRoot = Join-Path $workRoot 'source'
$stageRoot = Join-Path $workRoot 'packages'
$artifactRoot = Join-Path $workRoot 'dotnet'
New-Item -ItemType Directory -Path $cleanRoot,$stageRoot,$toolingRoot -Force | Out-Null
$sourceListPath = Join-Path $PSScriptRoot 'Packaging/source-files.json'
$sourceListText = [IO.File]::ReadAllText($sourceListPath,[Text.Encoding]::UTF8)
if (-not $sourceListText.TrimStart().StartsWith('[')) { throw 'Mac source inventory must be a nonempty JSON array.' }
$sourceFiles = $sourceListText | ConvertFrom-Json
if (-not $sourceFiles) { throw 'Mac source inventory is empty.' }
$seen = @{}
$sourceHashes = @{}
foreach ($relative in $sourceFiles) {
    if ($relative -isnot [string] -or $relative -match '[:\\]|(^|/)\.{1,2}(/|$)|(^|/)(Data|output|release|archive|backups|bin|obj|artifacts|__pycache__|\.tools|\.git|\.codex|\.aws)(/|$)|\.(exe|dll|pdb|log|bak|zip|tmp|pyc)$' -or $relative.Split('/') -contains '' -or [IO.Path]::IsPathRooted($relative)) { throw ('Invalid source entry: ' + $relative) }
    if ($seen.ContainsKey($relative.ToLowerInvariant())) { throw ('Duplicate source entry: ' + $relative) }
    $seen[$relative.ToLowerInvariant()] = $true
    $source = ProjectPath $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw ('Missing source file: ' + $relative) }
    if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source symlinks are not allowed.' }
    $destination = Join-Path $cleanRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw ('Source copy hash mismatch: ' + $relative) }
    $sourceHashes[$relative] = (Get-FileHash -LiteralPath $destination).Hash
}
foreach ($relative in $sourceFiles) {
    if ((Get-FileHash -LiteralPath (ProjectPath $relative)).Hash -ne $sourceHashes[$relative]) { throw ('Source changed while creating clean snapshot: ' + $relative) }
}
$sourceLockBytes = @{}
$sourcePackageIdentities = @{}
function PackageIdentities([string]$Path) {
    $lock = [IO.File]::ReadAllText($Path,[Text.Encoding]::UTF8) | ConvertFrom-Json
    $identities = @{}
    foreach ($target in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $target.Value.PSObject.Properties) {
            if ($package.Value.type -ne 'Project' -and $null -ne $package.Value.resolved) {
                $key = $package.Name.ToLowerInvariant() + '|' + $package.Value.resolved + '|' + $package.Value.contentHash
                $identities[$key] = $true
            }
        }
    }
    return $identities
}
function Assert-PackageIdentities {
    foreach ($relative in $sourceLockBytes.Keys) {
        $actual = PackageIdentities (Join-Path $cleanRoot $relative)
        $expected = $sourcePackageIdentities[$relative]
        if ($actual.Count -ne $expected.Count) { throw ('Resolved package inventory changed: ' + $relative) }
        foreach ($key in $actual.Keys) {
            if (-not $expected.ContainsKey($key)) { throw ('Resolved package version or hash changed: ' + $relative) }
        }
    }
}
foreach ($relative in @('macOS/Core/packages.lock.json','macOS/Desktop/packages.lock.json','macOS/Tests/packages.lock.json')) {
    $sourceLockBytes[$relative] = [IO.File]::ReadAllBytes((Join-Path $cleanRoot $relative))
    $sourcePackageIdentities[$relative] = PackageIdentities (Join-Path $cleanRoot $relative)
}
$env:DOTNET_ROOT = Split-Path -Parent $DotNetPath
$env:DOTNET_CLI_HOME = Join-Path $toolingRoot 'cli-home'
$env:NUGET_PACKAGES = Join-Path $toolingRoot 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
function Invoke-DotNet([string[]]$Arguments) {
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('dotnet failed: ' + ($Arguments -join ' ')) }
}
function Invoke-Python([string[]]$Arguments) {
    $captured = & $PythonPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('Packaging failed: ' + ($Arguments -join ' ')) }
    $captured | ForEach-Object { Write-Host $_ }
}
$properties = @('-p:UseArtifactsOutput=true','-p:UseSharedCompilation=false',('-p:ArtifactsPath=' + $artifactRoot))
$testProject = Join-Path $cleanRoot 'macOS/Tests/CornerTodo.MacTests.csproj'
$desktopProject = Join-Path $cleanRoot 'macOS/Desktop/CornerTodo.MacDesktop.csproj'
$packager = Join-Path $cleanRoot 'macOS/Packaging/build_bundle.py'
$testOutput = Join-Path $workRoot 'tests'
$targets = if ($Architecture -eq 'all') { @('arm64','x64') } else { @($Architecture) }
$completed = @()
$previousTemp = [Environment]::GetEnvironmentVariable('TEMP')
$previousTmp = [Environment]::GetEnvironmentVariable('TMP')
$previousTmpDir = [Environment]::GetEnvironmentVariable('TMPDIR')
$buildTemp = Join-Path $workRoot 'temp'
New-Item -ItemType Directory -Path $buildTemp -Force | Out-Null
$env:TEMP = $buildTemp; $env:TMP = $buildTemp; $env:TMPDIR = $buildTemp
Push-Location (Join-Path $cleanRoot 'macOS')
try {
    Invoke-Python -Arguments @((Join-Path $cleanRoot 'macOS/Packaging/test_bundle.py'),'--output-dir',(Join-Path $workRoot 'package-tests'))
    Invoke-DotNet -Arguments (@('restore',$testProject,'--configfile',$NuGetConfigPath) + $properties)
    Assert-PackageIdentities
    Invoke-DotNet -Arguments (@('build',$testProject,'-c','Release','--no-restore','-o',$testOutput) + $properties)
    Invoke-DotNet -Arguments @((Join-Path $testOutput 'CornerTodo.MacTests.dll'),'--output',(Join-Path $workRoot 'test-data'))
    foreach ($target in $targets) {
        $runtime = 'osx-' + $target
        $publish = Join-Path $workRoot ('publish-' + $runtime)
        Invoke-DotNet -Arguments (@('restore',$desktopProject,'--configfile',$NuGetConfigPath,'-r',$runtime,'--use-lock-file','-p:SelfContained=true') + $properties)
        Assert-PackageIdentities
        Invoke-DotNet -Arguments (@('publish',$desktopProject,'-c','Release','-r',$runtime,'--self-contained','true','--no-restore','-o',$publish,
            '-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:UseAppHost=true') + $properties)
        $publishedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'CornerTodo.MacDesktop.dll'))
        if ($publishedVersion.FileVersion -ne ($buildNumber + '.0') -or
            ($publishedVersion.ProductVersion -ne $version -and -not $publishedVersion.ProductVersion.StartsWith($version + '+'))) {
            throw 'Published managed assembly version/build differs from the release identity.'
        }
        $runtimeConfig = [IO.File]::ReadAllText((Join-Path $publish 'CornerTodo.MacDesktop.runtimeconfig.json')) | ConvertFrom-Json
        $includedRuntime = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object {$_.name -eq 'Microsoft.NETCore.App'})
        if ($includedRuntime.Count -ne 1 -or $includedRuntime[0].version -ne '10.0.14') { throw 'Runtime differs from the pinned 10.0.14 license inventory.' }
        Copy-Item -LiteralPath (Join-Path $cleanRoot 'LICENSE') -Destination (Join-Path $publish 'LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $cleanRoot 'macOS/THIRD-PARTY-NOTICES.md') -Destination (Join-Path $publish 'THIRD-PARTY-NOTICES.md')
        $publishedLicenses = Join-Path $publish 'ThirdPartyLicenses'
        New-Item -ItemType Directory -Path $publishedLicenses -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $cleanRoot 'macOS/ThirdPartyLicenses') -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $publishedLicenses $_.Name)
        }
        Invoke-Python -Arguments @($packager,'--publish-dir',$publish,'--output-dir',$stageRoot,'--arch',$target,'--version',$version,'--build-number',$buildNumber,'--guide',(Join-Path $cleanRoot 'macOS/TESTING.md'),'--feedback',(Join-Path $cleanRoot 'macOS/FEEDBACK.txt'),'--icon-png',(Join-Path $cleanRoot 'macOS/Desktop/Assets/CornerTodo.png'))
        $completed += $target
    }
    # Restore only metadata changes made by RID-specific restores in the owned
    # clean copy. The source archive must preserve the selected public snapshot.
    foreach ($relative in $sourceLockBytes.Keys) { [IO.File]::WriteAllBytes((Join-Path $cleanRoot $relative),$sourceLockBytes[$relative]) }
    foreach ($relative in $sourceFiles) {
        if ((Get-FileHash -LiteralPath (Join-Path $cleanRoot $relative)).Hash -ne $sourceHashes[$relative]) {
            throw ('Build modified a public source file: ' + $relative)
        }
    }
    Invoke-Python -Arguments @($packager,'--source-root',$cleanRoot,'--source-list',(Join-Path $cleanRoot 'macOS/Packaging/source-files.json'),'--output-dir',$stageRoot,'--version',$version,'--build-number',$buildNumber)
    $checksums = @(Get-ChildItem -LiteralPath $stageRoot -File -Filter '*.zip' | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    })
    [IO.File]::WriteAllLines((Join-Path $stageRoot 'SHA256SUMS.txt'),$checksums,(New-Object Text.UTF8Encoding $false))
    $report = @(('CornerTodo macOS ' + $version),('Bundle build: ' + $buildNumber),'Status: experimental internal preview','Clean explicit source build and shared core tests: PASS',
        ('Self-contained architectures: ' + ($completed -join ', ')),'Runtime: .NET 10.0.14; UI: Avalonia 11.3.19',
        'Static package inventory / Mach-O code-page hashes / ZIP CRC / UNIX permissions: PASS',
        'Public source bytes preserved; resolved package versions and hashes unchanged: PASS',
        'macOS native launch: NOT VERIFIED','Developer ID signing: NOT PERFORMED','Apple notarization: NOT PERFORMED',
        'Personal Data, local caches and Windows release bundles: excluded','No files were uploaded.')
    [IO.File]::WriteAllLines((Join-Path $stageRoot 'PACKAGE-REPORT.txt'),$report,(New-Object Text.UTF8Encoding $false))
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $staged = @(Get-ChildItem -LiteralPath $stageRoot -Force)
    foreach ($entry in $staged) {
        if (Test-Path -LiteralPath (Join-Path $outputRoot $entry.Name)) { throw ('Output already exists; choose a fresh directory: ' + $entry.Name) }
    }
    $movedNames = New-Object 'System.Collections.Generic.List[string]'
    try {
        foreach ($entry in $staged) {
            $resolvedSource = [IO.Path]::GetFullPath($entry.FullName)
            $resolvedDestination = [IO.Path]::GetFullPath((Join-Path $outputRoot $entry.Name))
            if (-not $resolvedSource.StartsWith($workRoot.TrimEnd($separator) + $separator,$pathComparison) -or
                -not $resolvedDestination.StartsWith($projectPrefix,$pathComparison)) { throw 'Final move escaped the intended project paths.' }
            Move-Item -LiteralPath $resolvedSource -Destination $resolvedDestination
            $movedNames.Add($entry.Name)
        }
    } catch {
        $publicationError = $_
        for ($index = $movedNames.Count - 1; $index -ge 0; $index--) {
            $from = [IO.Path]::GetFullPath((Join-Path $outputRoot $movedNames[$index]))
            $to = [IO.Path]::GetFullPath((Join-Path $stageRoot $movedNames[$index]))
            if (-not $from.StartsWith($projectPrefix,$pathComparison) -or
                -not $to.StartsWith($workRoot.TrimEnd($separator) + $separator,$pathComparison)) { throw 'Rollback path escaped its staging root.' }
            Move-Item -LiteralPath $from -Destination $to -ErrorAction Stop
        }
        throw $publicationError
    }
    Write-Output ('Experimental release files: ' + $outputRoot)
    Write-Output ('Build evidence: ' + $workRoot)
    Write-Output 'Mac native behavior remains unverified. Nothing was uploaded.'
} finally {
    Pop-Location
    [Environment]::SetEnvironmentVariable('TEMP',$previousTemp)
    [Environment]::SetEnvironmentVariable('TMP',$previousTmp)
    [Environment]::SetEnvironmentVariable('TMPDIR',$previousTmpDir)
}

