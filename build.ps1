$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Compiler .NET Framework tidak ditemukan. Gunakan Windows dengan .NET Framework bawaan.'
}
$source = Join-Path $PSScriptRoot 'src\CctvDesk.cs'
$output = Join-Path $PSScriptRoot 'CCTV Report Desk.exe'
& $compiler /nologo /target:winexe /optimize+ "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Data.dll $source
if ($LASTEXITCODE -ne 0) { throw "Kompilasi gagal dengan kode $LASTEXITCODE." }
Write-Host "Berhasil dibuat: $output"
