// GMConverter mesh particles: a particle's model drawn with its transform as the model matrix. The
// particle's values arrive through the ambient cube, set before each draw:
//   +X = colour rgb (linear, may exceed 1), -X = (alpha, age, -), +Y = parameters xyz, -Y = (parameter w, -, -), +Z = (time, -, -)
#include "common_vs_fxc.h"

struct VS_INPUT {
    float4 vPos : POSITION;
    float2 uv   : TEXCOORD0;
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
    o.color = float4(cAmbientCubeX[0], cAmbientCubeX[1].x);
    o.params = float4(cAmbientCubeY[0], cAmbientCubeY[1].x);
    o.misc = float4(cAmbientCubeZ[0].x, cAmbientCubeX[1].y, 0, 0);
    return o;
}
