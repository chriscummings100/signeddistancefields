Shader "SDF/DistanceField" 
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BorderWidth("BorderWidth", Float) = 0.5
        _Background("Background", Color) = (0,0,0.25,1)
        _Border("Border", Color) = (0,1,0,1)
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

            //texture info
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;

            //field controls
            float _BorderWidth;

            //colours
            float4 _Background;
            float4 _Border;
                        
            //simple vertex shader that fiddles with vertices + uvs of a quad to work nicely in the demo
            v2f vert(appdata v)
            {
                v2f o;
                v.vertex.y *= _MainTex_TexelSize.x * _MainTex_TexelSize.w; //stretch quad to maintain aspect ratio
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv = 1 - o.uv; //flip uvs
                return o;
            }

            //distance field fragment shader
			fixed4 frag (v2f i) : SV_Target
			{
				//sample distance field
				float4 sdf = tex2D(_MainTex, i.uv);

				//return border or background colour depending on distance from geometry
				float d = sdf.r;
				if (d < _BorderWidth)
					return _Border;
				else
					return _Background;
			}
            ENDCG
        }
    }
}
