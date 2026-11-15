[CmdletBinding()]
param(
    [ValidateSet('Windows', 'Linux')]
    [string] $Target = 'Windows',

    [string] $UnityPath = $env:DYNAMIC_SKY_UNITY,

    [string] $LogPath
)

$ErrorActionPreference = 'Stop'
$runningOnWindows = $env:OS -eq 'Windows_NT'
if (!$UnityPath -and $runningOnWindows) {
    $UnityPath = 'C:/Program Files/Unity/Editor/Unity.exe'
}
if (!$UnityPath -or !(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw 'Set -UnityPath or DYNAMIC_SKY_UNITY to the Unity 2019.1.8f1 executable.'
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'UnityProject'
$resources = Join-Path $root 'Resources'
if (!$LogPath) {
    $LogPath = Join-Path $root "Logs/assetbundle-$($Target.ToLowerInvariant()).log"
}
$LogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null

$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
if (!(Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion: 2019\.1\.8f1\s*$')) {
    throw 'This project must target Unity 2019.1.8f1.'
}
$bundleName = if ($Target -eq 'Linux') { 'dynamic-sky-linux.bundle' } else { 'dynamic-sky.bundle' }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('DynamicSky-bundle-' + [Guid]::NewGuid().ToString('N'))
$previousStage = $env:DYNAMIC_SKY_BUNDLE_STAGE
$previousTarget = $env:DYNAMIC_SKY_BUNDLE_TARGET
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    $env:DYNAMIC_SKY_BUNDLE_STAGE = $stage
    $env:DYNAMIC_SKY_BUNDLE_TARGET = $Target
    $launch = @{
        FilePath = (Resolve-Path -LiteralPath $UnityPath).Path
        ArgumentList = @('-batchmode', '-quit', '-projectPath', ('"' + $project + '"'),
            '-executeMethod', 'BuildDynamicSky.Build', '-logFile', ('"' + $LogPath + '"'))
        WorkingDirectory = $project
        PassThru = $true
        Wait = $true
    }
    if ($runningOnWindows) { $launch.WindowStyle = 'Hidden' }
    $process = Start-Process @launch
    $bundle = Join-Path $stage $bundleName
    $successful = (Test-Path -LiteralPath $LogPath) -and
        (Select-String -LiteralPath $LogPath -Pattern 'DYNAMIC_SKY_BUILD_OK' -Quiet) -and
        !(Select-String -LiteralPath $LogPath -Pattern 'Shader error|error CS\d+' -Quiet)
    if ($process.ExitCode -ne 0 -or !$successful -or !(Test-Path -LiteralPath $bundle -PathType Leaf)) {
        throw "Unity failed to build the $Target bundle (exit $($process.ExitCode)). See $LogPath"
    }
    Copy-Item -LiteralPath $bundle -Destination (Join-Path $resources $bundleName)
    Write-Output "Built $bundleName. Log: $LogPath"
}
finally {
    $env:DYNAMIC_SKY_BUNDLE_STAGE = $previousStage
    $env:DYNAMIC_SKY_BUNDLE_TARGET = $previousTarget
    foreach ($file in @($bundleName, "$bundleName.manifest", (Split-Path -Leaf $stage), "$(Split-Path -Leaf $stage).manifest")) {
        $path = Join-Path $stage $file
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    if ((Test-Path -LiteralPath $stage -PathType Container) -and !(Get-ChildItem -LiteralPath $stage -Force)) {
        Remove-Item -LiteralPath $stage
    }
}
