### [Русский](./README.md) | [English](./README_EN.md) | [> 简体中文 <](./README_ZH.md)
---

# FusionSky

适用于 MadOut2 PC 9.4 / [BigCityLegacy](https://github.com/LxgacyTeam/BigCityLegacy) 的动态天空插件，提供增强的光照效果和灵活的自定义设置。

在游戏过程中按 **Alt + G** 打开设置界面。

## 功能特性
- 动态昼夜循环，白天与夜晚平滑过渡
- 可灵活调整并根据环境自适应的动态云层
- 天体：太阳和月亮，具有逼真的旋转轴
- 逼真的阳光与光束效果
- 灵活调整光照和色彩
- 为白天、夜晚、日落和日出分别提供独立设置
- 可调整路灯和车辆前照灯
- 可调整雾效
- 可调整阴影
- 提供多种内置预设，并支持保存自定义预设

## 安装

运行本插件需要 BigCityLegacy 1.4.2 或更高版本。支持 Windows 和 Linux 版本的游戏。

从 [Releases](https://github.com/LxgacyTeam/FusionSky/releases) 页面下载插件压缩包，并将其解压到游戏根目录。

解压后，`FusionSky.dll` 文件应位于 `BepInEx\plugins\FusionSky` 路径下。

## 预设

用户自定义预设以 `.cfg` 文件的形式保存在游戏目录下的 `BigCityLegacy\sky_presets` 中。

预设仅保存和应用环境参数。当前时间、暂停状态和昼夜循环时长不受预设影响。

## 构建

要求：
- MadOut2 PC 9.4，已安装 BepInEx 5 和 BigCityLegacy 1.4.2 或更高版本。
- .NET SDK

为 Windows x64 和 Wine/Proton 构建时，请使用 `Release` 配置：

```powershell
dotnet build ./FusionSky.csproj -c Release '-p:GameDir=D:\path\to\MadOut2' -p:CopyToPlugins=true
```

为原生 Linux UnityPlayer 构建时，请使用 `Linux` 配置：

```bash
dotnet build ./FusionSky.csproj -c Linux -p:GameDir=/path/to/MadOut2
```

### 重新构建 AssetBundle

修改着色器后，如需构建新的 AssetBundle，请使用 `scripts` 文件夹中的 PowerShell 脚本 `Build-AssetBundle.ps1`。
需要安装 Unity **2019.1.8f1**，并包含目标平台对应的 Standalone Build Support 模块。可以通过 `-UnityPath` 参数或 `DYNAMIC_SKY_UNITY` 环境变量指定 Unity 路径。

```powershell
./scripts/Build-AssetBundle.ps1 -Target Windows -UnityPath 'C:/Program Files/Unity/Editor/Unity.exe'
./scripts/Build-AssetBundle.ps1 -Target Linux -UnityPath 'C:/Program Files/Unity/Editor/Unity.exe'
```

脚本会将验证通过的资源包放置在 `Resources/dynamic-sky.bundle` 或 `Resources/dynamic-sky-linux.bundle`。日志保存在 `Logs/assetbundle-windows.log` 或 `Logs/assetbundle-linux.log`。DLL 需要单独重新构建。如果只修改 `.cfg` 文件，则需要重新构建 DLL，但无需启动 Unity。

如需在编辑器中进行检查，请使用 `-c Debug` 构建 DLL，设置 `DYNAMIC_SKY_GAME_DIR`，然后运行 `SkyRuntimeProbe.Run`。
