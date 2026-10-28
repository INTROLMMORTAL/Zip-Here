# ZipHere

[![Build](https://github.com/INTROLMMORTAL/Zip-Here/actions/workflows/build.yml/badge.svg)](https://github.com/INTROLMMORTAL/Zip-Here/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)
![Dependencies](https://img.shields.io/badge/dependencies-none-brightgreen)

[Русская версия](README.md)

**ZipHere** is a tiny Windows utility that takes care of ZIP archives for you:

- **automatically extracts** every new ZIP you download into your Downloads folder;
- adds **"Распаковать здесь"** ("Extract here") to the right-click menu of any ZIP;
- **moves the archive to the Recycle Bin** after successful extraction.

No Python, no .NET SDK, no third-party archivers. The installer is a single `.exe`, built with the compiler that ships with Windows.

```
Download archive.zip  →  a few seconds later: folder archive\ next to it, the ZIP is in the Recycle Bin
```

## Features

- **Auto-extract downloads.** A tray app watches your Downloads folder and unpacks new `.zip` files once the browser has finished downloading them, then shows a notification.
- **Starts with Windows.** Install once, never open anything manually.
- **"Extract here"** in the context menu for ZIPs anywhere else.
- **Archive goes to the Recycle Bin** after a successful extraction, so it is recoverable.
- **Safe for your files.** Nothing is overwritten (`name_1`, `name_2`, … if the folder exists). A corrupt or password-protected archive stays where it is, and the half-made folder is removed.
- **Tray control.** Turn auto-extraction on and off at any time.
- **No admin rights.** Only the current user's profile is touched (`HKCU`, `%LOCALAPPDATA%`).
- **No dependencies.** Runs on a clean Windows 10/11 using the built-in .NET Framework 4.
- **One file.** `ZipHereSetup.exe` installs, updates and uninstalls.

## Install

1. Download `ZipHereSetup.exe` from [**Releases**](https://github.com/INTROLMMORTAL/Zip-Here/releases).
2. Run it, read the description and click **Yes**.
3. A tray icon appears and ZipHere will start with Windows.

A SHA-256 checksum is published next to each release:

```powershell
(Get-FileHash .\ZipHereSetup.exe -Algorithm SHA256).Hash.ToLower()
```

To update, run the new `ZipHereSetup.exe` and choose **Yes — update**.

## Usage

**Automatic.** Just download ZIP files. When a download finishes, a folder with the contents appears next to it and the archive goes to the Recycle Bin. Tray menu: toggle **auto-extraction**, **open Downloads**, **exit**. The setting persists across restarts. On Windows 11 the icon may hide under the `^` arrow near the clock.

**Manual.** Right-click any `.zip` → **"Распаковать здесь"**. On Windows 11 it lives under **"Show more options"** (or press `Shift + F10`).

> The labels are currently in Russian. Changing the `MenuText` constant and the strings in `src/Installer.cs` and rebuilding is all it takes to localize.

## What happens to archives

- Only the current user's **Downloads folder** is watched, and only **new files**; ZIPs that were already there are left alone.
- The file must be fully downloaded: openable and no longer growing.
- The archive goes to the **Recycle Bin**, not permanently deleted.
- If extraction fails (corrupt, password-protected, disk full) the archive is **kept**, the partial folder is removed and an error notification is shown.
- Need to keep a particular ZIP (e.g. to send it on)? Turn auto-extraction off in the tray menu, or restore it from the Recycle Bin.

## Uninstall

Run `ZipHereSetup.exe` again and choose **No — uninstall**. Or manually:

```bat
taskkill /f /im ZipHere.exe
reg delete "HKCU\Software\Classes\SystemFileAssociations\.zip\shell\ZipHere" /f
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ZipHere /f
reg delete "HKCU\Software\ZipHere" /f
rmdir /s /q "%LOCALAPPDATA%\ZipHere"
```

## Build from source

Windows only; the compiler (`csc.exe`) is part of the built-in .NET Framework 4.

```bat
git clone https://github.com/INTROLMMORTAL/Zip-Here.git
cd Zip-Here
build.bat
```

Output: `dist\ZipHereSetup.exe`. Pass `nopause` to `build.bat` to skip the final pause (used in CI).

## How it works

The whole program is [`src/Installer.cs`](src/Installer.cs) with three modes:

| Launch | Behavior |
|---|---|
| No arguments | Installer: offers to install, or to update/uninstall if already installed |
| `--watch` | Background mode: tray icon + Downloads watcher; started by Windows autostart; single instance via a `Mutex` |
| `path\to\archive.zip` | Manual extraction from the context menu: progress window, then the archive goes to the Recycle Bin |

Installation stops running copies, copies the `.exe` to `%LOCALAPPDATA%\ZipHere\ZipHere.exe`, writes the registry entries below (all under `HKCU`) and starts the watcher immediately:

```
HKCU\Software\Classes\SystemFileAssociations\.zip\shell\ZipHere   → "…\ZipHere.exe" "%1"
HKCU\Software\Microsoft\Windows\CurrentVersion\Run  ZipHere        → "…\ZipHere.exe" --watch
HKCU\Software\ZipHere  AutoUnpack = 1                              (tray toggle)
```

The watcher uses a `FileSystemWatcher` on the Downloads folder (resolved with `SHGetKnownFolderPath`). Browsers download to a temp file and rename it to `.zip` at the end, so both create and rename events are handled. Each file is processed on the thread pool: wait until it is ready (up to 15 min), extract, notify.

`Unpack` extracts with `ZipFile.ExtractToDirectory` into a fresh folder named after the archive and then sends the archive to the Recycle Bin via `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile`. On failure the new folder is deleted and the archive is left intact. Zip Slip protection comes from .NET Framework 4.6.2+.

The code is intentionally C# 5 so that the compiler built into Windows can build it.

## Limitations

- Only `.zip` (no RAR/7z/tar.gz), no password-protected archives.
- Auto-extraction covers the Downloads folder only; use the context menu elsewhere.
- The executable is not code-signed, so SmartScreen may warn on first run ("More info" → "Run anyway"). You can always build it yourself.
- Archives made by legacy tools without UTF-8 file names may show garbled non-ASCII names.
- Per-user installation only.

## Releases & CI

[`build.yml`](.github/workflows/build.yml) builds on `windows-latest` for every push/PR. Publishing a release with a tag like `v1.1.0` attaches `ZipHereSetup.exe` and its checksum.

## Contributing

Issues and PRs are welcome. Keep it minimal, dependency-free, C# 5 compatible and careful with user files (never overwrite, never delete permanently).

## License

[MIT](LICENSE) © 2026 INTROLMMORTAL
