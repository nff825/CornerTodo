[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectDirectory 'build\Common.ps1')

Invoke-CornerTodoTestSuite -ProjectDirectory $projectDirectory -Name 'OffscreenUi'
