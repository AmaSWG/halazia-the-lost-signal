param([string]$EditorData = 'C:/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $project
New-Item -ItemType Directory -Path 'Logs/SentinelCompile' -Force | Out-Null
$outDir = Join-Path $project 'Logs/SentinelCompile'
$csc = Join-Path $EditorData 'DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll'
$dotnet = Join-Path $EditorData 'DotNetSdk/dotnet.exe'
$template = Join-Path $EditorData 'Resources/PackageManager/ProjectTemplates/libcache/com.unity.template.urp-blank-17.2.1/ScriptAssemblies'
$refs = @(Get-ChildItem (Join-Path $EditorData 'NetStandard/ref/2.1.0') -Filter '*.dll')
$refs += @(Get-ChildItem (Join-Path $EditorData 'Managed/UnityEngine') -Filter '*.dll')
$refs += @(Get-ChildItem (Join-Path $EditorData 'Managed') -Filter 'UnityEditor*.dll' | Where-Object { $_.Name -ne 'UnityEditor.dll' })
$refs += Get-Item (Join-Path $template 'Unity.InputSystem.dll')
$refs += Get-Item (Join-Path $template 'Unity.AI.Navigation.dll')
$common = @('-nologo','-target:library','-langversion:latest','-define:UNITY_EDITOR,ENABLE_INPUT_SYSTEM','-nostdlib+')
$common += $refs | ForEach-Object { '-r:"' + $_.FullName + '"' }
$runtime = @(Get-ChildItem Assets/Scripts -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '[\\/]Editor[\\/]' })
$runtimeRsp = $common + ('-out:"' + $outDir + '/Sentinel.Runtime.dll"') + ($runtime | ForEach-Object { '"' + $_.FullName + '"' })
[IO.File]::WriteAllLines((Join-Path $outDir 'Runtime.rsp'), $runtimeRsp)
& $dotnet $csc ('@' + (Join-Path $outDir 'Runtime.rsp')) 2>&1 | Tee-Object (Join-Path $outDir 'Runtime.log')
if ($LASTEXITCODE -ne 0) { throw 'Runtime compilation failed.' }
$editor = @(Get-ChildItem Assets/Scripts/Editor -Filter '*.cs')
$editorRsp = $common + ('-r:"' + $outDir + '/Sentinel.Runtime.dll"') + ('-out:"' + $outDir + '/Sentinel.Editor.dll"') + ($editor | ForEach-Object { '"' + $_.FullName + '"' })
[IO.File]::WriteAllLines((Join-Path $outDir 'Editor.rsp'), $editorRsp)
& $dotnet $csc ('@' + (Join-Path $outDir 'Editor.rsp')) 2>&1 | Tee-Object (Join-Path $outDir 'Editor.log')
if ($LASTEXITCODE -ne 0) { throw 'Editor compilation failed.' }
Write-Output 'SENTINEL_COMPILE_PASS: runtime and Editor scripts compiled. Uses installed template package DLLs; not a Unity import, build, or Play Mode test.'

