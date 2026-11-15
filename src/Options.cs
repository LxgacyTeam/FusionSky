using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace DynamicSky
{
    internal sealed class Options
    {
        internal sealed class FloatOption
        {
            internal readonly string Key;
            internal readonly float Default, Min, Max;
            private float value;

            internal FloatOption(string key, float fallback, float min, float max, float value)
            {
                Key = key;
                Default = fallback;
                Min = min;
                Max = max;
                Value = value;
            }

            internal float Value
            {
                get => value;
                set => this.value = SkyModel.Safe(value, Default, Min, Max);
            }

            internal void Reset() => Value = Default;
        }

        private const string DefaultPrefix = "FusionSky.Settings.";
        private readonly string prefix;
        private readonly List<FloatOption> floats = new List<FloatOption>();
        internal bool Enabled, AutoStart, Paused;
        internal const string MenuShortcut = "Alt + G";
        internal readonly FloatOption StartHour, DayMinutes, CloudCoverage, CloudDensity, CloudSpeed, CloudScale, CloudDirection;
        internal readonly FloatOption FogAmount, Visibility, FogStart, Latitude, Declination, North, SunDiameter, SunSoftness;
        internal readonly FloatOption SunYellow, ShaftsIntensity, ShaftsLength, FlareIntensity;
        internal readonly FloatOption SunBrightness, DayAmbient, NightAmbient, DayExposure, NightExposure, DayReflection, NightReflection;
        internal readonly FloatOption StarsBrightness, TwilightWarmth, DaySaturation, DayTemperature, NightTemperature, DayBloom, NightBloom;
        internal readonly FloatOption DawnPink, DuskPink, StreetBrightness, StreetYellow, HeadlightBrightness, HeadlightYellow;
        internal readonly FloatOption DawnBrightness, DuskBrightness, TwilightAmbient, TwilightDepth, TwilightSky;
        internal readonly FloatOption MoonDiameter, MoonSoftness, MoonYellow, MoonBrightness, MoonShafts, MoonShaftsLength;
        internal readonly FloatOption DayShadowStrength, NightShadowStrength, ShadowFill, ShadowDistance, ShadowSoftness;
        private float saveAt = -1;

        internal Options(ConfigFile legacy, string prefix = DefaultPrefix)
        {
            this.prefix = prefix;
            bool initialize = !PlayerPrefs.HasKey(prefix + "Version");
            if (!initialize || legacy == null || !File.Exists(legacy.ConfigFilePath)) legacy = null;
            if (legacy != null) legacy.SaveOnConfigSet = false;

            Enabled = ReadBool(legacy, "General", "Enabled", true);
            AutoStart = ReadBool(legacy, "General", "AutoStart", true);
            Paused = ReadBool(legacy, "Time", "Paused", false);
            StartHour = Add("Time", "StartHour", 6.6f, 0, 24, ReadLegacy(legacy, "Time", "StartHour", 6.4f));
            DayMinutes = Add("Time", "DayMinutes", 60, 1, 240, ReadLegacy(legacy, "Time", "DayMinutes", 60));
            CloudCoverage = Add("Clouds", "Coverage", .40f, 0, 1, ReadLegacy(legacy, "Clouds", "Coverage", .40f));
            CloudDensity = Add("Clouds", "Density", .80f, 0, 1, ReadLegacy(legacy, "Clouds", "Density", .80f));
            CloudSpeed = Add("Clouds", "Speed", .25f, 0, 1, ReadLegacy(legacy, "Clouds", "Speed", .25f));
            CloudScale = Add("Clouds", "Scale", 1, .25f, 3, ReadLegacy(legacy, "Clouds", "Scale", 1));
            CloudDirection = Add("Clouds", "Direction", 0, 0, 360, ReadLegacy(legacy, "Clouds", "Direction", 0));
            FogAmount = Add("Fog", "Amount", 1, 0, 1, ReadLegacy(legacy, "Fog", "Amount", 1));
            Visibility = Add("Fog", "Visibility", 1800, 100, 2500, ReadLegacy(legacy, "Fog", "Visibility", 1800));
            FogStart = Add("Fog", "Start", .06f, 0, .9f, ReadLegacy(legacy, "Fog", "Start", .06f));
            Latitude = Add("Sun", "Latitude", 40, -70, 70, ReadLegacy(legacy, "Sun", "Latitude", 40));
            Declination = Add("Sun", "Declination", 20, -23.4f, 23.4f, ReadLegacy(legacy, "Sun", "Declination", 20));
            North = Add("Sun", "North", 90, 0, 360, ReadLegacy(legacy, "Sun", "North", 90));
            SunDiameter = Add("Sun", "Diameter", 5.5f, .1f, 8);
            SunSoftness = Add("Sun", "Softness", .75f, 0, 1);
            SunYellow = Add("Sun", "Yellow", .10f, 0, .5f);
            ShaftsIntensity = Add("Sun", "ShaftsIntensity", .90f, 0, 1);
            ShaftsLength = Add("Sun", "ShaftsLength", .70f, .1f, 1);
            FlareIntensity = Add("Sun", "FlareIntensity", 0, 0, 1);

            float ambient = SkyModel.Safe(ReadLegacy(legacy, "Lighting", "AmbientBrightness", 1), 1, .25f, 2);
            float night = SkyModel.Safe(ReadLegacy(legacy, "Lighting", "NightBrightness", 1.5f), 1, .25f, 3);

            float reflection = ReadLegacy(legacy, "Lighting", "ReflectionBrightness", 1);
            float? exposure = legacy == null ? (float?)null : ReadLegacy(legacy, "Lighting", "SkyExposure", float.NaN);
            SunBrightness = Add("Day", "SunBrightness", 1, 0, 2, ReadLegacy(legacy, "Lighting", "SunBrightness", 1));
            DayAmbient = Add("Day", "Ambient", 1, .25f, 2, ambient);
            NightAmbient = Add("Night", "Ambient", 1.50f, .0625f, 6, legacy == null ? (float?)null : ambient * night);
            DayExposure = Add("Day", "Exposure", 1, .25f, 2, exposure);
            NightExposure = Add("Night", "Exposure", 1.50f, .25f, 2, exposure);
            DayReflection = Add("Day", "Reflection", 1, 0, 2, reflection);
            NightReflection = Add("Night", "Reflection", 1, 0, 2, reflection);
            StarsBrightness = Add("Night", "StarsBrightness", 1.50f, 0, 2, ReadLegacy(legacy, "Lighting", "StarsBrightness", 1.50f));
            TwilightWarmth = Add("Day", "TwilightWarmth", 1.10f, 0, 3, ReadLegacy(legacy, "Lighting", "TwilightWarmth", 1.10f));
            DaySaturation = Add("Day", "Saturation", 1, 0, 2);
            DayTemperature = Add("Day", "Temperature", 0, -1, 1);
            NightTemperature = Add("Night", "Temperature", 0, -1, 1);
            DayBloom = Add("Day", "Bloom", 1, 0, 3);
            NightBloom = Add("Night", "Bloom", 2, 0, 3);
            DawnPink = Add("Day", "DawnPink", .10f, -.35f, 1.35f);
            DuskPink = Add("Day", "DuskPink", 0, -.35f, 1.35f);
            StreetBrightness = Add("Lights", "StreetBrightness", .80f, 0, 2);
            StreetYellow = Add("Lights", "StreetYellow", .10f, 0, 1);
            HeadlightBrightness = Add("Lights", "HeadlightBrightness", .75f, 0, 2);
            HeadlightYellow = Add("Lights", "HeadlightYellow", .10f, 0, 1);

            DawnBrightness = Add("Twilight", "DawnBrightness", .90f, .25f, 3);
            DuskBrightness = Add("Twilight", "DuskBrightness", 1, .25f, 3);
            TwilightAmbient = Add("Twilight", "Ambient", 1, .25f, 2);
            TwilightDepth = Add("Twilight", "Depth", 2, 0, 2);
            TwilightSky = Add("Twilight", "Sky", 1.35f, .25f, 2);
            MoonDiameter = Add("Moon", "Diameter", 5.5f, .1f, 8);
            MoonSoftness = Add("Moon", "Softness", .40f, 0, 1);
            MoonYellow = Add("Moon", "Yellow", .20f, 0, 1);
            MoonBrightness = Add("Moon", "DiscBrightness", 1.40f, 0, 2);
            MoonShafts = Add("Moon", "Shafts", .50f, 0, 1);
            MoonShaftsLength = Add("Moon", "ShaftsLength", .95f, .1f, 1);
            DayShadowStrength = Add("Shadows", "DayStrength", .80f, 0, 1);
            NightShadowStrength = Add("Shadows", "NightStrength", .80f, 0, 1);
            ShadowFill = Add("Shadows", "Fill", 1, .25f, 1.5f);
            ShadowDistance = Add("Shadows", "Distance", 0, 0, 500);
            ShadowSoftness = Add("Shadows", "Soft", 1, 0, 1);
            if (initialize) Save();
        }

        private static float ReadLegacy(ConfigFile legacy, string section, string key, float fallback) =>
            legacy?.Bind(section, key, fallback).Value ?? fallback;

        private bool ReadBool(ConfigFile legacy, string section, string key, bool fallback)
        {
            bool old = legacy?.Bind(section, key, fallback).Value ?? fallback;
            return PlayerPrefs.GetInt(prefix + section + "." + key, old ? 1 : 0) != 0;
        }

        private FloatOption Add(string section, string key, float fallback, float min, float max, float? legacyValue = null)
        {
            string pref = prefix + section + "." + key;
            var option = new FloatOption(pref, fallback, min, max, PlayerPrefs.GetFloat(pref, legacyValue ?? fallback));
            floats.Add(option);
            return option;
        }

        internal void Change(FloatOption option, float value)
        {
            if (Mathf.Approximately(option.Value, value)) return;
            option.Value = value;
            Dirty();
        }

        internal void Dirty() => saveAt = Time.realtimeSinceStartup + .8f;

        internal void Tick()
        {
            if (saveAt >= 0 && Time.realtimeSinceStartup >= saveAt) Save();
        }

        internal void Save()
        {
            foreach (var option in floats) PlayerPrefs.SetFloat(option.Key, option.Value);
            PlayerPrefs.SetInt(prefix + "General.Enabled", Enabled ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "General.AutoStart", AutoStart ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "Time.Paused", Paused ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "Version", 1);
            PlayerPrefs.Save();
            saveAt = -1;
        }

        internal IEnumerable<KeyValuePair<string, string>> Capture()
        {
            foreach (var option in floats)
                if (IsPresetOption(option))
                    yield return new KeyValuePair<string, string>(PresetKey(option), option.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        internal bool ApplyPreset(IReadOnlyDictionary<string, string> values)
        {
            var pending = new float[floats.Count];
            for (int i = 0; i < floats.Count; i++)
            {
                var option = floats[i];
                if (!IsPresetOption(option)) continue;
                pending[i] = option.Default;
                if (values == null || !values.TryGetValue(PresetKey(option), out var raw)) continue;
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out pending[i]) ||
                    float.IsNaN(pending[i]) || float.IsInfinity(pending[i])) return false;
            }

            for (int i = 0; i < floats.Count; i++)
                if (IsPresetOption(floats[i])) floats[i].Value = pending[i];
            Dirty();
            return true;
        }

        private bool IsPresetOption(FloatOption option) => option != StartHour && option != DayMinutes;

        private string PresetKey(FloatOption option) => option.Key.Substring(prefix.Length);

        internal enum Section { Time, Clouds, Lighting, Fog, Celestial, StreetLights, Shadows }

        internal void ResetSection(Section section)
        {
            switch (section)
            {
                case Section.Time:
                    StartHour.Reset();
                    DayMinutes.Reset();
                    Paused = false;
                    break;
                case Section.Clouds:
                    Reset(CloudCoverage, CloudDensity, CloudSpeed, CloudScale, CloudDirection);
                    break;
                case Section.Lighting:
                    Reset(SunBrightness, DayAmbient, NightAmbient, DayExposure, NightExposure, DayReflection, NightReflection,
                        StarsBrightness, DaySaturation, DayTemperature, NightTemperature, DayBloom, NightBloom);
                    Reset(TwilightWarmth, DawnPink, DuskPink, DawnBrightness, DuskBrightness, TwilightAmbient, TwilightDepth, TwilightSky);
                    break;
                case Section.Fog:
                    Reset(FogAmount, Visibility, FogStart);
                    break;
                case Section.Celestial:
                    Reset(Latitude, Declination, North, SunDiameter, SunSoftness, SunYellow, ShaftsIntensity, ShaftsLength, FlareIntensity);
                    Reset(MoonDiameter, MoonSoftness, MoonYellow, MoonBrightness, MoonShafts, MoonShaftsLength);
                    break;
                case Section.StreetLights:
                    Reset(StreetBrightness, StreetYellow, HeadlightBrightness, HeadlightYellow);
                    break;
                case Section.Shadows:
                    Reset(DayShadowStrength, NightShadowStrength, ShadowFill, ShadowDistance, ShadowSoftness);
                    break;
            }
            Dirty();
        }

        private static void Reset(params FloatOption[] options)
        {
            foreach (var option in options) option.Reset();
        }
    }
}
