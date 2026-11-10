**Русский** · [English](README.en.md)

# OldYM Desktop

![OldYM](https://github.com/astapovbro/OldYM/blob/main/img/icon.png)

Неофициальный клиент Яндекс Музыки для слабых ПК под Windows.
В одном репозитории — две независимые редакции: лёгкая WinForms и более близкая к оригиналу WPF.

> Проект не связан с ООО «Яндекс». «Яндекс Музыка» — товарный знак соответствующего правообладателя.
> Клиент не поставляет музыку и не обходит платную подписку: он работает с тем, что API отдаёт аккаунту.

## Какую версию брать

| | Legacy | WPF |
|---|---|---|
| папка | `legacy/` | `desktop/` |
| интерфейс | WinForms | WPF |
| язык | C# 4 | C# 5 |
| рантайм | .NET Framework 4.0, x86 | .NET Framework 4.5.2, x86 |
| ОС | Windows XP SP3, Vista SP2, 7 SP1, 8, 8.1+ | Windows 7 SP1 и новее |
| сборка | `csc.exe` из .NET Framework | MSBuild из .NET Framework |
| результат | `bin\OldYM.Desktop.exe` | `bin\Release\OldYM.exe` |

**Legacy** — для самых старых машин: без анимаций, минимум зависимостей.
**Desktop** — когда нужен интерфейс побогаче, а система не старше 7ки.

Обе редакции используют одну и ту же логику API и собственный TLS с корневыми сертификатами в `assets/roots.pem`, поэтому не зависят от системного хранилища и работают на старых Windows.

## Возможности

- вход через OAuth device flow: код показывается в окне, подтверждается на любом другом устройстве;
- сохранение входа между запусками (`account.dat`, DPAPI, привязка к текущему пользователю);
- поиск треков, «Моя волна» с бесконечной очередью, «Мне нравится», плейлисты, скачанное;
- воспроизведение MP3, перемотка, обложка трека, лайк (♥) и скачивание (↓) прямо из списка;
- тёмная тема.

Набор функций в редакциях различается: они развиваются отдельно, общего кода кроме `Api`/`Net`/`Storage` пока нет.

## Структура репозитория

```
/README.md          этот файл
/README.en.md       английская версия
/legacy/            WinForms-редакция (XP+)
/desktop/               WPF-редакция (7 SP1+)
```

## Сборка: legacy

Нужен только .NET Framework 4.0 или новее — Visual Studio не требуется.

```
build.cmd      сборка через %WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
build.sh       сборка через Mono (mcs) в Linux
```

Готовый `bin\OldYM.Desktop.exe` запускается **вместе с содержимым `bin\`**: рядом должны лежать `BouncyCastle.Crypto.dll`, `OldYM.Desktop.exe.config` и папка `assets` с `roots.pem`. Отдельный exe без них не запустится.

## Сборка: WPF

Нужен .NET Framework 4.5.2 и MSBuild вместе с `Microsoft.WinFX.targets` (входит в Framework, не в студию).

```
build.cmd      сборка в bin\Release\OldYM.exe
```

## Где хранятся данные

| редакция | путь |
|---|---|
| legacy | `%LOCALAPPDATA%\OldYMDesktopLegacy` |
| WPF | `%LOCALAPPDATA%\OldYMDesktop` |
| скачанные треки (обе) | `%USERPROFILE%\Музыка\OldYM` |

Удаление папки сбрасывает вход и кэш обложек; скачанная музыка лежит отдельно.

## Лицензия

Основной код — GNU LGPL v3.0 (полный текст в `LICENSE`).

Сторонние компоненты:
- Portable.BouncyCastle 1.9.0 — MIT-подобная лицензия, `licenses/BouncyCastle-LICENSE.txt`;
- корневые сертификаты certifi — MPL-2.0, источник указан в `assets/roots-source.txt`;
- .NET Framework лицензируется Microsoft отдельно.
