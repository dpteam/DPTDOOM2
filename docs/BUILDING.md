# Сборка и запуск

## Требования

- Windows (основная и проверенная платформа).
- .NET SDK 9 или новее — **только чтобы собрать**. Сама игра собирается под
  .NET Framework 4.0 и работает без установленного .NET на Windows XP SP3+,
  Vista, 7, 8, 10, 11 (4.0 входит в Windows с Vista; на XP нужен .NET 4.0).
- Свой IWAD (`DOOM2.WAD`, `DOOM.WAD`, `FREEDOOM*.WAD`).

Пакет `Microsoft.NETFramework.ReferenceAssemblies` подтягивается NuGet-ом и нужен
только на этапе компиляции: это reference assemblies для net40, а не рантайм.

## Сборка

```powershell
dotnet build DPTDOOM.slnx -c Release
```

Результат:

```
DPTDOOM/bin/Release/net40/dptdoom.exe
DPTDOOM.Master/bin/Release/net40/dptdoom-master.exe
DPTDOOM.Launcher/bin/Release/net40/dptdoom-launcher.exe
```

Собрать один проект:

```powershell
dotnet build DPTDOOM\DPTDOOM.csproj -f net40 -c Release
```

## Запуск

```powershell
.\DPTDOOM\bin\Release\net40\dptdoom.exe -iwad refs\iwads\doom2.wad
```

Если IWAD лежит рядом с EXE, `-iwad` можно не указывать: игра ищет
`DOOM2.WAD`, `PLUTONIA.WAD`, `TNT.WAD`, `DOOM.WAD`, `DOOM1.WAD`,
`FREEDOOM2.WAD`, `FREEDOOM1.WAD`.

## Параметры командной строки

| Параметр | Значение |
|---|---|
| `-iwad <file>` | основной WAD |
| `-file <wad...>` | PWAD, порядок загрузки важен |
| `-deh <file...>` | DeHackEd патчи |
| `-warp <map>` / `-warp <ep> <map>` | сразу на карту |
| `-skill <1..5>` | сложность |
| `-deathmatch`, `-altdeath` | режимы deathmatch |
| `-fast`, `-respawn`, `-nomonsters` | правила |
| `-playdemo <lmp>`, `-timedemo <lmp>` | проигрывание демо |
| `-record <file.lmp>` | запись демо |
| `-loadgame <0..5>` | загрузка сохранения |
| `-nomouse`, `-nosound`, `-nosfx`, `-nomusic` | отключение подсистем |
| `-headless` | без окна (для тестов) |
| `-tics <n>` | ограничить число тиков в headless |
| `-shot <file.bmp>` | сохранить кадр в BMP |
| `-soundtest <file.wav>` | отрендерить звуки в WAV (нужен `-headless`) |
| `-sfx <NAME,...>` | какие звуки рендерить (`PISTOL,SHOTGN`); по умолчанию набор |
| `-fpslog <file.tsv>` | лог тайминга каждого кадра |
| `-net host\|join` | сетевая сессия |
| `-players <2..4>` | размер сессии (хост) |
| `-port <n>` | TCP-порт сессии |
| `-address <host:port>` | адрес хоста |
| `-name <nick>` | имя игрока |
| `-userdir <dir>` | каталог конфига и сохранений |
| `-botinput` | детерминированный ввод (автотесты сети) |
| `-version` | версия и выход |

## Файлы пользователя

По умолчанию рядом с EXE: `dptdoom.cfg`, `doomsavN.dsg`.
Если рядом с EXE лежит `portable.txt`, всё уходит в `user/`.
`-userdir` перекрывает оба варианта — так лаунчер держит отдельные профили.

## Тесты

```powershell
dotnet test DPTDOOMTest\DPTDOOMTest.csproj -c Release
```

- Тесты читают IWAD из каталога сборки тестов; `refs/iwads/*.wad` копируются
  туда автоматически при сборке.
- Без IWAD тесты совместимости помечаются пропущенными (`Inconclusive`), а не
  падают: репозиторий не содержит игровых данных.
- Геймплейные тесты сравнивают хеши состояния с эталонами Managed Doom.

Проверки вне MSTest:

```powershell
powershell -NoProfile -File tools\test-master.ps1     # протокол master
powershell -NoProfile -File tools\test-launcher.ps1   # мост HTML-лаунчера
powershell -NoProfile -File tools\check-standalone.ps1 # нет сторонних зависимостей
powershell -NoProfile -File tools\test-icon.ps1       # иконка совместима с XP
powershell -NoProfile -File tools\test-window.ps1     # заголовок окна (нужен IWAD)
```

## Иконка

`resources/dptdoom.ico` содержит только классические DIB-кадры (16, 24, 32, 48,
64, 128). PNG внутри `.ico` Windows XP не понимает, поэтому исходный файл был
пересобран:

```powershell
powershell -NoProfile -File tools\fix-icon.ps1
```

Скрипт декодирует каждый кадр и пишет `BITMAPINFOHEADER` + BGRA-строки снизу
вверх + AND-маску, как ждёт XP. Прозрачность берётся из альфа-канала.

