Shader "SDF/SignedDistanceField" 
{

	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
			#pragma target 5.0
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
            sampler2D _ArrowTex;
            float _ArrowOpacity;
            float _ArrowTiles;

            //basic effect args (blog post 7)
            float _NeonBrightness;
            float _NeonPower;
			sampler2D _EdgeTex;
            float _ShadowDist;
            float _ShadowBorderWidth;
            float _CircleMorphRadius;
            float _CircleMorphAmount;
            sampler2D _TileTex;

            //gradient effect args (blog post 8)
            float _BevelCurvature;
            int _EdgeFindSteps;
            sampler2D _NoiseTex;
            float _NoiseAnimTime;
            float _EdgeNoiseA;
            float _EdgeNoiseB;
            int _EnableEdgeNoise;
            int _FixGradient;
		
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
                res.yz = normalize(offset_from_centre);
                res.w = 1;
                return res;
            }

            float4 tiledcircle(float2 uv)
            {
                uv.x *= 2;
                float2 centre = round(uv*10)/10;
                return float4(length(uv-centre)<0.04,0,0,1);
            }
            //samples the animated noise texture
            float samplenoise(float2 uv)
            {
                float t = frac(_NoiseAnimTime)*2.0f*3.1415927f;
                float k = 0.5f*3.1415927f;
                float4 sc = float4(0,k,2*k,3*k)+t;
                float4 sn = (sin(sc)+1)*0.4;       
                return dot(sn,tex2D(_NoiseTex,uv));
            }

            //cut down version of samplesdf that ignores gradient
            float samplesdfnograd(float2 uv)
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
                
                //if edge based noise is on, adjust distance by sampling noise texture
                if(_EnableEdgeNoise)
                {
                    sdf.r += lerp(_EdgeNoiseA,_EdgeNoiseB,samplenoise(uv));
                }

                return sdf;      
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

                //re-normalize gradient in gb components as bilinear filtering
                //and the morph can mess it up 
                sdf.gb = normalize(sdf.gb);
                
                //if edge based noise is on, adjust distance by sampling noise texture
                if(_EnableEdgeNoise)
                {
                    sdf.r += lerp(_EdgeNoiseA,_EdgeNoiseB,samplenoise(uv));
                }

                
                //if requested, overwrite sampled gradient with one calculated live in
                //shader that takes into account morphing and noise
                if(_FixGradient)
                {
                    float d = sdf.r;
                    float sign = d > 0 ? 1 : -1;
                    float x0 = samplesdfnograd(uv+_MainTex_TexelSize.xy*float2(-1,0));
                    float x1 = samplesdfnograd(uv+_MainTex_TexelSize.xy*float2(1,0));
                    float y0 = samplesdfnograd(uv+_MainTex_TexelSize.xy*float2(0,-1));
                    float y1 = samplesdfnograd(uv+_MainTex_TexelSize.xy*float2(0,1));
               
                    float xgrad = sign*x0 < sign*x1 ? -(x0-d) : (x1-d);
                    float ygrad = sign*y0 < sign*y1 ? -(y0-d) : (y1-d);
                
                    sdf.gb = float2(xgrad,ygrad);
                }
                

                return sdf;
            }
		
            //from http://www.chilliant.com/rgb2hsv.html 
            //gets rgb from a hue, used in gradient visualisation
            float3 HUEtoRGB(in float H)
            {
                float R = abs(H * 6 - 3) - 1;
                float G = 2 - abs(H * 6 - 2);
                float B = 2 - abs(H * 6 - 4);
                return saturate(float3(R,G,B));
            }

            //given an input field sample and uv, moves and resamples closer to the edge
            void steptowardsedge(inout float4 edgesdf, inout float2 edgeuv)
            {
                edgeuv -= edgesdf.gb*edgesdf.r*_MainTex_TexelSize.xy;
                edgesdf = samplesdf(edgeuv);
            }
            bool sampleedge(float4 sdf, float2 uv, out float4 edgesdf, out float2 edgeuv, out int steps)
            {
                edgesdf = sdf;
                edgeuv = uv;
                steps = 0;

                [unroll(8)]
                for(int i = 0; i < _EdgeFindSteps; i++)
                {
                    if(abs(edgesdf.r) < 0.5)
                        break;
                    steptowardsedge(edgesdf,edgeuv);
                    steps++;
                }

                return abs(edgesdf.r) < 0.5;
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
					float d = sdf.r;
                    float2 grad = sdf.gb;
					res.rgb = HUEtoRGB((1+atan2(grad.y,grad.x)/3.1415926f)*0.5);
                    //res.gb = (grad+1)*0.5; res.r = 0;
                    res.rgb *= 0.5+0.5*saturate(abs(d));
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
                else if(_Mode == 11) //Bevel
                {
                    //sample distance as normal
                    float d = sdf.r + _Offset;

                    //choose a light direction (just [0.5,0.5]), then dot product
                    //with the gradient to get a brightness
                    float2 lightdir = normalize(float2(0.5,0.5));
                    float diffuse = saturate(dot(sdf.gb,-lightdir));

                    //by default diffuse is linear (flat edge). combine with border distance
                    //to fake curvy one if desired
                    float curvature = pow(saturate(d/_BorderWidth),_BevelCurvature);
                    diffuse = lerp(1,diffuse,curvature);

                    //calculate the border colour (diffuse contributes 75% of 'light')
                    float4 border_col = _Fill * (diffuse*0.75+0.25);
                    border_col.a = 1;

                    //choose output
                    if(d < 0)
                    {
                        //inside the goemetry, just use fill
                        res = _Fill;
                    }
                    else if(d < _BorderWidth)
                    {
                        //inside border, use border_col with tiny lerp across 1 pixel 
                        //to avoid aliasing
                        res = lerp(_Fill,border_col,saturate(d));
                    }
                    else
                    {
                        //outside border, use fill col, with tiny lerp across 1 pixel 
                        //to avoid aliasing 
                        res = lerp(border_col,_Background,saturate(d-_BorderWidth));
                    }

                }
                else if(_Mode == 12) //EdgeFind
                {
                    //use sampleedge to get the edge field values and uv
                    float2 edgeuv;
                    float4 edgesdf;
                    int edgesteps;
                    bool success = sampleedge(sdf,uv,edgesdf,edgeuv,edgesteps);

                    //visualize number of steps to reach edge (or black if didn't get there')
                    res.rgb = success ? HUEtoRGB(0.75f*edgesteps/8.0f) : 0;
                }
                else if(_Mode == 13) //ShowNoiseTexture
                {
                    res = samplenoise(uv);
                }
                else if(_Mode == 14) //NoiseyEdge
                {
                    //use sampleedge to get the edge field values and uv
                    float2 edgeuv;
                    float4 edgesdf;
                    int edgesteps;
                    bool success = sampleedge(sdf,uv,edgesdf,edgeuv,edgesteps);

                    sdf.r -= (samplenoise(uv)*2-1)*20;

                    //res.rg = abs(sin(edgeuv*50));
                    //res.b = 0;

                    if(sdf.r < 0)
                        res.rgb = 0;

                    //float cells = 20;
                    //float2 tilecentre = round(uv*cells)/cells;
                    //float2 tileoffset = (uv-tilecentre)*cells*2;
                    //
                    //sdf = samplesdf(tilecentre);
                    //
                    //float d = sdf.r + _Offset;
                    //float2 grad = sdf.gb;
                    //float2 grad2 = float2(-grad.y,grad.x);
                    //
                    //tileoffset -= 0.5;
                    //tileoffset = grad*tileoffset.x + grad2*tileoffset.y;
                    //tileoffset += 0.5;
                    //
                    //res.rgb = tex2D(_TileTex,tileoffset);

                    //
                    //
                    //
					////res.r = saturate(edged);
					////res.g = saturate(-edged);
					////res.b = 0;
                    ////
                    ////res.rgb = float3(abs(edged),0,0);
                    //
                    //
                    //float tiling = 4;
                    //float2 samplecentre = round(uv*tiling)/tiling;
                    //float2 sampleuv = (uv-samplecentre)*tiling;
                    //
                    //
                    //
                    ////float d = sdf.r + _Offset;
                    //float2 grad = samplesdf(samplecentre).gb;
                    //float2 grad2 = float2(-grad.y,grad.x);
                    //
                    //sampleuv = sampleuv.x*grad+sampleuv.y*grad2;
                    //
                    //res.rgb = tex2D(_TileTex,sampleuv);

                    /*float2 weights = abs(grad);
                    weights = pow(weights,5*(1-saturate(abs(d)*0.2)));
                    weights /= (weights.x+weights.y);
                    float tile = 2;

                    res = tex2D(_TileTex,uv.xy*tile)*weights.x+tex2D(_TileTex,uv.yx*tile)*weights.y;
                    */
                    //float fill_t = 1-saturate((d-_BorderWidth)/_BorderWidth);
                    //res.a = fill_t;
                    

                    //float2 suv = grad * uv.x + grad2 * uv.y;
                    //res = tiledcircle(suv);
                    ////res = float4(dot(grad,uv),0,0,1);
                    //
                    //float val = dot(grad,uv);
                    //float ruler = frac(abs(val)*10);
                    //res = float4(ruler,0,0,1);
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

                //float contour = cos(3.1415926 * sdf.r * _MainTex_TexelSize.x * 20);
                //contour = abs(contour);
                //contour = pow(contour,100);
				//res = lerp(res, fixed4(0, 0, 1, 1), contour);

                //blend in gradient arrows
                if(_ArrowOpacity > 0)
                {
                    //calculate the arrow cell/offset, then sample sdf distance and grad
                    float cells = _ArrowTiles;
                    float2 tilecentre = floor(i.uv*cells)/cells+0.5/cells;
                    float2 tileoffset = (i.uv-tilecentre)*cells*1.75;
                    float4 arrowsdf = samplesdf(tilecentre);
                    float d = arrowsdf.r + _Offset;
                    if(sign(d) == sign(sdf.r))
                    {
                        float2 grad = normalize(arrowsdf.gb);
                        float2 grad2 = float2(-grad.y,grad.x);

                        //rotate the offset using the grad
                        tileoffset = grad*tileoffset.x - grad2*tileoffset.y;
                        tileoffset += 0.5;

                        //sample arrow texture and update colour
                        float4 tilecol = tex2D(_ArrowTex,tileoffset);
                        tilecol.a *= _ArrowOpacity;
                        res.rgb = lerp(res.rgb, tilecol.rgb, tilecol.a);
                    }
                }

				return res;
			}
			ENDCG
		}
	}
}
