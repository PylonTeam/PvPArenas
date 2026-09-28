sampler image0 : register(s0);

float globalTime;
float3 borderColor;
float opacity;
float2 borderSize;
float outerEdgeFade;
float innerEdgeFade;
float pulseStrength;
float shimmerStrength;
float shimmerScale;
float shimmerSpeed;

float SmootherStep(float edge0, float edge1, float value)
{
    float t = saturate((value - edge0) / (edge1 - edge0));
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float4 PixelShaderFunction(float4 sampleColor : COLOR0, float2 coords : TEXCOORD0) : COLOR0
{
    // The caller draws a full quad around the box. Keep only its border band;
    // the interior must stay transparent regardless of the box's dimensions.
    float2 edgeDistance = min(coords, 1.0 - coords) / max(borderSize, 0.000001);
    float distance = min(edgeDistance.x, edgeDistance.y);
    float borderMask = SmootherStep(0.0, max(outerEdgeFade, 0.0001), distance)
        * SmootherStep(0.0, max(innerEdgeFade, 0.0001), 1.0 - distance);

    float4 tex = tex2D(image0, coords);
    float pulse = 0.5 + 0.5 * sin(globalTime * 0.075);
    float shimmer = 0.5 + 0.5 * sin((coords.x + coords.y) * shimmerScale + globalTime * shimmerSpeed);
    float glow = 1.0 + pulse * pulseStrength + shimmer * shimmerStrength;
    float3 color = borderColor * glow;
    float alpha = tex.a * sampleColor.a * opacity * borderMask;
    return float4(color * alpha, alpha);
}

technique t0
{
    pass p0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
