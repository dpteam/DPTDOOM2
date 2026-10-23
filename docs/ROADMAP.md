# План и текущее состояние

## Готово

### Движок

- Перенос Managed Doom (C#-перевод Linux Doom) в `DPTDOOM/`.
- Целевая платформа — **.NET Framework 4.0** (`net40`), плюс `net9.0` только
  для тестов.
- Программный рендерер без изменений по поведению: BSP, стены, полы/потолки,
  спрайты, свет, небо, fuzz, палитровые эффекты, интерполяция кадров.
- DeHackEd, сохранения, демо, automap, интермиссия, финалы.
- Геймплейные хеши совпадают с эталонами Managed Doom — демо-совместимость
  сохранена (проверяется тестами).

### Standalone: без сторонних библиотек

Удалены Silk.NET, TrippyGL, DrippyAL, MeltySynth и их нативные зависимости
(GLFW, OpenAL Soft), вместе с их лицензиями в `licenses/`.

Взамен написано своё:

| Было | Стало | Файл |
|---|---|---|
| Silk.NET window + OpenGL | `user32` + `gdi32` `StretchDIBits` | `src/Win32/Win32Window.cs` |
| TrippyGL текстура | свой BGRA-буфер | `src/Win32/Win32Video.cs` |
| DrippyAL/OpenAL | `winmm` `waveOut` + свой микшер | `src/Win32/Win32Sound.cs` |
| MeltySynth + soundfont | MUS → MIDI → системный синтезатор | `src/Win32/Win32Music.cs`, `Mus.cs` |
| Silk input | `WM_KEYDOWN`/мышь | `src/Win32/Win32UserInput.cs` |

Совместимость с net40 закрыта в `src/Common/Compat.cs` (`MathF`, `Math.Clamp`,
`TryAdd`, `ExceptionDispatchInfo`), а `Span`/`MathF`/`AggressiveInlining` в
движке заменены на переносимые варианты.

### Headless

`-headless` работает без окна, звука и ввода, умеет:

- `-tics N` — остановиться на N тиках;
- `-shot file.bmp` — сохранить настоящий кадр (тот же рендерер);
- печатать `State: mobj=... sector=...` для проверки детерминизма;
- запускать сетевую сессию для soak-тестов.

### Сеть

- Lockstep TCP host/join на 2–4 игроков (`src/Net/`).
- Сверка digest WAD перед стартом.
- Статус подключения на экране шрифтом Doom.
- Проверено: 1500 тиков, хеши мира у хоста и гостя совпадают.

### Master server

- `DPTDOOM.Master` — directory-сервер: LIST/REGISTER/HEARTBEAT/REMOVE.
- Ban/allow-списки, CIDR, срок действия, rate limit, lease с токеном.
- Совместим по протоколу с оригинальным C-мастером.
- `tools/test-master.ps1` проверяет полный цикл.

### Лаунчер

- HTML/CSS/JS внутри EXE, рендер через MSHTML (работает на XP).
- Мост через `dptdoom://`-навигацию, без COM.
- Профили, темы, браузер серверов, предпросмотр аргументов, лог сессии.
- `tools/test-launcher.ps1` проверяет мост и сборку аргументов.

### Документация и инструменты

`README.md`, `docs/BUILDING.md`, `docs/LAUNCHER.md`, `docs/MASTER.md`,
`docs/NETWORK.md`, `docs/CI.md`, `AGENTS.md`, скрипты захвата и автотестов
в `tools/`.

### CI

- `.github/workflows/ci.yml`: тесты на Windows и Linux, сборка `net40`.
- `tools/check-standalone.ps1` — обещание «без сторонних библиотек» (нет NuGet
  в поставляемых проектах, нет лишних DLL рядом с exe, цель действительно
  `net40`). Проверен негативными тестами.
- `tools/package-build.ps1` — ZIP для тестировщиков: бинарники, лицензия,
  исходники коммита. IWAD/конфиги/сохранения не попадают.
- Регрессионные тесты на реальные баги: заголовок окна
  (`Win32WindowTest` + `test-window.ps1`), мост лаунчера, протокол master.

## Следующие шаги

### Ближайшее

1. **Первый remote run CI** — подтвердить, что workflow проходит на раннере,
   особенно задача на `ubuntu-latest`.
2. **Проверка на Windows XP** — реальный запуск, не только «код подобран под XP».
3. Звук: проверить миксер и MIDI на живом железе, подобрать буфер.
4. Настройки видео в меню: разрешение, fullscreen без пересоздания окна.
5. `-record`: запись демо (сейчас есть только проигрывание).

### Средний срок

- Relay через master (операции 5/6 протокола уже объявлены).
- Server browser: ping, избранное, фильтры.
- Reconnect и late join.
- PK3/ZIP как контейнер ресурсов, включая WAD внутри.
- DeHackEd: расширить подмножество, оценить BEX/MBF.

### Долгий срок

- DECORATE-подобные расширения (смотреть DOOM ACE, `refs/doom_ace-master`).
- Боты для DM/COOP, проходящие карты.
- 3D-полы, воксели, HD-текстуры, Vulkan/иной ускоритель.
- Кроссплатформенные бэкенды (SDL/X11) на тех же интерфейсах.

## Осознанные ограничения

- Нет заявки на полную совместимость с Boom/MBF/MBF21 — сначала аудит и тесты.
- Сеть не заявлена как Internet-ready: нет шифрования, NAT traversal, аккаунтов.
- IWAD не распространяется; P2P-обмен ресурсами не реализован.
