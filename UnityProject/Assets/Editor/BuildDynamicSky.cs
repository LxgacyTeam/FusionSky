using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildDynamicSky
{
    public static void Build()
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Sky/DynamicSky.shader");
        if(!shader || ShaderUtil.ShaderHasError(shader))throw new InvalidDataException("Dynamic sky shader compilation failed");
        var shafts=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Sky/SunShafts.shader");
        if(!shafts || ShaderUtil.ShaderHasError(shafts))throw new InvalidDataException("Sun shafts shader compilation failed");
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Sky/DynamicSky.mat");
        if(!material) { material=new Material(shader);AssetDatabase.CreateAsset(material,"Assets/Sky/DynamicSky.mat"); }
        material.shader=shader;EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
        var output=Environment.GetEnvironmentVariable("DYNAMIC_SKY_BUNDLE_STAGE") ??
            Path.GetFullPath(Path.Combine(Application.dataPath,"../../Resources"));
        bool linux=Environment.GetEnvironmentVariable("DYNAMIC_SKY_BUNDLE_TARGET")=="Linux";
        var target=linux?BuildTarget.StandaloneLinux64:BuildTarget.StandaloneWindows64;
        var bundleName=linux?"dynamic-sky-linux.bundle":"dynamic-sky.bundle";
        if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,target))
            throw new InvalidOperationException("Install Unity build support for "+target);
        Directory.CreateDirectory(output);
        var result=BuildPipeline.BuildAssetBundles(output,new[] {
            new AssetBundleBuild {
                assetBundleName=bundleName,
                assetNames=new[]{"Assets/Sky/DynamicSky.mat","Assets/Sky/SunShafts.shader"},
                addressableNames=new[]{"sky/dynamic","sky/sunshafts"}
            }
        },BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.ForceRebuildAssetBundle,target);
        if(!result)throw new Exception("Dynamic sky bundle build failed");
        var bundle=AssetBundle.LoadFromFile(Path.Combine(output,bundleName));
        if(!bundle)throw new Exception("Cannot reload dynamic sky bundle");
        try
        {
            if(!bundle.Contains("sky/dynamic") || !bundle.Contains("sky/sunshafts"))
                throw new InvalidDataException("Asset bundle is missing its material or effects shader.");

            // A cross-platform editor can read the archive, but cannot instantiate
            // materials compiled exclusively for the other platform's graphics APIs.
            bool samePlatform=linux ? Application.platform==RuntimePlatform.LinuxEditor :
                Application.platform==RuntimePlatform.WindowsEditor;
            var loaded=samePlatform?bundle.LoadAsset<Material>("sky/dynamic"):material;
            foreach(var name in new[]{"_CloudCoverage","_CloudDensity","_CloudPhase","_SunDirection","_SunRadius","_SunSoftness","_SunDiscColor","_SunYellow","_CloudShade","_CloudLighting","_SkyDepth","_MoonRadius","_MoonSoftness","_MoonBrightness","_MoonColor","_MoonDirection","_MoonVisibility","_Stars","_Horizon"})
                if(!loaded || !loaded.HasProperty(name))throw new InvalidDataException("Missing sky property "+name);
            if(samePlatform && !bundle.LoadAsset<Shader>("sky/sunshafts"))
                throw new InvalidDataException("Missing sun shafts shader");
        }
        finally { bundle.Unload(true); }
        Debug.Log("DYNAMIC_SKY_BUILD_OK");
    }
}
