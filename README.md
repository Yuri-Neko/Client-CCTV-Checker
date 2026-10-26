# CCTV Report Desk

A portable Windows GUI for Hikvision recording reports, daily camera checks, and editing NVR/camera CSV configuration.

## Download the app

Open the repository's **Releases** page and download `CCTV-Report-Desk-<version>-win-x64.zip`. GitHub Actions builds the EXE on a Windows runner. To publish a release from **Actions → Build Windows app → Run workflow**, enter a version such as `v1.0.0`. Pushing a `v*` tag also publishes a release automatically.

## First use

1. Extract the ZIP to a writable folder.
2. Copy `cameras.csv.example` to `cameras.csv` and `nvr_list.csv.example` to `nvr_list.csv`.
3. Put the license key issued for this computer in `license.key`, or open the app and use **Ganti license key**.
4. Open `CCTV Report Desk.exe`.

The app needs internet access when running a report because its loader checks the license and retrieves the report code. It does not need Excel to edit the CSV files. Report history stays in the app's `Report CCTV` folder; new files can also be copied to a folder you choose.

## Build locally

Windows 10/11 includes Windows PowerShell and the .NET Framework compiler used by `build.ps1`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

No third-party runtime packages are required.

## Keep credentials out of GitHub

The real `cameras.csv`, `nvr_list.csv`, `license.key`, report history, logs, and custom output path are ignored by Git. The repository includes header-only CSV templates. Do not add real NVR passwords or license keys to an issue, commit, or release.

The NVR password column is hidden by default in the app and can be revealed explicitly in the NVR editor. Changing a license key creates a local backup; the server validates the new key when the next report runs.

