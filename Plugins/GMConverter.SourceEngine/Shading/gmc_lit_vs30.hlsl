// GMConverter lit models: the model's skinned surface for the generated pixel shaders. The engine's
// lighting for the model (ambient cube, up to four lights) is committed by the Lua runtime drawing a
// hidden VertexLitGeneric model at the same place just before; each light's falloff is taken here,
// as Source's own bump-mapped models do.
#include "common_vs_fxc.h"

struct VS_INPUT {
    float4 vPos    : POSITION;
    float4 vNormal : NORMAL;
    float2 uv      : TEXCOORD0;
};

struct VS_OUTPUT {
    float4 proj   : POSITION;
    float2 uv     : TEXCOORD0;
    float3 world  : TEXCOORD1;
    float3 normal : TEXCOORD2;
    float3 eye    : TEXCOORD3;
    float4 atten  : TEXCOORD4;
};

VS_OUTPUT main(VS_INPUT v) {
    float3 world, normal;
    SkinPositionAndNormal(0, v.vPos, v.vNormal.xyz, 0, 0, world, normal);
    VS_OUTPUT o = (VS_OUTPUT)0;
    o.proj = mul(float4(world, 1), cViewProj);
    o.uv = v.uv;
    o.world = world;
    o.normal = normal;
    o.eye = cEyePos;
    o.atten = float4(
        GetVertexAttenForLight(world, 0, true),
        GetVertexAttenForLight(world, 1, true),
        GetVertexAttenForLight(world, 2, true),
        GetVertexAttenForLight(world, 3, true));
    return o;
}