Окну иконка назначается дважды: в `WNDCLASSEX` (`hIcon`, `hIconSm`) и через
`WM_SETICON` после создания — часть оболочек берёт иконку задачи из окна, а не
из класса.

Упаковка для тестировщиков:

```powershell
powershell -NoProfile -File tools\package-build.ps1 -Version 0.1.0-local
# -> artifacts/dptdoom-windows-x64-0.1.0-local.zip
```

Всё это же выполняется в CI: [CI.md](CI.md).

## Скриншоты

```powershell
powershell -NoProfile -File tools\capture-game.ps1     # кадры движка
powershell -NoProfile -File tools\capture-showcase.ps1 # лаунчер, обе темы
```

Оба скрипта пишут только содержимое окна или кадрового буфера: без рамок,
рабочего стола и личных путей.

## Проверка звука

Звук нельзя оценить по скриншоту, поэтому микшер можно отрендерить в файл:

```powershell
dptdoom.exe -headless -iwad doom2.wad -soundtest sfx.wav
```

Пишет два файла:

- `sfx.wav` — эффекты по очереди;
- `sfx-mixed.wav` — все одновременно (худший случай по перегрузу).

Рендер идёт через тот же `SoundMixer`, что и игра, поэтому слышно ровно то, что
услышит игрок. Конкретные звуки: `-sfx PISTOL,SHOTGN,CLAW`.

Отдельно проверяются юнит-тестами: `SoundMixerTest` (перегруз, ресемплинг,
громкость, детерминизм) и `SoundPanTest` (сторона источника, constant-power
панорама, затухание по расстоянию).

## Проверка плавности

Подёргивание картинки нельзя оценить на глаз, поэтому есть лог тайминга:

```powershell
dptdoom.exe -iwad doom2.wad -fpslog fps.tsv
```

Одна строка на кадр:

```
frame  nominal_ms  interval_ms  render_ms  present_ms  wait_ms  update  frac
```

- `interval_ms` — реальный интервал между кадрами; при 70 fps цель 14.286 мс.
- `render_ms` — софтверный рендер.
- `present_ms` — вывод кадра в окно через GDI.
- `wait_ms` — сколько цикл простоял, ожидая следующий кадр.
- `update` — обновлялся ли мир на этом кадре.
- `frac` — доля интерполяции, должна расти внутри тика.

Здоровый прогон: `interval` около цели, `frac` монотонно растёт (`0.500 → 1.000`)
и сбрасывается только на кадре с `update=1`. Если `present_ms` большой — узкое
место в выводе, а не в движке.

## Меню окна и трей

В оконном режиме есть обычное меню Win32 — как у программ той эпохи, под
которую сделан порт. Оно нужно потому, что не всё выражается клавишами:
громкость, размер окна, гамма и отладочные инструменты должны быть
обнаружимы, а не спрятаны в биндах.

```
Игра     Новая игра, Следующая карта, Сохранить (F2), Загрузить (F3),
         Пауза, Выход (F10)
Вид      Полный экран, Размер окна (320/640/960/1280), Гамма ярче/темнее,
         Сообщения (F8), Счётчик кадров
Звук     Эффекты/музыка громче-тише, Без эффектов, Без музыки
Отладка  Лог кадров в файл, Сохранить кадр в BMP, Сбросить состояние мира,
         Отрендерить звуки в WAV, Бог, Сквозь стены, Всё оружие и патроны
Справка  Управление (F1), О программе
```

Пункты с галочками отражают реальное состояние: полный экран, сообщения,
счётчик, лог, читаемость чит-флагов, выключенный звук.

**Трей.** Пункт «Свернуть в трей» прячет окно и оставляет иконку в области
уведомлений: двойной клик возвращает окно, правый клик открывает меню (показать,
свернуть, счётчик кадров, лог, кадр, выход). Это поведение фоновых программ
Windows 3.x, и оно удобно, когда игра ждёт напарника.

**Счётчик кадров.** Рисуется поверх кадра средствами GDI, а не в кадровом
буфере: отладочная информация не должна попадать в сохранённые кадры и в
сравнения хешей.

**Отладочные файлы** пишутся в `debug/` и `screenshots/` внутри каталога данных
(`user/` в portable-режиме), чтобы не смешиваться с бинарниками.

Меню и трей проверяются `tools/test-menu.ps1`: он шлёт те же `WM_COMMAND`, что
и клик, и проверяет, что состояние действительно изменилось (размер окна,
полный экран, скрытие в трей), а не только что пункт существует.

## Ограничения

- Проверено на Windows 10/11 с .NET Framework 4.8 и на .NET 9 SDK.
  Реальный запуск на Windows XP **ещё не проверялся** — код и API подобраны под
  XP (user32/gdi32/winmm, без LINQ-новинок, без `MathF`, без `Span`), но это
  требует отдельной машины или виртуальной среды.
- Linux/macOS сборка сейчас не поддерживается: Win32-бэкенд основной.
  Переносимые точки — `IVideo`, `ISound`, `IMusic`, `IUserInput`; headless уже
  платформенно-нейтрален.
- Звук проверен на реальном железе через `waveOut`; MIDI зависит от системного
  синтезатора (на XP это Microsoft GS Wavetable SW Synth).
