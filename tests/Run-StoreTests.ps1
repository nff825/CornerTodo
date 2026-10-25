[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$testBuildDirectory = Join-Path $PSScriptRoot ('CornerTodo-TestBuild-' + [Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($testBuildDirectory)
$testExecutable = Join-Path $testBuildDirectory 'StoreTests.exe'
$provider = $null

try {
    Add-Type -AssemblyName Microsoft.CSharp
    $providerOptions = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    $providerOptions.Add('CompilerVersion', 'v4.0')
    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider -ArgumentList (,$providerOptions)
    $parameters = New-Object System.CodeDom.Compiler.CompilerParameters
    $parameters.GenerateExecutable = $true
    $parameters.GenerateInMemory = $false
    $parameters.IncludeDebugInformation = $false
    $parameters.OutputAssembly = $testExecutable
    $parameters.CompilerOptions = '/optimize+ /target:exe /platform:anycpu /langversion:5 /warn:4'
    [void]$parameters.ReferencedAssemblies.Add('System.dll')
    [void]$parameters.ReferencedAssemblies.Add('System.Core.dll')
    [void]$parameters.ReferencedAssemblies.Add('System.Web.Extensions.dll')
    $sourceFiles = @(
        (Join-Path $projectDirectory 'AppearanceSettings.cs'),
        (Join-Path $projectDirectory 'Models.cs'),
        (Join-Path $projectDirectory 'TodoStore.cs'),
        (Join-Path $PSScriptRoot 'StoreTests.cs')
    )
    $result = $provider.CompileAssemblyFromFile($parameters, [string[]]$sourceFiles)
    if ($result.Errors.HasErrors) {
        foreach ($compileError in $result.Errors) {
            Write-Host $compileError.ToString()
        }
        throw 'Store tests failed to compile.'
    }
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw "Store tests failed with exit code $LASTEXITCODE." }
}
finally {
    if ($null -ne $provider) { $provider.Dispose() }
    # Verify the recursively removed target remains the unique test-owned build directory.
    $resolvedBuildDirectory = [System.IO.Path]::GetFullPath($testBuildDirectory)
    $resolvedTestsDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    if ($resolvedBuildDirectory.StartsWith($resolvedTestsDirectory, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedBuildDirectory).StartsWith('CornerTodo-TestBuild-')) {
        Remove-Item -LiteralPath $resolvedBuildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
