### [> Русский <](./README.md) | [English](./README_EN.md) | [简体中文](./README_ZH.md)
---

# FusionSky

Динамическое небо с улучшенным освещением и гибкой настройкой для MadOut2 PC 9.4 / BigCityLegacy.

Для открытия настроек используйте сочетание **Alt + G** во время геймплея.

## Возможности:
- Динамический таймцикл с плавной сменой дня и ночи
- Динамические облака с гибкой настройкой и адаптацией под окружение
- Небесные тела: солнце и луна с реалистичной осью вращения
- Реалистичный эффект солнечного света и лучей
- Гибкая настройка освещения и цветовой гаммы
- Раздельные настройки дня/ночи/заката и рассвета
- Настройка уличных фонарей и фар авто
- Настройка тумана
- Настройка теней
- Ряд предустановленных пресетов и возможность сохранять собственные

## Установка

Для работы необходим BigCityLegacy 1.4.2+. Поддерживаются Windows и Linux версии игры. 

Скачайте архив с плагином из Releases и распакуйте в корневую папку с игрой. 

После распаковки файл `FusionSky.dll` должен появиться по пути `BepInEx\plugins\FusionSky`.

## Пресеты

Пользовательские пресеты хранятся в `BigCityLegacy\sky_presets` в папке игры в виде файлов с расширением `.cfg`.

Пресеты сохраняют и применяют только параметры окружения. Время суток, пауза и длительность суток независимы от пресетов.

## Сборка

Требования:
- MadOut2 PC 9.4 с BepInEx 5 и BigCityLegacy 1.4.2+.
- .NET SDK

Для сборки под Windows x64 и Wine/Proton используйте конфигурацию `Release`:

```powershell
dotnet build ./FusionSky.csproj -c Release '-p:GameDir=D:\path\to\MadOut2' -p:CopyToPlugins=true
```

Для сборки под нативный Linux UnityPlayer используйте конфигурацию `Linux`:

```bash
dotnet build ./FusionSky.csproj -c Linux -p:GameDir=/path/to/MadOut2
```

### Пересборка AssetBundle

При изменении шейдеров для сборки нового AssetBundle используйте PowerShell-скрипт `Build-AssetBundle.ps1` из папки `scripts`.
Нужен Unity **2019.1.8f1** с модулем Standalone Build Support для нужной платформы. Путь к Unity можно задать аргументом `-UnityPath` или переменной `DYNAMIC_SKY_UNITY`.

```powershell
./scripts/Build-AssetBundle.ps1 -Target Windows -UnityPath 'C:/Program Files/Unity/Editor/Unity.exe'
./scripts/Build-AssetBundle.ps1 -Target Linux -UnityPath 'C:/Program Files/Unity/Editor/Unity.exe'
```

Скрипт помещает проверенный пакет в `Resources/dynamic-sky.bundle` или `Resources/dynamic-sky-linux.bundle`. Журнал сохраняется в `Logs/assetbundle-windows.log` или `Logs/assetbundle-linux.log`. DLL небоходимо пересобирать отдельно. Изменение только `.cfg` требует пересборки DLL и не требует запуска Unity.

Для проверки в редакторе соберите DLL с `-c Debug`, задайте `DYNAMIC_SKY_GAME_DIR` и запустите `SkyRuntimeProbe.Run`.
