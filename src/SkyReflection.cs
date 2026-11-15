using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicSky
{
    internal sealed class SkyReflection
    {
        internal const int Size=32;
        private const int FacePixels=Size*Size;
        internal readonly Cubemap Texture;
        private readonly Color[] pixels=new Color[FacePixels];
        private readonly Vector3[] directions=new Vector3[6*FacePixels];
        private const int CloudSamplesPerFrame=64;
        private const float CloudInterval=.25f, ColorInterval=1f/30, LocalCaptureInterval=1;
        private readonly List<int> cloudSamples=new List<int>();
        private float[] cloudOpacity=new float[6*FacePixels],pendingOpacity=new float[6*FacePixels];
        private SkyState sampledClouds,pendingClouds;
        private int cloudCursor=-1;
        private float nextColorUpload;
        private SkyState pendingColors;
        private int colorFace=-1;
        private SkyState previous;
        private float nextCloudSample;
        private bool initialized;
        private readonly Dictionary<ReflectionProbe,ProbeState> managed=new Dictionary<ReflectionProbe,ProbeState>();
        private ReflectionProbe localProbe;
        private int localRender=-1;
        private float nextLocalCapture;
        private ReflectionProbeTimeSlicingMode originalTimeSlicing;
        private bool localRefreshRequested;
        private SkyState localState;

        private struct ProbeState
        {
            internal ReflectionProbeMode Mode;
            internal Texture CustomTexture;
            internal float Intensity;
        }

        internal SkyReflection(SkyState initial)
        {
            Texture=new Cubemap(Size,TextureFormat.RGBAHalf,true) { name="Dynamic Sky Reflection",hideFlags=HideFlags.DontUnloadUnusedAsset };
            for(int face=0;face<6;face++)
                for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
                {
                    int index=face*FacePixels+y*Size+x;
                    directions[index]=Direction(face,(x+.5f)*2/Size-1,(y+.5f)*2/Size-1).normalized;
                    if(directions[index].y>.01f)cloudSamples.Add(index);
                }
            Update(initial,true);
        }

        internal void Update(SkyState state,bool immediate=false)
        {
            float now=Time.realtimeSinceStartup;
            bool cloudsReady;
            using(FrameDiagnostics.Measure(FrameDiagnostics.Stage.CloudMask))
                cloudsReady=UpdateClouds(state,now);
            if(immediate || cloudsReady || colorFace<0)
            {
                if(initialized && !cloudsReady && colorFace<0 && SameColors(previous,state))return;
                if(initialized && !immediate && !cloudsReady && now<nextColorUpload)return;
                nextColorUpload=now+ColorInterval;
                pendingColors=state;
                colorFace=0;
            }

            bool linear=QualitySettings.activeColorSpace==ColorSpace.Linear;
            int lastFace=immediate?6:colorFace+1;
            using(FrameDiagnostics.Measure(FrameDiagnostics.Stage.CubemapColors))
            {
                while(colorFace<lastFace)
                {
                    for(int index=0;index<FacePixels;index++)
                    {
                        int sample=colorFace*FacePixels+index;
                        var color=SkyModel.ReflectionColor(directions[sample],pendingColors,cloudOpacity[sample]);
                        pixels[index]=linear?color.linear:color;
                    }
                    Texture.SetPixels(pixels,(CubemapFace)colorFace++);
                }
            }

            if(colorFace<6)return;
            using(FrameDiagnostics.Measure(FrameDiagnostics.Stage.CubemapUpload))
                Texture.Apply(true,false);
            colorFace=-1;
            initialized=true;
            previous=pendingColors;
        }

        private bool UpdateClouds(SkyState state,float now)
        {
            if(cloudCursor>=0 && !SameCloudSettings(pendingClouds,state)) cloudCursor=-1;
            if(cloudCursor<0)
            {
                if(initialized && SameCloudSettings(sampledClouds,state) &&
                    (now<nextCloudSample || state.Phase==sampledClouds.Phase))return false;
                pendingClouds=state;
                cloudCursor=0;
                nextCloudSample=now+CloudInterval;
            }

            if(state.Coverage<=.0001f || state.Density<=0)
            {
                System.Array.Clear(pendingOpacity,0,pendingOpacity.Length);
                cloudCursor=cloudSamples.Count;
            }
            int end=initialized?Mathf.Min(cloudSamples.Count,cloudCursor+CloudSamplesPerFrame):cloudSamples.Count;
            while(cloudCursor<end)
            {
                int index=cloudSamples[cloudCursor++];
                pendingOpacity[index]=SkyModel.CloudOpacity(directions[index],pendingClouds);
            }
            if(cloudCursor<cloudSamples.Count)return false;

            var oldOpacity=cloudOpacity;
            cloudOpacity=pendingOpacity;
            pendingOpacity=oldOpacity;
            sampledClouds=pendingClouds;
            cloudCursor=-1;
            return true;
        }

        private static bool SameCloudSettings(SkyState a,SkyState b) =>
            a.Coverage==b.Coverage && a.Density==b.Density && a.CloudScale==b.CloudScale && a.CloudDirection==b.CloudDirection;

        private static bool SameColors(SkyState a,SkyState b) =>
            a.Zenith==b.Zenith && a.Horizon==b.Horizon && a.Clouds==b.Clouds && a.Ground==b.Ground &&
            a.Warm==b.Warm && a.SunDirection==b.SunDirection && a.Exposure==b.Exposure && a.CloudShade==b.CloudShade &&
            a.CloudLighting==b.CloudLighting && a.SkyDepth==b.SkyDepth && a.MoonDirection==b.MoonDirection && a.Daylight==b.Daylight;

        internal void ApplyToProbes(float intensity)
        {
            foreach(var probe in Probe.instances)
            {
                if(!probe || !probe.isGlobal)continue;
                var component=probe.GetComponent<ReflectionProbe>();
                if(!component || component.cullingMask!=0)continue;
                if(!managed.ContainsKey(component))
                {
                    managed.Add(component,new ProbeState { Mode=component.mode, CustomTexture=component.customBakedTexture, Intensity=component.intensity });
                    component.mode=ReflectionProbeMode.Custom;
                    component.customBakedTexture=Texture;
                }
                component.intensity=intensity;
            }
        }

        internal void RequestLocalRefresh() => localRefreshRequested=true;

        internal void UpdateLocalProbe(Camera camera,SkyState state,bool immediate)
        {
            if(immediate)RequestLocalRefresh();
            float now=Time.realtimeSinceStartup;
            if(!camera || now<nextLocalCapture)return;
            if(localProbe && localRender>=0 && !localProbe.IsFinishedRendering(localRender))return;
            nextLocalCapture=now+LocalCaptureInterval;

            ReflectionProbe closest=null;
            foreach(var probe in Probe.instances)
            {
                if(!probe || probe.isGlobal)continue;
                var component=probe.GetComponent<ReflectionProbe>();
                if(!component || component.mode!=ReflectionProbeMode.Realtime ||
                    !component.isActiveAndEnabled || !component.bounds.Contains(camera.transform.position))continue;
                if(!closest || component.importance>closest.importance)closest=component;
            }

            if(closest!=localProbe)
            {
                RestoreLocalProbe();
                localProbe=closest;
                if(localProbe)
                {
                    originalTimeSlicing=localProbe.timeSlicingMode;
                    localProbe.timeSlicingMode=ReflectionProbeTimeSlicingMode.IndividualFaces;
                    RequestLocalRefresh();
                }
            }
            if(!localProbe || (!localRefreshRequested && !LocalLightingChanged(localState,state)))return;

            using(FrameDiagnostics.Measure(FrameDiagnostics.Stage.LocalProbeRequest))
                localRender=localProbe.RenderProbe();
            localState=state;
            localRefreshRequested=false;
        }

        private static bool LocalLightingChanged(SkyState a,SkyState b) =>
            Mathf.Abs(a.SunIntensity-b.SunIntensity)>.01f ||
            Mathf.Abs(a.Exposure-b.Exposure)>.01f ||
            Mathf.Abs(a.Reflection-b.Reflection)>.01f ||
            (a.LightDirection-b.LightDirection).sqrMagnitude>.0003f ||
            ((Vector4)(a.Horizon-b.Horizon)).sqrMagnitude>.0001f;

        private void RestoreLocalProbe()
        {
            if(localProbe)localProbe.timeSlicingMode=originalTimeSlicing;
            localProbe=null;
            localRender=-1;
        }

        internal void RestoreProbes()
        {
            foreach(var pair in managed)
            {
                if(!pair.Key)continue;
                pair.Key.customBakedTexture=pair.Value.CustomTexture;
                pair.Key.intensity=pair.Value.Intensity;
                pair.Key.mode=pair.Value.Mode;
            }
            managed.Clear();
            RestoreLocalProbe();
            localRefreshRequested=false;
            nextLocalCapture=0;
        }

        private static Vector3 Direction(int face,float u,float v)
        {
            switch(face)
            {
                case 0:return new Vector3(1,-v,-u);
                case 1:return new Vector3(-1,-v,u);
                case 2:return new Vector3(u,1,v);
                case 3:return new Vector3(u,-1,-v);
                case 4:return new Vector3(u,-v,1);
                default:return new Vector3(-u,-v,-1);
            }
        }

        internal void Destroy()
        {
            RestoreProbes();
            SkyRuntime.DestroyOwned(Texture);
        }
    }
}

