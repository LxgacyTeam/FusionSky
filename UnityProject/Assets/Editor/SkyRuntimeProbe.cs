using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class SkyRuntimeProbe
{
    private const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly string Game=Environment.GetEnvironmentVariable("DYNAMIC_SKY_GAME_DIR") ??
        "C:/Program Files (x86)/Steam/steamapps/common/MadOut2_BigCityOnline";
    private static int checks;
    private static void Check(bool condition,string message) { if(!condition)throw new Exception(message);checks++; }
    private static object Field(object target,string name) { return target.GetType().GetField(name,Instance).GetValue(target); }
    private static void Set(object target,string name,object value) { target.GetType().GetField(name,Instance).SetValue(target,value); }
    private static object Call(object target,string name,params object[] args) { return target.GetType().GetMethod(name,Instance).Invoke(target,args); }
    private static void Reset(object options,string section)
    {
        var type=options.GetType().GetNestedType("Section",BindingFlags.NonPublic);
        Call(options,"ResetSection",Enum.Parse(type,section));
    }

    private static void Value(object options,string name,object value)
    {
        var field=options.GetType().GetField(name,Instance);
        if(field.FieldType==typeof(bool)) field.SetValue(options,value);
        else field.FieldType.GetProperty("Value",Instance).SetValue(field.GetValue(options),value,null);
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        var assembly=Assembly.LoadFile(Path.GetFullPath("../bin/Debug/DynamicSky.dll"));
        ProbePresets(assembly);
        var model=assembly.GetType("DynamicSky.SkyModel",true);
        var evaluate=model.GetMethod("Evaluate",Static,null,new[]{typeof(float),typeof(float),typeof(float),typeof(float),typeof(float)},null);
        ProbeReflectionWork(assembly,model,evaluate);
        var night=model.GetMethod("Night",Static);
        var map=model.GetMethod("LightingHour",Static);
        Func<float,float> lightingHour=h=>(float)map.Invoke(null,new object[]{h});
        Check(lightingHour(6)==6 && lightingHour(12)==12 && lightingHour(18.5f)==16 && lightingHour(21.5f)==19 &&
            lightingHour(24)==0,"Clock phase anchors moved incorrectly");
        for(int i=1;i<1440;i++)
            Check(lightingHour(i/60f)>lightingHour((i-1)/60f),"Lighting phase reversed direction");
        Func<float,float,float,object> sample=(h,c,d)=>evaluate.Invoke(null,new object[]{h,c,d,1f,0f});
        var clockType=assembly.GetType("DynamicSky.CycleClock",true);
        var clock=Activator.CreateInstance(clockType,Instance,null,new object[]{4.5},null);
        Call(clock,"Advance",3600d,60d,false);
        Check(Math.Abs((double)clockType.GetProperty("Hours",Instance).GetValue(clock,null)-4.5)<.00001,"Clock day wrap");
        Call(clock,"Advance",100d,60d,true);
        Check(Math.Abs((double)clockType.GetProperty("Hours",Instance).GetValue(clock,null)-4.5)<.00001,"Paused clock advanced");
        Call(clock,"Set",-1d);Check((double)clockType.GetProperty("Hours",Instance).GetValue(clock,null)==23,"Negative time wrap");
        Call(clock,"Set",double.NaN);Check((double)clockType.GetProperty("Hours",Instance).GetValue(clock,null)==4.5,"NaN time guard");
        var midnight=sample(0,.35f,.55f);var end=sample(24,.35f,.55f);
        Check(((Color)Field(midnight,"Zenith")-(Color)Field(end,"Zenith")).maxColorComponent<.00001,"Midnight color jump");
        Check((float)Field(sample(12,1,1),"SunIntensity")<(float)Field(sample(12,0,0),"SunIntensity"),"Overcast does not dim sunlight");
        Check((bool)night.Invoke(null,new object[]{0f,true}) && !(bool)night.Invoke(null,new object[]{0f,false}),"Twilight hysteresis missing");
        bool wasNight=true;int transitions=0;
        for(int i=0;i<=1440;i++)
        {
            var state=sample(i/60f,.35f,.55f);
            var direction=(Vector3)Field(state,"SunDirection");
            foreach(var f in state.GetType().GetFields(Instance))
                if(f.FieldType==typeof(float)) { float v=(float)f.GetValue(state);Check(!float.IsNaN(v)&&!float.IsInfinity(v),"Invalid state "+f.Name); }
            bool next=(bool)night.Invoke(null,new object[]{direction.y,wasNight});
            if(next!=wasNight)transitions++;wasNight=next;
        }
        Check(transitions==2,"Expected exactly one dawn and one dusk per day");

        var bepinex=Assembly.Load("BepInEx");
        var pathsType=bepinex.GetType("BepInEx.Paths",true);
        pathsType.GetProperty("BepInExConfigPath",Static).GetSetMethod(true).Invoke(null,new object[]{Path.GetFullPath("../probe-bepinex.cfg")});
        var configType=bepinex.GetType("BepInEx.Configuration.ConfigFile",true);
        var legacyPath=Path.GetFullPath("../probe.cfg");
        File.WriteAllText(legacyPath,"[Lighting]\nAmbientBrightness = 1.5\nNightBrightness = 2\nSkyExposure = 1.25\n");
        object config=null;
        foreach(var ctor in configType.GetConstructors())
        {
            var parameters=ctor.GetParameters();
            if(parameters.Length<2 || parameters[0].ParameterType!=typeof(string) || parameters[1].ParameterType!=typeof(bool))continue;
            var args=new object[parameters.Length];args[0]=legacyPath;args[1]=false;
            for(int i=2;i<args.Length;i++)args[i]=parameters[i].HasDefaultValue?parameters[i].DefaultValue:null;
            config=ctor.Invoke(args);break;
        }
        Check(config!=null,"Cannot create isolated test configuration");
        var optionsType=assembly.GetType("DynamicSky.Options",true);
        string prefs="DynamicSky.Probe."+Guid.NewGuid().ToString("N")+".";
        var options=Activator.CreateInstance(optionsType,Instance,null,new object[]{config,prefs},null);
        Check(PlayerPrefs.GetFloat(prefs+"Day.Ambient")==1.5f,"Day ambient migration");
        Check(PlayerPrefs.GetFloat(prefs+"Night.Ambient")==3f,"Night ambient migration");
        Check(PlayerPrefs.GetFloat(prefs+"Day.Exposure")==1.25f && PlayerPrefs.GetFloat(prefs+"Night.Exposure")==1.25f,"Exposure migration");
        Reset(options,"Lighting");
        Value(options,"DayTemperature",.5f);Call(options,"Save");
        var reloaded=Activator.CreateInstance(optionsType,Instance,null,new object[]{null,prefs},null);
        Check((float)Field(reloaded,"DayTemperature").GetType().GetProperty("Value",Instance).GetValue(Field(reloaded,"DayTemperature"),null)==.5f,"PlayerPrefs reload");
        Value(options,"DayTemperature",0f);
        Value(options,"NightAmbient",1f);Value(options,"NightExposure",1f);
        Value(options,"DawnPink",0f);Value(options,"TwilightWarmth",1f);
        Value(options,"StreetYellow",0f);
        Value(options,"AutoStart",false);Value(options,"Paused",true);
        var runtimeType=assembly.GetType("DynamicSky.SkyRuntime",true);
        var runtime=Activator.CreateInstance(runtimeType,Instance,null,new object[]{options},null);
        var pluginType=assembly.GetType("DynamicSky.Plugin",true);
        pluginType.GetField("Runtime",Static).SetValue(null,runtime);
        var legacyHotkeys=Assembly.Load("BigCityLegacy").GetType("LegacyHotkeys",true).GetMethod("Update",Static);
        var harmonyType=Assembly.Load("0Harmony").GetType("HarmonyLib.Harmony",true);
        var harmony=Activator.CreateInstance(harmonyType,new object[]{"dynamic.sky.probe"});
        harmonyType.GetMethod("PatchAll",new[]{typeof(Assembly)}).Invoke(harmony,new object[]{assembly});

        var hotkeyPatch=assembly.GetType("DynamicSky.WeatherHotkeyPatch");
        Check((MethodBase)hotkeyPatch.GetMethod("TargetMethod",Static).Invoke(null,null)==legacyHotkeys &&
            harmonyType.GetMethod("GetPatchInfo",Static).Invoke(null,new object[]{legacyHotkeys})!=null,"F5 weather handler not patched");
        var game=Assembly.Load("Assembly-CSharp");
        var weatherType=game.GetType("Weather",true);var managerType=game.GetType("WeatherManager",true);var entryType=managerType.GetNestedType("It");
        var managerRoot=new GameObject("Weather Manager Probe");
        var manager=managerRoot.AddComponent(managerType);managerType.GetField("me").SetValue(null,manager);
        var sourceRoot=new GameObject("Main");sourceRoot.SetActive(false);sourceRoot.AddComponent<Light>();
        var source=sourceRoot.AddComponent(weatherType);Set(source,"ourLight",sourceRoot.GetComponent<Light>());
        var sourceEntry=Activator.CreateInstance(entryType);Set(sourceEntry,"name","Main");Set(sourceEntry,"loaded",source);Set(sourceEntry,"parent",manager);
        var entries=(IList)Field(manager,"items");entries.Add(sourceEntry);Set(manager,"cur",sourceEntry);
        Check((Material)Field(runtime,"Sky"),"Shader was not loaded before weather registration");
        var references=assembly.GetReferencedAssemblies();
        Check(Array.Exists(references,item=>item.Name=="LegacyUIFramework"),"UI framework is not referenced");
        Check(pluginType.GetMethod("DrawControls",Instance)!=null,"Framework controls are missing");
        Call(runtime,"Register",manager,source);
        Check((bool)runtimeType.GetProperty("Registered",Instance).GetValue(runtime,null),"Weather entry not registered");
        Call(runtime,"EnsureRegistered");Check(entries.Count==2,"Weather entry duplicated");

        var sunsetSky=new Material(Shader.Find("Skybox/Procedural"));
        var sunsetRoot=new GameObject("Stock Night");sunsetRoot.SetActive(false);var sunsetLight=sunsetRoot.AddComponent<Light>();
        sunsetLight.intensity=.6f;sunsetLight.color=new Color(1,.96f,.91f);
        var sunset=sunsetRoot.AddComponent(weatherType);Set(sunset,"ourLight",sunsetLight);Set(sunset,"skybox_game",sunsetSky);
        Set(sunset,"ambientSkyColor",new Color(.46f,.54f,.69f));Set(sunset,"ambientEquatorColor",new Color(.33f,.38f,.49f));
        Set(sunset,"ambientGroundColor",new Color(.17f,.20f,.27f));Set(sunset,"ambientMode",AmbientMode.Trilight);
        Set(sunset,"ambientLight",new Color(.2f,.2f,.2f));Set(sunset,"fogMode",FogMode.Linear);
        var sunsetEntry=Activator.CreateInstance(entryType);Set(sunsetEntry,"name","Sunset");Set(sunsetEntry,"loaded",sunset);Set(sunsetEntry,"parent",manager);entries.Add(sunsetEntry);
        var methodType=Assembly.Load("0Harmony").GetType("HarmonyLib.HarmonyMethod",true);
        var skip=Activator.CreateInstance(methodType,new object[]{typeof(SkyRuntimeProbe).GetMethod("SkipDisable",Static)});
        foreach(var patchMethod in harmonyType.GetMethods())
        {
            var parameters=patchMethod.GetParameters();
            if(patchMethod.Name!="Patch" || parameters.Length<5 || parameters[0].ParameterType!=typeof(MethodBase) || parameters[1].ParameterType!=methodType)continue;
            var arguments=new object[parameters.Length];arguments[0]=weatherType.GetMethod("OnDisable",Instance);arguments[1]=skip;
            patchMethod.Invoke(harmony,arguments);break;
        }

        var lampRoot=new GameObject("Native Lamp");lampRoot.SetActive(false);var streetLight=lampRoot.AddComponent<Light>();streetLight.intensity=2;
        var distanceType=game.GetType("DistanceViewLight",true);var dayType=game.GetType("DayNight",true);
        var distance=lampRoot.AddComponent(distanceType);var day=lampRoot.AddComponent(dayType);Set(day,"nightLight",distance);lampRoot.SetActive(true);
        Call(distance,"Start");
        var probeRoot=new GameObject("Global Reflection Probe");
        var nativeProbe=probeRoot.AddComponent<ReflectionProbe>();
        nativeProbe.mode=ReflectionProbeMode.Realtime;
        nativeProbe.cullingMask=0;
        nativeProbe.intensity=.8f;
        var probeType=game.GetType("Probe",true);
        var probe=probeRoot.AddComponent(probeType);Set(probe,"isGlobal",true);
        ((IList)probeType.GetField("instances",Static).GetValue(null)).Add(probe);
        Shader.SetGlobalColor("EmisionTreeGlobalColor",new Color(.8f,.8f,.8f,1));
        Call(runtime,"SelectDynamic");Check((bool)runtimeType.GetProperty("IsActive",Instance).GetValue(runtime,null),"Dynamic sky not active");
        Call(runtime,"SetHour",0f);Check(!(bool)dayType.GetField("isNowDay").GetValue(null),"Native night state not set");
        Check((bool)distanceType.GetProperty("canEnabled",Instance).GetValue(distance,null),"Native streetlamp disabled at night");
        var nightLight=(Light)Field(runtime,"Sun");
        Check(nightLight.enabled && nightLight.intensity>.5f && nightLight.shadows==LightShadows.Soft &&
            nightLight.color.b>nightLight.color.r && Mathf.Abs(nightLight.shadowStrength-.8f)<.001f,"Native-style night shadows");
        Check(RenderSettings.ambientSkyColor.maxColorComponent>.15f,"Night ambient is unreadable");
        Check(Shader.GetGlobalColor("EmisionTreeGlobalColor").r<.001f,"Night foliage emission remains on");
        var reflection=RenderSettings.customReflection;
        Check(nativeProbe.mode==ReflectionProbeMode.Custom && nativeProbe.texture==reflection,"Global probe masks out the live cubemap");
        Color reflectionBefore=reflection.GetPixels(CubemapFace.PositiveY)[0];
        Value(options,"NightExposure",1.5f);Call(runtime,"Apply",true);
        Check(reflection.GetPixels(CubemapFace.PositiveY)[0].grayscale>reflectionBefore.grayscale*1.4f,"Reflection change waits for a timer");
        Value(options,"NightExposure",1f);
        var sceneLights=Field(runtime,"Lights");
        Call(sceneLights,"AfterStreetUpdate",streetLight,options);
        Check(Mathf.Abs(streetLight.intensity-1.6f)<.001f,"Street light brightness default "+streetLight.intensity);
        Value(options,"StreetBrightness",.5f);Value(options,"StreetYellow",1f);Call(runtime,"Apply",true);
        Check(Mathf.Abs(streetLight.intensity-1)<.001f && streetLight.color.b<.5f,"Street light brightness and tint");
        Call(sceneLights,"BeforeStreetUpdate",streetLight);streetLight.intensity=.6f;
        Call(sceneLights,"AfterStreetUpdate",streetLight,options);
        Check(Mathf.Abs(streetLight.intensity-.3f)<.001f,"Street light does not preserve native distance fade");
        var carRoot=new GameObject("Headlight Probe");
        carRoot.SetActive(false);
        var car=carRoot.AddComponent(game.GetType("CarControl",true));
        var carLamp=carRoot.AddComponent(game.GetType("CarLight",true));
        var headlight=carRoot.AddComponent<Light>();
        headlight.intensity=4;
        Set(carLamp,"car",car);Set(carLamp,"useLight",headlight);
        Value(options,"HeadlightBrightness",.6f);Value(options,"HeadlightYellow",1f);
        Call(sceneLights,"TrackHeadlight",carLamp,options);
        Check(Mathf.Abs(headlight.intensity-2.4f)<.001f && headlight.color.b<.5f,"Front headlight settings");
        var oreol=new GameObject("Oreol");
        oreol.transform.SetParent(carRoot.transform,false);
        var halo=oreol.AddComponent<MeshRenderer>();
        halo.sharedMaterial=new Material(Shader.Find("Standard")) { color=new Color(1,1,1,.6f) };
        var haloMat=halo.material;
        var pooled=oreol.AddComponent(game.GetType("CarLight",true));
        var pooledLight=oreol.AddComponent<Light>();pooledLight.intensity=3;
        Set(pooled,"car",car);Set(pooled,"useLight",pooledLight);Set(pooled,"oreol",halo);
        Call(sceneLights,"Discover",options);
        Check(Mathf.Abs(pooledLight.intensity-1.8f)<.001f && haloMat.color.b<.5f,"Inactive pooled car was missed");
        Value(options,"HeadlightBrightness",.4f);Value(options,"HeadlightYellow",.5f);Call(runtime,"Apply",true);
        Check(Mathf.Abs(pooledLight.intensity-1.2f)<.001f && Mathf.Abs(haloMat.color.b-.71f)<.001f &&
            Mathf.Abs(haloMat.color.a-.6f)<.001f,"Live headlights or Oreol fade alpha");
        Set(pooled,"needAlpha",.5f);Call(pooled,"FixedUpdate");
        Check(Mathf.Abs(haloMat.color.a-.25f)<.001f && Mathf.Abs(haloMat.color.b-.71f)<.001f,"Game fade lost halo tint");
        var rearRoot=new GameObject("Rear Light Probe");rearRoot.SetActive(false);
        var rearLamp=rearRoot.AddComponent(game.GetType("CarLight",true));
        var rearLight=rearRoot.AddComponent<Light>();rearLight.color=Color.red;
        Set(rearLamp,"car",car);Set(rearLamp,"useLight",rearLight);Set(rearLamp,"IsBack",true);
        Call(sceneLights,"TrackHeadlight",rearLamp,options);
        Check(rearLight.intensity==1 && rearLight.color==Color.red,"Rear light was changed");
        var lightState=Field(runtime,"State");
        using(var buffer=new CommandBuffer())
            assembly.GetType("DynamicSky.SkyLighting").GetMethod("Setup",Static).Invoke(null,new[]{(object)buffer,lightState,Field(sunset,"customReflection")});
        var beforeNight=RenderSettings.ambientSkyColor;
        Value(options,"NightAmbient",2f);Call(runtime,"Apply",true);
        Check(RenderSettings.ambientSkyColor.r>beforeNight.r*1.8f,"Night brightness control does not work");
        Value(options,"NightAmbient",1f);
        var sky=(Material)Field(runtime,"Sky");
        var nightZenith=sky.GetColor("_Zenith");
        Value(options,"NightTemperature",1f);Call(runtime,"Apply",true);
        Check(RenderSettings.ambientSkyColor.b<beforeNight.b*.85f,"Night temperature does not warm ambient light");
        Check(sky.GetColor("_Zenith").b<nightZenith.b*.9f,"Night temperature does not warm the sky");
        float moon=sky.GetFloat("_MoonVisibility");
        Value(options,"StarsBrightness",0f);Call(runtime,"Apply",true);
        Check(moon>0 && sky.GetFloat("_MoonVisibility")==moon && sky.GetFloat("_Stars")==0,"Stars brightness changes the moon");
        Value(options,"StarsBrightness",1f);
        Call(runtime,"SetHour",4.5f);
        var naturalDawn=sky.GetColor("_Horizon");
        Value(options,"DuskPink",1f);Call(runtime,"Apply",true);
        Check(sky.GetColor("_Horizon")==naturalDawn,"Dusk setting is applied at dawn");
        Value(options,"DawnPink",1f);Call(runtime,"Apply",true);
        var pinkDawn=sky.GetColor("_Horizon");
        Check(naturalDawn.g>naturalDawn.b && pinkDawn.b/pinkDawn.g>naturalDawn.b/naturalDawn.g*1.4f,"Dawn rose tint control");
        Value(options,"DuskPink",0f);Value(options,"DawnPink",0f);
        Call(runtime,"SetHour",12f);Check((bool)dayType.GetField("isNowDay").GetValue(null),"Native day state not set");
        var dayColor=RenderSettings.ambientSkyColor;
        var dayHorizon=sky.GetColor("_Horizon");
        Value(options,"NightTemperature",-1f);Call(runtime,"Apply",true);
        Check(RenderSettings.ambientSkyColor==dayColor,"Night temperature changes noon");
        Value(options,"DayTemperature",-1f);Call(runtime,"Apply",true);
        Check(RenderSettings.ambientSkyColor.r<dayColor.r*.85f,"Day temperature does not cool ambient light");
        Check(sky.GetColor("_Horizon").b>dayHorizon.b*1.15f &&
            Mathf.Abs(sky.GetColor("_Horizon").grayscale-dayHorizon.grayscale)<.001f,"Day temperature does not cool the sky consistently");
        Value(options,"DayTemperature",0f);Value(options,"NightTemperature",0f);
        Value(options,"DaySaturation",0f);Call(runtime,"Apply",true);
        var desaturated=sky.GetColor("_Zenith");
        Check(Mathf.Abs(desaturated.r-desaturated.b)<.001f,"Day sky saturation does not reach grayscale");
        Value(options,"DaySaturation",1f);
        Value(options,"SunDiameter",4f);Value(options,"SunSoftness",.9f);Call(runtime,"Apply",true);
        Check(Mathf.Abs(sky.GetFloat("_SunRadius")-2*Mathf.Sin(Mathf.Deg2Rad))<.0001f &&
            Mathf.Abs(sky.GetFloat("_SunSoftness")-.9f)<.001f,"Solar disk controls");
        var sunColor=((Light)Field(runtime,"Sun")).color;
        Value(options,"SunYellow",1f);Call(runtime,"Apply",true);
        Check(Mathf.Abs(sky.GetColor("_SunDiscColor").b-.52f)<.002f && ((Light)Field(runtime,"Sun")).color==sunColor,"Solar disk color is not independent");
        Reset(options,"Celestial");
        var postRoot=ProbeBloom(game,assembly,runtime,options);
        Check(!(bool)distanceType.GetProperty("canEnabled",Instance).GetValue(distance,null),"Native streetlamp stays enabled in daylight");
        Value(options,"FogAmount",0f);Call(runtime,"Apply",true);Check(!RenderSettings.fog,"Zero haze leaves fog enabled");
        Value(options,"FogAmount",1f);Value(options,"Visibility",700f);Call(runtime,"Apply",true);
        Check(RenderSettings.fog && RenderSettings.fogMode==FogMode.Linear && Mathf.Abs(RenderSettings.fogEndDistance-700)<.01f,"Fog slider not applied");
        Value(options,"CloudCoverage",.9f);Value(options,"CloudDensity",.8f);Call(runtime,"Apply",true);
        Value(options,"CloudScale",2f);Value(options,"CloudDirection",90f);Value(options,"DayExposure",1.4f);
        Call(runtime,"Apply",true);
        Check(Mathf.Abs(sky.GetFloat("_CloudScale")-2)<.001f,"Cloud scale control");
        Check(Mathf.Abs(sky.GetFloat("_CloudDirection")-Mathf.PI/2)<.001f,"Cloud direction control");
        Check(Mathf.Abs(sky.GetFloat("_Exposure")-1.4f)<.001f,"Sky exposure control");
        Value(options,"CloudScale",1f);Value(options,"CloudDirection",0f);Value(options,"DayExposure",1f);
        Check(Mathf.Abs(sky.GetFloat("_CloudCoverage")-.9f)<.0001f,"Cloud coverage control");
        Check(Mathf.Abs(sky.GetFloat("_CloudDensity")-.8f)<.0001f,"Cloud density control");
        Call(sunsetEntry,"MakeCurrent");
        Check(!(bool)runtimeType.GetProperty("IsActive",Instance).GetValue(runtime,null),"Dynamic sky still active under Sunset");
        Call(runtime,"Tick");
        Check(Mathf.Abs(Shader.GetGlobalColor("EmisionTreeGlobalColor").r-.8f)<.001f,"Foliage emission was not restored");
        Check(RenderSettings.skybox==sunsetSky,"Original weather was modified");
        Check(nativeProbe.mode==ReflectionProbeMode.Realtime && !nativeProbe.customBakedTexture &&
            Mathf.Abs(nativeProbe.intensity-.8f)<.001f,"Original global reflection probe was not restored");
        Check(Mathf.Abs(streetLight.intensity-.6f)<.001f && streetLight.color==Color.white,"Native street light was not restored");
        Check(headlight.intensity==4 && headlight.color==Color.white,"Native headlight was not restored");
        Check(pooledLight.intensity==3 && Mathf.Abs(haloMat.color.b-1)<.001f &&
            Mathf.Abs(haloMat.color.a-.25f)<.001f,"Stock halo tint or current fade was not restored");
        Check(sunsetLight.enabled && !((Light)Field(runtime,"Sun")).enabled,"Dynamic lighting leaked into Sunset");
        Check(assembly.GetType("DynamicSky.InputPatch")==null,"Dynamic Sky still patches player input");

        Call(runtime,"SelectDynamic");Value(options,"CloudCoverage",.35f);Value(options,"CloudDensity",.55f);Value(options,"FogAmount",.45f);Value(options,"Visibility",1800f);
        CreatePreviewWorld();Directory.CreateDirectory("../previews");
        ProbeTheme(assembly);
        ProbeSunEffects(game,runtime,options);
        ProbeTwilight(assembly,options,runtime);
        foreach(var pair in new[]{new Vector2(0,0),new Vector2(4.5f,1),new Vector2(12,2),new Vector2(21.5f,3)})
        {
            Call(runtime,"SetHour",pair.x);for(int i=0;i<6;i++)Call(runtime,"Apply",false);
            Preview("../previews/"+(int)pair.y+".png",pair.x);
        }
        Call(runtime,"Remove",true);Check(entries.Count==2 && RenderSettings.skybox==sunsetSky,"Removing active cycle does not restore previous weather");
        Call(runtime,"Shutdown");pluginType.GetField("Runtime",Static).SetValue(null,null);
        sunsetRoot.SetActive(false);
        harmonyType.GetMethod("UnpatchSelf").Invoke(harmony,null);
        Object.DestroyImmediate(managerRoot);Object.DestroyImmediate(sourceRoot);Object.DestroyImmediate(sunsetRoot);
        Object.DestroyImmediate(lampRoot);Object.DestroyImmediate(postRoot);Object.DestroyImmediate(sunsetSky);
        Object.DestroyImmediate(carRoot);Object.DestroyImmediate(rearRoot);
        ((IList)probeType.GetField("instances",Static).GetValue(null)).Remove(probe);
        Object.DestroyImmediate(probeRoot);
        managerType.GetField("me").SetValue(null,null);
        foreach(var option in (IEnumerable)Field(options,"floats"))PlayerPrefs.DeleteKey((string)Field(option,"Key"));
        foreach(string key in new[]{"General.Enabled","General.AutoStart","General.ToggleKey","Time.Paused","Version"})PlayerPrefs.DeleteKey(prefs+key);
        PlayerPrefs.Save();
        File.WriteAllText("../runtime-verification.json","{\"checks\":"+checks+",\"nightShadows\":true,\"exposureRestore\":true,\"liveReflections\":true,\"sceneLights\":true,\"dawnTint\":true,\"twilightContinuity\":true,\"moonRays\":true,\"shadowRestore\":true,\"flareLifecycle\":true,\"presetRoundTrip\":true,\"localizedUI\":true}");
        Debug.Log("DYNAMIC_SKY_RUNTIME_OK checks="+checks);
    }

    private static void ProbeTwilight(Assembly assembly,object options,object runtime)
    {
        foreach(string section in new[]{"Clouds","Lighting","Fog","Celestial","StreetLights","Shadows"})Reset(options,section);
        // After verifying current defaults separately, exercise the normalized twilight fixture.
        Value(options,"CloudCoverage",.35f);Value(options,"CloudDensity",.55f);
        Value(options,"Latitude",51f);Value(options,"North",145f);
        Value(options,"NightAmbient",1f);Value(options,"NightExposure",1f);
        Value(options,"DawnPink",0f);Value(options,"DawnBrightness",1f);
        Value(options,"TwilightWarmth",1f);Value(options,"TwilightDepth",1f);Value(options,"TwilightSky",1f);
        var model=assembly.GetType("DynamicSky.SkyModel",true);
        var evaluate=model.GetMethod("Evaluate",Static,null,new[]{options.GetType(),typeof(float),typeof(float)},null);
        Func<float,object> sample=h=>evaluate.Invoke(null,new object[]{options,h,0f});
        object previous=sample(0);
        var rows=new System.Text.StringBuilder("hour,elevation,direct,ambient,horizonR,horizonG,horizonB\n");
        var phase=model.GetMethod("LightingHour",Static);
        for(int minute=1;minute<=1440;minute++)
        {
            float hour=minute/60f;var state=sample(hour);
            float elapsed=(float)phase.Invoke(null,new object[]{hour})-(float)phase.Invoke(null,new object[]{(minute-1)/60f});
            float step=Mathf.Max(1,Mathf.Repeat(elapsed,24)*60);
            foreach(string field in new[]{"Zenith","Horizon","Clouds","CloudShade","SunColor","AmbientSky","AmbientEquator","AmbientGround","FogColor"})
            {
                var a=(Color)Field(previous,field);var b=(Color)Field(state,field);
                Check(Mathf.Abs(a.r-b.r)<.035f*step && Mathf.Abs(a.g-b.g)<.035f*step && Mathf.Abs(a.b-b.b)<.035f*step,"Color discontinuity "+field+" at "+hour);
            }
            Check(Mathf.Abs((float)Field(previous,"SunIntensity")-(float)Field(state,"SunIntensity"))<.04f*step,"Light discontinuity at "+hour);
            if(minute%15==0)
            {
                var h=(Color)Field(state,"Horizon");
                rows.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4},{5},{6}\n",hour,((Vector3)Field(state,"SunDirection")).y,Field(state,"SunIntensity"),((Color)Field(state,"AmbientSky")).grayscale,h.r,h.g,h.b);
            }
            previous=state;
        }
        File.WriteAllText("../transition-samples.csv",rows.ToString());
        var early=sample(3.5f);var middle=sample(4f);
        Check(((Color)Field(middle,"AmbientEquator")).grayscale>((Color)Field(early,"AmbientEquator")).grayscale*1.2f,"Objects do not brighten with the predawn sky");
        var night=sample(0);
        Check(((Color)Field(night,"Clouds")).grayscale>((Color)Field(night,"Horizon")).grayscale,"Night clouds turn black");
        var baselinePath=Environment.GetEnvironmentVariable("DYNAMIC_SKY_BASELINE_DLL");
        if(!string.IsNullOrEmpty(baselinePath))
        {
            var baseline=Assembly.LoadFile(baselinePath);
            var optionType=baseline.GetType("DynamicSky.Options",true);
            string prefix="DynamicSky.Baseline."+Guid.NewGuid().ToString("N")+".";
            var oldOptions=Activator.CreateInstance(optionType,Instance,null,new object[]{null,prefix},null);
            var oldEvaluate=baseline.GetType("DynamicSky.SkyModel").GetMethod("Evaluate",Static,null,new[]{optionType,typeof(float),typeof(float)},null);
            foreach(float hour in new[]{0f,7f,8f,9f,10f,11f,12f,13f,14f,15f,16f,16.5f,23f})
            {
                var before=oldEvaluate.Invoke(null,new object[]{oldOptions,model.GetMethod("LightingHour",Static).Invoke(null,new object[]{hour}),0f});var after=sample(hour);
                foreach(string field in new[]{"AmbientSky","AmbientEquator","AmbientGround","SunColor","FogColor","Zenith","Horizon"})
                    Check((Color)Field(before,field)==(Color)Field(after,field),"Established day/night changed: "+field+" at "+hour);
                Check(Mathf.Abs((float)Field(before,"SunIntensity")-(float)Field(after,"SunIntensity"))<.00001f,"Established intensity changed at "+hour);
                Check(Vector3.Distance((Vector3)Field(before,"LightDirection"),(Vector3)Field(after,"LightDirection"))<.00001f,"Established light direction changed");
            }
            foreach(var item in (IEnumerable)Field(oldOptions,"floats"))PlayerPrefs.DeleteKey((string)Field(item,"Key"));
            foreach(string key in new[]{"General.Enabled","General.AutoStart","General.ToggleKey","Time.Paused","Version"})PlayerPrefs.DeleteKey(prefix+key);
        }
        Value(options,"DawnBrightness",.25f);var dark=sample(4.5f);
        Value(options,"DawnBrightness",3f);var bright=sample(4.5f);
        Check((float)Field(bright,"SunIntensity")>(float)Field(dark,"SunIntensity")*3,"Dawn brightness has insufficient range");
        Reset(options,"Lighting");
        Call(runtime,"SetHour",4.5f);
        var reflection=RenderSettings.customReflection;
        var reflectedBefore=reflection.GetPixels(CubemapFace.PositiveZ)[16*32+16];
        Value(options,"TwilightDepth",.5f);Call(runtime,"Apply",true);
        Check(reflection.GetPixels(CubemapFace.PositiveZ)[16*32+16]!=reflectedBefore,"Twilight depth leaves stale reflections");
        Value(options,"TwilightSky",.5f);Call(runtime,"Apply",true);
        Check(((Material)Field(runtime,"Sky")).GetFloat("_Exposure")<.8f,"Twilight sky brightness ignored");
        Reset(options,"Lighting");
        Value(options,"TwilightWarmth",3f);
        var cap=model.GetMethod("TwilightSaturationLimit",Static);
        foreach(var pair in new[]{new Vector2(6,1.5f),new Vector2(6.25f,2.25f),new Vector2(6.5f,3)})
        {
            Check(Mathf.Abs((float)cap.Invoke(null,new object[]{pair.x})-pair.y)<.001f,"Incorrect dawn saturation cap");
            var limited=sample(pair.x);
            Value(options,"TwilightWarmth",pair.y);
            Check((Color)Field(sample(pair.x),"Horizon")== (Color)Field(limited,"Horizon"),"Saturation cap does not affect rendered sky");
            Value(options,"TwilightWarmth",3f);
        }
        Call(options,"Save");
        Check(PlayerPrefs.GetFloat((string)Field(Field(options,"TwilightWarmth"),"Key"))==3f,"Applied cap overwrote saved saturation");
        Reset(options,"Lighting");
        var noon=sample(12f);
        Value(options,"DawnBrightness",3f);Value(options,"DuskBrightness",3f);Value(options,"DawnPink",1f);Value(options,"DuskPink",1f);
        Value(options,"TwilightWarmth",3f);Value(options,"TwilightSky",2f);
        var controlledNoon=sample(12f);
        Check((Color)Field(noon,"SunColor")== (Color)Field(controlledNoon,"SunColor") && (Color)Field(noon,"Horizon")== (Color)Field(controlledNoon,"Horizon") && (float)Field(controlledNoon,"Exposure")==1f,"Twilight controls leak into daylight");
        Reset(options,"Lighting");
        foreach(float pink in new[]{0f,1f})
        {
            Value(options,"DawnPink",pink);Value(options,"DuskPink",pink);
            foreach(float hour in new[]{3f,4f,4.5f,5f,6f,18f,19f,20f,21f})
            {
                Call(runtime,"SetHour",hour);
                Preview("../previews/twilight-"+hour.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"-"+pink+".png",hour);
            }
        }
        Reset(options,"Lighting");
        Call(runtime,"SetHour",12f);
        var sun=(Light)Field(runtime,"Sun");
        float distance=QualitySettings.shadowDistance;
        Value(options,"DayShadowStrength",.4f);Value(options,"ShadowDistance",230f);Value(options,"ShadowSoftness",0f);
        Call(runtime,"Apply",true);
        Check(sun.shadowStrength==.4f && sun.shadows==LightShadows.Hard && QualitySettings.shadowDistance==230,"Shadow controls not applied");
        Value(options,"ShadowFill",.5f);Call(runtime,"Apply",true);
        var shaded=RenderSettings.ambientSkyColor;
        Reset(options,"Shadows");Call(runtime,"Apply",true);
        Check(RenderSettings.ambientSkyColor.grayscale>shaded.grayscale*1.99f && QualitySettings.shadowDistance==distance,"Shadow fill or reset failed");
        Value(options,"ShadowDistance",210f);Call(runtime,"Apply",true);Call(runtime,"Deactivate");
        Check(QualitySettings.shadowDistance==distance,"Shadow distance leaked after deactivation");
        Reset(options,"Shadows");Call(runtime,"Applied",Field(runtime,"Weather"));
        Value(options,"MoonDiameter",3f);Value(options,"MoonSoftness",.8f);Value(options,"MoonYellow",1f);
        Call(runtime,"SetHour",0f);var sky=(Material)Field(runtime,"Sky");
        Check(Mathf.Abs(sky.GetFloat("_MoonRadius")-2*Mathf.Sin(3*Mathf.Deg2Rad*.25f))<.00001f && sky.GetFloat("_MoonSoftness")==.8f && sky.GetColor("_MoonColor").r>sky.GetColor("_MoonColor").b,"Moon disc controls not applied");
        Reset(options,"Celestial");
    }

    private static void ProbeReflectionWork(Assembly assembly,Type model,MethodInfo evaluate)
    {
        var state=evaluate.Invoke(null,new object[]{12f,.7f,.8f,1f,0f});
        var reflectionType=assembly.GetType("DynamicSky.SkyReflection",true);
        var reflection=Activator.CreateInstance(reflectionType,Instance,null,new[]{state},null);
        var warm=reflectionType.GetMethod("UpdateClouds",Instance);
        var cloudOpacity=model.GetMethod("CloudOpacity",Static);
        Set(state,"Phase",.9f);
        var samples=(List<int>)Field(reflection,"cloudSamples");
        var directions=(Vector3[])Field(reflection,"directions");
        var original=Field(reflection,"cloudOpacity");
        var clock=System.Diagnostics.Stopwatch.StartNew();
        double slowestBatch=0;
        bool complete=false;
        int batches=0; 
        float now=Time.realtimeSinceStartup+1;
        while(!complete && batches<100)
        {
            clock.Restart();
            complete=(bool)warm.Invoke(reflection,new[]{state,(object)(now+batches/120f)});
            clock.Stop();
            slowestBatch=Math.Max(slowestBatch,clock.Elapsed.TotalMilliseconds);
            if(!complete)
            {
                Check((int)Field(reflection,"cloudCursor")<=(++batches)*64,"Cloud batch exceeded its work budget");
                Check(ReferenceEquals(original,Field(reflection,"cloudOpacity")),"Partial cloud mask was published");
            }
        }
        Check(complete && batches>1 && !ReferenceEquals(original,Field(reflection,"cloudOpacity")),"Full cloud mask was not published");
        var mask=(float[])Field(reflection,"cloudOpacity");
        foreach(int i in samples)
            Check(mask[i]==(float)cloudOpacity.Invoke(null,new[]{(object)directions[i],state}),"Batched mask differs from direct evaluation");

        Set(reflection,"nextColorUpload",0f);
        Set(state,"Exposure",.8f);
        for(int face=1;face<=6;face++)
        {
            Call(reflection,"Update",state,false);
            Check((int)Field(reflection,"colorFace")== (face==6?-1:face),"Cubemap did not process one face per frame");
        }

        // Compare the same work done in one pass and in batches using Unity's Mono runtime.
        var fullTimings=new List<double>();
        for(int i=0;i<12;i++)
        {
            Set(state,"CloudDirection",i*.1f);
            Set(reflection,"initialized",false);
            clock.Restart();Call(reflection,"Update",state,true);clock.Stop();
            fullTimings.Add(clock.Elapsed.TotalMilliseconds);
        }
        fullTimings.Sort();

        Set(state,"CloudDirection",1.4f);
        Set(reflection,"nextColorUpload",0f);
        double batchedMax=0;
        for(int i=0;i<batches+4;i++)
        {
            clock.Restart();Call(reflection,"Update",state,false);clock.Stop();
            batchedMax=Math.Max(batchedMax,clock.Elapsed.TotalMilliseconds);
        }
        Debug.LogFormat("DYNAMIC_SKY_REFLECTION_BENCH full_cubemap_median_ms={0:F2} batch_max_ms={1:F2} mask_batch_max_ms={2:F2} mask_frames={3}",
            fullTimings[6],batchedMax,slowestBatch,batches+1);
        Call(reflection,"Destroy");
    }

    private static void ProbePresets(Assembly assembly)
    {
        var optionsType=assembly.GetType("DynamicSky.Options",true);
        string prefix="DynamicSky.Presets.Probe."+Guid.NewGuid().ToString("N")+".";
        var options=Activator.CreateInstance(optionsType,Instance,null,new object[]{null,prefix},null);
        var folder=Path.Combine(Path.GetTempPath(),"DynamicSky-presets-"+Guid.NewGuid().ToString("N"));
        var store=Activator.CreateInstance(assembly.GetType("DynamicSky.SkyPresets",true),Instance,null,new object[]{folder},null);
        Func<Dictionary<string,string>> capture=()=>((IEnumerable<KeyValuePair<string,string>>)Call(options,"Capture"))
            .ToDictionary(pair=>pair.Key,pair=>pair.Value);
        try
        {
            var embedded=assembly.GetManifestResourceNames().Where(resource=>resource.StartsWith("DynamicSky.Presets/",StringComparison.Ordinal)).ToArray();
            var standard=(IList)store.GetType().GetMethod("LoadStandard",Static).Invoke(null,new object[]{new Action<string,Exception>((resourcePath,error)=>{throw new Exception(resourcePath,error);})});
            Check(standard.Count==embedded.Length && standard.Count>=9,"Some standards were not embedded");
            for(int i=0;i<standard.Count;i++)
            {
                if(i>0)Check((int)Field(standard[i-1],"Order")<=(int)Field(standard[i],"Order"),"Standard preset sort order");
                Check(!string.IsNullOrWhiteSpace((string)Field(standard[i],"RussianName")) &&
                    !string.IsNullOrWhiteSpace((string)Field(standard[i],"Name")),"Missing localized preset name");
            }
            var defaults=capture();
            var defaultPreset=standard.Cast<object>().Single(item=>(string)Field(item,"Name")=="Default");
            var bright=standard.Cast<object>().Single(item=>(string)Field(item,"Name")=="Bright");
            Check((bool)Call(options,"ApplyPreset",Field(bright,"Values")) &&
                capture()["Clouds.Coverage"]!=defaults["Clouds.Coverage"],"Embedded preset values were not applied");
            Check((bool)Call(options,"ApplyPreset",Field(defaultPreset,"Values")) && capture().SequenceEqual(defaults),
                "Embedded default differs from clean defaults");
            var expected=new Dictionary<string,float> {
                {"Clouds.Coverage",.4f},{"Clouds.Density",.8f},{"Night.Ambient",1.5f},{"Night.Exposure",1.5f},
                {"Night.StarsBrightness",1.5f},{"Night.Bloom",2f},{"Twilight.DawnBrightness",.9f},{"Day.DawnPink",.1f},
                {"Day.TwilightWarmth",1.1f},{"Twilight.Sky",1.35f},{"Twilight.Depth",2f},{"Lights.StreetBrightness",.8f},
                {"Lights.StreetYellow",.1f},{"Lights.HeadlightBrightness",.75f},{"Lights.HeadlightYellow",.1f},
                {"Shadows.DayStrength",.8f},{"Shadows.NightStrength",.8f},{"Fog.Amount",1f},{"Fog.Visibility",1800},
                {"Fog.Start",.06f},{"Sun.Diameter",5.5f},{"Sun.Softness",.75f},{"Sun.Yellow",.1f},{"Sun.North",90f},
                {"Sun.Latitude",40f},{"Sun.Declination",20f},{"Sun.ShaftsIntensity",.9f},{"Sun.ShaftsLength",.7f},
                {"Moon.Diameter",5.5f},{"Moon.Softness",.4f},{"Moon.Yellow",.2f},{"Moon.DiscBrightness",1.4f},
                {"Moon.Shafts",.5f},{"Moon.ShaftsLength",.95f}
            };
            foreach(var pair in expected)
                Check(float.Parse(defaults[pair.Key],System.Globalization.CultureInfo.InvariantCulture)==pair.Value,"Wrong fresh default: "+pair.Key);

            Check(!defaults.Keys.Any(key=>key.StartsWith("Time.",StringComparison.Ordinal)), "Preset includes time settings");
            Value(options,"Paused",true);Value(options,"StartHour",18.25f);Value(options,"DayMinutes",75f);
            Value(options,"CloudCoverage",.95f);Value(options,"Enabled",false);
            var custom=capture();
            const string name="Закат у воды";
            Check(Call(store,"Save",name,custom).ToString()=="Saved","Preset was not saved");
            var path=Path.Combine(folder,name+".cfg");
            Check(File.Exists(path) && File.ReadAllText(path).Contains("name = "+name),"Preset path or name is incorrect");
            Check(Call(store,"Save",name,defaults).ToString()=="Exists" &&
                  Call(store,"Save","../invalid",defaults).ToString()=="InvalidName","Preset can overwrite or escape its directory");
            File.Move(path,Path.Combine(folder,"renamed.cfg"));
            File.WriteAllText(Path.Combine(folder,"broken.cfg"),"Clouds.Coverage = 1");
            int errors=0;
            var entries=(IList)Call(store,"LoadSaved",new Action<string,Exception>((_,error)=>errors++));
            Check(entries.Count==1 && errors==1 && (string)Field(entries[0],"Name")==name,"Listing must read the name field and skip broken files");

            Check((bool)Call(options,"ApplyPreset",new object[]{null}),"Standard preset did not apply");
            Check(capture().SequenceEqual(defaults) && !(bool)Field(options,"Enabled"),"Standard preset differs from first-run settings");
            var loaded=Field(entries[0],"Values");
            var oldValues=((IReadOnlyDictionary<string,string>)loaded).ToDictionary(pair=>pair.Key,pair=>pair.Value);
            oldValues.Add("Time.StartHour","6");
            oldValues.Add("Time.DayMinutes","30");
            oldValues.Add("Time.Paused","false");
            Check((bool)Call(options,"ApplyPreset",oldValues) && capture().SequenceEqual(custom),"Saved preset does not round-trip");
            Check(PlayerPrefs.GetFloat(prefix+"Time.StartHour")==4.5f,"Options were persisted before the caller finished applying");
            Call(options,"Save");
            Check(PlayerPrefs.GetFloat(prefix+"Time.StartHour")==18.25f && PlayerPrefs.GetFloat(prefix+"Time.DayMinutes")==75f &&
                PlayerPrefs.GetInt(prefix+"Time.Paused")==1,"Applying a preset changed the time settings");
            var malformed=new Dictionary<string,string>{{"Clouds.Coverage","0.01"},{"Moon.Diameter","NaN"}};
            Check(!(bool)Call(options,"ApplyPreset",malformed) && capture().SequenceEqual(custom),"Invalid preset partially changed settings");
            var renamed=Path.Combine(folder,"renamed.cfg");
            var oldContents=File.ReadAllText(renamed);
            File.AppendAllText(renamed,"# edited");
            Check(!(bool)Call(store,"Delete",entries[0]) && File.Exists(renamed),"Confirmed deletion used an outdated file");
            File.WriteAllText(renamed,oldContents);
            Check((bool)Call(store,"Delete",entries[0]) && !File.Exists(renamed),"Deletion did not use the actual filename");
            Check(!(bool)Call(store,"Delete",standard[0]),"Embedded preset was deletable");
            Reset(options,"Clouds");
            Check(capture()["Clouds.Coverage"]==defaults["Clouds.Coverage"],"Section reset differs from the standard preset");

            var localization=Assembly.Load("Assembly-CSharp").GetType("LocalizationManager",true);
            var languageType=localization.GetNestedType("Lang");

            var manager=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(localization);
            var singleton=localization.GetField("me",Static);
            var old=singleton.GetValue(null);
            try
            {
                singleton.SetValue(null,manager);
                var language=Activator.CreateInstance(languageType);
                var current=localization.GetField("curLang",Instance); 
                if(current!=null)current.SetValue(manager,language); 
                else localization.GetProperty("curLang",Instance).SetValue(manager,language,null);
                var text=assembly.GetType("DynamicSky.SkyText",true);
                var display=text.GetProperty("Standard",Static);
                var displayPreset=bright.GetType().GetProperty("DisplayName",Instance);
                Set(language,"lang","en");Check((string)display.GetValue(null,null)=="Standard" &&
                    (string)displayPreset.GetValue(bright,null)=="Bright","English UI or embedded localization");
                Set(language,"lang","ru");Check((string)display.GetValue(null,null)=="Стандартные" &&
                    (string)displayPreset.GetValue(bright,null)=="Яркий","Russian UI or embedded localization");
            }
            finally
            {
                singleton.SetValue(null,old);

            }
        }
        finally
        {
            if(Directory.Exists(folder))Directory.Delete(folder,true);
            foreach(var item in (IEnumerable)Field(options,"floats"))PlayerPrefs.DeleteKey((string)Field(item,"Key"));
            foreach(string key in new[]{"General.Enabled","General.AutoStart","Time.Paused","Version"})PlayerPrefs.DeleteKey(prefix+key);
            PlayerPrefs.Save();
        }
    }

    private static void ProbeTheme(Assembly plugin)
    {
        var themeType=plugin.GetType("DynamicSky.SkyTheme",true);
        var theme=Activator.CreateInstance(themeType);
        var get=themeType.GetMethod("Accent",Static);
        Func<float,Color> accent=hour=>(Color)get.Invoke(null,new object[]{hour,0f,0f});
        Check(accent(0)==accent(24),"Accent jumps at midnight");
        Check(accent(8)==Color.Lerp(accent(4.5f),accent(12),Mathf.SmoothStep(0,1,3.5f/7.5f)),"Accent interpolation");
        Check((Color)get.Invoke(null,new object[]{4.5f,1f,0f})!=accent(4.5f),"Rose dawn leaves the accent unchanged");
        Call(theme,"Update",4.5f,0f,0f);
        var legacy=Field(theme,"Theme");
        var textures=legacy.GetType().GetProperty("Textures").GetValue(legacy,null);
        var border=(Texture2D)textures.GetType().GetProperty("Border").GetValue(textures,null);
        Call(theme,"Update",12f,0f,0f);
        var expected=QualitySettings.activeColorSpace==ColorSpace.Linear?accent(12).linear:accent(12);
        Check(Mathf.Abs(ReadTexture(border)[0].r-expected.r)<.025f,"Theme border did not receive the interpolated color");
        Check(ReferenceEquals(textures,legacy.GetType().GetProperty("Textures").GetValue(legacy,null)),"Theme recreated its textures");
        Call(theme,"Destroy");
    }

    private static void ProbeSunEffects(Assembly game,object runtime,object options)
    {
        var camType=game.GetType("Cam",true);
        var singleton=camType.GetField("me",Static);
        var root=new GameObject("Solar Effects Probe");root.SetActive(false);
        var camera=root.AddComponent<Camera>();
        var gameCam=root.AddComponent(camType);
        singleton.SetValue(null,gameCam);
        Set(gameCam,"unityCam",camera);
        camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;
        camera.transform.position=new Vector3(0,6,-15);
        Value(options,"MoonBrightness",1f);Value(options,"MoonShafts",0f);Value(options,"ShaftsIntensity",0f);
        Value(options,"CloudCoverage",0f);Value(options,"SunDiameter",3f);
        Value(options,"SunYellow",.8f);
        Call(runtime,"SetHour",19.5f);
        var direction=(Vector3)Field(Field(runtime,"State"),"SunDirection");
        camera.transform.rotation=Quaternion.LookRotation(direction);
        var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position=camera.transform.position+direction*45+camera.transform.right*3;
        obstacle.transform.rotation=camera.transform.rotation;
        obstacle.transform.localScale=new Vector3(5,15,4);
        var target=new RenderTexture(640,360,24,RenderTextureFormat.ARGBHalf);
        camera.targetTexture=target;
        root.SetActive(true);
        var originalDepth=camera.depthTextureMode;
        camera.Render();
        var off=ReadTexture(target);
        var discPixel=off[180*640+320];
        Check(discPixel.r>discPixel.b*1.8f,"The yellow solar disk clips to white");

        Value(options,"ShaftsIntensity",1f);Value(options,"ShaftsLength",.75f);
        Call(runtime,"Apply",true);
        var shafts=(Behaviour)Field(runtime,"shafts");
        Check(shafts && shafts.enabled,"Rays were not attached to the game camera");
        camera.Render();
        Check(camera.depthTextureMode==originalDepth,"Rays did not restore camera depth flags");
        var on=ReadTexture(target,"../previews/shafts.png");
        float difference=0;
        for(int i=0;i<off.Length;i++)difference+=Mathf.Abs(on[i].grayscale-off[i].grayscale);
        Check(difference/off.Length>.001f,"Rays do not affect the rendered image");
        obstacle.transform.localScale=new Vector3(100,100,4);
        camera.Render();
        var occluded=ReadTexture(target);
        Value(options,"ShaftsIntensity",0f);Call(runtime,"Apply",true);
        camera.Render();
        var occludedOff=ReadTexture(target);
        float occludedDifference=0;
        for(int i=0;i<off.Length;i++)occludedDifference+=Mathf.Abs(occluded[i].grayscale-occludedOff[i].grayscale);
        Check(occludedDifference/off.Length<.0005f,"Rays leak through a fully occluded sun");

        Value(options,"FlareIntensity",1f);Call(runtime,"Apply",true);
        camera.Render();
        Check(FrameDifference(occludedOff,ReadTexture(target))<.0005f,"Lens flare leaks through a roof");
        obstacle.SetActive(false);
        camera.transform.rotation=Quaternion.LookRotation(Quaternion.Euler(0,12,0)*direction);
        camera.Render();var flareOn=ReadTexture(target,"../previews/lens-flare.png");
        Value(options,"FlareIntensity",0f);Call(runtime,"Apply",true);
        camera.Render();var flareOff=ReadTexture(target);
        Check(FrameDifference(flareOff,flareOn)>.002f,"Lens flare does not render by itself");
        Check(camera.depthTextureMode==originalDepth,"Lens flare leaked depth settings");
        Value(options,"FlareIntensity",1f);Value(options,"ShaftsIntensity",1f);Call(runtime,"SetHour",0f);
        Check(!shafts.enabled,"Solar effects remain active at night");
        // The moon uses the same depth-aware effect at a deliberately lower intensity.
        obstacle.SetActive(false);
        Value(options,"MoonShafts",0f);Call(runtime,"Apply",true);
        var moonDirection=(Vector3)Field(Field(runtime,"State"),"MoonDirection");
        camera.transform.rotation=Quaternion.LookRotation(moonDirection);
        camera.Render();var moonOff=ReadTexture(target);
        Value(options,"MoonShafts",1f);Call(runtime,"Apply",true);
        var lunar=(Behaviour)Field(runtime,"moonShafts");
        Check(lunar && lunar.enabled,"Lunar shafts not attached at night");
        camera.Render();var moonOn=ReadTexture(target,"../previews/moon-rays.png");
        float moonDifference=0;
        for(int i=0;i<moonOff.Length;i++)moonDifference+=Mathf.Abs(moonOn[i].grayscale-moonOff[i].grayscale);
        Check(moonDifference/moonOff.Length>.0001f && (float)Field(lunar,"strength")<.2f,"Moon rays should be visible with reduced strength: "+moonDifference/moonOff.Length);
        Check(camera.depthTextureMode==originalDepth,"Moon rays leaked camera depth flags");
        obstacle.SetActive(true);obstacle.transform.position=camera.transform.position+moonDirection*45;
        obstacle.transform.rotation=camera.transform.rotation;
        camera.Render();var moonOccluded=ReadTexture(target);
        Value(options,"MoonShafts",0f);Call(runtime,"Apply",true);camera.Render();var moonOccludedOff=ReadTexture(target);
        float moonOcclusion=0;for(int i=0;i<moonOff.Length;i++)moonOcclusion+=Mathf.Abs(moonOccluded[i].grayscale-moonOccludedOff[i].grayscale);
        Check(moonOcclusion/moonOff.Length<.0005f,"Moon rays leak through geometry");
        Reset(options,"Celestial");
        Value(options,"ShaftsIntensity",0f);Value(options,"FlareIntensity",0f);
        Reset(options,"Celestial");Value(options,"CloudCoverage",.35f);
        singleton.SetValue(null,null);
        Call(runtime,"Apply",true);
        Check(!shafts && !lunar,"Old camera retained a rays component");
        Object.DestroyImmediate(obstacle);
        camera.targetTexture=null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(root);
    }

    private static float FrameDifference(Color[] a,Color[] b)
    {
        float sum=0;
        for(int i=0;i<a.Length;i++)sum+=Mathf.Abs(a[i].grayscale-b[i].grayscale);
        return sum/a.Length;
    }

    private static Color[] ReadTexture(Texture source,string path=null)
    {
        var previous=RenderTexture.active;
        var target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBHalf);
        Graphics.Blit(source,target);
        RenderTexture.active=target;
        var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
        image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);
        var colors=image.GetPixels();
        if(path!=null)
        {
            var png=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
            png.SetPixels(colors);png.Apply();
            File.WriteAllBytes(path,png.EncodeToPNG());
            Object.DestroyImmediate(png);
        }
        Object.DestroyImmediate(image);
        RenderTexture.active=previous;
        RenderTexture.ReleaseTemporary(target);
        return colors;
    }
    private static GameObject ProbeBloom(Assembly game,Assembly plugin,object runtime,object options)
    {
        var root=new GameObject("Bloom Probe");
        root.SetActive(false);
        var camera=root.AddComponent<Camera>();
        var camType=game.GetType("Cam",true);
        var cam=root.AddComponent(camType);
        var singleton=camType.GetField("me",Static);
        singleton.SetValue(null,cam);
        Set(cam,"unityCam",camera);
        var firstpass=Assembly.Load("Assembly-CSharp-firstpass");
        var postType=firstpass.GetType("UnityEngine.PostProcessing.PostProcessingBehaviour",true);
        var profileType=firstpass.GetType("UnityEngine.PostProcessing.PostProcessingProfile",true);
        var post=root.AddComponent(postType);
        var profile=ScriptableObject.CreateInstance(profileType);
        Set(post,"profile",profile);
        var bloom=Field(profile,"bloom");
        var enabled=bloom.GetType().GetProperty("enabled");
        enabled.SetValue(bloom,true,null);
        var settings=bloom.GetType().GetProperty("settings");
        var original=settings.GetValue(bloom,null);
        Func<float> intensity=()=>
        {
            var inner=Field(settings.GetValue(bloom,null),"bloom");
            return (float)Field(inner,"intensity");
        };
        float baseline=intensity();
        Check(baseline>0,"Bloom test has no baseline");
        Value(options,"DayBloom",2f);Call(runtime,"Apply",true);
        var patch=plugin.GetType("DynamicSky.BloomPatch",true);
        var prefix=patch.GetMethod("Prefix",Static);
        var finalizer=patch.GetMethod("Finalizer",Static);
        var args=new object[]{post,null};
        prefix.Invoke(null,args);
        Check(Mathf.Abs(intensity()-2*baseline)<.0001f,"Day Bloom multiplier");
        finalizer.Invoke(null,new[]{args[1],null});
        Check(settings.GetValue(bloom,null).Equals(original),"Bloom profile was not restored");
        Call(runtime,"SetHour",0f);
        Value(options,"NightBloom",0f);Call(runtime,"Apply",true);
        var eye=Field(profile,"eyeAdaptation");
        eye.GetType().GetProperty("enabled").SetValue(eye,true,null);
        var eyeSettings=eye.GetType().GetProperty("settings");
        var originalEye=eyeSettings.GetValue(eye,null);
        prefix.Invoke(null,args);
        Check(intensity()==0,"Night Bloom control");
        Check(Mathf.Abs((float)Field(eyeSettings.GetValue(eye,null),"minLuminance")-.7f)<.001f,"Night exposure floor");
        finalizer.Invoke(null,new[]{args[1],null});
        Check(eyeSettings.GetValue(eye,null).Equals(originalEye),"Night exposure settings leaked into the game profile");
        enabled.SetValue(bloom,false,null);
        prefix.Invoke(null,args);
        Check(intensity()==baseline && !(bool)enabled.GetValue(bloom,null),"Bloom ignores the game's graphics setting");
        Value(options,"DayBloom",1f);Value(options,"NightBloom",1f);Call(runtime,"SetHour",12f);
        singleton.SetValue(null,null);
        Object.DestroyImmediate(profile);
        return root;
    }
    private static bool SkipDisable(Component __instance) { return __instance.name=="Dynamic Sky"; }
    private static void CreatePreviewWorld()
    {
        var material=new Material(Shader.Find("Standard")) { color=new Color(.53f,.55f,.54f) };
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.position=new Vector3(0,-.05f,0);floor.transform.localScale=Vector3.one*35;floor.GetComponent<Renderer>().sharedMaterial=material;
        for(int i=0;i<7;i++)
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.position=new Vector3((i-3)*18,9+(i%3)*4,90+(i%2)*25);
            cube.transform.localScale=new Vector3(10,18+(i%3)*8,14);cube.GetComponent<Renderer>().sharedMaterial=material;
        }
    }
    private static void Preview(string path,float hour)
    {
        var root=new GameObject("Sky Preview Camera");var camera=root.AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.Skybox;camera.fieldOfView=72;camera.farClipPlane=1500;camera.allowHDR=true;
        camera.transform.position=new Vector3(0,4,-15);camera.transform.rotation=Quaternion.Euler(-12,0,0);
        var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;
        Object.DestroyImmediate(image);Object.DestroyImmediate(target);Object.DestroyImmediate(root);
    }
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {
        var name=new AssemblyName(args.Name).Name+".dll";
        foreach(var directory in new[]{Game+"/game_Data/Managed",Game+"/BepInEx/core",Game+"/BepInEx/plugins/BigCityLegacy"})
            if(File.Exists(directory+"/"+name))return Assembly.LoadFile(directory+"/"+name);
        return null;
    }
}






