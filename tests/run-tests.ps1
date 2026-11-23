$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkPath 'csc.exe'
$fixturePath = Join-Path $projectRoot 'build-cache\test-fixture'
$toolsPath = Join-Path $fixturePath 'tools'
New-Item -ItemType Directory -Path $toolsPath -Force | Out-Null
$fakePath = Join-Path $toolsPath 'adb.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $fakePath) (Join-Path $PSScriptRoot 'FakeTransport.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
Copy-Item -LiteralPath $fakePath -Destination (Join-Path $toolsPath 'scrcpy.exe') -Force
Set-Content -LiteralPath (Join-Path $toolsPath 'scrcpy-server') -Value 'Test fixture only'
$testPath = Join-Path $fixturePath 'Tests.exe'
$appPath = Join-Path $projectRoot 'dist\DeX-Bridge\DeXBridge.exe'
$arguments = @('/nologo','/target:exe','/platform:x64','/warnaserror+',('/out:' + $testPath),('/reference:' + $appPath),'/reference:System.Xaml.dll','/reference:System.Core.dll')
foreach ($assembly in @('PresentationFramework','PresentationCore','WindowsBase')) { $arguments += ('/reference:' + (Join-Path $frameworkPath ('WPF\' + $assembly + '.dll'))) }
$arguments += (Join-Path $PSScriptRoot 'Tests.cs')
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath $appPath -Destination (Join-Path $fixturePath 'DeXBridge.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'DeXBridge.exe.config') -Destination ($testPath + '.config') -Force
& $testPath $projectRoot $fixturePath
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
