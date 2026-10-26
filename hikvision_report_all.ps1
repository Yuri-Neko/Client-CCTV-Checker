#requires -Version 5.1
<#
Hikvision Recording Report - LOADER.
File ini kecil dan tidak berisi logika laporan; logikanya diambil dari server lisensi
setiap kali dijalankan, lalu dijalankan di memori. Butuh koneksi internet.

Taruh sefolder: hikvision_report_all.ps1 (ini), nvr_list.csv, cameras.csv, license.key

  Set-ExecutionPolicy -Scope Process Bypass
  .\hikvision_report_all.ps1                          # semua NVR
  .\hikvision_report_all.ps1 -NvrIp 192.168.150.44    # satu NVR
  .\hikvision_report_all.ps1 -ShowMachineId           # tampilkan ID komputer
#>

$ErrorActionPreference = "Stop"

# === GANTI dengan alamat server lisensimu ===
$LicenseServer = "https://hikvision-license-server.vercel.app"

function Get-MachineId {
    $uuid = ""; $board = ""
    try { $uuid = [string](Get-CimInstance Win32_ComputerSystemProduct).UUID } catch { }
    try { $board = [string](Get-CimInstance Win32_BaseBoard).SerialNumber } catch { }
    if (-not $uuid -and -not $board) {
        try { $uuid = [string](Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Cryptography").MachineGuid } catch { }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("HKV|$uuid|$board"))
    $hex = ($hash | ForEach-Object { $_.ToString("X2") }) -join ""
    return ($hex.Substring(0, 20) -replace '(.{5})(?!$)', '$1-')
}

$machineId = Get-MachineId

if ($args -contains "-ShowMachineId") {
    Write-Host "ID Komputer: $machineId" -ForegroundColor Cyan
    exit 0
}

# License key dari license.key (sefolder) atau ditanya sekali lalu disimpan
$dir = if ($PSScriptRoot) { $PSScriptRoot } else { $PWD.Path }
$keyFile = Join-Path $dir "license.key"
if (Test-Path -LiteralPath $keyFile) {
    $licenseKey = (Get-Content -LiteralPath $keyFile -Raw).Trim()
}
else {
    $licenseKey = (Read-Host "License key").Trim()
    if ($licenseKey) { Set-Content -LiteralPath $keyFile -Value $licenseKey -Encoding ASCII }
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$body = @{ key = $licenseKey; machineId = $machineId } | ConvertTo-Json -Compress

# Cadangan kalau DNS jaringan mati: tanya DNS publik langsung, lalu sambung lewat curl.exe --resolve
# (SNI dan sertifikat HTTPS tetap divalidasi). IP statis hanya pilihan terakhir; bisa berubah sewaktu-waktu.
$FallbackDns = @("1.1.1.1", "8.8.8.8")
$FallbackIps = @("216.198.79.67", "64.29.17.67")

function Invoke-LicenseViaCurl {
    param([string]$Url, [string]$Json)

    $hostName = ([Uri]$Url).Host
    $ips = @()
    foreach ($srv in $FallbackDns) {
        try {
            $ips += @(Resolve-DnsName -Name $hostName -Type A -Server $srv -DnsOnly -ErrorAction Stop |
                Where-Object { $_.Type -eq "A" } | ForEach-Object { $_.IPAddress })
        }
        catch { }
        if ($ips.Count -gt 0) { break }
    }
    $ips += $FallbackIps

    $tmpIn = [IO.Path]::GetTempFileName()
    $tmpOut = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($tmpIn, $Json, (New-Object Text.UTF8Encoding($false)))
        foreach ($ip in @($ips | Select-Object -Unique)) {
            try {
                $null = & curl.exe --silent --show-error --connect-timeout 10 --max-time 40 `
                    --resolve "${hostName}:443:$ip" `
                    -H "Content-Type: application/json" --data-binary "@$tmpIn" `
                    -o $tmpOut "$Url/api/run"
                if ($LASTEXITCODE -ne 0) { continue }
                $txt = [IO.File]::ReadAllText($tmpOut, [Text.Encoding]::UTF8)
                if ([string]::IsNullOrWhiteSpace($txt)) { continue }
                return ($txt | ConvertFrom-Json)
            }
            catch { }
        }
    }
    finally {
        Remove-Item $tmpIn, $tmpOut -Force -ErrorAction SilentlyContinue
    }
    return $null
}

$r = $null
try {
    $r = Invoke-RestMethod -Uri "$LicenseServer/api/run" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 30
}
catch {
    $reason = $null
    try {
        $stream = $_.Exception.Response.GetResponseStream()
        $reason = (New-Object IO.StreamReader($stream)).ReadToEnd() | ConvertFrom-Json | Select-Object -ExpandProperty reason
    }
    catch { }

    if (-not $reason) {
        # Tidak ada jawaban HTTP dari server = masalah jaringan/DNS -> coba jalur cadangan.
        Write-Host "[INFO] Server lisensi tidak terjangkau lewat DNS jaringan; mencoba jalur cadangan (DNS publik)..." -ForegroundColor Yellow
        $r = Invoke-LicenseViaCurl -Url $LicenseServer -Json $body
        if ($null -eq $r) { $reason = "Tidak bisa menghubungi server lisensi (cek koneksi internet / DNS)." }
    }
    if ($null -eq $r) {
        Write-Host "[LISENSI] $reason" -ForegroundColor Red
        Write-Host "ID Komputer ini: $machineId" -ForegroundColor Yellow
        exit 1
    }
}

if (-not $r.ok -or -not $r.script) {
    Write-Host "[LISENSI] $($r.reason)" -ForegroundColor Red
    exit 1
}

Write-Host ("Lisensi : {0} (berlaku s/d {1})" -f $r.customer, $r.expires) -ForegroundColor DarkGray

$global:LoaderDir = $dir
$sb = [scriptblock]::Create($r.script)
& $sb @args