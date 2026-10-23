# Лаунчер

`dptdoom-launcher.exe` — локальный HTML-интерфейс в одном EXE. Никаких CDN,
Node.js, Python, WebView2 или HTTP-сервера.

## Почему MSHTML, а не WebView2

Оригинальный DPT.DOOM использовал WebView2. Для порта на .NET Framework 4.0 это
неподходяще:

- **WebView2 не устанавливается на Windows XP** — ни Evergreen, ни Fixed Version.
- Он требует скачивания рантайма и наличия Edge в системе.
- Это внешняя зависимость, а цель порта — полностью standalone.

Поэтому интерфейс рендерит `WebBrowser` — обёртка над **MSHTML** (`mshtml.dll`),
движком Internet Explorer. Он есть в каждой поддерживаемой версии Windows и не
требует ничего доустанавливать.

Цена решения: движок старый. Страница написана под него:

- ES5-скрипт: без стрелочных функций, шаблонных строк, `Promise`, `let/const`.
- CSS без custom properties, `grid`, `gap` и `flex`.
- Только `div`-разметка: HTML5-теги в старом документ-режиме не стилизуются.
- Документ-режим поднимается до IE11 через `FEATURE_BROWSER_EMULATION`
  (ключ на текущего пользователя, запись необязательна).

## Как страница общается с C#

COM-мост `window.external` на части конфигураций молча не отдаёт методы, поэтому
используется схема, работающая везде:

```
JS  -> host:  window.__dptOutbox = payload;
              location.href = "dptdoom://<verb>"
host:         Navigating -> e.Cancel = true;
              verb = url после схемы
              payload = InvokeScript("dptdoomTakePayload")
host -> JS:   InvokeScript("dptReceive", [verb, payload])
```

Переход всегда отменяется — это только перенос сообщения. Никакой навигации в
интернет не происходит.

### Команды

| Verb | Что делает |
|---|---|
| `ready` | страница загрузилась: хост отдаёт тему, состояние, статус, путь данных |
| `preview` | собрать аргументы запуска и показать их |
| `browse` | `exe` / `iwad` / `pwad` — диалог выбора файла |
| `launch` | запустить игру |
| `profile` | `load` / `save` / `select` — профили 1–3 |
| `themeSave` | сохранить выбор темы |
| `masterSave` | сохранить адрес master |
| `masterRefresh` | запросить список сессий |
| `openLog` | открыть лог последней сессии |

### Формат payload

Строки через `\n`, PWAD-список — через `\u001f`:

```
executable, iwad, map, skill, address, port, name,
flag bits, mode, pwads, profile slot
```

`flag bits`: 1 = без монстров, 2 = быстрые монстры, 4 = возрождение,
8 = deathmatch. `mode`: 0 = одиночная, 1 = хост, 2 = подключение.
Тот же формат кодирует `LauncherState.Encode()`, поэтому страница и хост не
расходятся.

## Данные

| Режим | Каталог |
|---|---|
| `portable.txt` рядом с EXE | `<exe>/user/` |
| иначе | `%LOCALAPPDATA%\DPTDOOM\` |

```
launcher.ini          тема, master, последнее состояние
profileN.ini          сохранённые профили 1..3
webview/launcher.html собранная страница (перезаписывается при старте)
game/profileN/        конфиг и сохранения игры (-userdir)
sessions/<время>/game.log  лог запуска
```

Игра запускается напрямую (`Process.Start`), без shell, с постоянным рабочим
каталогом профиля.

## Запуск

```powershell
.\DPTDOOM.Launcher\bin\Release\net40\dptdoom-launcher.exe
```

`dptdoom.exe` ищется рядом с лаунчером, затем в `DPTDOOM/bin/Release/net40`.
Путь можно задать вручную в поле «Исполняемый файл».

## Отладка страницы

```powershell
# собрать HTML и открыть в браузере: интерфейс без хоста (кнопки не работают)
.\dptdoom-launcher.exe --dump-html launcher-preview.html

# диагностика документа
$env:DPTDOOM_LAUNCHER_DEBUG=1
.\dptdoom-launcher.exe
# -> %LOCALAPPDATA%\DPTDOOM\launcher-debug.txt, launcher-nav.txt
```

## Автотест

`tools/test-launcher.ps1` создаёт форму, дожидается загрузки страницы и
проверяет: мост отвечает, статус приходит из C#, тема переключается, режим
«Создать COOP» показывает сетевые поля, а аргументы запуска собирает C#.
