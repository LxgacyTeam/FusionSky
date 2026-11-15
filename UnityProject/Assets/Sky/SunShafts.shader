Shader "Hidden/BigCityLegacy/SunShafts"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _ShaftColor ("Shaft color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _ShaftMask;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize;
        float4 _SunViewport;
        float4 _ShaftColor;
        float4 _BrightnessThreshold;
        float _BlurLength, _ShaftStrength, _FlareStrength;

        float2 ScreenUV(float2 uv)
        {
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0) uv.y = 1 - uv.y;
            #endif
            return uv;
        }

        float4 Mask(v2f_img i) : SV_Target
        {
            float2 screenUV=ScreenUV(i.uv);
            float depth=Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,screenUV));
            float2 offset=(screenUV-_SunViewport.xy)*float2(_SunViewport.z,1);
            float radius=length(offset);
            float falloff=1-smoothstep(0,_SunViewport.w,radius);
            float3 scene=tex2D(_MainTex,i.uv).rgb;
            float brightness=dot(scene,float3(.2126,.7152,.0722));
            return step(.99999,depth)*falloff*smoothstep(_BrightnessThreshold.x,_BrightnessThreshold.y,brightness);
        }

        float4 Blur(v2f_img i) : SV_Target
        {
            float2 stepUV=(_SunViewport.xy-i.uv)*_BlurLength/7;
            float sum=0;
            [unroll] for(int sample=0;sample<8;sample++)
                sum+=tex2D(_MainTex,i.uv+sample*stepUV).r;
            return sum/8;
        }

        float3 LensArtifacts(float2 uv)
        {
            if (_FlareStrength <= .001) return 0;
            float2 sun=_SunViewport.xy;
            float aspect=_SunViewport.z;
            float visibility=0;
            // Sample around the projected disc for a soft fade behind a roof or tree.
            [unroll] for(int x=-1;x<=1;x++)
                [unroll] for(int y=-1;y<=1;y++)
                {
                    float2 sampleUV=sun+float2(x/aspect,y)*.006;
                    float depth=Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,saturate(sampleUV)));
                    visibility+=step(.99999,depth)*step(0,sampleUV.x)*step(sampleUV.x,1)*step(0,sampleUV.y)*step(sampleUV.y,1);
                }
            float2 scale=float2(aspect,1);
            float2 delta=(uv-sun)*scale;
            float halo=.27*exp(-dot(delta,delta)*105);
            float2 axis=.5-sun;
            float3 artifacts=_ShaftColor.rgb*halo;
            [unroll] for(int i=0;i<3;i++)
            {
                float2 offset=(uv-(.5+axis*(.30+i*.65)))*scale;
                float radius=length(offset);
                float size=.026+i*.017;
                float ring=1-smoothstep(.001,size*.40,abs(radius-size));
                float core=1-smoothstep(0,size,radius);
                float3 tint=lerp(float3(.40,.70,1),float3(1,.62,.32),i*.5);
                artifacts+=tint*(ring*.032+core*.07);
            }
            return artifacts*(_FlareStrength*visibility/9);
        }

        float4 FlareOnly(v2f_img i) : SV_Target
        {
            float4 scene=tex2D(_MainTex,i.uv);
            return float4(scene.rgb+LensArtifacts(ScreenUV(i.uv)),scene.a);
        }

        float4 Composite(v2f_img i) : SV_Target
        {
            float4 scene=tex2D(_MainTex,i.uv);
            float mask=tex2D(_ShaftMask,ScreenUV(i.uv)).r;
            return float4(scene.rgb+mask*_ShaftColor.rgb*_ShaftStrength+LensArtifacts(ScreenUV(i.uv)),scene.a);
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Mask
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Blur
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Composite
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment FlareOnly
            ENDCG
        }
    }
    Fallback Off
}
