// GMConverter sprite particles: quads the effect runtime builds each frame in world space, each vertex
// carrying its particle's values. Time arrives through the ambient cube, set once per draw.
#include "common_vs_fxc.h"

struct VS_INPUT {
    float4 vPos     : POSITION;
    float4 vNormal  : NORMAL;    // particle colour, linear RGB (may exceed 1)
    float2 uv       : TEXCOORD0;
    float2 alphaAge : TEXCOORD1; // alpha, age 0 to 1
    float2 paramsXY : TEXCOORD2;
    float2 paramsZW : TEXCOORD3;
};

struct VS_OUTPUT {
    float4 proj   : POSITION;
    float2 uv     : TEXCOORD0;
    float4 color  : TEXCOORD1;
    float4 params : TEXCOORD2;
    float4 misc   : TEXCOORD3;
};

VS_OUTPUT main(VS_INPUT v) {
    float3 world;
    SkinPosition(0, v.vPos, 0, 0, world);
    VS_OUTPUT o = (VS_OUTPUT)0;
    o.proj = mul(float4(world, 1), cViewProj);
    o.uv = v.uv;
    o.color = float4(v.vNormal.xyz, v.alphaAge.x);
    o.params = float4(v.paramsXY, v.paramsZW);
    o.misc = float4(cAmbientCubeZ[0].x, v.alphaAge.y, 0, 0);
    return o;
}
