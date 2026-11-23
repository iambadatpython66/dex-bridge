param([switch]$SkipPackage)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkPath 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.x compiler is required to build.' }
$outputPath = Join-Path $projectRoot 'dist\DeX-Bridge'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warnaserror+',('/out:' + (Join-Path $outputPath 'DeXBridge.exe')),('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),('/resource:' + (Join-Path $projectRoot 'src\MainWindow.xaml') + ',DexBridge.MainWindow.xaml'))
$iconPath = Join-Path $projectRoot 'assets\App.ico'
if (Test-Path -LiteralPath $iconPath) { $arguments += ('/win32icon:' + $iconPath); $arguments += ('/resource:' + $iconPath + ',DexBridge.App.ico') }
foreach ($assembly in @('PresentationFramework','PresentationCore','WindowsBase')) { $arguments += ('/reference:' + (Join-Path $frameworkPath ('WPF\' + $assembly + '.dll'))) }
$arguments += @('/reference:System.Xaml.dll','/reference:System.Core.dll','/reference:System.Xml.dll')
$arguments += @(Join-Path $projectRoot 'src\Core.cs'),@(Join-Path $projectRoot 'src\WindowController.cs'),@(Join-Path $projectRoot 'src\Program.cs')
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'DeXBridge.exe.config') -Destination $outputPath -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'COMPATIBILITY.md'),(Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $outputPath -Force
$toolSource = Join-Path $projectRoot 'build-cache\scrcpy\scrcpy-win64-v5.0.1'
if (!(Test-Path -LiteralPath (Join-Path $toolSource 'scrcpy.exe'))) { throw 'Run node scripts\download-tools.mjs, then extract the verified archive as described in README.md.' }
$toolTarget = Join-Path $outputPath 'tools'
New-Item -ItemType Directory -Path $toolTarget -Force | Out-Null
Get-ChildItem -LiteralPath $toolSource -File | Where-Object { $_.Extension -eq '.exe' -or $_.Extension -eq '.dll' -or $_.Name -eq 'scrcpy-server' -or $_.Name -eq 'LICENSE.txt' } | Copy-Item -Destination $toolTarget -Force
$licenseCache = Join-Path $projectRoot 'build-cache\licenses'
if (Test-Path -LiteralPath $licenseCache) {
  $licenseTarget = Join-Path $toolTarget 'licenses'
  New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
  Get-ChildItem -LiteralPath $licenseCache -File | Copy-Item -Destination $licenseTarget -Force
}
if (!$SkipPackage) {
  $zipPath = Join-Path $projectRoot 'dist\DeX-Bridge-Windows-x64.zip'
  # Select release files explicitly so runtime ADB keys and settings never enter the package.
  Compress-Archive -LiteralPath (Join-Path $outputPath 'DeXBridge.exe'),(Join-Path $outputPath 'DeXBridge.exe.config'),(Join-Path $outputPath 'README.md'),(Join-Path $outputPath 'COMPATIBILITY.md'),(Join-Path $outputPath 'THIRD-PARTY-NOTICES.md'),$toolTarget -DestinationPath $zipPath -Force
  Write-Output ('Portable release: ' + $zipPath)
}
Write-Output ('App: ' + (Join-Path $outputPath 'DeXBridge.exe'))
