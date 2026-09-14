Shader "Cracks/Cracks"
{
	Properties 
	{
			_Color("Inner Color", Color) = (1,1,1,1)
			_CoreColor("Core Color", Color) = (1,1,1,1)
			_EdgeColor("Edge Color", Color) = (1,1,1,1)
			_EdgeHeight("Edge Height", Range(0.0, 1.0)) = 0.97
			[NoScaleOffset] _MainTex ("Inner Texture (RGB)", 2D) = "white" {}
			_Emission("Emission Amount", Float) = 0
			_EmissionColor("Emission Color", Color) = (1,1,1,1)
			[NoScaleOffset] _EmissionTex("Emission Texture", 2D) = "white" {}
			[NoScaleOffset] _Mask("Mask Texture", 2D) = "white" {}
			[HideInInspector] _ParallaxMap ("Parallax Map", 2D) = "white" {}
			_Parallax ("Height Scale", Range (0.005, 0.1)) = 0.08
			_ParallaxSamples ("Parallax Samples", Range (10, 500)) = 40
			_CrackAmount("Cracks Size", Float) = 64.0
			_CrackThickness("Crack Thickness", Float) = 0.1
			_NoiseOffset("Noise Offset", Vector) = (0.0, 0.0, 0.0, 0.0)
	}
	SubShader 
	{
		Tags{ "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma target 3.0
			#include "UnityCG.cginc"

			sampler2D _MainTex;	
			sampler2D _ParallaxMap;
			sampler2D _Mask;
			sampler2D _EmissionTex;
			float _Parallax;
			float _ParallaxSamples;
			float _EdgeHeight;
			float _CrackAmount;
			float _CrackThickness;
			float _Emission;
			float2 _NoiseOffset;
			fixed4 _Color;
			fixed4 _CoreColor;
			fixed4 _EdgeColor;
			fixed4 _EmissionColor;
			uniform float4 _LightColor0;

			struct appdata 
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 texcoord : TEXCOORD0;
				float4 tangent : TANGENT;
			};
			
			struct v2f 
			{
				float4 pos : SV_POSITION;
				float2 tex : TEXCOORD0;
				float4 posWorld : TEXCOORD1;
				float3 tSpace0 : TEXCOORD2;
				float3 tSpace1 : TEXCOORD3;
				float3 tSpace2 : TEXCOORD4;
				float3 normal : TEXCOORD5;
				UNITY_FOG_COORDS(7)
			};

			float random(float2 uv)
			{
				return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453123);
			}

			float4 permute(float4 x)
			{
				return fmod(34.0 * pow(x, 2) + x, 289.0);
			}

			float2 fade(float2 t) {
				return 6.0 * pow(t, 5.0) - 15.0 * pow(t, 4.0) + 10.0 * pow(t, 3.0);
			}

			float4 taylorInvSqrt(float4 r) {
				return 1.79284291400159 - 0.85373472095314 * r;
			}

			#define DIV_289 0.00346020761245674740484429065744f

			float mod289(float x) {
				return x - floor(x * DIV_289) * 289.0;
			}

			float PerlinNoise(float2 P)
			{
				float4 Pi = floor(P.xyxy) + float4(0.0, 0.0, 1.0, 1.0);
				float4 Pf = frac(P.xyxy) - float4(0.0, 0.0, 1.0, 1.0);

				float4 ix = Pi.xzxz;
				float4 iy = Pi.yyww;
				float4 fx = Pf.xzxz;
				float4 fy = Pf.yyww;

				float4 i = permute(permute(ix) + iy);

				float4 gx = frac(i / 41.0) * 2.0 - 1.0;
				float4 gy = abs(gx) - 0.5;
				float4 tx = floor(gx + 0.5);
				gx = gx - tx;

				float2 g00 = float2(gx.x, gy.x);
				float2 g10 = float2(gx.y, gy.y);
				float2 g01 = float2(gx.z, gy.z);
				float2 g11 = float2(gx.w, gy.w);

				float4 norm = taylorInvSqrt(float4(dot(g00, g00), dot(g01, g01), dot(g10, g10), dot(g11, g11)));
				g00 *= norm.x;
				g01 *= norm.y;
				g10 *= norm.z;
				g11 *= norm.w;

				float n00 = dot(g00, float2(fx.x, fy.x));
				float n10 = dot(g10, float2(fx.y, fy.y));
				float n01 = dot(g01, float2(fx.z, fy.z));
				float n11 = dot(g11, float2(fx.w, fy.w));

				float2 fade_xy = fade(Pf.xy);
				float2 n_x = lerp(float2(n00, n01), float2(n10, n11), fade_xy.x);
				float n_xy = lerp(n_x.x, n_x.y, fade_xy.y);
				return n_xy + 0.25;
			}

			float Turbulence(float x, float y, float size, float2 offset) {
				x *= 512;
				y *= 512;
				float val = 0.0;
				float initSize = size;
				while (size >= 1) {
					val += PerlinNoise(float2(x / size, y / size)) * size;
					size *= 0.5;
				}
				
				val /= initSize;
				float sub1 = (val - 0.5);
				float sub2 = (0.5 - val);
				val = sub1 > sub2 ? sub1 : sub2;
				val -= _CrackThickness;
				val *= 64;

				return val > 0.5 ? 1 : 0;
			}

			v2f vert( appdata v ) 
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				UNITY_TRANSFER_FOG(o, o.pos);
				o.posWorld = mul(unity_ObjectToWorld, v.vertex);
				fixed3 worldNormal = UnityObjectToWorldNormal(v.normal);
				fixed3 worldTangent =  normalize(mul((float3x3)unity_ObjectToWorld, v.tangent.xyz));
				fixed3 worldBinormal = cross(worldNormal, worldTangent) * v.tangent.w; 
				o.tSpace0 = float3(worldTangent.x, worldBinormal.x, worldNormal.x);
				o.tSpace1 = float3(worldTangent.y, worldBinormal.y, worldNormal.y);
				o.tSpace2 = float3(worldTangent.z, worldBinormal.z, worldNormal.z);
				o.tex = v.texcoord;
				o.normal=v.normal;
				return o;
			}

			float4 frag( v2f i ): SV_TARGET 
			{
				float3 normalDirection = normalize(i.normal);
				fixed3 worldViewDir = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
				fixed3 viewDir = i.tSpace0.xyz * worldViewDir.x + i.tSpace1.xyz * worldViewDir.y  + i.tSpace2.xyz * worldViewDir.z;
				float2 vParallaxDirection = normalize(viewDir.xy);
				float fLength = length(viewDir);
				float fParallaxLength = sqrt(fLength * fLength - viewDir.z * viewDir.z) / viewDir.z;
				float2 vParallaxOffsetTS = vParallaxDirection * fParallaxLength * _Parallax;   
				float nMinSamples = 6;
				float nMaxSamples = min(_ParallaxSamples, 500);
				int nNumSamples = (int)(lerp(nMinSamples, nMaxSamples, 1-dot(worldViewDir, i.normal)));
				float fStepSize = 1.0 / (float)nNumSamples;   
				int nStepIndex = 0;
				float fCurrHeight = 0.0;
				float fPrevHeight = 1.0;
				float2 vTexOffsetPerStep = fStepSize * vParallaxOffsetTS;
				float2 vTexCurrentOffset = i.tex.xy;
				float fCurrentBound = 1.0;
				float fParallaxAmount = 0.0;
				float2 pt1 = 0;
				float2 pt2 = 0;
				float2 dx = ddx(i.tex.xy);
				float2 dy = ddy(i.tex.xy);

				float3 color = fixed4(1, 1, 1, 1);

				float2 offset = float2(random(i.tex.xy), random(i.tex.yx));

				for (nStepIndex = 0; nStepIndex < nNumSamples; nStepIndex++)
				{
					vTexCurrentOffset -= vTexOffsetPerStep;
					float msk = tex2D(_Mask, vTexCurrentOffset, dx, dy).r;
					if (msk == 0) {
						fCurrHeight = 1;
					}
					else {
						fCurrHeight = Turbulence(vTexCurrentOffset.x + _NoiseOffset.x, vTexCurrentOffset.y + _NoiseOffset.y, _CrackAmount, offset);
					}
					
					fCurrentBound -= fStepSize;
					if ( fCurrHeight > fCurrentBound ) 
					{   
						pt1 = float2( fCurrentBound, fCurrHeight );
						pt2 = float2( fCurrentBound + fStepSize, fPrevHeight );
						nStepIndex = nNumSamples + 1;
						fPrevHeight = fCurrHeight;
					}
					else
					{
						fPrevHeight = fCurrHeight;
					}
				}  
				float fDelta2 = pt2.x - pt2.y;
				float fDelta1 = pt1.x - pt1.y;  
				float fDenominator = fDelta2 - fDelta1;
				if ( fDenominator == 0.0f )
				{
					fParallaxAmount = 0.0f;
				}
				else
				{
					fParallaxAmount = (pt1.x * fDelta2 - pt2.x * fDelta1 ) / fDenominator;
				}
				i.tex.xy -= vParallaxOffsetTS * (1 - fParallaxAmount );
				float alpha = _Color.a;
				float4 tex = tex2D(_MainTex, i.tex);
				tex = lerp(tex * _CoreColor, tex * _Color, fParallaxAmount);
				if (fParallaxAmount > 0.99) {
					// URP has no GrabPass. Sampling the grabbed background and blending it
					// back with SrcAlpha/OneMinusSrcAlpha reproduces the destination exactly,
					// so writing alpha 0 gives the same pixel without the fullscreen copy.
					alpha = 0;
				}
				else {
					color += (tex2D(_EmissionTex, i.tex) * _EmissionColor) * _Emission;
					UNITY_APPLY_FOG(i.fogCoord, color);
				}
				if (fParallaxAmount > _EdgeHeight && fParallaxAmount < 0.99) {
					color += _EdgeColor * fParallaxAmount;
				}

				fixed4 col = float4(tex.xyz * color, alpha);
				return col;
			} 		

			ENDCG
		}
	}
}