using System.Collections.Generic;
using UnityEngine;

namespace DynamicSky
{
    internal sealed class SceneLights
    {
        private sealed class LightState
        {
            internal float Intensity;
            internal Color Color;
        }

        private sealed class HeadlightState
        {
            internal Light Light;
            internal Material Halo;
            internal float Intensity;
            internal Color LightColor, HaloColor;
        }

        private readonly Dictionary<CarLight,HeadlightState> headlights=new Dictionary<CarLight,HeadlightState>();
        private readonly List<CarLight> destroyedHeadlights=new List<CarLight>();
        private readonly Dictionary<Light,LightState> lights=new Dictionary<Light,LightState>();
        private readonly List<Light> destroyed=new List<Light>();
        private float streetBrightness=-1,streetYellow=-1,headlightBrightness=-1,headlightYellow=-1;

        internal void Discover(Options options)
        {
            foreach(var dayNight in DayNight.instances)
            {
                if(!dayNight || !dayNight.nightLight)continue;
                var light=dayNight.nightLight.useLight;
                var state=Track(light);
                if(state!=null)Apply(light,state,options);
            }
            // Cars in the pool can already have run Awake while inactive at discovery time.
            foreach(var carLight in Resources.FindObjectsOfTypeAll<CarLight>())
                if(carLight.gameObject.scene.IsValid())EnsureHeadlight(carLight,options);
        }

        private LightState Track(Light light)
        {
            if(!light)return null;
            if(!lights.TryGetValue(light,out var state))
            {
                state=new LightState { Intensity=light.intensity, Color=light.color };
                lights.Add(light,state);
            }
            return state;
        }

        internal void BeforeStreetUpdate(Light light)
        {
            if(light && lights.TryGetValue(light,out var state))
                light.intensity=state.Intensity;
        }

        internal void AfterStreetUpdate(Light light,Options options)
        {
            var state=Track(light);
            if(state==null)return;
            state.Intensity=light.intensity;
            Apply(light,state,options);
        }

        internal void TrackHeadlight(CarLight carLight,Options options)
        {
            if(carLight.IsBack || !carLight.car)return;
            if(headlights.ContainsKey(carLight))return;
            var light=carLight.useLight;
            var halo=carLight.oreol?carLight.oreol.material:null;
            if(!light && !halo)return;
            var state=new HeadlightState {
                Light=light,Halo=halo,Intensity=light?light.intensity:0,
                LightColor=light?light.color:Color.white,HaloColor=carLight.UseColor
            };
            headlights.Add(carLight,state);
            ApplyHeadlight(carLight,state,options);
        }

        internal void EnsureHeadlight(CarLight carLight,Options options)
        {
            if(!carLight.IsBack && !headlights.ContainsKey(carLight))TrackHeadlight(carLight,options);
        }

        private static void ApplyHeadlight(CarLight carLight,HeadlightState state,Options options)
        {
            var tint=Color.Lerp(Color.white,new Color(1,.80f,.42f),options.HeadlightYellow.Value);
            if(state.Light)
            {
                state.Light.intensity=state.Intensity*options.HeadlightBrightness.Value;
                state.Light.color=state.LightColor*tint;
            }
            SetHaloColor(carLight,state,state.HaloColor*tint);
        }

        private static void SetHaloColor(CarLight carLight,HeadlightState state,Color color)
        {
            // The game writes UseColor to Oreol every time the lamp fades.
            color.a=carLight.UseColor.a;
            carLight.UseColor=color;
            if(state.Halo && carLight.oreol && carLight.oreol.gameObject.activeSelf)
            {
                color.a=state.Halo.color.a;
                state.Halo.color=color;
            }
        }

        internal void Apply(Options options)
        {
            bool changed=streetBrightness!=options.StreetBrightness.Value || streetYellow!=options.StreetYellow.Value;
            foreach(var pair in lights)
            {
                if(!pair.Key)destroyed.Add(pair.Key);
                else if(changed)Apply(pair.Key,pair.Value,options);
            }
            foreach(var light in destroyed)lights.Remove(light);
            destroyed.Clear();
            bool headlightsChanged=headlightBrightness!=options.HeadlightBrightness.Value || headlightYellow!=options.HeadlightYellow.Value;
            foreach(var pair in headlights)
            {
                if(!pair.Key)destroyedHeadlights.Add(pair.Key);
                else if(headlightsChanged)ApplyHeadlight(pair.Key,pair.Value,options);
            }
            foreach(var carLight in destroyedHeadlights)headlights.Remove(carLight);
            destroyedHeadlights.Clear();
            streetBrightness=options.StreetBrightness.Value;
            streetYellow=options.StreetYellow.Value;
            headlightBrightness=options.HeadlightBrightness.Value;
            headlightYellow=options.HeadlightYellow.Value;
        }

        private static void Apply(Light light,LightState state,Options options)
        {
            float brightness=options.StreetBrightness.Value;
            float yellow=options.StreetYellow.Value;
            float intensity=state.Intensity*brightness;
            var color=state.Color*Color.Lerp(Color.white,new Color(1,.80f,.42f),yellow);
            if(light.intensity!=intensity)light.intensity=intensity;
            if(light.color!=color)light.color=color;
        }

        internal void Restore()
        {
            foreach(var pair in lights)
            {
                if(!pair.Key)continue;
                pair.Key.intensity=pair.Value.Intensity;
                pair.Key.color=pair.Value.Color;
            }
            lights.Clear();
            foreach(var pair in headlights)
            {
                if(!pair.Key)continue;
                var state=pair.Value;
                if(state.Light)
                {
                    state.Light.intensity=state.Intensity;
                    state.Light.color=state.LightColor;
                }
                SetHaloColor(pair.Key,state,state.HaloColor);
            }
            headlights.Clear();
        }
    }
}
