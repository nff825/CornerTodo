# Shared by the system-csc build, test runners and source packaging. Windows PowerShell 5.1 compatible.

function Get-CornerTodoLiteral {
    param([string]$Value, [string]$Description)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '[$%@]\(') {
        throw ('Expected a literal value for ' + $Description + '.')
    }
    return $Value.Trim()
}

function Resolve-CornerTodoPath {
    param([string]$ProjectDirectory, [string]$RelativePath, [switch]$Directory)
    $relative = Get-CornerTodoLiteral $RelativePath 'a repository path'
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '[*?;:]') {
        throw ('Only explicit repository-relative paths are supported: ' + $relative)
    }
    $root = [IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('The declared path leaves the project directory: ' + $relative)
    }
    if (-not $Directory -and -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw ('A declared build file is missing: ' + $relative)
    }
    return $path
}

function Get-CornerTodoBuildDefinition {
    param([Parameter(Mandatory=$true)][string]$ProjectDirectory)
    $root = [IO.Path]::GetFullPath($ProjectDirectory)
    $manifest = Join-Path $root 'CornerTodo.Build.props'
    $settings = New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($manifest, $settings)
    $xml = New-Object Xml.XmlDocument
    $xml.XmlResolver = $null
    try { $xml.Load($reader) } finally { $reader.Dispose() }
    if ($xml.DocumentElement.LocalName -ne 'Project' -or $xml.DocumentElement.NamespaceURI -ne 'http://schemas.microsoft.com/developer/msbuild/2003') {
        throw 'The shared build definition must be an MSBuild Project document.'
    }
    if ($xml.SelectNodes('//*[@Condition or @Exclude or @Remove or @Update]').Count -ne 0) {
        throw 'Conditions, exclusions and dynamic item updates are not supported in the shared build definition.'
    }
    $properties = @{}
    $propertyNames = @('TargetFrameworkVersion','LangVersion','PlatformTarget','Prefer32Bit','OutputType','AssemblyName','RootNamespace','StartupObject','ApplicationManifest','AppConfig','AssemblyInfoFile','WarningLevel')
    $metadataNames = @{
        Compile=@('TestGroups'); Reference=@('FrameworkFile');
        CornerTodoTestSuite=@('SourceGroup','EntryPoint','Runner','OutputArgument');
        CornerTodoTestSource=@('Suite'); CornerTodoPublicFile=@()
    }
    $sources = @(); $references = @(); $suites = @(); $testSources = @(); $publicFiles = @()
    $seenSources = @{}; $seenReferences = @{}; $seenSuites = @{}
    foreach ($group in $xml.DocumentElement.ChildNodes) {
        if ($group.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
        if ($group.NamespaceURI -ne $xml.DocumentElement.NamespaceURI) { throw 'Shared build elements must use the MSBuild namespace.' }
        if ($group.Attributes.Count -ne 0) { throw 'Build groups must be unconditional.' }
        if ($group.LocalName -eq 'PropertyGroup') {
            foreach ($property in $group.ChildNodes) {
                if ($property.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                if ($propertyNames -notcontains $property.LocalName -or $property.NamespaceURI -ne $xml.DocumentElement.NamespaceURI -or $property.SelectNodes('*').Count -ne 0) { throw ('Unsupported shared build property: ' + $property.LocalName) }
                if ($properties.ContainsKey($property.LocalName) -or $property.Attributes.Count -ne 0) { throw 'Build properties must be unique literal declarations.' }
                $properties[$property.LocalName] = Get-CornerTodoLiteral $property.InnerText $property.LocalName
            }
        }
        elseif ($group.LocalName -eq 'ItemGroup') {
            foreach ($item in $group.ChildNodes) {
                if ($item.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                if (-not $metadataNames.ContainsKey($item.LocalName) -or $item.NamespaceURI -ne $xml.DocumentElement.NamespaceURI) { throw ('Unsupported shared build item: ' + $item.LocalName) }
                if ($item.Attributes.Count -ne 1 -or -not $item.HasAttribute('Include')) { throw 'Build items must have one literal Include attribute.' }
                $include = Get-CornerTodoLiteral $item.GetAttribute('Include') $item.LocalName
                $metadata = @{}
                foreach ($value in $item.ChildNodes) {
                    if ($value.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                    if ($metadataNames[$item.LocalName] -notcontains $value.LocalName -or $value.NamespaceURI -ne $xml.DocumentElement.NamespaceURI -or $value.SelectNodes('*').Count -ne 0) { throw ('Unsupported metadata for ' + $item.LocalName + ': ' + $value.LocalName) }
                    if ($metadata.ContainsKey($value.LocalName) -or $value.Attributes.Count -ne 0) { throw 'Item metadata must be unique literal declarations.' }
                    $metadata[$value.LocalName] = Get-CornerTodoLiteral $value.InnerText $value.LocalName
                }
                switch ($item.LocalName) {
                    'Compile' {
                        $path = Resolve-CornerTodoPath $root $include
                        if ($seenSources.ContainsKey($path)) { throw ('Duplicate application source: ' + $include) }
                        $seenSources[$path] = $true
                        $sources += [PSCustomObject]@{ RelativePath=$include.Replace('\','/'); Path=$path; TestGroups=@(($metadata['TestGroups'] -split ';') | Where-Object { $_ }) }
                    }
                    'Reference' {
                        if ($seenReferences.ContainsKey($include) -or $include -notmatch '^[A-Za-z0-9_.]+$') { throw ('Invalid or duplicate assembly reference: ' + $include) }
                        $file = Get-CornerTodoLiteral $metadata['FrameworkFile'] 'FrameworkFile'
                        if ([IO.Path]::IsPathRooted($file) -or $file -match '[*?;:]|(^|[\\/])\.\.([\\/]|$)') { throw ('Invalid framework reference: ' + $file) }
                        $seenReferences[$include] = $true
                        $references += [PSCustomObject]@{ Name=$include; FrameworkFile=$file }
                    }
                    'CornerTodoTestSuite' {
                        if ($include -notmatch '^[A-Za-z]+$') { throw ('Test suite names must contain only ASCII letters: ' + $include) }
                        if ($seenSuites.ContainsKey($include)) { throw ('Duplicate test suite: ' + $include) }
                        $seenSuites[$include] = $true
                        $suites += [PSCustomObject]@{ Name=$include; SourceGroup=(Get-CornerTodoLiteral $metadata['SourceGroup'] 'SourceGroup'); EntryPoint=(Get-CornerTodoLiteral $metadata['EntryPoint'] 'EntryPoint'); Runner=$metadata['Runner']; RunnerPath=(Resolve-CornerTodoPath $root $metadata['Runner']); OutputArgument=$metadata['OutputArgument'] }
                    }
                    'CornerTodoTestSource' {
                        $testSources += [PSCustomObject]@{ RelativePath=$include.Replace('\','/'); Path=(Resolve-CornerTodoPath $root $include); Suite=(Get-CornerTodoLiteral $metadata['Suite'] 'Suite') }
                    }
                    'CornerTodoPublicFile' {
                        # Existence is checked by Get-CornerTodoPublicSourceFiles, before packaging.
                        $publicFiles += $include.Replace('\','/')
                    }
                    default { throw ('Unsupported shared build item: ' + $item.LocalName) }
                }
            }
        }
        else { throw ('The shared definition is data-only; unsupported element: ' + $group.LocalName) }
    }
    foreach ($required in @('TargetFrameworkVersion','LangVersion','PlatformTarget','Prefer32Bit','OutputType','AssemblyName','StartupObject','ApplicationManifest','AppConfig','AssemblyInfoFile','WarningLevel')) {
        if (-not $properties.ContainsKey($required)) { throw ('Missing build property: ' + $required) }
    }
    if ($sources.Count -eq 0 -or $references.Count -eq 0) { throw 'The build definition must declare sources and references.' }
    foreach ($test in $testSources) { if (-not $seenSuites.ContainsKey($test.Suite)) { throw ('Unknown suite for test source: ' + $test.RelativePath) } }
    foreach ($suite in $suites) {
        if (@($testSources | Where-Object { $_.Suite -eq $suite.Name }).Count -eq 0) { throw ('Test suite has no sources: ' + $suite.Name) }
        if ($suite.SourceGroup -ne 'All' -and @($sources | Where-Object { $_.TestGroups -contains $suite.SourceGroup }).Count -eq 0) { throw ('Unknown source group: ' + $suite.SourceGroup) }
        if ($suite.OutputArgument) { [void](Resolve-CornerTodoPath $root $suite.OutputArgument -Directory) }
    }
    return [PSCustomObject]@{ ProjectDirectory=$root; Properties=$properties; Sources=$sources; SourceFiles=@($sources | ForEach-Object { $_.Path }); RelativeSourceFiles=@($sources | ForEach-Object { $_.RelativePath }); References=$references; TestSuites=$suites; TestSources=$testSources; PublicFiles=$publicFiles }
}

function Get-CornerTodoPublicSourceFiles {
    param([Parameter(Mandatory=$true)]$Definition)
    $paths = @($Definition.RelativeSourceFiles) + @($Definition.TestSources | ForEach-Object { $_.RelativePath }) + @($Definition.TestSuites | ForEach-Object { $_.Runner }) + @($Definition.PublicFiles)
    $seen = @{}
    foreach ($relative in $paths) {
        $relative = $relative.Replace('\','/')
        [void](Resolve-CornerTodoPath $Definition.ProjectDirectory $relative)
        if ($relative -match '(^|/)([Dd]ata|dist[^/]*|release|output|bin|obj)(/|$)' -or $relative -match '\.(exe|dll|pdb|zip|log|bak|tmp)$' -or $relative -match '(^|/)todos[^/]*\.json') { throw ('Runtime or generated files cannot enter the public source list: ' + $relative) }
        if ($seen.ContainsKey($relative)) { throw ('Duplicate public source path: ' + $relative) }
        $seen[$relative] = $true
        Write-Output $relative
    }
}

function Get-CornerTodoSourceFiles {
    param([Parameter(Mandatory=$true)]$Definition, [string]$Group='All')
    $selected = @($Definition.Sources | Where-Object { $Group -eq 'All' -or $_.TestGroups -contains $Group })
    if ($selected.Count -eq 0) { throw ('No sources in group: ' + $Group) }
    return @($selected | ForEach-Object { $_.Path })
}

function Get-CornerTodoCompiler {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) { $framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319' }
    $compiler = Join-Path $framework 'csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.7.2 or later.' }
    return [PSCustomObject]@{ Path=$compiler; FrameworkDirectory=$framework }
}

function Invoke-CornerTodoCompilation {
    param([Parameter(Mandatory=$true)]$Definition, [Parameter(Mandatory=$true)][string[]]$SourceFiles,
        [Parameter(Mandatory=$true)][string]$OutputPath, [string]$EntryPoint, [string]$Target='exe', [string]$ManifestPath, [string]$IconPath)
    $compiler = Get-CornerTodoCompiler
    $properties = $Definition.Properties
    if ($properties.Prefer32Bit -notin @('true','false')) { throw 'Prefer32Bit must be true or false.' }
    $platform = $properties.PlatformTarget.ToLowerInvariant()
    if ($platform -eq 'anycpu' -and $properties.Prefer32Bit -eq 'true') { $platform = 'anycpu32bitpreferred' }
    $arguments = @('/nologo','/optimize+',('/target:' + $Target.ToLowerInvariant()),('/platform:' + $platform),('/langversion:' + $properties.LangVersion),('/warn:' + $properties.WarningLevel),('/out:' + $OutputPath))
    if ($EntryPoint) { $arguments += '/main:' + $EntryPoint }
    if ($ManifestPath) { $arguments += '/win32manifest:' + $ManifestPath }
    if ($IconPath) { $arguments += '/win32icon:' + $IconPath }
    foreach ($reference in $Definition.References) {
        $path = Join-Path $compiler.FrameworkDirectory $reference.FrameworkFile
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Required .NET Framework assembly was not found: ' + $path) }
        $arguments += '/reference:' + $path
    }
    $arguments += $SourceFiles
    $diagnostics = & $compiler.Path @arguments 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $diagnostics) { Write-Output $line }
    if ($code -ne 0) { throw ('Compilation failed with exit code ' + $code + '.') }
}

function Invoke-CornerTodoTestSuite {
    param([Parameter(Mandatory=$true)][string]$ProjectDirectory, [Parameter(Mandatory=$true)][string]$Name)
    $definition = Get-CornerTodoBuildDefinition $ProjectDirectory
    $suite = @($definition.TestSuites | Where-Object { $_.Name -eq $Name })
    if ($suite.Count -ne 1) { throw ('Unknown test suite: ' + $Name) }
    $suite = $suite[0]
    $testsDirectory = Join-Path $definition.ProjectDirectory 'tests'
    $buildDirectory = Join-Path $testsDirectory ('CornerTodo-' + $Name + 'TestBuild-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($buildDirectory)
    $testExecutable = Join-Path $buildDirectory ($Name + 'Tests.exe')
    try {
        $sources = @(Get-CornerTodoSourceFiles $definition $suite.SourceGroup) + @($definition.TestSources | Where-Object { $_.Suite -eq $Name } | ForEach-Object { $_.Path })
        Invoke-CornerTodoCompilation -Definition $definition -SourceFiles $sources -OutputPath $testExecutable -EntryPoint $suite.EntryPoint
        if ($suite.OutputArgument) { & $testExecutable (Resolve-CornerTodoPath $definition.ProjectDirectory $suite.OutputArgument -Directory) }
        else { & $testExecutable }
        if ($LASTEXITCODE -ne 0) { throw ($Name + ' tests failed with exit code ' + $LASTEXITCODE + '.') }
    }
    finally {
        $resolved = [IO.Path]::GetFullPath($buildDirectory)
        $prefix = [IO.Path]::GetFullPath($testsDirectory).TrimEnd('\') + '\'
        if ($resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved) -match '^CornerTodo-[A-Za-z]+TestBuild-[a-f0-9]{32}$') { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# Running this helper directly performs the same contract check before an IDE build.
# Dot-sourcing it only defines the functions used by the script entry points.
if ($MyInvocation.InvocationName -ne '.') {
    $definition = Get-CornerTodoBuildDefinition -ProjectDirectory (Split-Path -Parent $PSScriptRoot)
    Write-Output ('Validated shared build definition: ' + $definition.SourceFiles.Count + ' application sources.')
}
