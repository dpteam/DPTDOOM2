# CI

Workflow: `.github/workflows/ci.yml`. Триггеры — push, pull request,
`workflow_dispatch`.

## Задачи

### `tests` — Windows и Linux

`dotnet test DPTDOOMTest` на `windows-latest` и `ubuntu-latest`.

Почему Linux тоже: у игрового проекта нет WinForms, только P/Invoke в
`src/Win32`, поэтому цель `net9.0` собирается и тесты идут везде. Это ловит
случайную зависимость от Windows-специфичных API в общем коде.

Тесты совместимости требуют IWAD. В CI игровых данных нет, поэтому
`WadPath.Require` помечает их `Inconclusive` — прогон остаётся зелёным, но и не
притворяется, что совместимость проверена. Юнит-тесты (Fixed, Angle, Trig, WAD,
карты, окно) выполняются по-настоящему.

### `build` — Windows

1. Сборка всего решения в `Release` (включая `net40`).
2. `tools/check-standalone.ps1` — обещание «без сторонних библиотек».
3. `tools/test-window.ps1` — заголовок окна (пропускается без IWAD).
4. `tools/test-master.ps1` — полный цикл directory-протокола против живого
   сервера.
5. `tools/test-launcher.ps1` — мост HTML-лаунчера.
6. `tools/package-build.ps1` — ZIP с сборкой, лицензией и исходниками.
7. Артефакт `dptdoom-windows-<sha>` на 30 дней.

## Что именно проверяет `check-standalone.ps1`

Три независимые вещи, потому что каждая может сломаться по отдельности:

| Проверка | Что ловит |
|---|---|
| Нет `PackageReference` кроме reference assemblies | кто-то добавил NuGet-пакет в поставляемый проект |
| В `bin/Release/net40` нет лишних DLL | пакет просочился в вывод (игра должна быть одним exe) |
| `TargetFrameworks` содержит `net40` и `.exe.config` объявляет `.NETFramework,Version=v4.0` | цель случайно сменили на net6/net8 |

`tools/test-icon.ps1` проверяет иконку отдельно, потому что там два независимых
способа всё сломать: картинки в `.ico` должны быть классическими DIB (Windows XP
не понимает PNG внутри `.ico`, а System.Drawing читает их как мусор), и окно
должно действительно получить иконку, а не только EXE-ресурс.

Скан «запрещённых API» регулярками сознательно **не** делается: компилятор под
net40 сам является авторитетом (`Span<T>` или `Math.Clamp` просто не
скомпилируются), а регулярки давали ложные срабатывания на собственных шимах
(`MathF`, `TryAdd`) и на комментариях со словом «record».

Проверки проверены негативными тестами: подложенный DLL и добавленный
`Newtonsoft.Json` действительно валят скрипт.

## Почему оконный тест не требует IWAD

`DPTDOOMTest/src/UnitTests/Win32WindowTest.cs` создаёт `Win32Window` напрямую и
читает заголовок через `GetWindowTextW`. Игра для этого не нужна — а баг,
который он закрывает, был именно в объявлении `DefWindowProc`: без `CharSet`
P/Invoke брал ANSI-вариант, и заголовок приходил как `D`.

Проверено: при `DefWindowProcA` оба теста падают, при `DefWindowProcW` проходят.

`tools/test-window.ps1` остаётся как end-to-end проверка (реальный процесс игры)
и аккуратно пропускается, когда IWAD нет.

## Артефакты

`dptdoom-windows-<sha>.zip` содержит:

```
dptdoom.exe                 игра
dptdoom-master.exe          сервер списка
dptdoom-launcher.exe        лаунчер
DPTDOOM.Shared.dll          общий код (нужен master и launcher)
READ-ME-FIRST.txt           как запускать, что нужно
README.md
licenses/
source.zip                  исходники этого коммита (GPL)
```

IWAD, конфиги, сохранения и логи в пакет не попадают — это проверяется глазами
при изменении `package-build.ps1` и косвенно тем, что сборка идёт с чистого
дерева.

## Локально

```powershell
dotnet build DPTDOOM.slnx -c Release
dotnet test DPTDOOMTest\DPTDOOMTest.csproj -c Release

powershell -NoProfile -File tools\check-standalone.ps1
powershell -NoProfile -File tools\test-window.ps1
powershell -NoProfile -File tools\test-launcher.ps1
powershell -NoProfile -File tools\test-master.ps1 -Port 25145   # нужен запущенный master
powershell -NoProfile -File tools\package-build.ps1 -Version 0.1.0-local
```

`test-launcher.ps1` запускать через Windows PowerShell 5.1, не через PowerShell
7: скрипт загружает сборку net40 с WinForms, а PowerShell 7 работает на .NET и
такую сборку не хостит.

## Ограничения

- **Remote run ещё не выполнялся.** Все шаги прогнаны локально на Windows и
  проходят, но фактический прогон на раннере GitHub не подтверждён — пушит
  владелец. Особенно это касается задачи `tests` на `ubuntu-latest`: она
  добавлена потому, что общий код не зависит от WinForms, но реального запуска
  на Linux пока не было.
- Windows XP в CI не проверяется: на GitHub-hosted раннерах его нет. Проверка
  XP — ручная, на отдельной машине или в ВМ.
- Сетевой soak-тест (`-botinput`) в CI не включён: он требует двух процессов и
  стабильного тайминга. Запускается вручную, см. `docs/NETWORK.md`.
- Артефакт собирается из рабочего дерева, а не из чистого клона, поэтому
  `source.zip` берётся через `git archive HEAD`: если дерево грязное, исходники
  в пакете не совпадут с бинарниками. В CI дерево чистое по определению.
