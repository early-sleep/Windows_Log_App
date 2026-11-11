# Build LogAnalyzer.exe (needs the Windows .NET Framework compiler only)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'LogAnalyzer.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found (.NET Framework 4.x required)' }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /out:$out `
    /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $root 'LogAnalyzer.cs')
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
Write-Host ("built: {0} ({1:N0} bytes)" -f $out, (Get-Item $out).Length)
