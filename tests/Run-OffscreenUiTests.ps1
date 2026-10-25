[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkDirectory 'csc.exe'))) {
    $frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkDirectory 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
$buildDirectory = Join-Path $PSScriptRoot ('CornerTodo-UiTestBuild-' + [Guid]::NewGuid().ToString('N'))
$outputDirectory = Join-Path $PSScriptRoot 'visual-qa'
[void][System.IO.Directory]::CreateDirectory($buildDirectory)
$testExecutable = Join-Path $buildDirectory 'OffscreenUiTests.exe'
try {
    $arguments = @('/nologo', '/optimize+', '/target:exe', '/platform:anycpu', '/warn:4',
        '/main:OffscreenUiTests', ('/out:' + $testExecutable))
    foreach ($assembly in @('System.dll', 'System.Core.dll', 'System.Web.Extensions.dll', 'System.Xaml.dll',
        'System.Windows.Forms.dll', 'System.Drawing.dll',
        'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')) {
        $arguments += '/reference:' + (Join-Path $frameworkDirectory $assembly)
    }
    foreach ($source in (Get-ChildItem -LiteralPath $projectDirectory -Filter '*.cs' -File | Sort-Object Name)) {
        $arguments += $source.FullName
    }
    $arguments += Join-Path $PSScriptRoot 'OffscreenUiTests.cs'
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Offscreen UI tests failed to compile: $LASTEXITCODE." }
    & $testExecutable $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Offscreen UI tests failed: $LASTEXITCODE." }
}
finally {
    $resolvedBuildDirectory = [System.IO.Path]::GetFullPath($buildDirectory)
    $resolvedTestsDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    if ($resolvedBuildDirectory.StartsWith($resolvedTestsDirectory, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedBuildDirectory).StartsWith('CornerTodo-UiTestBuild-')) {
        Remove-Item -LiteralPath $resolvedBuildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
