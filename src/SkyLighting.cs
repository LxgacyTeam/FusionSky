using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicSky
{
    internal static class SkyLighting
    {
        private static readonly int[] A={Shader.PropertyToID("unity_SHAr"),Shader.PropertyToID("unity_SHAg"),Shader.PropertyToID("unity_SHAb")};
        private static readonly int[] B={Shader.PropertyToID("unity_SHBr"),Shader.PropertyToID("unity_SHBg"),Shader.PropertyToID("unity_SHBb")};
        private static readonly int C=Shader.PropertyToID("unity_SHC");
        private static Color Linear(Color color) => QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color;
        internal static SphericalHarmonicsL2 Ambient(SkyState state)
        {
            var sh=new SphericalHarmonicsL2();
            sh.AddAmbientLight(Linear(state.AmbientEquator)*.5f);
            sh.AddDirectionalLight(Vector3.up,Linear(state.AmbientSky),.5f);
            sh.AddDirectionalLight(Vector3.down,Linear(state.AmbientGround),.5f);
            return sh;
        }
        internal static void Setup(CommandBuffer buffer,SkyState state,Cubemap reflection)
        {
            var sh=Ambient(state);
            for(int i=0;i<3;i++)
            {
                buffer.SetGlobalVector(A[i],new Vector4(sh[i,3],sh[i,1],sh[i,2],sh[i,0]-sh[i,6]));
                buffer.SetGlobalVector(B[i],new Vector4(sh[i,4],sh[i,6],sh[i,5]*3,sh[i,7]));
            }
            buffer.SetGlobalVector(C,new Vector4(sh[0,8],sh[1,8],sh[2,8],1));
            var direction=state.LightDirection;
            buffer.SetGlobalVector("_WorldSpaceLightPos0",new Vector4(direction.x,direction.y,direction.z,0));
            buffer.SetGlobalColor("_LightColor0",Linear(state.SunColor)*state.SunIntensity);
            buffer.SetGlobalTexture("unity_SpecCube0",reflection);
            buffer.SetGlobalTexture("unity_SpecCube1",reflection);
            buffer.EnableShaderKeyword("LIGHTPROBE_SH");
        }
    }
}
