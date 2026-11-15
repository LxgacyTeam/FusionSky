using System;
using UnityEngine;

namespace DynamicSky
{
    internal struct SkyState
    {
        internal Color Zenith, Horizon, Ground, Clouds, Warm, FogColor, AmbientSky, AmbientEquator, AmbientGround, SunColor, SunDiscColor;
        internal Color CloudShade, MoonColor;
        internal Vector3 MoonDirection;
        internal float Twilight, SkyDepth, CloudLighting, MoonDiameter, MoonSoftness, MoonBrightness;
        internal Vector3 SunDirection;
        internal Vector3 LightDirection;
        internal float SunIntensity, MoonVisibility, Stars, Reflection, Coverage, Density, Phase, Daylight, Exposure, CloudScale, CloudDirection;
        internal float SunDiameter, SunSoftness, SunYellow, Bloom;
    }

    internal sealed class CycleClock
    {
        internal double Hours { get; private set; }
        internal CycleClock(double hours) { Set(hours); }
        internal void Set(double hours) { Hours = double.IsNaN(hours) || double.IsInfinity(hours) ? 4.5 : ((hours % 24) + 24) % 24; }
        internal void Advance(double seconds, double minutes, bool paused)
        {
            if (paused || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            if (double.IsNaN(minutes) || double.IsInfinity(minutes)) minutes = 60;
            Set(Hours + seconds * 24 / (Math.Max(1, minutes) * 60));
        }
    }

    internal static class SkyModel
    {
        private struct Key
        {
            internal float Time;
            internal Color Zenith, Horizon, Ambient;
            internal Key(float time, Color zenith, Color horizon, Color ambient) { Time=time; Zenith=zenith; Horizon=horizon; Ambient=ambient; }
        }
        private static Color R(float r,float g,float b) { return new Color(r,g,b,1); }
        private static readonly Color WhiteSun=R(1,1,.95f), YellowSun=R(1,.76f,.09f);
        private static readonly Vector3 NightDirection=-(new Quaternion(-.246028f,-.266570f,.273363f,-.890889f)*Vector3.forward);
        private static readonly Key[] Keys = {
            new Key(0,R(.024f,.037f,.061f),R(.075f,.105f,.17f),R(.272f,.272f,.272f)),
            new Key(3,R(.045f,.063f,.105f),R(.14f,.15f,.18f),R(.27f,.28f,.29f)),
            new Key(4,R(.16f,.21f,.25f),R(.49f,.38f,.26f),R(.39f,.35f,.30f)),
            new Key(4.7f,R(.29f,.36f,.39f),R(.74f,.60f,.44f),R(.51f,.46f,.40f)),
            new Key(6,R(.31f,.44f,.63f),R(.77f,.70f,.59f),R(.50f,.48f,.45f)),
            new Key(9,R(.21f,.43f,.77f),R(.68f,.78f,.89f),R(.49f,.57f,.70f)),
            new Key(12,R(.18f,.39f,.73f),R(.70f,.81f,.91f),R(.53f,.60f,.71f)),
            new Key(17,R(.28f,.43f,.64f),R(.76f,.70f,.59f),R(.49f,.48f,.47f)),
            new Key(19,R(.27f,.34f,.40f),R(.76f,.58f,.40f),R(.47f,.41f,.34f)),
            new Key(20.3f,R(.14f,.20f,.29f),R(.47f,.34f,.24f),R(.34f,.30f,.28f)),
            new Key(21.5f,R(.045f,.062f,.11f),R(.13f,.14f,.19f),R(.27f,.27f,.28f)),
            new Key(24,R(.024f,.037f,.061f),R(.075f,.105f,.17f),R(.272f,.272f,.272f)) };

