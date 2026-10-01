// Lit terrain coloured by the mesh's vertex colours (InfiniteWorld chunks).
Shader "Skybound/VertexColorTerrain"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        struct Input
        {
            float4 color : COLOR;
        };

        half _Glossiness;

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = IN.color.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
