using UnityEngine;

namespace DynamicSky
{
    [RequireComponent(typeof(Camera))]
    [ExecuteInEditMode]
    internal sealed class SunShaftsEffect:MonoBehaviour
    {
        private Camera target;
        private Material material;
        private Vector3 direction;
        private Color color;
        private float strength,length,flare;
        private bool addedDepth,lunar;

        internal void Configure(Camera camera,Shader shader)
        {
            target=camera;
            material=new Material(shader) { hideFlags=HideFlags.HideAndDontSave };
        }

        internal void UpdateSun(Vector3 lightDirection,Color lightColor,float intensity,float blurLength,bool lunar=false,float flareStrength=0)
        {
            direction=lightDirection;
            color=lightColor;
            strength=intensity;
            length=blurLength;
            this.lunar=lunar;
            flare=flareStrength;
        }

        private bool ProjectSun(out Vector3 viewport)
        {
            viewport=target.WorldToViewportPoint(target.transform.position+direction*100);
            return Mathf.Max(strength,flare)>.001f && viewport.z>0 &&
                viewport.x>-.15f && viewport.x<1.15f && viewport.y>-.15f && viewport.y<1.15f;
        }

        private void OnPreCull()
        {
            if(!target || !ProjectSun(out _))return;
            addedDepth=(target.depthTextureMode&DepthTextureMode.Depth)==0;
            target.depthTextureMode|=DepthTextureMode.Depth;
        }

        private void OnPostRender() => RestoreDepth();
        private void OnDisable() => RestoreDepth();

        private void RestoreDepth()
        {
            if(!addedDepth)return;
            if(target)target.depthTextureMode&=~DepthTextureMode.Depth;
            addedDepth=false;
        }

        private void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
            if(!material || !target || !ProjectSun(out var sun))
            {
                Graphics.Blit(source,destination);
                return;
            }
            float outside=Mathf.Max(Mathf.Max(-sun.x,sun.x-1),Mathf.Max(-sun.y,sun.y-1));
            float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.15f,outside));
            material.SetVector("_SunViewport",new Vector4(sun.x,sun.y,(float)source.width/source.height,.45f));
            material.SetColor("_ShaftColor",color);
            material.SetFloat("_FlareStrength",flare*fade);
            if(strength<=.001f)
            {
                Graphics.Blit(source,destination,material,3);
                return;
            }
            var format=SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)?RenderTextureFormat.R8:RenderTextureFormat.ARGB32;
            int width=Mathf.Max(1,source.width/4),height=Mathf.Max(1,source.height/4);
            var mask=RenderTexture.GetTemporary(width,height,0,format,RenderTextureReadWrite.Linear);
            RenderTexture blur=null;
            try
            {
                blur=RenderTexture.GetTemporary(width,height,0,format,RenderTextureReadWrite.Linear);
                mask.wrapMode=blur.wrapMode=TextureWrapMode.Clamp;
                mask.filterMode=blur.filterMode=FilterMode.Bilinear;
                material.SetVector("_BrightnessThreshold",lunar?new Vector4(.002f,.06f,0,0):new Vector4(.12f,.8f,0,0));
                material.SetFloat("_ShaftStrength",strength*fade*.65f);
                Graphics.Blit(source,mask,material,0);
                material.SetFloat("_BlurLength",length*.3f);
                Graphics.Blit(mask,blur,material,1);
                material.SetFloat("_BlurLength",length*.75f);
                Graphics.Blit(blur,mask,material,1);
                material.SetTexture("_ShaftMask",mask);
                Graphics.Blit(source,destination,material,2);
            }
            finally
            {
                material.SetTexture("_ShaftMask",null);
                RenderTexture.ReleaseTemporary(mask);
                if(blur)RenderTexture.ReleaseTemporary(blur);
            }
        }

        private void OnDestroy()
        {
            if(material)SkyRuntime.DestroyOwned(material);
        }
    }
}
