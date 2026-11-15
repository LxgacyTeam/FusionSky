Shader "BigCityLegacy/Sky/Dynamic"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (.4,.49,.7,1)
        _Horizon ("Horizon", Color) = (.86,.66,.74,1)
        _Ground ("Ground", Color) = (.2,.22,.3,1)
        _CloudColor ("Clouds", Color) = (.8,.75,.8,1)
        _WarmColor ("Sun glow", Color) = (1,.65,.76,1)
        _SunDiscColor ("Solar disc", Color) = (1,1,.95,1)
        _SunDirection ("Sun direction", Vector) = (0,.1,1,0)
        _MoonDirection ("Moon direction", Vector) = (0,-.1,-1,0)
        _CloudCoverage ("Cloud coverage", Range(0,1)) = .35
        _CloudDensity ("Cloud density", Range(0,1)) = .55
        _CloudPhase ("Cloud movement", Float) = 0
        _CloudScale ("Cloud scale", Float) = 1
        _CloudDirection ("Cloud direction", Float) = 0
        _SunVisibility ("Sun visibility", Range(0,1)) = 1
        _SunRadius ("Sun radius (chord)", Float) = .005236
        _SunSoftness ("Sun edge softness", Range(0,1)) = .33
        _SunYellow ("Solar disc warmth", Range(0,1)) = 0
        _CloudShade ("Cloud shadow", Color) = (.2,.25,.3,1)
        _CloudLighting ("Cloud lighting", Range(0,1)) = 0
        _SkyDepth ("Twilight depth", Range(0,2)) = 0
        _Daylight ("Daylight", Range(0,1)) = 1
        _MoonRadius ("Moon radius (chord)", Float) = .01047
        _MoonSoftness ("Moon edge softness", Range(0,1)) = .45
        _MoonColor ("Moon color", Color) = (.7,.8,1,1)
        _MoonBrightness ("Moon brightness", Float) = 1
        _MoonVisibility ("Moon disk visibility", Range(0,1)) = 0
        _Stars ("Night stars", Range(0,2)) = 0
        _Exposure ("Exposure", Range(0,3)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Zenith,_Horizon,_Ground,_CloudColor,_WarmColor,_SunDiscColor,_SunDirection,_MoonDirection;
            float _CloudCoverage,_CloudDensity,_CloudPhase,_SunVisibility,_MoonVisibility,_Stars,_Exposure,_CloudScale,_CloudDirection;
            float _SunRadius,_SunSoftness,_SunYellow,_CloudLighting,_SkyDepth,_Daylight;
            float _MoonRadius,_MoonSoftness,_MoonBrightness;
            float4 _CloudShade,_MoonColor;
            struct appdata { float4 vertex:POSITION; };
            struct v2f { float4 vertex:SV_POSITION; float3 direction:TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex);o.direction=v.vertex.xyz;return o; }
            float hash(float3 p)
            {
                p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);
            }
            float noise(float3 p)
            {
                float3 c=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash(c),hash(c+float3(1,0,0)),f.x),lerp(hash(c+float3(0,1,0)),hash(c+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(hash(c+float3(0,0,1)),hash(c+float3(1,0,1)),f.x),lerp(hash(c+float3(0,1,1)),hash(c+1),f.x),f.y),f.z);
            }
            float disc(float distanceToLight,float radius,float softness)
            {
                float width=max(fwidth(distanceToLight),radius*softness*(1+softness));
                return 1-smoothstep(max(0,radius-width*.5),radius+width*.5,distanceToLight);
            }
            float4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.direction);
                float h=saturate(d.y);
                float3 color=lerp(_Horizon.rgb,_Zenith.rgb,pow(saturate((d.y+.035*_SkyDepth)/(1+.035*_SkyDepth)),.68+.21*_SkyDepth));
                float3 p=d*(4.3/max(.25,_CloudScale))+float3(_CloudPhase*cos(_CloudDirection),0,3.2+_CloudPhase*sin(_CloudDirection));
                float n=noise(p)*.57+noise(p*2.03+17.1)*.27+noise(p*4.09+31.7)*.11+noise(p*8.17+5.4)*.05;
                float threshold=lerp(.85,.18,_CloudCoverage);
                float cloud=smoothstep(threshold,threshold+.15,n)*_CloudDensity*smoothstep(.01,.25,h)*step(.0001,_CloudCoverage);
                float cloudFacing=lerp(saturate(dot(d,_MoonDirection.xyz)*.5+.5),saturate(dot(d,_SunDirection.xyz)*.5+.5),_Daylight);
                float cloudLight=saturate(.80-.25*cloud+pow(cloudFacing,8)*.48);
                float3 litCloud=lerp(_CloudColor.rgb,lerp(_CloudShade.rgb,_CloudColor.rgb,cloudLight),_CloudLighting);
                color=lerp(color,litCloud,cloud);
                float3 sunDirection=normalize(_SunDirection.xyz);
                float facing=dot(d,sunDirection);
                float glow=pow(saturate(facing*.5+.5),12)*exp(-h*4)*.45*_SunVisibility;
                color=lerp(color,_WarmColor.rgb,glow);
                float distanceToSun=length(d-sunDirection);
                float sun=disc(distanceToSun,_SunRadius,_SunSoftness)*_SunVisibility;
                // Softer discs have a lower peak so the edge remains visible even after tone mapping.
                float discIntensity=lerp(5,.9,_SunSoftness*_SunSoftness);
                discIntensity=lerp(discIntensity,min(discIntensity,1.5),_SunYellow);
                color=lerp(color,_SunDiscColor.rgb*discIntensity,sun*(1-cloud*.92));
                float moonDistance=length(d-normalize(_MoonDirection.xyz));
                float moon=disc(moonDistance,_MoonRadius,_MoonSoftness)*_MoonVisibility*saturate(_MoonDirection.y*8);
                float moonIntensity=lerp(.85,.45,_MoonSoftness*_MoonSoftness)*_MoonBrightness;
                color=lerp(color,_MoonColor.rgb*moonIntensity,moon*(1-cloud*.92)*saturate(_MoonBrightness));
                float star=pow(saturate((hash(floor(d*550))-.9985)/.0015),8);
                color+=star*_Stars*smoothstep(.02,.3,h)*(1-cloud)*.8;
                color=lerp(color,_Ground.rgb,smoothstep(0,.35,-d.y));
                return float4(color*_Exposure,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
