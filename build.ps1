$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-icon.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Security.dll /reference:System.Xml.dll ("/win32icon:" + (Join-Path $PSScriptRoot 'assets\app.ico')) ("/out:" + (Join-Path $PSScriptRoot 'QuickPPPoE.exe')) $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Built QuickPPPoE.exe (single-file distribution; requires system .NET Framework 4.8)'
