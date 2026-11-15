using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace DynamicSky
{
    internal sealed class SkyRuntime
    {
        internal const string Name="Dynamic Sky";
        internal readonly Options Options;
        internal readonly CycleClock Clock;
        internal Weather Weather;
        internal Material Sky;
        internal Light Sun;
        internal SkyState State;
        private AssetBundle bundle;
        private Shader shaftsShader;
        private SunShaftsEffect shafts;
        private SunShaftsEffect moonShafts;
        private float originalShadowDistance,appliedShadowDistance;
        private bool ownsShadowDistance;
        private Camera effectsCamera;
        private WeatherManager manager;
        private WeatherManager.It entry,previous;
        private SkyReflection reflection;
        private bool autoDone,active,failed;
        private float nextRegister,nextRefresh,phase,lastRefreshHour=-100;
        private bool night;
        private bool capturedLighting;
        private Color originalTreeEmission;
        private const float NightTreeEmission=0;
        private SphericalHarmonicsL2 originalAmbientProbe;
        internal readonly SceneLights Lights=new SceneLights();
        internal bool IsActive => active && Weather && manager && manager.cur==entry && Weather.gameObject.activeInHierarchy;
        internal bool IsOurs(Weather weather) => weather && weather==Weather;
        internal bool Registered => Weather && manager && entry!=null && manager.items.Contains(entry);
        internal SkyRuntime(Options options)
        {
            Options=options;Clock=new CycleClock(options.StartHour.Value);
            if(!Load())throw new InvalidDataException("Dynamic Sky could not initialize its shader");
        }

        internal void Tick()
        {
            Options.Tick();
            if(!Options.Enabled) { Remove(true);return; }
            if(Time.realtimeSinceStartup>=nextRegister)
            {
                nextRegister=Time.realtimeSinceStartup+.5f;
                EnsureRegistered();
            }
            if(!IsActive)return;
            Clock.Advance(Mathf.Clamp(Time.unscaledDeltaTime,0,1),Options.DayMinutes.Value,Options.Paused || Time.timeScale<.0001f);
            phase=Mathf.Repeat(phase+Mathf.Clamp(Time.unscaledDeltaTime,0,.2f)*Options.CloudSpeed.Value*.008f,4096);
            Apply(false);
            FrameDiagnostics.ReportFrame(Time.unscaledDeltaTime);
        }
        internal void EnsureRegistered()
        {
            if(failed || !Options.Enabled || !WeatherManager.me || NetManager.lastWasServer)return;
            var current=WeatherManager.me;
            if(manager==current && Registered)return;
            if(manager!=current)Remove(false);
            Weather source=null;
            foreach(var item in current.items)
                if(item.loaded && item.loaded.GetComponent<Light>() && item.name!=Name)
                { source=item.loaded;if(!source.IsNight && !source.IsRian)break; }
            if(!source)return;
            Register(current,source);
            if(Options.AutoStart && !autoDone) { autoDone=true;SelectDynamic(); }
        }
        internal void Register(WeatherManager target,Weather source)
        {
            if(!Load())return;
            var root=new GameObject(Name);root.SetActive(false);root.transform.SetParent(target.transform,false);
            Sun=root.AddComponent<Light>();Sun.type=LightType.Directional;Sun.shadows=LightShadows.Soft;
            var original=source.GetComponent<Light>();Sun.cullingMask=original.cullingMask;Sun.shadowBias=original.shadowBias;
            Sun.shadowNormalBias=original.shadowNormalBias;Sun.shadowStrength=.85f;
            Sun.enabled=false;
            Weather=root.AddComponent<Weather>();
            foreach(var field in typeof(Weather).GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                if(!field.IsInitOnly)field.SetValue(Weather,field.GetValue(source));
            Weather.IsRian=false;Weather.skyBoxPath="";Weather.skybox_game=Sky;Weather.ourLight=Sun;Weather.lightFlare=null;
            Weather.ambientMode=AmbientMode.Trilight;Weather.haloStrength=0;
            Weather.defaultReflectionMode=DefaultReflectionMode.Custom;Weather.defaultReflectionResolution=SkyReflection.Size;
            State=SkyModel.Evaluate(Options,(float)Clock.Hours,phase);
            Weather.reflectionIntensity=State.Reflection;
            if(reflection==null)reflection=new SkyReflection(State);
            Weather.customReflection=reflection.Texture;
            Weather.fogMode=FogMode.Linear;
            PrepareWeather();
            manager=target;entry=new WeatherManager.It { parent=target,name=Name,loaded=Weather,wasStartLoad=true };
            target.items.Add(entry);
        }
        private bool Load()
        {
            if(Sky)return true;
            try
            {
                using(var stream=typeof(SkyRuntime).Assembly.GetManifestResourceStream("DynamicSky.bundle"))
                using(var output=new MemoryStream())
                {
                    if(stream==null)throw new FileNotFoundException("Dynamic sky resource is missing");
                    stream.CopyTo(output);bundle=AssetBundle.LoadFromMemory(output.ToArray());
                }
                if(!bundle)throw new InvalidDataException("Cannot load dynamic sky bundle");
                var template=bundle.LoadAsset<Material>("sky/dynamic");
                if(!template || !template.shader || !template.shader.isSupported)throw new InvalidDataException("Dynamic sky shader is missing or unsupported");
                shaftsShader=bundle.LoadAsset<Shader>("sky/sunshafts");
                if(!shaftsShader || !shaftsShader.isSupported)Plugin.Log?.LogWarning("Sun shafts shader is missing or unsupported");
                Sky=new Material(template) { name="Dynamic Sky Runtime",hideFlags=HideFlags.DontUnloadUnusedAsset };
                ApplyMaterial(Sky,SkyModel.Evaluate(Options,(float)Clock.Hours,phase));
                var previousTarget=RenderTexture.active;
                var target=RenderTexture.GetTemporary(8,8,0,RenderTextureFormat.ARGB32);
                try { Graphics.Blit(Texture2D.blackTexture,target,Sky,0); }
                finally { RenderTexture.active=previousTarget;RenderTexture.ReleaseTemporary(target); }
                return true;
            }
            catch(Exception error)
            {
                failed=true;
                if(Plugin.Log!=null)Plugin.Log.LogError("Dynamic Sky unavailable: "+error);
                else Debug.LogError("Dynamic Sky unavailable: "+error);
                return false;
            }
        }
        internal void SelectDynamic()
        {
            if(!Registered)return;
            if(manager.cur!=entry)previous=manager.cur;
            entry.MakeCurrent();
        }
        internal void Applied(Weather weather)
        {
            if(IsOurs(weather))
            {
                active=true;night=DayNight.isNowDay==false;lastRefreshHour=-100;Apply(true);
                Lights.Discover(Options);
                ImposterRefresh.Invalidate(true);
            }
            else if(active)
            {
                Deactivate();
            }
        }
        internal void Deactivate()
        {
            RestoreShadowDistance();
            active=false;if(Sun)Sun.enabled=false;
            Lights.Restore();
            RemoveShafts();
            if(capturedLighting)
            {
                Shader.SetGlobalColor("EmisionTreeGlobalColor",originalTreeEmission);
                RenderSettings.ambientProbe=originalAmbientProbe;capturedLighting=false;
            }
            if(RenderSettings.skybox==Sky)RenderSettings.skybox=null;
            reflection?.RestoreProbes();
            ImposterRefresh.Invalidate();
        }
        internal void SetHour(float hour)
        {
            Clock.Set(hour);Options.Change(Options.StartHour,(float)Clock.Hours);
            if(IsActive)Apply(true);
        }
        internal void Apply(bool force)
        {
            using(FrameDiagnostics.Measure(FrameDiagnostics.Stage.Apply))
                ApplyLighting(force);
        }

        internal void RequestLocalProbeRefresh() => reflection?.RequestLocalRefresh();

        private void ApplyLighting(bool force)
        {
            State=SkyModel.Evaluate(Options,(float)Clock.Hours,phase);
            ApplyMaterial(Sky,State);
            Sun.transform.rotation=Quaternion.LookRotation(-State.LightDirection,Vector3.up);
            Sun.color=State.SunColor;Sun.intensity=State.SunIntensity;Sun.enabled=State.SunIntensity>.0001f;
            Sun.shadowStrength=Mathf.Lerp(Options.NightShadowStrength.Value,Options.DayShadowStrength.Value,State.Daylight);
            Sun.shadows=Options.ShadowSoftness.Value>=.5f?LightShadows.Soft:LightShadows.Hard;
            ApplyShadowDistance();
            Lights.Apply(Options);
            UpdateSunEffects();
            if(force && ImposterManager.instance)ImposterManager.instance.UpCurLight();
            Weather.IsNight=SkyModel.Night(State.SunDirection.y,night);
            if(Weather.IsNight!=night || force) { night=Weather.IsNight;DayNight.SetDay(!night); }
            AmbienceWeather.isNight=night;AmbienceWeather.isDay=!night;AmbienceWeather.isRain=false;
            Weather.ambientSkyColor=State.AmbientSky;Weather.ambientEquatorColor=State.AmbientEquator;Weather.ambientGroundColor=State.AmbientGround;
            Weather.ambientLight=State.AmbientEquator;Weather.ambientIntensity=1;
            RenderSettings.ambientLight=State.AmbientEquator;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=State.AmbientSky;
            RenderSettings.ambientEquatorColor=State.AmbientEquator;RenderSettings.ambientGroundColor=State.AmbientGround;
            RenderSettings.ambientIntensity=1;RenderSettings.skybox=Sky;RenderSettings.sun=Sun;
            if(!capturedLighting)
            {
                originalTreeEmission=Shader.GetGlobalColor("EmisionTreeGlobalColor");
                originalAmbientProbe=RenderSettings.ambientProbe;capturedLighting=true;
            }
            Shader.SetGlobalColor("EmisionTreeGlobalColor",originalTreeEmission*Mathf.Lerp(NightTreeEmission,1,State.Daylight));
            Weather.ambientProbe=SkyLighting.Ambient(State);
            RenderSettings.ambientProbe=Weather.ambientProbe;
            RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;
            RenderSettings.customReflection=reflection.Texture;RenderSettings.reflectionIntensity=State.Reflection;
            Weather.reflectionIntensity=State.Reflection;
            float fog=SkyModel.Safe(Options.FogAmount.Value,.45f,0,1);
            float end=Mathf.Clamp(SkyModel.Safe(Options.Visibility.Value,1800,100,2500)/Mathf.Max(.02f,fog),100,50000);
            Weather.fogMode=FogMode.Linear;Weather.fogColor=State.FogColor;Weather.fogStartDistance=end*Options.FogStart.Value;Weather.fogEndDistance=end;
            RenderSettings.fog=fog>.0001f;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=State.FogColor;
            RenderSettings.fogStartDistance=Weather.fogStartDistance;RenderSettings.fogEndDistance=end;
            Weather.groundLodColor=Color.Lerp(new Color(.46f,.51f,.68f),Color.white,State.Daylight);
            float now=Time.realtimeSinceStartup;
            if(force || now>=nextRefresh)
            {
                using(FrameDiagnostics.Measure(FrameDiagnostics.Stage.LodAndImpostors))
                {
                    if(GroundLoader.me)GroundLoader.me.UpLodMaterial();
                    float delta=Mathf.Abs((float)Clock.Hours-lastRefreshHour);
                    delta=Mathf.Min(delta,24-delta);
                    if(force || delta>.03f)
                    {
                        lastRefreshHour=(float)Clock.Hours;
                        ImposterRefresh.Invalidate(force);
                    }
                }
                nextRefresh=now+1;
            }
            reflection.Update(State,force);
            reflection.ApplyToProbes(State.Reflection);
            if(Cam.me)reflection.UpdateLocalProbe(Cam.me.unityCam,State,force);
        }

        private void UpdateSunEffects()
        {
            var gameCamera=Cam.me;
            var camera=gameCamera?gameCamera.unityCam:null;
            if(effectsCamera!=camera)RemoveShafts();
            float rays=Options.ShaftsIntensity.Value;
            float flare=Options.FlareIntensity.Value;
            float visibility=0;
            if(camera && (rays>0 || flare>0))
            {
                visibility=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.12f,State.SunDirection.y));
                visibility*=1-.95f*SkyModel.CloudOpacity(State.SunDirection,State);
            }

            if(camera && shaftsShader && shaftsShader.isSupported && Mathf.Max(rays,flare)*visibility>.001f)
            {
                if(!shafts)
                {
                    shafts=camera.gameObject.AddComponent<SunShaftsEffect>();
                    shafts.Configure(camera,shaftsShader);
                    effectsCamera=camera;
                }
                var rayColor=Color.Lerp(State.SunDiscColor,State.SunColor,State.Twilight*.8f);
                shafts.UpdateSun(State.SunDirection,rayColor,rays*visibility,Options.ShaftsLength.Value,flareStrength:flare*visibility);
                shafts.enabled=true;
            }
            else if(shafts)shafts.enabled=false;

            float lunarVisibility=State.MoonVisibility*Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.12f,State.MoonDirection.y));
            lunarVisibility*=1-.95f*SkyModel.CloudOpacity(State.MoonDirection,State);
            float lunarRays=Options.MoonShafts.Value*lunarVisibility*State.MoonBrightness*.18f;
            if(camera && shaftsShader && shaftsShader.isSupported && lunarRays>.001f)
            {
                if(!moonShafts)
                {
                    moonShafts=camera.gameObject.AddComponent<SunShaftsEffect>();
                    moonShafts.Configure(camera,shaftsShader);
                    effectsCamera=camera;
                }
                moonShafts.UpdateSun(State.MoonDirection,State.MoonColor,lunarRays,Options.MoonShaftsLength.Value,true);
                moonShafts.enabled=true;
            }
            else if(moonShafts)moonShafts.enabled=false;

        }

        private void RemoveShafts()
        {
            if(shafts)
            {
                shafts.enabled=false;
                DestroyOwned(shafts);
            }
            shafts=null;
            if(moonShafts)
            {
                moonShafts.enabled=false;
                DestroyOwned(moonShafts);
            }
            moonShafts=null;
            effectsCamera=null;
        }

        private void ApplyShadowDistance()
        {
            float requested=Options.ShadowDistance.Value;
            if(requested<=0) { RestoreShadowDistance();return; }
            if(!ownsShadowDistance || QualitySettings.shadowDistance!=appliedShadowDistance)
                originalShadowDistance=QualitySettings.shadowDistance;
            appliedShadowDistance=requested;
            QualitySettings.shadowDistance=requested;
            ownsShadowDistance=true;
        }
        private void RestoreShadowDistance()
        {
            if(ownsShadowDistance && QualitySettings.shadowDistance==appliedShadowDistance)
                QualitySettings.shadowDistance=originalShadowDistance;
            ownsShadowDistance=false;
        }

        internal static void ApplyMaterial(Material material,SkyState state)
        {
            material.SetColor("_Zenith",state.Zenith);material.SetColor("_Horizon",state.Horizon);material.SetColor("_Ground",state.Ground);
            material.SetColor("_CloudColor",state.Clouds);material.SetColor("_WarmColor",state.Warm);
            material.SetVector("_SunDirection",state.SunDirection);material.SetVector("_MoonDirection",state.MoonDirection);
            material.SetColor("_CloudShade",state.CloudShade);
            material.SetFloat("_CloudLighting",state.CloudLighting);
            material.SetFloat("_SkyDepth",state.SkyDepth);
            material.SetFloat("_Daylight",state.Daylight);
            material.SetFloat("_MoonRadius",2*Mathf.Sin(state.MoonDiameter*Mathf.Deg2Rad*.25f));
            material.SetFloat("_MoonSoftness",state.MoonSoftness);
            material.SetColor("_MoonColor",state.MoonColor);
            material.SetFloat("_MoonBrightness",state.MoonBrightness);
            material.SetFloat("_CloudCoverage",state.Coverage);material.SetFloat("_CloudDensity",state.Density);material.SetFloat("_CloudPhase",state.Phase);
            material.SetFloat("_SunVisibility",Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.12f,.04f,state.SunDirection.y)));
            material.SetFloat("_SunRadius",2*Mathf.Sin(state.SunDiameter*Mathf.Deg2Rad*.25f));
            material.SetFloat("_SunSoftness",state.SunSoftness);
            material.SetColor("_SunDiscColor",state.SunDiscColor);
            material.SetFloat("_SunYellow",state.SunYellow);
            material.SetFloat("_MoonVisibility",state.MoonVisibility);
            material.SetFloat("_Stars",state.Stars);material.SetFloat("_Exposure",state.Exposure);
            material.SetFloat("_CloudScale",state.CloudScale);material.SetFloat("_CloudDirection",state.CloudDirection);
        }
        private void PrepareWeather()
        {
            Weather.IsNight=SkyModel.Night(State.SunDirection.y,false);
            Weather.ambientSkyColor=State.AmbientSky;Weather.ambientEquatorColor=State.AmbientEquator;
            Weather.ambientGroundColor=State.AmbientGround;Weather.ambientLight=State.AmbientEquator;Weather.ambientIntensity=1;
            Sun.intensity=State.SunIntensity;Sun.color=State.SunColor;
        }
        internal void QualityChanged()
        {
            if(!IsActive)return;
            originalTreeEmission=Shader.GetGlobalColor("EmisionTreeGlobalColor");
            Shader.SetGlobalColor("EmisionTreeGlobalColor",originalTreeEmission*Mathf.Lerp(NightTreeEmission,1,State.Daylight));
            ImposterRefresh.Invalidate();
        }
        internal void Remove(bool restore)
        {
            if(entry==null && !Weather && !manager)return;
            if(manager && entry!=null)
            {
                if(manager.cur==entry && restore)
                {
                    var fallback=previous!=null && manager.items.Contains(previous) && previous.loaded?previous:
                        manager.items.Find(it=>it!=entry && it.loaded);
                    if(fallback!=null)fallback.MakeCurrent();
                }
                manager.items.Remove(entry);
            }
            Deactivate();
            if(Weather) { Weather.gameObject.SetActive(false);DestroyOwned(Weather.gameObject); }
            Weather=null;Sun=null;entry=null;manager=null;previous=null;autoDone=false;
        }
        internal void Shutdown()
        {
            Remove(true);Options.Save();reflection?.Destroy();reflection=null;
            if(Sky)DestroyOwned(Sky);Sky=null;
            if(bundle)bundle.Unload(false);bundle=null;
        }
        internal static void DestroyOwned(Object value)
        {
            if(!value)return;if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);
        }
    }
}
