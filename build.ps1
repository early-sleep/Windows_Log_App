# 一键编译 LogCollector.exe
# 依赖：Windows 自带的 .NET Framework 4.x 编译器（csc.exe），无需安装 Visual Studio
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$outDir = Join-Path $root 'build'
$outExe = Join-Path $outDir 'LogCollector.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw '找不到 csc.exe，请确认已安装 .NET Framework 4.x' }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$files = @('Core.cs', 'Jobs.cs', 'Report.cs', 'MainForm.cs', 'Program.cs', 'AssemblyInfo.cs') |
    ForEach-Object { Join-Path $src $_ }

$args = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/out:' + $outExe),
    ('/win32manifest:' + (Join-Path $src 'app.manifest')),
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
) + $files

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$item = Get-Item $outExe
Write-Host ("编译成功：{0}  ({1:N0} 字节)" -f $item.FullName, $item.Length)
