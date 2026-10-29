// 4B TUNES - SIMPLE-DIVERSE 원근 투영 공통 함수
//  Assets/Resources/Shaders/ 폴더에 셰이더들과 같이 넣어 주세요.
//  값은 DiversePlayfield.cs 가 재질마다 매 프레임 넣어 준다 (각 셰이더 Properties 에 선언됨).
#ifndef DIVERSE_PROJECT_INCLUDED
#define DIVERSE_PROJECT_INCLUDED

float4 _DivCamPos;   // 가상 카메라 위치
float4 _DivCamF;     // 앞 방향
float4 _DivCamU;     // 위 방향
float4 _DivCamR;     // 오른쪽 방향
float4 _DivProj;     // x: 1/(tan*aspect)  y: 1/tan
float4 _DivFade;     // x: 페이드 시작 월드 y  y: 완전히 사라지는 월드 y
float4 _DivClip;     // x: 판정선 (이보다 아래는 _ClipBelow 일 때 잘림)

// 월드 좌표 → 클립 좌표 (w = 깊이 → 텍스처가 원근에 맞게 보간됨)
inline float4 DiverseProject(float3 wp)
{
    float3 r = wp - _DivCamPos.xyz;
    float z = max(dot(r, _DivCamF.xyz), 0.001);
    float x = dot(r, _DivCamR.xyz) * _DivProj.x;
    float y = dot(r, _DivCamU.xyz) * _DivProj.y;
    return float4(x, y * _ProjectionParams.x, 0.5 * z, z);
}

// 먼 쪽 끝에서 부드럽게 사라짐
inline float DiverseFade(float worldY)
{
    float t = saturate((worldY - _DivFade.x) / max(_DivFade.y - _DivFade.x, 0.0001));
    return 1.0 - t * t * (3.0 - 2.0 * t);
}

#endif
