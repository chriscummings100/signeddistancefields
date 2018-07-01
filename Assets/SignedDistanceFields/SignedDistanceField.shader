Shader "SDF/SignedDistanceField" 
{

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

            //basic effect args (blog post 7)
            float _NeonBrightness;
            float _NeonPower;
			sampler2D _EdgeTex;
            float _ShadowDist;
            float _ShadowBorderWidth;
            float _CircleMorphRadius;
            float _CircleMorphAmount;
		
			v2f vert (appdata v)
			{
				v2f o;
				v.vertex.y *= _MainTex_TexelSize.x * _MainTex_TexelSize.w;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}

            //analytic circle function used for morph demo
            //returns the field distance and gradient for a circle at 
            //coordinate uv of a given radius (in texels)
            float4 centeredcirclesdf(float2 uv, float radius)
            {
                //calculate offset from [0.5,0.5] in uv space, then multiply
                //by _MainTex_TexelSize.zw to get offset in texels
                float2 offset_from_centre = (uv - 0.5) * _MainTex_TexelSize.zw;

                //signed distance for a circle is |offset| - radius 
                float signeddist = length(offset_from_centre) - radius;

                //build result: [signeddist,0,0,1]
                float4 res = 0;
                res.x = signeddist;
                res.yz = 0;
                res.w = 1;
                return res;
            }

            //helper to perform the sdf sample 
            float4 samplesdf(float2 uv)
            {
  	            //sample distance field
	            float4 sdf = tex2D(_MainTex, uv);

                //if we want to do the 'circle morph' effect, lerp from the sampled 
                //value to a circle value here
                if(_CircleMorphAmount > 0)
                {
                    float4 circlesdf = centeredcirclesdf(uv,_CircleMorphRadius);
                    sdf = lerp(sdf, circlesdf, _CircleMorphAmount);
                }
                
                return sdf;
            }
		
			//takes a pixel colour from the sdf texture and returns the output colour
            //uv arg is source sample pos, added in blog 7
			float4 sdffunc(float4 sdf, float2 uv)
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
					res.rg = (sdf.gb+1)*0.5;
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
                else if (_Mode == 7) //SoftBorder (Blog post 7)
                {
                    float d = sdf.r + _Offset;

                    if (d < -_BorderWidth)
                    {
                        //if inside shape by more than _BorderWidth, use pure fill
                        res = _Fill;
                    }
                    else if (d < 0)
                    {
                        //if inside shape but within range of border, lerp from fill to border colour
                        float t = -d / _BorderWidth;
                        t = t * t;
                        res = lerp(_Border, _Fill, t);
                    }
                    else if (d < _BorderWidth)
                    {
                        //if outside shape but within range of border, lerp from border to background colour
                        float t = d / _BorderWidth;
                        t = t * t;
                        res = lerp(_Border, _Background, t);
                    }
                }
                else if (_Mode == 8) //Neon (Blog post 7)
                {
                    float d = sdf.r + _Offset;

                    //only do something if within range of border
                    if (d > -_BorderWidth && d < _BorderWidth)
                    {
                        //calculate a value of 't' that goes from 0->1->0
                        //around the edge of the geometry
                        float t = d / _BorderWidth; //[-1:0:1]
                        t = 1 - abs(t);             //[0:1:0]

                        //lerp between background and border using t
                        res = lerp(_Background, _Border, t);

                        //raise t to a high power and add in as white
                        //to give bloom effect
                        res.rgb += pow(t, _NeonPower)*_NeonBrightness;
                    }
                }
                else if (_Mode == 9) //Edge Texture (Blog post 7)
                {
                    float d = sdf.r + _Offset;

                    if (d < -_BorderWidth)
                    {
                        //if inside shape by more than _BorderWidth, use pure fill
                        res = _Fill;
                    }
                    else if (d < _BorderWidth)
                    {
                        //if inside shape but within range of border, calculate a 
                        //'t' from 0 to 1
                        float t = d / _BorderWidth; //[-1:0:1]
                        t = (t+1)*0.5;             //[0:1]

                        //now use 't' as the 'u' coordinate in sampling _EdgeTex
                        res = tex2D(_EdgeTex, float2(t,0.5));
                    }
                }
                else if (_Mode == 10) //Drop shadow (Blog post 7)
                {
                    //sample distance as normal
                    float d = sdf.r + _Offset;

                    //take another sample, _ShadowDist texels up/right from the first
                    float d2 = samplesdf(uv+_ShadowDist*_MainTex_TexelSize.xy).r + _Offset;

                    //calculate interpolators (go from 0 to 1 across border)
                    float fill_t = 1-saturate((d-_BorderWidth)/_BorderWidth);
                    float shadow_t = 1-saturate((d2-_ShadowBorderWidth)/_ShadowBorderWidth);

                    //apply the shadow colour, then over the top apply fill colour
                    res = lerp(res,_Border,shadow_t);
                    res = lerp(res,_Fill,fill_t);                 
                }
                else if(_Mode == 11) //Bevel experiments
                {
                    //sample distance as normal
                    float d = sdf.r + _Offset;
                    float3 normal;

                    if(d < _BorderWidth)
                    {
                        if(d < 0)
                        {
                            normal = float3(0,0,1);
                        }
                        else
                        {
                            float2 grad = sdf.gb;
                            float zgrad = sqrt(dot(grad,grad));
                            float3 edgenormal = float3(grad,zgrad);
                            
                            normal = normalize(lerp(float3(0,0,1),edgenormal,d/_BorderWidth));
                        }

                            float diffuse = saturate(dot(normal,normalize(float3(0.5,0.5,1))));
                            //float t = d/_BorderWidth;
                            //t  = pow(t,3);
                            //t = 1-t;
                            //diffuse *= t;
                            res = _Fill * saturate(0.5+diffuse*0.5);

                    }
                }


				res.rgb *= res.a;
				res.a = 1;


				return res;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				//sample distance field
				float4 sdf = samplesdf(i.uv);

				fixed4 res = sdffunc(sdf,i.uv);

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
