param(
    [switch]$Test,
    [string]$OutputDirectory = 'dist'
)

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$outputDir = Join-Path $projectDir $OutputDirectory
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.7.2 or later.'
}

$sourceFiles = @(Get-ChildItem -LiteralPath $projectDir -Filter '*.cs' -File | Sort-Object Name)
if ($sourceFiles.Count -eq 0) {
    throw 'No C# source files were found beside Build.ps1.'
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# Generate an original checklist icon. This only uses built-in Windows drawing APIs.
$iconPath = Join-Path $outputDir 'CornerTodo.ico'
$hasIcon = $false
$bitmap = $null
$graphics = $null
$shape = $null
$gradient = $null
$pen = $null
$icon = $null
$iconStream = $null
try {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 64, 64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(2, 2, 20, 20, 180, 90)
    $shape.AddArc(42, 2, 20, 20, 270, 90)
    $shape.AddArc(42, 42, 20, 20, 0, 90)
    $shape.AddArc(2, 42, 20, 20, 90, 90)
    $shape.CloseFigure()
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        (New-Object System.Drawing.Point 0, 0),
        (New-Object System.Drawing.Point 64, 64),
        [System.Drawing.Color]::FromArgb(255, 30, 120, 192),
        [System.Drawing.Color]::FromArgb(255, 36, 186, 180)
    )
    $graphics.FillPath($gradient, $shape)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 3.5
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($rowY in @(18, 32, 46)) {
        $graphics.DrawLine($pen, 13, $rowY, 17, ($rowY + 4))
        $graphics.DrawLine($pen, 17, ($rowY + 4), 24, ($rowY - 4))
        $graphics.DrawLine($pen, 33, $rowY, 49, $rowY)
    }
    $icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
    $iconStream = [System.IO.File]::Create($iconPath)
    $icon.Save($iconStream)
    $hasIcon = $true
}
catch {
    Write-Warning ('Icon generation was skipped: ' + $_.Exception.Message)
}
finally {
    foreach ($disposable in @($iconStream, $icon, $pen, $gradient, $shape, $graphics, $bitmap)) {
        if ($null -ne $disposable) { $disposable.Dispose() }
    }
}

$references = @(
    (Join-Path $frameworkRoot 'System.dll'),
    (Join-Path $frameworkRoot 'System.Core.dll'),
    (Join-Path $frameworkRoot 'System.Web.Extensions.dll'),
    (Join-Path $frameworkRoot 'System.Xaml.dll'),
    (Join-Path $frameworkRoot 'System.Windows.Forms.dll'),
    (Join-Path $frameworkRoot 'System.Drawing.dll'),
    (Join-Path $frameworkRoot 'WPF\WindowsBase.dll'),
    (Join-Path $frameworkRoot 'WPF\PresentationCore.dll'),
    (Join-Path $frameworkRoot 'WPF\PresentationFramework.dll')
)
foreach ($referencePath in $references) {
    if (-not (Test-Path -LiteralPath $referencePath)) {
        throw ('Required .NET Framework assembly was not found: ' + $referencePath)
    }
}

$executablePath = Join-Path $outputDir 'CornerTodo.exe'
$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/main:CornerTodo.Program',
    ('/out:' + $executablePath),
    ('/win32manifest:' + (Join-Path $projectDir 'app.manifest'))
)
if ($hasIcon) { $compilerArgs += '/win32icon:' + $iconPath }
foreach ($referencePath in $references) { $compilerArgs += '/reference:' + $referencePath }
foreach ($sourceFile in $sourceFiles) { $compilerArgs += $sourceFile.FullName }

$compilerOutput = & $compiler @compilerArgs 2>&1
$compilerExitCode = $LASTEXITCODE
foreach ($diagnosticLine in $compilerOutput) { Write-Output $diagnosticLine }
if ($compilerExitCode -ne 0) {
    throw ('Compilation failed with exit code ' + $compilerExitCode + '. See compiler diagnostics above.')
}

Copy-Item -LiteralPath (Join-Path $projectDir 'CornerTodo.exe.config') -Destination ($executablePath + '.config') -Force
Write-Output ('Built: ' + $executablePath)

if ($Test) {
    foreach ($testName in @('Run-StoreTests.ps1', 'Run-GrowthLayoutTests.ps1', 'Run-OffscreenUiTests.ps1', 'Run-AppearanceUiTests.ps1', 'Run-StartupTests.ps1')) {
        $testScript = Join-Path (Join-Path $projectDir 'tests') $testName
        if (-not (Test-Path -LiteralPath $testScript)) {
            throw ('The requested test script was not found: ' + $testScript)
        }
        & $testScript
        if (-not $?) { throw ('Tests failed: ' + $testName) }
    }
}
