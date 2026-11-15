using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace DynamicSky
{
    internal static class ImposterRefresh
    {
        private sealed class Stamp { internal int Epoch=-1,HardEpoch; }
        private static readonly ConditionalWeakTable<ImposterJob,Stamp> stamps=new ConditionalWeakTable<ImposterJob,Stamp>();
        private static int epoch;
        private static int hardEpoch;
        internal static void Invalidate(bool discard=false)
        {
            if(discard)Interlocked.Increment(ref hardEpoch);
            Interlocked.Increment(ref epoch);
        }
        internal static void Refresh(ImposterJob job)
        {
            int current=Volatile.Read(ref epoch);if(current==0)return;
            var stamp=stamps.GetValue(job,_=>new Stamp());if(stamp.Epoch==current)return;
            stamp.Epoch=current;job.lastDistToCam=0;
            int hard=Volatile.Read(ref hardEpoch);
            if(stamp.HardEpoch!=hard) { stamp.HardEpoch=hard;job.NowImpostered(false); }
        }
    }
    [HarmonyPatch(typeof(Weather),"OnEnable")]
    internal static class EnablePatch
    {
        private static bool Prefix(Weather __instance)
        {
            var runtime=Plugin.Runtime;if(runtime==null || !runtime.IsOurs(__instance))return true;
            __instance.skybox_game=runtime.Sky;return false;
        }
    }
    [HarmonyPatch(typeof(Weather),"OnDisable")]
    internal static class DisablePatch
    {
        private static bool Prefix(Weather __instance)
        {
            var runtime=Plugin.Runtime;if(runtime==null || !runtime.IsOurs(__instance))return true;
            runtime.Deactivate();return false;
        }
    }
    [HarmonyPatch(typeof(Weather),"MakeCurrent")]
    internal static class AppliedPatch { private static void Postfix(Weather __instance) { Plugin.Runtime?.Applied(__instance); } }
    [HarmonyPatch(typeof(WeatherManager),"GetNames")]
    internal static class NamesPatch { private static void Prefix() { Plugin.Runtime?.EnsureRegistered(); } }
    [HarmonyPatch(typeof(WeatherManager.It),"Loaded")]
    internal static class LoadedPatch { private static void Postfix() { Plugin.Runtime?.EnsureRegistered(); } }
    [HarmonyPatch(typeof(WeatherManager),"Awake")]
    internal static class ManagerReadyPatch { private static void Postfix() { Plugin.Runtime?.EnsureRegistered(); } }
    [HarmonyPatch(typeof(Probe),"SetupCommandBuffer")]
    internal static class AtlasLightingPatch
    {
        private static bool Prefix(UnityEngine.Rendering.CommandBuffer __0)
        {
            var runtime=Plugin.Runtime;
            if(runtime==null || !runtime.IsActive)return true;
            SkyLighting.Setup(__0,runtime.State,runtime.Weather.customReflection);
            return false;
        }
    }
    [HarmonyPatch(typeof(Probe),"UpReflectionProbs")]
    internal static class ReflectionRefreshPatch
    {
        private static bool Prefix()
        {
            var runtime=Plugin.Runtime;
            if(runtime==null || !runtime.IsActive || !Application.isPlaying)return true;
            runtime.RequestLocalProbeRefresh();
            return false;
        }
    }
    [HarmonyPatch(typeof(MaterialsQuality),"SetValue")]
    internal static class QualityPatch
    {
        private static void Prefix(string __0,out bool __state) { __state=MaterialsQuality.GetCurType().ToString()!=__0; }
        private static void Postfix(bool __state) { if(__state)Plugin.Runtime?.QualityChanged(); }
    }
    [HarmonyPatch(typeof(WeatherManager),"UpdateSwitchByTime")]
    internal static class AutoSwitchPatch { private static bool Prefix() { return Plugin.Runtime==null || !Plugin.Runtime.IsActive; } }
    [HarmonyPatch(typeof(WeatherManager),"RandomIfNeed")]
    internal static class RandomPatch { private static bool Prefix() { return Plugin.Runtime==null || !Plugin.Runtime.IsActive; } }
    [HarmonyPatch(typeof(ImposterJob),"UpdateImposter")]
    internal static class ImposterPatch { private static void Prefix(ImposterJob __instance) { ImposterRefresh.Refresh(__instance); } }
    [HarmonyPatch(typeof(DistanceViewLight),"mUpdate")]
    internal static class StreetLightPatch
    {
        private static void Prefix(DistanceViewLight __instance,out Light __state)
        {
            __state=null;
            var runtime=Plugin.Runtime;
            if(runtime==null || !runtime.IsActive || !__instance.useLight)return;
            var dayNight=__instance.GetComponent<DayNight>();
            if(!dayNight || dayNight.nightLight!=__instance)return;
            __state=__instance.useLight;
            runtime.Lights.BeforeStreetUpdate(__state);
        }
        private static void Postfix(Light __state)
        {
            var runtime=Plugin.Runtime;
            if(__state && runtime!=null && runtime.IsActive)runtime.Lights.AfterStreetUpdate(__state,runtime.Options);
        }
    }
    [HarmonyPatch]
    internal static class WeatherHotkeyPatch
    {
        internal static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("LegacyHotkeys"),"Update");

        private static bool Prepare() => TargetMethod()!=null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original=AccessTools.Method(typeof(Input),nameof(Input.GetKeyDown),new[]{typeof(KeyCode)});
            var filtered=AccessTools.Method(typeof(WeatherHotkeyPatch),nameof(WeatherKeyDown));
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(original))instruction.operand=filtered;
                yield return instruction;
            }
        }

        private static bool WeatherKeyDown(KeyCode key) =>
            (key!=KeyCode.F5 || !Plugin.AltHeld) && Input.GetKeyDown(key);
    }

    [HarmonyPatch(typeof(CarLight),"FixedUpdate")]
    internal static class HeadlightFadePatch
    {
        private static void Postfix(CarLight __instance)
        {
            var runtime=Plugin.Runtime;
            if(runtime!=null && runtime.IsActive)runtime.Lights.EnsureHeadlight(__instance,runtime.Options);
        }
    }

    [HarmonyPatch(typeof(CarLight),"Awake")]
    internal static class HeadlightPatch
    {
        private static void Postfix(CarLight __instance)
        {
            var runtime=Plugin.Runtime;
            if(runtime!=null && runtime.IsActive)runtime.Lights.TrackHeadlight(__instance,runtime.Options);
        }
    }
    [HarmonyPatch(typeof(PostProcessingBehaviour),"OnRenderImage")]
    internal static class BloomPatch
    {
        private struct RenderState
        {
            internal BloomModel Model;
            internal BloomModel.Settings Original;
            internal EyeAdaptationModel Eye;
            internal EyeAdaptationModel.Settings OriginalEye;
        }

        private static void Prefix(PostProcessingBehaviour __instance,out RenderState __state)
        {
            __state=default;
            var runtime=Plugin.Runtime;
            if(runtime==null || !runtime.IsActive ||
                !Cam.me || !Cam.me.unityCam || __instance.gameObject!=Cam.me.unityCam.gameObject ||
                !__instance.profile) return;

            var model=__instance.profile.bloom;
            if(model!=null && model.enabled && runtime.State.Bloom!=1)
            {
                var settings=model.settings;
                __state.Model=model;
                __state.Original=settings;
                settings.bloom.intensity*=runtime.State.Bloom;
                model.settings=settings;
            }

            var eye=__instance.profile.eyeAdaptation;
            float night=1-runtime.State.Daylight;
            if(eye==null || !eye.enabled || night<=0)return;
            var exposure=eye.settings;
            __state.Eye=eye;
            __state.OriginalEye=exposure;
            exposure.minLuminance=Mathf.Lerp(exposure.minLuminance,.7f,night);
            exposure.maxLuminance=Mathf.Max(exposure.minLuminance,Mathf.Lerp(exposure.maxLuminance,1.3f,night));
            exposure.lowPercent=Mathf.Lerp(exposure.lowPercent,71.28f,night);
            exposure.highPercent=Mathf.Lerp(exposure.highPercent,99,night);
            exposure.keyValue=Mathf.Lerp(exposure.keyValue,.25f,night);
            if(night>.98f)exposure.adaptationType=EyeAdaptationModel.EyeAdaptationType.Fixed;
            eye.settings=exposure;
        }

        private static Exception Finalizer(RenderState __state,Exception __exception)
        {
            if(__state.Model!=null) __state.Model.settings=__state.Original;
            if(__state.Eye!=null) __state.Eye.settings=__state.OriginalEye;
            return __exception;
        }
    }
}
