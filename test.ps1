param([string]$NativeTestDirectory = '')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testExe = Join-Path $env:TEMP ('QuickPPPoE-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
& $compiler /nologo /target:exe /platform:anycpu /utf8output /reference:System.Security.dll /reference:System.Xml.dll ("/out:" + $testExe) (Join-Path $PSScriptRoot 'src\NativeRas.cs') (Join-Path $PSScriptRoot 'src\ConnectionController.cs') (Join-Path $PSScriptRoot 'src\Settings.cs') (Join-Path $PSScriptRoot 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
try {
    if ($NativeTestDirectory) { & $testExe $NativeTestDirectory } else { & $testExe }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Remove-Item -LiteralPath $testExe -ErrorAction SilentlyContinue }
