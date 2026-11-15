### [Русский](./README.md) | [> English <](./README_EN.md) | [简体中文](./README_ZH.md)
---

# FusionSky

Dynamic sky with enhanced lighting and extensive customization options for MadOut2 PC 9.4 / [BigCityLegacy](https://github.com/LxgacyTeam/BigCityLegacy).

To open the settings, press **Alt + G** during gameplay.

## Features:
- Dynamic day-night cycle with smooth transitions
- Dynamic clouds with extensive customization and environment adaptation
- Celestial bodies: the sun and moon with realistic rotation axes
- Realistic sunlight and sun rays
- Flexible lighting and color adjustments
- Separate settings for daytime, nighttime, sunset, and sunrise
- Streetlight and vehicle headlight adjustments
- Fog settings
- Shadow settings
- Several built-in presets and the ability to save your own

## Installation

Requires BigCityLegacy 1.4.2 or later. Both Windows and Linux versions of the game are supported.

Download the plugin archive from [Releases](https://github.com/LxgacyTeam/FusionSky/releases) and extract it to the game's root directory.

After extraction, `FusionSky.dll` should be located in `BepInEx\plugins\FusionSky`.

## Presets

Custom presets are stored as `.cfg` files in the `BigCityLegacy\sky_presets` directory inside the game folder.

Presets save and apply environment settings only. Time of day, pause state, and day length are independent of presets.

## Building

Requirements:
- MadOut2 PC 9.4 with BepInEx 5 and BigCityLegacy 1.4.2 or later.
- .NET SDK

To build for Windows x64 and Wine/Proton, use the `Release` configuration:

```powershell
dotnet build ./FusionSky.csproj -c Release '-p:GameDir=D:\path\to\MadOut2' -p:CopyToPlugins=true
```

To build for the native Linux UnityPlayer, use the `Linux` configuration:

```bash
dotnet build ./FusionSky.csproj -c Linux -p:GameDir=/path/to/MadOut2
```

### Rebuilding the AssetBundle

If you modify the shaders, use the `Build-AssetBundle.ps1` PowerShell script in the `scripts` directory to build a new AssetBundle.
Unity **2019.1.8f1** with the Standalone Build Support module for the target platform is required. You can specify the Unity path using the `-UnityPath` argument or the `DYNAMIC_SKY_UNITY` environment variable.

```powershell
./scripts/Build-AssetBundle.ps1 -Target Windows -UnityPath 'C:/Program Files/Unity/Editor/Unity.exe'
./scripts/Build-AssetBundle.ps1 -Target Linux -UnityPath 'C:/Program Files/Unity/Editor/Unity.exe'
```

The script places the validated bundle in `Resources/dynamic-sky.bundle` or `Resources/dynamic-sky-linux.bundle`. Logs are saved to `Logs/assetbundle-windows.log` or `Logs/assetbundle-linux.log`. The DLL must be rebuilt separately. Changes to `.cfg` files alone require rebuilding the DLL but do not require running Unity.

To test in the editor, build the DLL with `-c Debug`, set `DYNAMIC_SKY_GAME_DIR`, and run `SkyRuntimeProbe.Run`.
