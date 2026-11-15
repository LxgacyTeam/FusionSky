using BigCityLegacy.UI;
using UnityEngine;

namespace DynamicSky
{
    internal sealed class SkyTheme
    {
        private static readonly float[] Hours={0,4.5f,12,19,24};
        private static readonly Color Night=new Color(.25f,.33f,.55f);
        private static readonly Color Day=new Color(.23f,.48f,.69f);
        private static readonly Color Dawn=new Color(.73f,.40f,.22f);
        private static readonly Color Dusk=new Color(.68f,.33f,.20f);
        private static readonly Color Rose=new Color(.70f,.41f,.53f);

        internal readonly LegacyUITheme Theme=new LegacyUITheme();
        private Texture2D pixels;
        private Color32 lastAccent;

        internal static Color Accent(float hour,float dawnPink,float duskPink)
        {
            hour=SkyModel.LightingHour(hour);
            int interval=0;
            while(interval<Hours.Length-2 && hour>Hours[interval+1])interval++;
            float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Hours[interval],Hours[interval+1],hour));
            var dawn=Color.LerpUnclamped(Dawn,Rose,dawnPink);
            var dusk=Color.LerpUnclamped(Dusk,Rose,duskPink);
            switch(interval)
            {
                case 0:return Color.Lerp(Night,dawn,blend);
                case 1:return Color.Lerp(dawn,Day,blend);
                case 2:return Color.Lerp(Day,dusk,blend);
                default:return Color.Lerp(dusk,Night,blend);
            }
        }

        internal void Update(float hour,float dawnPink,float duskPink)
        {
            Color accent=Accent(hour,dawnPink,duskPink);
            if(pixels && lastAccent.Equals((Color32)accent))return;

            var palette=Theme.Palette;
            palette.Active=palette.Border=palette.SliderThumb=palette.ProgressFill=accent;
            palette.SliderThumbHover=Color.Lerp(accent,Color.white,.2f);
            palette.Status=Color.Lerp(accent,Color.white,.42f);

            if(!pixels)pixels=new Texture2D(2,1,TextureFormat.ARGB32,false) { hideFlags=HideFlags.HideAndDontSave };
            // The framework's GUIStyles keep references to these textures.
            pixels.SetPixel(0,0,palette.Active);
            pixels.SetPixel(1,0,palette.SliderThumbHover);
            pixels.Apply(false);
            var textures=Theme.Textures;
            Copy(0,textures.Active);
            Copy(0,textures.Border);
            Copy(0,textures.SliderThumb);
            Copy(0,textures.ProgressFill);
            Copy(1,textures.SliderThumbHover);
            Theme.Styles.Status.normal.textColor=palette.Status;
            lastAccent=accent;
        }

        private void Copy(int x,Texture2D destination) =>
            Graphics.CopyTexture(pixels,0,0,x,0,1,1,destination,0,0,0,0);

        internal void Destroy()
        {
            if(!pixels)return;
            foreach(var property in typeof(LegacyUITextures).GetProperties())
                if(property.PropertyType==typeof(Texture2D))
                    SkyRuntime.DestroyOwned((Texture2D)property.GetValue(Theme.Textures,null));
            SkyRuntime.DestroyOwned(pixels);
            pixels=null;
        }
    }
}
