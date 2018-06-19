Shader "SDF/SignedDistanceField" 
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Gradient ("Gradient", 2D) = "white" {}
		_Mode("Mode", Int) = 0
		_Grid("Grid", Float) = 0
		_Offset("Offset", Float) = 0
		_BorderWidth("BorderWidth", Float) = 1
		_Background("Background", Color) = (0,0,0.25,1)
		_Fill("Fill", Color) = (1,0,0,1)
		_Border("Border", Color) = (0,1,0,1)
		_DistanceVisualisationScale("DistanceVisualisationScale", Float) = 1
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag		
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			sampler2D _Gradient;
			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;
			int _Mode;
			float _Grid;
			float _Offset;
			float _BorderWidth;
			float4 _Background;
			float4 _Fill;
			float4 _Border;
			float _DistanceVisualisationScale;
			
			v2f vert (appdata v)
			{
				v2f o;
				v.vertex.y *= _MainTex_TexelSize.x * _MainTex_TexelSize.w;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
			
			//takes a pixel colour from the sdf texture and returns the output colour
			float4 sdffunc(float4 sdf)
			{
				float4 res = _Background;

				if (_Mode == 1) //Raw
				{
					return sdf;
				}
				else if (_Mode == 2) //Distance
				{
					//render colour for distance for valid pixels
					float d = sdf.r*_DistanceVisualisationScale;
					res.r = saturate(d);
					res.g = saturate(-d);
					res.b = 0;
				}
				else if (_Mode == 3) //Gradient
				{
					res.rg = abs(sdf.gb);
					res.b = 0;
				}
				else if (_Mode == 4) //Solid
				{
					float d = sdf.r + _Offset;
					if (d < 0)
						res = _Fill;
				}
				else if (_Mode == 5) //Border
				{
					float d = sdf.r + _Offset;
					if (abs(d) < _BorderWidth)
					{
						res = _Border;
					}
				}
				else if (_Mode == 6) //SolidWithBorder
				{
					float d = sdf.r + _Offset;
					if (abs(d) < _BorderWidth)
					{
						res = _Border;
					}
					else if (d < 0)
					{
						res = _Fill;
					}
				}

				res.rgb *= res.a;
				res.a = 1;


				return res;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				//sample distance field
				float4 sdf = tex2D(_MainTex, i.uv);
				fixed4 res = sdffunc(sdf);

				//blend in grid
				if (_Grid > 0)
				{
					float2 gridness = cos(3.1415926 * i.uv * _MainTex_TexelSize.zw);
					gridness = abs(gridness);
					gridness = pow(gridness,100);
					gridness *= _Grid;
					res = lerp(res, fixed4(0, 0, 0, 1), max(gridness.x,gridness.y));
				}

				return res;
			}
			ENDCG
		}
	}
}
