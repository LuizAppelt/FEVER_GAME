#ifndef PS1_BG3_CUTOUT_INCLUDED
#define PS1_BG3_CUTOUT_INCLUDED

#include "PS1_StomachFog.hlsl"

// Uniforms globais atualizados pelo componente BG3WallCutoutController ([ExecuteAlways])
float4 _BG3CamPos;       // xyz = Posicao da Camera no mundo, w = 1 se ativo
float4 _BG3PlayerPos;    // xyz = Posicao do foco no Player (peito), w = playerFocusHeight (>0)
float4 _BG3CutoutParams; // x = Raio perto do player, y = Multiplicador de cone perto da camera, z = Largura da borda dither, w = Desativado/Ativo

void ApplyBG3CameraCutout(float3 positionWS, float4 positionCS, float3 normalWS)
{
    if (_BG3CamPos.w < 0.5 || _BG3PlayerPos.w < 0.05 || _BG3CutoutParams.w < 0.5)
        return;

    float3 camPos = _BG3CamPos.xyz;
    float3 playerPos = _BG3PlayerPos.xyz;
    float focusHeight = max(_BG3PlayerPos.w, 0.5);
    float playerFeetY = playerPos.y - focusHeight;

    // -------------------------------------------------------------------------
    // PROTECAO TOTAL DO CHAO E SUPERFICIES CAMINHAVIS (NUNCA CORTA O PISO!)
    // -------------------------------------------------------------------------
    // 1. Qualquer vertice/pixel no nivel dos pes do jogador ou abaixo e chao/deck/terreno
    if (positionWS.y <= (playerFeetY + 0.15))
        return;

    // 2. Qualquer superficie horizontal ou degrau voltada para cima (normal.y >= 0.45)
    // na faixa de altura de caminhada (ate a cintura do jogador) e preservada intacta
    float nDotUp = normalWS.y;
    if (nDotUp >= 0.45 && positionWS.y <= (playerPos.y + 0.35))
        return;

    float3 rayVec = playerPos - camPos;
    float rayLen = max(length(rayVec), 0.001);
    float3 rayDir = rayVec / rayLen;

    float3 rel = positionWS - camPos;
    float proj = dot(rel, rayDir);
    float t = proj / rayLen;

    // Corta desde a frente imediata da lente (t >= 0) ate logo antes do jogador (nunca corta paredes atras do jogador)
    float nearMask = smoothstep(-0.04, 0.005, t);
    float farMask = smoothstep(0.985, 0.85, t);
    float segmentMask = nearMask * farMask;

    if (segmentMask <= 0.001)
        return;

    float3 closestPt = camPos + rayDir * clamp(proj, 0.0, rayLen);
    float perpDist = length(positionWS - closestPt);

    float baseRadius = max(_BG3CutoutParams.x, 0.5);
    float camConeScale = max(_BG3CutoutParams.y, 1.0);
    float feather = max(_BG3CutoutParams.z, 0.25);

    // Cone mais largo perto da camera e mais focado proximo ao jogador (estilo Baldur's Gate 3)
    float coneRadius = lerp(baseRadius * camConeScale, baseRadius, saturate(t));
    float edgeT = saturate((perpDist - coneRadius) / feather);

    // Suaviza a transicao perto do chao para que as paredes abram suavemente ao tocar o piso
    float groundFade = saturate((positionWS.y - playerFeetY) / max(0.4, focusHeight * 0.5));
    float holeAmount = (1.0 - edgeT) * segmentMask * groundFade;
    float opacity = 1.0 - holeAmount;

    if (opacity <= 0.01)
    {
        discard;
    }

    if (opacity < 0.99)
    {
        // Dithering ordenado Bayer 4x4 autentico PS1 na borda da janela de corte
        float bayer01 = GetPS1BayerDither4x4(positionCS.xy) + 0.5;
        clip(opacity - (bayer01 * 0.96 + 0.02));
    }
}

void ApplyBG3CameraCutout(float3 positionWS, float4 positionCS)
{
    ApplyBG3CameraCutout(positionWS, positionCS, float3(0.0, 0.0, 0.0));
}

#endif
