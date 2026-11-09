# ZipHere

[![Build](https://github.com/INTROLMMORTAL/Zip-Here/actions/workflows/build.yml/badge.svg)](https://github.com/INTROLMMORTAL/Zip-Here/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)
![Dependencies](https://img.shields.io/badge/dependencies-none-brightgreen)

[Русская версия](README.md)

**ZipHere** is a tiny Windows utility that takes care of unpacking **ZIP, RAR, 7z and TAR** archives:

- **automatically extracts** every new archive you download into the Downloads folder;
- adds an **"Распаковать здесь" ("Extract here")** item to the right-click menu of any archive;
- **moves the archive to the Recycle Bin** after a successful extraction.

No Python, no .NET SDK, no archiver to install: the installer is a single `.exe` file, built with the compiler that ships with Windows.

```
Downloaded archive.rar  →  a few seconds later there is an archive\ folder next to it, and the archive is in the Recycle Bin
```

## Contents

- [Features](#features)
- [Supported formats](#supported-formats)
- [Installation](#installation)
- [Usage](#usage)
- [What happens to your archives](#what-happens-to-your-archives)
- [Uninstalling](#uninstalling)
- [Building from source](#building-from-source)
- [How it works](#how-it-works)
- [FAQ and limitations](#faq-and-limitations)
- [Project structure](#project-structure)
- [Releases and CI](#releases-and-ci)
- [Contributing](#contributing)
- [Third-party components](#third-party-components)
- [License](#license)

## Features

- **Auto-extract downloads.** A background program with a tray icon watches the Downloads folder. As soon as the browser finishes a new archive, it is extracted and you get a notification.
- **Many formats.** ZIP, RAR (including RAR5 with a large dictionary), 7z, TAR and compressed tar archives. The format is detected from the file contents, not from its extension.
- **Autostart.** Install once and it starts with Windows; there is nothing to open by hand.
- **"Extract here"** in the right-click menu for archives in any other folder.
- **Archive goes to the Recycle Bin.** After a successful extraction the original archive is moved to the Recycle Bin and can be restored from there.
- **Safe for your files.** Nothing is overwritten: if the folder already exists, `name_1`, `name_2`, etc. is created. If an archive is corrupted or password-protected, it stays in place and the half-made folder is removed.
- **Clear errors.** When extraction fails, the program shows the real reason and what each extractor reported.
- **Tray control.** Auto-extraction can be switched off and on at any time.
- **No administrator rights** — everything is written only to the current user's profile (`HKCU` and `%LOCALAPPDATA%`).
- **No third-party programs to install** — works on a clean Windows 10/11 (it uses the .NET Framework 4 already built into the system and an `UnRAR.exe` embedded in the `.exe`).
- **One file** — `ZipHereSetup.exe` installs, updates and uninstalls the program.

## Supported formats

| Format | Extensions | Extracted with |
| ------ | ---------- | -------------- |
| ZIP | `.zip` | built-in .NET |
| RAR | `.rar` | the embedded `UnRAR.exe` |
| 7z | `.7z` | 7-Zip if installed, otherwise Windows' `tar.exe` |
| TAR | `.tar`, `.tgz`, `.tbz2`, `.txz`, `.tar.gz`, `.tar.bz2`, `.tar.xz` | Windows' `tar.exe` |

The "Extract here" item also appears for standalone `.gz`, `.bz2`, `.xz` files. They are not extracted automatically from Downloads, though: these can be single compressed files rather than archives.

If **7-Zip** or **WinRAR** is installed, ZipHere uses it first: they support more format variants.

## Installation

1. Open the [**Releases**](https://github.com/INTROLMMORTAL/Zip-Here/releases) page and download `ZipHereSetup.exe`.
2. Run it, read the description and click **"Yes"**.
3. Done: the ZipHere icon appears near the clock and the program starts with Windows.

> Want to make sure the file hasn't been tampered with? The release includes `ZipHereSetup.exe.sha256`. Check in PowerShell:
>
> ```
> (Get-FileHash .\ZipHereSetup.exe -Algorithm SHA256).Hash.ToLower()
> ```
>
> The result must match the contents of the `.sha256` file.

Or [build the `.exe` yourself](#building-from-source) — it takes two clicks.

**Updating.** Run the new `ZipHereSetup.exe` again and choose "Yes — update".

## Usage

### Automatically

Just download archives. Once the download finishes, a folder with the extracted contents appears next to the archive, and the archive goes to the Recycle Bin. A notification with the result pops up above the tray icon.

Tray icon (in Windows 11 it may hide under the `^` arrow near the clock):

| Menu item                                   | Action                                                           |
| ------------------------------------------- | ---------------------------------------------------------------- |
| **Auto-extract downloads** (checkbox)       | turn automatic extraction on or off                              |
| **Open Downloads folder**                   | open Downloads in Explorer (same as double-clicking the icon)    |
| **Exit**                                    | close the program until the next Windows sign-in                 |

Switching auto-extraction off is remembered between restarts.

### Manually

Right-click any supported archive in any folder → **"Extract here"**. The contents end up in a folder named after the archive next to it, and the archive goes to the Recycle Bin.

**Windows 11.** The item is not in the new short context menu — it is under **"Show more options"** (or press `Shift + F10` on the selected archive). This is a Windows limitation for classic menu items.

Examples:

| Before                                           | After                                    |
| ------------------------------------------------ | ---------------------------------------- |
| `Downloads\files.zip`                            | `Downloads\files\…`, archive in the bin  |
| `Downloads\mod.rar`                              | `Downloads\mod\…`, archive in the bin    |
| `Downloads\files.zip` (folder `files` exists)    | `Downloads\files_1\…`, archive in the bin|

## What happens to your archives

Since the program deletes files by itself, here are its rules in one place:

- It watches **only the current user's Downloads folder** and **only new files**: archives that were there before launch are left alone.
- It waits until the file has finished downloading: it must be openable and stop growing.
- The archive goes **to the Recycle Bin**, not deleted permanently. It can be restored.
- If extraction fails (corrupted, password-protected, out of disk space), the archive is **not deleted**, the unfinished folder is removed, and the tray shows an error notification.
- **Multi-volume RAR** (`name.part1.rar`, `name.r00`): only the first volume is extracted, the other volumes are skipped, and the archive is not sent to the Recycle Bin. These need 7-Zip or WinRAR.
- If you need to keep a specific archive (for example, to send it to someone), turn auto-extraction off in the tray menu or take the archive out of the Recycle Bin.

## Uninstalling

**Normally:** run `ZipHereSetup.exe` again and choose "No — uninstall". The menu items, autostart, settings and program files (including the embedded `UnRAR.exe`) are removed.

**Manually** (if you don't have the installer at hand), in `cmd`:

```
taskkill /f /im ZipHere.exe
for %e in (.zip .rar .7z .tar .tgz .tbz2 .txz .gz .bz2 .xz) do reg delete "HKCU\Software\Classes\SystemFileAssociations\%e\shell\ZipHere" /f
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ZipHere /f
reg delete "HKCU\Software\ZipHere" /f
rmdir /s /q "%LOCALAPPDATA%\ZipHere"
```

## Building from source

Windows only: the `csc.exe` compiler is part of .NET Framework 4, which is present in every Windows 10/11.

```
git clone https://github.com/INTROLMMORTAL/Zip-Here.git
cd Zip-Here
build.bat
```

The result is `dist\ZipHereSetup.exe`.

If `UnRAR.exe` is in the `third_party` folder, `build.bat` embeds it into the `.exe` as a resource. The build also succeeds without it, but RAR files with a large dictionary won't open unless 7-Zip or WinRAR is installed.

The same compiler call without the script:

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /codepage:65001 ^
  /out:dist\ZipHereSetup.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:Microsoft.VisualBasic.dll ^
  /resource:third_party\UnRAR.exe,UnRAR.exe ^
  src\Installer.cs
```

`build.bat` accepts an optional `nopause` argument that disables the pause at the end (used in CI).

## How it works

All the code is a single file, [`src/Installer.cs`](src/Installer.cs). The program runs in three modes depending on its launch arguments:

| Launch                | What happens                                                                                                                                       |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| No arguments          | Installer: offers to install; if already installed, to update or uninstall                                                                         |
| `--watch`             | Background mode: tray icon and Downloads watching. Started by Windows autostart; a second instance does not start (checked via a `Mutex`)        |
| `path\to\archive.<ext>` | Manual extraction from the right-click menu: a progress window, then the archive goes to the Recycle Bin                                         |

### Installation

1. Stops already running copies (`ZipHere.exe`) so that the file can be updated.
2. Copies its own `.exe` to `%LOCALAPPDATA%\ZipHere\ZipHere.exe`.
3. Writes to the registry (everything in `HKCU`, no administrator rights needed):

```
HKCU\Software\Classes\SystemFileAssociations\<.ext>\shell\ZipHere   (for each supported extension)
    (default) = "Распаковать здесь"
    Icon      = "zipfldr.dll"
    \command
        (default) = "…\ZipHere\ZipHere.exe" "%1"

HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    ZipHere = "…\ZipHere\ZipHere.exe" --watch

HKCU\Software\ZipHere
    AutoUnpack = 1        (created the first time the tray checkbox is toggled)
```

4. Starts the background mode immediately, no reboot needed.

The `SystemFileAssociations\<.ext>` branch adds the item to files with that extension regardless of which program is the default for them. The item's icon comes from the system library `zipfldr.dll`.

### Auto-extraction

The `TrayApp` class creates a `FileSystemWatcher` on the Downloads folder (the path is requested from the system via `SHGetKnownFolderPath`, so it also works with a relocated folder). Browsers download into a temporary file (`.crdownload`, `.part`) and rename it to the archive at the end, so both creation and rename events are handled. For each file, on a thread-pool thread, it:

1. waits until the file can be opened exclusively and its size stops changing (at most 15 minutes);
2. calls `Unpack`;
3. shows a notification with the result.

### Extraction

`Unpack` determines the target folder (`<archive folder>\<archive name>`), picks a free name (`_1`, `_2`, …) and calls `Extract`, which:

1. detects the format from the file's first bytes (`Sniff`), not from its extension;
2. for ZIP calls `ZipFile.ExtractToDirectory`; if that fails, falls back to the external extractors;
3. for other formats tries in turn: **7-Zip** (if installed) → **UnRAR** (an installed WinRAR or the one embedded in the `.exe`) → Windows' **`tar.exe`** (libarchive-based). The target folder is cleaned before each attempt;
4. if all attempts fail, shows the errors from each extractor.

On success the original file goes to the Recycle Bin via `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` with `RecycleOption.SendToRecycleBin`; on failure the created folder is removed and the archive is left untouched.

The embedded `UnRAR.exe` is saved to `%LOCALAPPDATA%\ZipHere\UnRAR.exe` on first use and removed together with the program. It is needed because Windows' built-in `tar.exe` cannot read some RAR files (for example, with a large dictionary: "Declared dictionary size is not supported").

Protection against Zip Slip (escaping the target folder via `..\`) is provided for ZIP by .NET Framework 4.6.2+ itself; on older versions, rely on archives coming from trusted sources. For the other formats the extractors provide that protection themselves.

### Why C# and `csc.exe`

The goal is "install nothing". `csc.exe` is present in every Windows since 7, so the project builds without Visual Studio, an SDK or third-party packages. The code is deliberately written in C# 5 so that this built-in compiler can build it.

## FAQ and limitations

**How do I turn off auto-extraction but keep the menu item?** Tray icon → uncheck "Auto-extract downloads".

**There is no tray icon.** In Windows 11 new icons hide under the `^` arrow near the clock; drag the icon onto the taskbar to keep it visible. If it's empty there, run `ZipHereSetup.exe` and choose "Yes — update": the background program will start again.

**Not everything was extracted / the archive stayed in place.** That means extraction failed. The reason is shown in the tray or in the window. Common cases: the archive is corrupted, password-protected, or the disk is full.

**A file was downloaded somewhere other than Downloads.** Only the Downloads folder is handled automatically. For other places there is "Extract here" in the right-click menu.

**Antivirus or SmartScreen complains about the file.** The file is not digitally signed (signing is paid), so SmartScreen may show a "Windows protected your PC" warning — click "More info" → "Run anyway". The program edits registry keys, copies its own `.exe`, adds autostart and saves the embedded `UnRAR.exe`, which sometimes trips antivirus heuristics. The source is open, and you can build the `.exe` yourself.

**Which formats are supported?** ZIP, RAR, 7z, TAR (including `.tar.gz`, `.tgz`, `.tar.bz2`, `.tar.xz`). See [Supported formats](#supported-formats).

**Password-protected archives?** Not supported: an error is shown and the archive stays in place.

**"Declared dictionary size is not supported".** This is how the built-in `tar.exe` fails on a RAR with a very large dictionary. It should not appear in ZipHere: such archives are extracted by the embedded `UnRAR.exe`. If you still see it, update to the latest version.

**Multi-volume archives (`.part1.rar`, `.r00`).** Only the first volume is extracted and the archive stays in place. You need 7-Zip or WinRAR: they pick up the remaining volumes by themselves.

**Garbled file names.** If a ZIP was created by an old program that doesn't use UTF-8 for names, Cyrillic inside may be displayed incorrectly: .NET Framework uses the system code page.

**The item didn't appear in the right-click menu.** Check that you are looking at a supported archive, and in Windows 11 open "Show more options". If that doesn't help, restart Explorer (Task Manager → Windows Explorer → Restart).

**Can it be installed for all users?** Not at the moment: installation goes into the current user's profile so that no administrator rights are required.

## Project structure

```
Zip-Here/
├── .github/workflows/build.yml   CI: build, checksum, release publishing on tag
├── src/Installer.cs              the entire source code
├── third_party/
│   ├── UnRAR.exe                 embedded into ZipHereSetup.exe at build time
│   └── UnRAR-license.txt         RARLAB's license for UnRAR
├── build.bat                     build via the built-in csc.exe
├── CHANGELOG.md                  change history
├── LICENSE                       MIT
├── README.md                     description (RU)
├── README.en.md                  description (EN)
├── .gitattributes                correct line endings (.bat → CRLF)
└── .gitignore
```

## Releases and CI

The [`build.yml`](.github/workflows/build.yml) workflow runs on `windows-latest`:

- on every push to `main` and every pull request — builds `ZipHereSetup.exe` and attaches it as a run artifact;
- on a tag push like `v1.4.0` — additionally creates a GitHub Release with `ZipHereSetup.exe`, the checksum file and an auto-generated changelog.

Releasing a new version:

1. Update the version in `src/Installer.cs` (`AssemblyVersion` and `AssemblyFileVersion`) and add an entry to `CHANGELOG.md`.
2. Make sure the changes are committed to `main`.
3. Create a release on GitHub: **Releases → Create a new release**, with a tag like `v1.4.0` (**Create new tag on publish**). The build starts by itself and attaches the `.exe`.

> If you had to delete a release, delete its tag too (Releases → Tags): otherwise a new release with the same name will reuse the old commit.

## Contributing

Issues and pull requests are welcome. Please keep the project's principles in mind:

- **minimalism** — only unpacking archives, no "just in case" settings;
- **nothing to install** — the build must work on a clean Windows with a single `build.bat`; external extractors (7-Zip, WinRAR) are allowed only as an optional bonus;
- **C# 5 compatibility** — no string interpolation, `?.`, `nameof` and the like, otherwise the built-in compiler can't cope;
- **care with user files** — never overwrite or permanently delete anything.

Ideas that fit the project: choosing the folder to watch, a multilingual interface (by system language), signing the executable, installing for all users, an icon for the `.exe`, a password prompt for protected archives.

## Third-party components

`ZipHereSetup.exe` embeds `UnRAR.exe` by RARLAB (the [`third_party`](third_party) folder). It is used only to read RAR archives and is distributed under RARLAB's license, whose text is stored next to it in `UnRAR-license.txt`. That license does not apply to the rest of the ZipHere code.

## License

[MIT](LICENSE) © 2026 INTROLMMORTAL (except `third_party`, see above)
