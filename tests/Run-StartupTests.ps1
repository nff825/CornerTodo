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
$buildDirectory = Join-Path $PSScriptRoot ('CornerTodo-StartupTestBuild-' + [Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($buildDirectory)
$testExecutable = Join-Path $buildDirectory 'StartupTests.exe'
try {
    $arguments = @('/nologo', '/optimize+', '/target:exe', '/platform:anycpu', '/warn:4',
        '/main:StartupTests', ('/out:' + $testExecutable))
    foreach ($assembly in @('System.dll', 'System.Core.dll', 'System.Web.Extensions.dll', 'System.Xaml.dll',
        'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')) {
        $arguments += '/reference:' + (Join-Path $frameworkDirectory $assembly)
    }
    foreach ($source in (Get-ChildItem -LiteralPath $projectDirectory -Filter '*.cs' -File | Sort-Object Name)) {
        $arguments += $source.FullName
    }
    foreach ($test in @('StartupTests.cs', 'LaunchOptionsTests.cs', 'StartupUiTests.cs')) {
        $arguments += Join-Path $PSScriptRoot $test
    }
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Startup tests failed to compile: $LASTEXITCODE." }
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw "Startup tests failed: $LASTEXITCODE." }
}
finally {
    $resolvedBuildDirectory = [System.IO.Path]::GetFullPath($buildDirectory)
    $resolvedTestsDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    if ($resolvedBuildDirectory.StartsWith($resolvedTestsDirectory, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedBuildDirectory).StartsWith('CornerTodo-StartupTestBuild-')) {
        Remove-Item -LiteralPath $resolvedBuildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
