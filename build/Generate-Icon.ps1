[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputPath)

$ErrorActionPreference = 'Stop'
$iconPath = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $iconPath))
$bitmap = $null; $graphics = $null; $shape = $null; $gradient = $null
$pen = $null; $icon = $null; $iconStream = $null
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
        (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 64, 64),
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
    $iconStream = [IO.File]::Create($iconPath)
    $icon.Save($iconStream)
}
finally {
    foreach ($disposable in @($iconStream, $icon, $pen, $gradient, $shape, $graphics, $bitmap)) {
        if ($null -ne $disposable) { $disposable.Dispose() }
    }
}
