using System;
using BepInEx;
using BepInEx.Logging;
using BigCityLegacy.UI;
using BigCityLegacy.UI.Blur;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicSky
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.bigcitylegacy.baseplugin", "1.4.2")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string PluginVersion = "0.7.1";
        public const string PluginGuid = "com.bigcitylegacy.fusionsky";
        public const string PluginName = "FusionSky";

        internal static SkyRuntime Runtime;
        internal static ManualLogSource Log;
        private static readonly System.Reflection.FieldInfo MiddleMouseHidden=AccessTools.Field(typeof(hCursor),"hiddentByMiddleMouse");
        private enum Tab { Time, Clouds, Presets, Advanced }
        private static string[] Tabs => new[]{SkyText.Time,SkyText.Clouds,SkyText.Presets,SkyText.Advanced};
        private static string[] AdvancedTabs => new[]{SkyText.Light,SkyText.Fog,SkyText.SunMoon,SkyText.StreetAndCar,SkyText.Shadows};
        private static readonly Options.Section[] AdvancedSections={
            Options.Section.Lighting,Options.Section.Fog,Options.Section.Celestial,Options.Section.StreetLights,Options.Section.Shadows
        };
        private Harmony harmony;
        private bool windowOpen,cursorWasVisible,cursorWasHiddenByMiddleMouse;
        private CursorLockMode previousCursorLock;
        private LegacyUIWindow window;
        private LegacyUIScrollView scroll;
        private LegacyBclBlur backdrop;
        private readonly LegacyUIAnimationSettings animations=new LegacyUIAnimationSettings();
        private readonly SkyTheme theme=new SkyTheme();
        private Tab tab;
        private int advancedTab;
        private bool supported;
        private void Awake()
        {
            Log=Logger;
            if(IsDedicated() || SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) { enabled=false;return; }
            if(Application.version.Trim()!="9.4") { Logger.LogWarning($"{PluginName} requires MadOut2 PC 9.4");enabled=false;return; }
            try
            {
                Runtime = new SkyRuntime(new Options(Config));
                LegacyInput.Init();
                backdrop=LegacyBclBlur.Create(gameObject);
                window = new LegacyUIWindow(SkyText.Title, new Rect(36,120,560,650), new LegacyUIWindowOptions
                {
                    ShowCloseButton=true,
                    RightHint=Options.MenuShortcut,
                    PlayerPrefsKey="FusionSky.Window",
                    BackdropBlur=true,
                    InputBlockMode=LegacyUIInputBlockMode.Window,
                    Theme=theme.Theme
                });
                window.Visible=false;
                window.Closed += () => SetWindow(false);
                scroll = new LegacyUIScrollView(new Rect()) { ShowHorizontalScrollbar=false, DrawBackground=false };
                harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                supported = true;
                Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. " + Options.MenuShortcut + " opens environment controls.");
            }
            catch(Exception error) { Cleanup();enabled=false;Logger.LogError(error); }
        }
        internal static bool IsDedicated()
        {
            foreach(var arg in Environment.GetCommandLineArgs())
                if(arg.IndexOf("bend_GameServer",StringComparison.OrdinalIgnoreCase)>=0 || arg.Equals("-server",StringComparison.OrdinalIgnoreCase) || arg.Equals("-dedicated",StringComparison.OrdinalIgnoreCase))return true;
            return false;
        }
        private void Update()
        {
            if(!supported || Runtime==null)return;
            try
            {
                if(AltHeld && Input.GetKeyDown(KeyCode.G) && !NetChat.nowTyping && LegacyHelpers.IsGameplayRunning && hVR.me && hVR.me.cursor)SetWindow(!windowOpen);
                if(windowOpen && (Input.GetKeyDown(KeyCode.Escape) || !LegacyHelpers.IsGameplayRunning || !hVR.me || !hVR.me.cursor))SetWindow(false);
                Runtime.Tick();
                TickSavedPresets();
            }
            catch(Exception error)
            {
                Logger.LogError($"{PluginName} stopped; restoring previous weather: " + error);
                Cleanup();enabled=false;
            }
        }
        internal static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        private void SetWindow(bool value)
        {
            if(windowOpen==value)return;
            windowOpen=value;
            window.Visible=value;
            if(value)
            {
                // The framework loads saved visibility on its first draw.
                PlayerPrefs.SetInt(window.Options.PlayerPrefsKey+".visible",1);
                OpenCursor();
            }
            else
            {
                RestoreCursor();
                Runtime?.Options.Save();
                window.SavePrefs();
            }
        }

        private void OpenCursor()
        {
            cursorWasVisible=Cursor.visible;
            previousCursorLock=Cursor.lockState;
            cursorWasHiddenByMiddleMouse=(bool)MiddleMouseHidden.GetValue(null);
            MiddleMouseHidden.SetValue(null,false);
            hCursor.CheckState();
        }

        private void RestoreCursor()
        {
            MiddleMouseHidden.SetValue(null,cursorWasHiddenByMiddleMouse);
            if(hVR.me && hVR.me.cursor) hCursor.CheckState();
            else
            {
                Cursor.lockState=previousCursorLock;
                Cursor.visible=cursorWasVisible;
            }
        }

        private void OnGUI()
        {
            if(window==null || !window.IsRendered || Runtime==null)return;
            using(new LegacyUIGuiScope(-18000,null,animations,backdrop))
            {
                var screen=LegacyUI.VirtualScreenSize;
                var rect=window.Rect;rect.width=Mathf.Min(560,screen.x-16);rect.height=Mathf.Min(650,screen.y-20);window.Rect=rect;
                if(Event.current.type==EventType.Repaint)theme.Update((float)Runtime.Clock.Hours,Runtime.Options.DawnPink.Value,Runtime.Options.DuskPink.Value);
                GUI.enabled=Cursor.visible && Cursor.lockState!=CursorLockMode.Locked;
                window.Options.InputBlockMode=GUI.enabled?LegacyUIInputBlockMode.Window:LegacyUIInputBlockMode.None;
                window.Title = SkyText.Title;
                window.Draw(DrawWindow);
            }
        }
        private void DrawWindow(Rect area)
        {
            bool enabled = LegacyUI.Toggle(new Rect(area.x,area.y,area.width,25), Runtime.Options.Enabled, SkyText.Enable);
            if (enabled != Runtime.Options.Enabled)
            {
                Runtime.Options.Enabled=enabled;
                Runtime.Options.Dirty();
                Runtime.Tick();
                if (enabled) Runtime.SelectDynamic();
            }
            int selected = DrawTabs(area,33,Tabs,(int)tab);

            if (selected != (int)tab)
            {
                tab = (Tab) selected;
                scroll.ResetScroll();
            }

            float offset=72;
            if (tab == Tab.Advanced)
            {
                selected=DrawTabs(area,66,AdvancedTabs,advancedTab);
                if(selected!=advancedTab) { advancedTab=selected;scroll.ResetScroll(); }
                offset=105;
            }

            if (tab == Tab.Presets)
            {
                offset = DrawPresetPanel(area);
            }
            scroll.ViewRect=new Rect(area.x,area.y+offset,area.width,Mathf.Max(0,area.height-offset-34));
            if (tab == Tab.Presets) scroll.Draw(DrawPresets);
            else scroll.Draw(DrawControls);
            LegacyUI.MiniHint(new Rect(area.x, area.yMax - 25, area.width, 22), SkyText.Footer);
        }
        private static int DrawTabs(Rect area,float offset,string[] labels,int selected)
        {
            float width=(area.width-4*(labels.Length-1))/labels.Length;
            for(int i=0;i<labels.Length;i++)
                if(LegacyUI.TabButton(new Rect(area.x+i*(width+4),area.y+offset,width,29),labels[i],selected==i))return i;
            return selected;
        }

        private void DrawControls(Rect content)
        {
            var options=Runtime.Options;var layout=new LegacyUILayout(content,7);
            bool oldEnabled=GUI.enabled;GUI.enabled=oldEnabled && options.Enabled;
            var section=tab==Tab.Advanced?AdvancedSections[advancedTab]:(Options.Section)tab;
            if(section==Options.Section.Time)
            {
                int hours=(int)Runtime.Clock.Hours,minutes=(int)((Runtime.Clock.Hours-hours)*60);
                LegacyUI.Title(layout.Row(26),SkyText.FormatTime(hours,minutes));
                float hour=LegacyUI.HorizontalSlider(layout.Row(22),(float)Runtime.Clock.Hours,0,23.999f);
                if(Mathf.Abs(hour-(float)Runtime.Clock.Hours)>.002f) { Activate();Runtime.SetHour(hour); }
                var row=layout.Row(28);string[] names={SkyText.Dawn,SkyText.Day,SkyText.Dusk,SkyText.Night};float[] times={5.6f,12,20.5f,1};
                for(int i=0;i<4;i++)if(LegacyUI.Button(new Rect(row.x+i*(row.width+4)/4,row.y,(row.width-12)/4,28),names[i])) { Activate();Runtime.SetHour(times[i]); }
                bool paused=LegacyUI.Toggle(layout.Row(25),options.Paused,SkyText.Pause);
                if(paused!=options.Paused) { options.Paused=paused;Changed(); }
                Slider(ref layout,options.DayMinutes,SkyText.DayLength,SkyText.Minutes);
            }
            else if(section==Options.Section.Clouds)
            {
                Slider(ref layout,options.CloudCoverage,SkyText.Coverage,"%",100);
                Slider(ref layout,options.CloudDensity,SkyText.Density,"%",100);
                Slider(ref layout,options.CloudScale,SkyText.CloudSize,"%",100);
                Slider(ref layout,options.CloudSpeed,SkyText.CloudSpeed,"%",100);
                Slider(ref layout,options.CloudDirection,SkyText.CloudDirection,"°");
            }
            else if(section==Options.Section.Lighting)
            {
                LegacyUI.Title(layout.Row(26),SkyText.Day);
                Slider(ref layout,options.SunBrightness,SkyText.SunBrightness,"%",100);
                Slider(ref layout,options.DayAmbient,SkyText.Ambient,"%",100);
                Slider(ref layout,options.DayExposure,SkyText.SkyBrightness,"%",100);
                Slider(ref layout,options.DayReflection,SkyText.Reflections,"%",100);
                Slider(ref layout,options.DayTemperature,SkyText.Temperature,"%",100,"+0;-0;0");
                Slider(ref layout,options.DaySaturation,SkyText.DaySaturation,"%",100);
                Slider(ref layout,options.DayBloom,SkyText.Bloom,"%",100);
                layout.Space(10);
                LegacyUI.Title(layout.Row(26),SkyText.Night);
                Slider(ref layout,options.NightAmbient,SkyText.NightBrightness,"%",100);
                Slider(ref layout,options.NightExposure,SkyText.SkyBrightness,"%",100);
                Slider(ref layout,options.NightReflection,SkyText.Reflections,"%",100);
                Slider(ref layout,options.NightTemperature,SkyText.Temperature,"%",100,"+0;-0;0");
                Slider(ref layout,options.StarsBrightness,SkyText.Stars,"%",100);
                Slider(ref layout,options.NightBloom,SkyText.Bloom,"%",100);
                layout.Space(10);
                LegacyUI.Title(layout.Row(26),SkyText.Twilight);
                Slider(ref layout,options.DawnBrightness,SkyText.DawnBrightness,"%",100);
                Slider(ref layout,options.DawnPink,SkyText.DawnTint,"%",100);
                Slider(ref layout,options.DuskBrightness,SkyText.DuskBrightness,"%",100);
                Slider(ref layout,options.DuskPink,SkyText.DuskTint,"%",100);
                Slider(ref layout,options.TwilightWarmth,SkyText.TwilightSaturation,"%",100);
                Slider(ref layout,options.TwilightAmbient,SkyText.Diffuse,"%",100);
                Slider(ref layout,options.TwilightSky,SkyText.SkyBrightness,"%",100);
                Slider(ref layout,options.TwilightDepth,SkyText.SkyDepth,"%",100);
            }
            else if(section==Options.Section.Fog)
            {
                Slider(ref layout,options.FogAmount,SkyText.FogAmount,"%",100);
                Slider(ref layout,options.Visibility,SkyText.FogDistance,SkyText.Metres);
                Slider(ref layout,options.FogStart,SkyText.FogStart,SkyText.DistancePercent,100);
                LegacyUI.MiniHint(layout.Row(46),SkyText.FogHint);
            }
            else if(section==Options.Section.Celestial)
            {
                Slider(ref layout, options.North, SkyText.North, "°");
                Slider(ref layout, options.Latitude, SkyText.Latitude, "°");
                Slider(ref layout, options.Declination, SkyText.Declination, "°");
                LegacyUI.Title(layout.Row(26),SkyText.Sun);
                Slider(ref layout,options.SunDiameter,SkyText.SunDiameter,"°",1,"0.0");
                Slider(ref layout,options.SunSoftness,SkyText.Softness,"%",100);
                Slider(ref layout,options.SunYellow,SkyText.SunYellow,"%",200);
                Slider(ref layout,options.ShaftsIntensity,SkyText.SunRays,"%",100);
                Slider(ref layout,options.ShaftsLength,SkyText.RayLength,"%",100);
                Slider(ref layout,options.FlareIntensity,SkyText.Flare,"%",100);
                layout.Space(10);
                LegacyUI.Title(layout.Row(26),SkyText.Moon);
                Slider(ref layout,options.MoonDiameter,SkyText.MoonDiameter,"°",1,"0.0");
                Slider(ref layout,options.MoonSoftness,SkyText.Softness,"%",100);
                Slider(ref layout,options.MoonYellow,SkyText.MoonTint,"%",100);
                Slider(ref layout,options.MoonBrightness,SkyText.MoonBrightness,"%",100);
                Slider(ref layout,options.MoonShafts,SkyText.MoonRays,"%",100);
                Slider(ref layout,options.MoonShaftsLength,SkyText.RayLength,"%",100);
            }
            else if(section==Options.Section.StreetLights)
            {
                LegacyUI.Title(layout.Row(26),SkyText.StreetLights);
                Slider(ref layout,options.StreetBrightness,SkyText.Brightness,"%",100);
                Slider(ref layout,options.StreetYellow,SkyText.YellowTint,"%",100);
                layout.Space(10);
                LegacyUI.Title(layout.Row(26),SkyText.Headlights);
                Slider(ref layout,options.HeadlightBrightness,SkyText.Brightness,"%",100);
                Slider(ref layout,options.HeadlightYellow,SkyText.YellowTint,"%",100);
            }
            else if(section==Options.Section.Shadows)
            {
                Slider(ref layout,options.DayShadowStrength,SkyText.DayShadows,"%",100);
                Slider(ref layout,options.NightShadowStrength,SkyText.NightShadows,"%",100);
                Slider(ref layout,options.ShadowFill,SkyText.ShadowFill,"%",100);
                Slider(ref layout,options.ShadowDistance,SkyText.ShadowDistance,SkyText.Metres);
                bool soft=LegacyUI.Toggle(layout.Row(25),options.ShadowSoftness.Value>=.5f,SkyText.SoftShadows);
                if(soft!=(options.ShadowSoftness.Value>=.5f)) { options.Change(options.ShadowSoftness,soft?1:0);Changed(); }
                LegacyUI.MiniHint(layout.Row(46),SkyText.ShadowHint);
            }
            layout.Space(10);
            if (LegacyUI.Button(layout.Row(29), SkyText.Reset))
            {
                options.ResetSection(section);
                if (section == Options.Section.Time) Runtime.SetHour(options.StartHour.Value);
                Changed();
            }
            GUI.enabled=oldEnabled;
        }
        private void Slider(ref LegacyUILayout layout,Options.FloatOption entry,string text,string unit,float multiplier=1,string format="0")
        {
            LegacyUI.Label(layout.Row(21),SkyText.FormatValue(text,(entry.Value*multiplier).ToString(format),unit));
            float value=LegacyUI.HorizontalSlider(layout.Row(20),entry.Value,entry.Min,entry.Max);
            if(Mathf.Abs(value-entry.Value)<.0001f)return;
            Runtime.Options.Change(entry,value);Changed();
        }
        private void Changed() { Runtime.Options.Dirty();Activate();if(Runtime.IsActive)Runtime.Apply(true); }
        private void Activate() { if(!Runtime.IsActive) { Runtime.EnsureRegistered();Runtime.SelectDynamic(); } }
        private void OnApplicationFocus(bool focus) { if(!focus && windowOpen)SetWindow(false); }
        private void OnDestroy() { Cleanup(); }
        private void Cleanup()
        {
            SetWindow(false);
            try { Runtime?.Shutdown(); }
            finally
            {
                Runtime=null;supported=false;harmony?.UnpatchSelf();harmony=null;
                theme.Destroy();
                if(backdrop)Destroy(backdrop);backdrop=null;
            }
        }
    }
}