        internal static float Safe(float v,float fallback,float min,float max) { return float.IsNaN(v)||float.IsInfinity(v)?fallback:Mathf.Clamp(v,min,max); }
        internal static SkyState Evaluate(float hour,float coverage,float density,float brightness,float phase)
        { return EvaluateAt(LightingHour(hour),coverage,density,brightness,phase,51,20,145); }
        private static SkyState EvaluateAt(float hour,float coverage,float density,float brightness,float phase,float latitudeDegrees,float declinationDegrees,float north)
        {
            hour=Mathf.Repeat(Safe(hour,4.5f,-100000,100000),24);
            coverage=Safe(coverage,.35f,0,1); density=Safe(density,.55f,0,1); brightness=Safe(brightness,1,0,2);
            int i=0; while(i+1<Keys.Length-1 && hour>Keys[i+1].Time) i++;
            var a=Keys[i]; var b=Keys[i+1]; float t=Mathf.SmoothStep(0,1,Mathf.InverseLerp(a.Time,b.Time,hour));
            float latitude=latitudeDegrees*Mathf.Deg2Rad,declination=declinationDegrees*Mathf.Deg2Rad,h=(hour-12)*15*Mathf.Deg2Rad;
            var direction=new Vector3(-Mathf.Cos(declination)*Mathf.Sin(h),
                Mathf.Sin(latitude)*Mathf.Sin(declination)+Mathf.Cos(latitude)*Mathf.Cos(declination)*Mathf.Cos(h),
                Mathf.Cos(latitude)*Mathf.Sin(declination)-Mathf.Sin(latitude)*Mathf.Cos(declination)*Mathf.Cos(h)).normalized;
            direction=Quaternion.Euler(0,north,0)*direction;
            float day=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.08f,.14f,direction.y));
            float overcast=coverage*density;
            var zenith=Color.Lerp(a.Zenith,b.Zenith,t); var horizon=Color.Lerp(a.Horizon,b.Horizon,t);
            var ambient=Color.Lerp(a.Ambient,b.Ambient,t);
            float highSun=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.65f,direction.y));
            float clearDay=highSun*(1-overcast);
            zenith=Color.Lerp(zenith,Color.Lerp(horizon,R(.58f,.62f,.69f),day*.55f),overcast*.65f);
            float twilight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.04f,.35f,Mathf.Abs(direction.y)));
            float dawnToDusk=Mathf.SmoothStep(0,1,Mathf.InverseLerp(10,14,hour));
            var dayColor=Color.Lerp(R(1,.805f,.596f),R(1,.97f,.91f),Mathf.InverseLerp(0,.4f,direction.y));
            float sunStrength=Mathf.Max(0,direction.y)*Mathf.Lerp(1.25f,1.45f,clearDay)*day*Mathf.Lerp(1,.32f,overcast)*brightness;
            sunStrength+=.50f*twilight*day*(1-overcast)*brightness;
            float nightLight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.10f,.08f,direction.y));
            float nightStrength=.90f*nightLight*Mathf.Lerp(1,.68f,overcast);
            float lightStrength=nightStrength+sunStrength;
            float sunWeight=lightStrength>.0001f?sunStrength/lightStrength:day;
            var fogColor=Color.Lerp(R(.149f,.208f,.322f),Color.Lerp(horizon,R(.396f,.318f,.247f),twilight*.72f),day);
            return new SkyState { Zenith=zenith,Horizon=horizon, Ground=Color.Lerp(ambient,R(.07f,.08f,.10f),.50f),
                Clouds=Color.Lerp(R(.037f,.048f,.071f),Color.Lerp(horizon,Color.white,Mathf.Lerp(.18f,.48f,highSun)),day),
                Warm=Color.Lerp(R(.91f,.72f,.47f),R(.93f,.66f,.40f),dawnToDusk),
                FogColor=fogColor,AmbientSky=ambient*Mathf.Lerp(.87f,1.06f,highSun),
                AmbientEquator=Color.Lerp(R(.035f,.114f,.145f),ambient*.72f,day),
                AmbientGround=Color.Lerp(R(.145f,.154f,.054f)*.67f,ambient*Mathf.Lerp(.40f,.35f,clearDay),day),
                SunDirection=direction,LightDirection=Vector3.Slerp(NightDirection,direction,sunWeight),
                SunColor=Color.Lerp(R(.603f,.721f,1),dayColor,sunWeight),
                SunDiscColor=WhiteSun,
                SunIntensity=lightStrength,
                MoonVisibility=(1-day)*(1-overcast),Stars=(1-day)*(1-overcast),
                Reflection=Mathf.Lerp(.115f,.72f,day),Coverage=coverage,Density=density,Phase=phase,Daylight=day,Exposure=1,CloudScale=1,
                SunDiameter=.6f,SunSoftness=.33f,Bloom=1 };
        }
        internal static SkyState Evaluate(Options options,float hour,float phase)
        {
            float lightingHour=LightingHour(hour);
            var state=EvaluateAt(lightingHour,options.CloudCoverage.Value,options.CloudDensity.Value,options.SunBrightness.Value,phase,options.Latitude.Value,options.Declination.Value,options.North.Value);
            ApplyTwilight(ref state,options,lightingHour);
            float day=state.Daylight;
            float ambient=Mathf.Lerp(options.NightAmbient.Value,options.DayAmbient.Value,day);
            state.SunIntensity*=Mathf.Lerp(Mathf.Sqrt(options.NightAmbient.Value),1,day);
            state.Reflection*=Mathf.Lerp(options.NightReflection.Value,options.DayReflection.Value,day);
            state.Stars*=options.StarsBrightness.Value;
            state.Exposure=Mathf.Lerp(options.NightExposure.Value,options.DayExposure.Value,day)*Mathf.Lerp(1,options.TwilightSky.Value,state.Twilight);
            state.Bloom=Mathf.Lerp(options.NightBloom.Value,options.DayBloom.Value,day);
            state.SunDiameter=options.SunDiameter.Value;
            state.SunSoftness=options.SunSoftness.Value;
            state.SunYellow=options.SunYellow.Value;
            state.SunDiscColor=Color.Lerp(WhiteSun,YellowSun,state.SunYellow);
            state.MoonDirection=-state.SunDirection;
            state.MoonDiameter=options.MoonDiameter.Value;
            state.MoonSoftness=options.MoonSoftness.Value;
            state.MoonBrightness=options.MoonBrightness.Value;
            state.MoonColor=Color.Lerp(R(.70f,.80f,1),R(1,.87f,.65f),options.MoonYellow.Value);
            state.CloudScale=options.CloudScale.Value;state.CloudDirection=options.CloudDirection.Value*Mathf.Deg2Rad;
            float twilight=state.Twilight;
            float legacyTwilight=1-Ramp(.04f,.35f,Mathf.Abs(state.SunDirection.y));
            float saturation=Mathf.Lerp(1,options.DaySaturation.Value,day*(1-legacyTwilight));
            float twilightSaturation=Mathf.Lerp(saturation,Mathf.Min(options.TwilightWarmth.Value,TwilightSaturationLimit(hour)),twilight);
            state.Zenith=Saturate(state.Zenith,saturation);
            state.Horizon=Saturate(state.Horizon,twilightSaturation);
            state.Ground=Saturate(state.Ground,saturation);
            state.Clouds=Saturate(state.Clouds,twilightSaturation);
            state.CloudShade=Saturate(state.CloudShade,twilightSaturation);
            state.Warm=Saturate(state.Warm,twilightSaturation);

            float temperature=Mathf.Lerp(options.NightTemperature.Value,options.DayTemperature.Value,day);
            var tint=TemperatureTint(temperature);
            var skyTint=tint;
            var lightTint=tint;
            state.Zenith=Tint(state.Zenith,skyTint);
            state.Horizon=Tint(state.Horizon,skyTint);
            state.Ground=Tint(state.Ground,lightTint);
            state.Clouds=Tint(state.Clouds,skyTint);
            state.CloudShade=Tint(state.CloudShade,skyTint);
            state.Warm=Tint(state.Warm,skyTint);
            state.FogColor=Tint(Saturate(state.FogColor,twilightSaturation),lightTint);
            state.AmbientSky=Tint(Saturate(state.AmbientSky,twilightSaturation),lightTint)*ambient;
            state.AmbientEquator=Tint(Saturate(state.AmbientEquator,twilightSaturation),lightTint)*ambient;
            state.AmbientGround=Tint(Saturate(state.AmbientGround,twilightSaturation),lightTint)*ambient;
            state.SunColor=Tint(Saturate(state.SunColor,twilightSaturation),lightTint);
            state.AmbientSky*=options.ShadowFill.Value;
            state.AmbientEquator*=options.ShadowFill.Value;
            state.AmbientGround*=options.ShadowFill.Value;
            return state;
        }

        // Keep the morning, stretch the afternoon and fit the last five lighting hours
        // into 21:30–24:00. Matching slopes keep solar motion smooth across the joins.
        internal static float LightingHour(float hour)
        {
            hour=Mathf.Repeat(Safe(hour,4.5f,-100000,100000),24);
            if(hour<=12) return hour;
            if(hour<18.5f) return hour-2.5f*SmoothFraction((hour-12)/6.5f);
            if(hour<=21.5f) return hour-2.5f;
            return hour-2.5f+2.5f*SmoothFraction((hour-21.5f)/2.5f);
        }

        private static float SmoothFraction(float value) => value*value*(3-2*value);

        internal static float TwilightSaturationLimit(float hour) =>
            Mathf.Lerp(1.5f,3,Mathf.InverseLerp(6,6.5f,Mathf.Repeat(hour,24)));

        private static float Ramp(float a,float b,float value) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));
        private static void ApplyTwilight(ref SkyState s,Options o,float hour)
        {
            hour=Mathf.Repeat(hour,24);
            float dawn=Ramp(2.5f,4.2f,hour)*(1-Ramp(5.3f,7,hour));
            float dusk=Ramp(16.5f,18.4f,hour)*(1-Ramp(20.2f,22.5f,hour));
            float window=dawn+dusk;
            float elevation=s.SunDirection.y;
            float diffuse=Ramp(-.24f,.20f,elevation);
            float warm=Ramp(-.16f,.02f,elevation)*(1-Ramp(.20f,.50f,elevation));
            float overcast=s.Coverage*s.Density;
            float pink=hour<12?o.DawnPink.Value:o.DuskPink.Value;
            float brightness=hour<12?o.DawnBrightness.Value:o.DuskBrightness.Value;
            var orange=R(1,.53f,.24f);
            var rose=R(1,.68f,.72f);
            var glow=Color.LerpUnclamped(orange,rose,pink);
            var horizon=Color.Lerp(R(.09f,.15f,.26f),R(.66f,.77f,.90f),diffuse);
            horizon=Color.Lerp(horizon,glow,warm*.86f);
            var zenith=Color.Lerp(R(.025f,.046f,.10f),R(.20f,.38f,.65f),diffuse);
            zenith=Color.Lerp(zenith,horizon,overcast*.3f);
            s.Zenith=Color.Lerp(s.Zenith,zenith,window);
            s.Horizon=Color.Lerp(s.Horizon,horizon,window);
            s.Warm=Color.Lerp(s.Warm,glow,window);
            s.Twilight=window;
            s.SkyDepth=window*o.TwilightDepth.Value;
            // Diffuse sky illumination rises before the solar disk reaches the horizon.
            var ambient=Color.Lerp(R(.272f,.272f,.272f)*.87f,R(.48f,.56f,.70f),diffuse);
            var equator=Color.Lerp(R(.035f,.114f,.145f),R(.32f,.35f,.40f),diffuse);
            var ground=Color.Lerp(R(.145f,.154f,.054f)*.67f,R(.19f,.18f,.17f),diffuse);
            float fill=o.TwilightAmbient.Value*Mathf.Sqrt(brightness);
            s.AmbientSky=Color.Lerp(s.AmbientSky,ambient*fill,window);
            s.AmbientEquator=Color.Lerp(s.AmbientEquator,equator*fill,window);
            s.AmbientGround=Color.Lerp(s.AmbientGround,ground*fill,window);
            s.Ground=Color.Lerp(s.Ground,ground,window);
            float direct=Ramp(-.035f,.10f,elevation)*(1.05f+.30f*diffuse)*Mathf.Lerp(1,.32f,overcast)*o.SunBrightness.Value*brightness;
            float moon=.90f*(1-Ramp(-.09f,.14f,elevation))*Mathf.Lerp(1,.68f,overcast);
            float weight=direct/Mathf.Max(.0001f,direct+moon);
            var solar=Color.Lerp(Color.LerpUnclamped(R(1,.59f,.30f),R(1,.75f,.70f),pink),R(1,.95f,.87f),Ramp(.12f,.40f,elevation));
            s.SunIntensity=Mathf.Lerp(s.SunIntensity,direct+moon,window);
            s.SunColor=Color.Lerp(s.SunColor,Color.Lerp(R(.603f,.721f,1),solar,weight),window);
            s.LightDirection=Vector3.Slerp(s.LightDirection,Vector3.Slerp(NightDirection,s.SunDirection,weight),window);
            s.Daylight=Mathf.Lerp(s.Daylight,diffuse,window);
            s.MoonVisibility=(1-s.Daylight)*(1-overcast);
            s.Stars=(1-s.Daylight)*(1-overcast);
            var haze=Color.Lerp(R(.149f,.208f,.322f),Color.Lerp(R(.51f,.55f,.63f),glow,.40f*warm),diffuse);
            s.FogColor=Color.Lerp(s.FogColor,haze,window);
            s.Reflection=Mathf.Lerp(s.Reflection,Mathf.Lerp(.115f,.72f,diffuse),window);
            // White cloud material illuminated by blue moonlight, cool skylight and warm direct light.
            var nightCloud=R(.22f,.27f,.36f);
            var cloud=Color.Lerp(nightCloud,Color.Lerp(R(.94f,.96f,1),glow*1.18f,warm*.65f),diffuse);
            s.Clouds=Color.Lerp(s.Clouds,cloud,Mathf.Max(window,1-s.Daylight));
            s.CloudShade=Color.Lerp(R(.115f,.15f,.22f),Color.Lerp(R(.34f,.41f,.52f),glow,.13f*warm),s.Daylight);
            s.CloudLighting=Mathf.Max(window,1-s.Daylight);
        }

        private static Color Saturate(Color color,float saturation)
        {
            var adjusted=Color.LerpUnclamped(color.grayscale*Color.white,color,saturation);
            return new Color(Mathf.Max(0,adjusted.r),Mathf.Max(0,adjusted.g),Mathf.Max(0,adjusted.b),color.a);
        }

        private static Color TemperatureTint(float temperature)
        {
            var warm=new Color(1.21f,1,.77f);
            var cool=new Color(.78f,.96f,1.23f);
            return Color.Lerp(Color.white,temperature>=0?warm:cool,Mathf.Abs(temperature));
        }

        private static Color Tint(Color color,Color tint)
        {
            var adjusted=color*tint;
            float luminance=adjusted.grayscale;
            return luminance>.0001f?adjusted*(color.grayscale/luminance):color;
        }

        internal static bool Night(float elevation,bool wasNight) { return wasNight?elevation<.015f:elevation<-.055f; }
        internal static Color ReflectionColor(Vector3 d,SkyState s,float cloudOpacity)
        {
            var color=Color.Lerp(s.Horizon,s.Zenith,Mathf.Pow(Mathf.Clamp01((d.y+.035f*s.SkyDepth)/(1+.035f*s.SkyDepth)),.68f+.21f*s.SkyDepth));
            float facing=Mathf.Lerp(Mathf.Clamp01(Vector3.Dot(d,s.MoonDirection)*.5f+.5f),Mathf.Clamp01(Vector3.Dot(d,s.SunDirection)*.5f+.5f),s.Daylight);
            float cloudLight=Mathf.Clamp01(.80f-.25f*cloudOpacity+Mathf.Pow(facing,8)*.48f);
            var cloud=Color.Lerp(s.Clouds,Color.Lerp(s.CloudShade,s.Clouds,cloudLight),s.CloudLighting);
            color=Color.Lerp(color,cloud,cloudOpacity);
            float glow=Mathf.Pow(Mathf.Clamp01(Vector3.Dot(d,s.SunDirection)*.5f+.5f),12)*Mathf.Exp(-Mathf.Max(0,d.y)*4);
            color=Color.Lerp(color,s.Warm,glow*.45f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.12f,.04f,s.SunDirection.y)));
            return Color.Lerp(color,s.Ground,Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.35f,-d.y)))*s.Exposure;
        }
        internal static float CloudOpacity(Vector3 direction,SkyState state)
        {
            if(state.Coverage<=.0001f || state.Density<=0 || direction.y<=.01f)return 0;
            var p=direction*(4.3f/state.CloudScale)+new Vector3(state.Phase*Mathf.Cos(state.CloudDirection),0,3.2f+state.Phase*Mathf.Sin(state.CloudDirection));
            float n=Noise(p)*.57f+Noise(p*2.03f+Vector3.one*17.1f)*.27f+Noise(p*4.09f+Vector3.one*31.7f)*.11f+Noise(p*8.17f+Vector3.one*5.4f)*.05f;
            float threshold=Mathf.Lerp(.85f,.18f,state.Coverage);
            float mask=Mathf.SmoothStep(0,1,Mathf.InverseLerp(threshold,threshold+.15f,n));
            return mask*state.Density*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.01f,.25f,direction.y));
        }
        private static float Frac(float x) { return x-Mathf.Floor(x); }
        private static float Hash(Vector3 p)
        {
            p=new Vector3(Frac(p.x*.1031f),Frac(p.y*.1031f),Frac(p.z*.1031f));
            float k=Vector3.Dot(p,new Vector3(p.y+33.33f,p.z+33.33f,p.x+33.33f));p+=Vector3.one*k;
            return Frac((p.x+p.y)*p.z);
        }
        private static float Noise(Vector3 p)
        {
            var c=new Vector3(Mathf.Floor(p.x),Mathf.Floor(p.y),Mathf.Floor(p.z)); var f=p-c;
            f=new Vector3(f.x*f.x*(3-2*f.x),f.y*f.y*(3-2*f.y),f.z*f.z*(3-2*f.z));
            float a=Mathf.Lerp(Mathf.Lerp(Hash(c),Hash(c+Vector3.right),f.x),Mathf.Lerp(Hash(c+Vector3.up),Hash(c+Vector3.right+Vector3.up),f.x),f.y);
            float b=Mathf.Lerp(Mathf.Lerp(Hash(c+Vector3.forward),Hash(c+Vector3.right+Vector3.forward),f.x),Mathf.Lerp(Hash(c+Vector3.up+Vector3.forward),Hash(c+Vector3.one),f.x),f.y);
            return Mathf.Lerp(a,b,f.z);
        }
    }
}
