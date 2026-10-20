[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDirectory 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $compiler = Join-Path $frameworkDirectory 'csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }

$buildDirectory = Join-Path $PSScriptRoot ('CornerTodo-GrowthTestBuild-' + [Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($buildDirectory)
$testExecutable = Join-Path $buildDirectory 'GrowthLayoutTests.exe'
try {
    $arguments = @(
        '/nologo', '/optimize+', '/target:exe', '/platform:anycpu', '/warn:4',
        ('/out:' + $testExecutable),
        (Join-Path $projectDirectory 'GrowthLayout.cs'),
        (Join-Path $PSScriptRoot 'GrowthLayoutTests.cs')
    )
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Growth-layout tests failed to compile: $LASTEXITCODE." }
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw "Growth-layout tests failed: $LASTEXITCODE." }
}
finally {
    # Remove only this uniquely named test-owned build directory.
    $resolvedBuildDirectory = [System.IO.Path]::GetFullPath($buildDirectory)
    $resolvedTestsDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    if ($resolvedBuildDirectory.StartsWith($resolvedTestsDirectory, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedBuildDirectory).StartsWith('CornerTodo-GrowthTestBuild-')) {
        Remove-Item -LiteralPath $resolvedBuildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
