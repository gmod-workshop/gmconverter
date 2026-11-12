// Unreal's GGX shading (MaterialShading.UnrealGgx), as CrossFire HD's compiled UE3 shaders compute it,
// lit by what Source gives a model: its ambient cube, up to four lights, and the map's cubemap.
//
// Units: Source lights a surface as colour * N.L * albedo, the Lambert term CrossFire HD writes as
// colour * N.L * diffuse / pi, so a Source light's colour is pi times CrossFire HD's; the ambient cube
// is already the diffuse light per unit albedo (CrossFire HD's irradiance / pi).

static const float PI = 3.14159265;

// The ambient cube for a world-space normal (Source's AmbientLight).
float3 AmbientCube(float3 n)
{
    float3 n2 = n * n;
    return n2.x * (n.x >= 0 ? g_AmbientCube[0] : g_AmbientCube[1])
         + n2.y * (n.y >= 0 ? g_AmbientCube[2] : g_AmbientCube[3])
         + n2.z * (n.z >= 0 ? g_AmbientCube[4] : g_AmbientCube[5]);
}

// Source's pixel-shader lights: three as colour and position, the fourth in their w components.
float3 LightColor(int i)
{
    return i == 3 ? float3(g_Lights[0].w, g_Lights[2].w, g_Lights[4].w) : g_Lights[min(i, 2) * 2].rgb;
}

float3 LightPosition(int i)
{
    return i == 3 ? float3(g_Lights[1].w, g_Lights[3].w, g_Lights[5].w) : g_Lights[min(i, 2) * 2 + 1].xyz;
}

// One light: N.L * (diffuse / pi + F * D * Vis), with D limited to `distributionLimit`.
float3 DirectGgx(float3 n, float3 v, float3 l, float3 diffuse, float3 f0, float roughness, float distributionLimit)
{
    float nl = saturate(dot(n, l));
    float nv = max(dot(n, v), 0.00001);
    float3 h = normalize(v + l);
    float vh = saturate(dot(v, h));
    float nh = saturate(dot(n, h));
    float a = roughness * roughness;

    float d = a / ((a * nh) * (a * nh) + 1 - nh * nh);
    d = min(distributionLimit, max(d, 0) * max(d, 0) / PI);
    float vis = 0.5 / (nl * (nv * (1 - a) + a) + nv * (nl * (1 - a) + a));
    float fc = pow(1 - vh, 5);
    float3 f = lerp(f0, saturate(50 * f0.g), fc);
    return nl * (diffuse / PI + f * (d * vis));
}

// The shaded surface, without its emission.
float3 ShadeUnrealGgx(float3 n, float3 v, float3 world, float4 atten, float3 baseColor, float metallic, float specular, float roughness, float3 reflection)
{
    metallic = saturate(metallic);
    roughness = roughness >= 0.004 ? saturate(roughness) : 0.004;
    float3 diffuse = baseColor * (1 - metallic);
    float3 f0 = lerp(0.08 * specular, baseColor, metallic);

    float3 color = diffuse * AmbientCube(n);
    float lightAtten[4] = { atten.x, atten.y, atten.z, atten.w };
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float3 l = normalize(LightPosition(i) - world);
        color += PI * LightColor(i) * lightAtten[i] * DirectGgx(n, v, l, diffuse, f0, roughness, g_Shading.y);
    }

    // Reflections, weighed by the model integrated over the hemisphere (split-sum).
    float2 brdf = tex2Dlod(EnvironmentBrdf, float4(roughness, saturate(dot(n, v)), 0, 0)).rg;
    color += (f0 * brdf.x + saturate(50 * f0.g) * brdf.y) * reflection * g_Shading.z;
    return color;
}

// The scene's reflection along the specular dominant direction, at a mip for the roughness.
float3 SceneReflection(float3 n, float3 v, float roughness)
{
    roughness = roughness >= 0.004 ? saturate(roughness) : 0.004;
    float3 r = 2 * dot(n, v) * n - v;
    float3 direction = lerp(n, r, (1 - roughness) * (sqrt(1 - roughness) + roughness));
    return texCUBElod(SceneProbe, float4(direction, roughness * g_Shading.w)).rgb * ENV_MAP_SCALE;
}
