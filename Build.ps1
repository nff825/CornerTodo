param(
    [switch]$Test,
    [string]$OutputDirectory = 'output/build'
)

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
. (Join-Path $projectDir 'build/Common.ps1')
$definition = Get-CornerTodoBuildDefinition -ProjectDirectory $projectDir
if ([IO.Path]::IsPathRooted($OutputDirectory)) { $outputDir = [IO.Path]::GetFullPath($OutputDirectory) }
else { $outputDir = [IO.Path]::GetFullPath((Join-Path $projectDir $OutputDirectory)) }
[void][IO.Directory]::CreateDirectory($outputDir)

$iconPath = Join-Path $outputDir 'CornerTodo.ico'
& (Join-Path $projectDir 'build/Generate-Icon.ps1') -OutputPath $iconPath
$executablePath = Join-Path $outputDir ($definition.Properties.AssemblyName + '.exe')
Invoke-CornerTodoCompilation -Definition $definition -SourceFiles $definition.SourceFiles -OutputPath $executablePath `
    -EntryPoint $definition.Properties.StartupObject -Target $definition.Properties.OutputType `
    -ManifestPath (Resolve-CornerTodoPath $projectDir $definition.Properties.ApplicationManifest) -IconPath $iconPath
Copy-Item -LiteralPath (Resolve-CornerTodoPath $projectDir $definition.Properties.AppConfig) -Destination ($executablePath + '.config') -Force
Write-Output ('Built: ' + $executablePath)

if ($Test) {
    foreach ($suite in $definition.TestSuites) {
        & $suite.RunnerPath
        if (-not $?) { throw ('Tests failed: ' + $suite.Name) }
    }
}
