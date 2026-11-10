[Русский](README.md) · **English**

# OldYM Desktop

![OldYM](https://github.com/astapovbro/OldYM/blob/main/img/icon.png)

An unofficial Yandex Music client for low-end Windows PCs.
This repository holds two independent editions: a light WinForms one and a closer-to-the-original WPF one.

> This project is not affiliated with Yandex. "Yandex Music" is a trademark of its respective owner.
> The client ships no music and bypasses no paid subscription: it works with whatever the API returns for the signed-in account.

## Which edition to pick

| | Legacy | WPF |
|---|---|---|
| folder | `legacy/` | `desktop/` |
| UI | WinForms | WPF |
| language | C# 4 | C# 5 |
| runtime | .NET Framework 4.0, x86 | .NET Framework 4.5.2, x86 |
| OS | Windows XP SP3, Vista SP2, 7 SP1, 8, 8.1+ | Windows 7 SP1 and newer |
| build | `csc.exe` from .NET Framework | MSBuild from .NET Framework |
| output | `bin\OldYM.Desktop.exe` | `bin\Release\OldYM.exe` |

**Legacy** targets the oldest machines: no animations, minimal dependencies.
**Desktop** is for a richer interface on a system no older than Windows 7.

Both editions share the same API logic and ship their own TLS with root certificates in `assets/roots.pem`, so they do not depend on the system trust store and keep working on old Windows.

## Features

- sign-in through the OAuth device flow: the code is shown in a window and confirmed on any other device;
- the session survives restarts (`account.dat`, DPAPI, bound to the current user);
- track search, "My Wave" with an endless queue, likes, playlists, downloads;
- MP3 playback, seeking, track artwork, like (♥) and download (↓) straight from the list;
- dark theme.

Feature sets differ between editions: they evolve separately and share no code apart from `Api`/`Net`/`Storage`.

## Repository layout

```
/README.md          Russian version
/README.en.md       this file
/legacy/            WinForms edition (XP+)
/wpf/               WPF edition (7 SP1+)
```

## Building legacy

Only .NET Framework 4.0 or newer is required; Visual Studio is not.

```
build.cmd      builds via %WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
build.sh       builds via Mono (mcs) on Linux
```

The resulting `bin\OldYM.Desktop.exe` must run **next to the contents of `bin\`**: `BouncyCastle.Crypto.dll`, `OldYM.Desktop.exe.config` and the `assets` folder with `roots.pem` have to sit beside it. The exe alone will not start.

## Building WPF

Requires .NET Framework 4.5.2 and MSBuild with `Microsoft.WinFX.targets` (part of the Framework itself, not of Visual Studio).

```
build.cmd      builds into bin\Release\OldYM.exe
```

## Where data lives

| edition | path |
|---|---|
| legacy | `%LOCALAPPDATA%\OldYMDesktopLegacy` |
| WPF | `%LOCALAPPDATA%\OldYMDesktop` |
| downloaded tracks (both) | `%USERPROFILE%\Music\OldYM` |

Deleting the data folder resets the session and the artwork cache; downloaded music is stored separately.

## Licence

The main code is GNU LGPL v3.0 (full text in `LICENSE`).

Third-party components:
- Portable.BouncyCastle 1.9.0 — MIT-like licence, `licenses/BouncyCastle-LICENSE.txt`;
- certifi root certificates — MPL-2.0, source listed in `assets/roots-source.txt`;
- .NET Framework is licensed by Microsoft separately.
