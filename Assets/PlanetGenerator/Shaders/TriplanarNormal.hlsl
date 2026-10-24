#ifndef PLANET_TRIPLANAR_NORMAL_INCLUDED
#define PLANET_TRIPLANAR_NORMAL_INCLUDED

// 本文件只负责：Object-Space 三向投影 Normal Map → Object-Space 细节法线。
// 输入：
//   positionOS : 已形变后的物体空间位置（此处调用前已经 / _Radius）。
//   normalOS   : 由高度梯度恢复的真实几何法线；用于投影权重和基底。
//   tiling     : 单位半径尺度上的贴图重复次数。
//   sharpness  : 哪个投影占主导的权重指数，越大切换越干脆。
//   strength   : 细节凹凸强度，0=原始几何法线。
// 输出：Object-Space 法线，调用方必须再 TransformObjectToWorldNormal。
//
// 与直接混合三个法线不同：此处把三次切线空间 normal 的 xy 斜率
// 通过各个投影的 T/B 基底变换回 Object Space，然后叠加在几何法线上。
// 这是 Surface-Gradient 风格的轻量近似，而不是逐字移植 Ben Golus 的 RNM。

float3 SamplePlanetTriplanarNormal(float3 positionOS, float3 normalOS,
                                  float tiling, float sharpness, float strength)
{
    float3 n = normalize(normalOS);
    float3 w = pow(abs(n) + 1e-5, sharpness);
    w /= max(w.x + w.y + w.z, 1e-5);

    // 注意负面：signX/Y/Z 让 T、B、投影平面方向构成右手坐标系。
    float sx = n.x >= 0.0 ? 1.0 : -1.0;
    float sy = n.y >= 0.0 ? 1.0 : -1.0;
    float sz = n.z >= 0.0 ? 1.0 : -1.0;
    float3 p = positionOS * tiling;

    // X 面：T=(0,0,-sx)，B=(0,1,0)
    // Y 面：T=(sy,0,0)，B=(0,0,-1)
    // Z 面：T=(sz,0,0)，B=(0,1,0)
    float2 uvX = float2(-sx * p.z, p.y);
    float2 uvY = float2( sy * p.x, -p.z);
    float2 uvZ = float2( sz * p.x, p.y);

    float3 mapX = UnpackNormal(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvX));
    float3 mapY = UnpackNormal(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvY));
    float3 mapZ = UnpackNormal(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvZ));

    // xy/z 近似法线对应的坡度；限制 z 以防极端 Normal 像素造成过大值。
    float2 slopeX = mapX.xy / max(mapX.z, 0.25);
    float2 slopeY = mapY.xy / max(mapY.z, 0.25);
    float2 slopeZ = mapZ.xy / max(mapZ.z, 0.25);

    float3 perturbX = float3(0, slopeX.y, -sx * slopeX.x);
    float3 perturbY = float3(sy * slopeY.x, 0, -slopeY.y);
    float3 perturbZ = float3(sz * slopeZ.x, slopeZ.y, 0);
    float3 perturb = perturbX * w.x + perturbY * w.y + perturbZ * w.z;

    // 只保留扰动在真实几何切面上的分量，避免沿几何法线方向缩短/拉长。
    perturb -= n * dot(n, perturb);
    return normalize(n + perturb * strength);
}

#endif
