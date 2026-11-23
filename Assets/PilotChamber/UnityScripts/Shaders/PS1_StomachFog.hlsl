#ifndef PS1_STOMACH_FOG_INCLUDED
#define PS1_STOMACH_FOG_INCLUDED

// Uniforms globais atualizados pelo controlador StomachSilentHillFog ([ExecuteAlways])
float4 _StomachFogPlayerPos;      // xyz = Posicao do Jogador (centro limpo), w = 1 se ativo
float4 _StomachFogRadii;          // x = Raio Limpo (sem neblina), y = Raio Medio, z = Raio Longo, w = Densidade Media
float4 _StomachFogParams;         // x = Densidade Maxima, y = Altura Base Acido, z = Queda Vertical, w = Turbulencia Neblina
float4 _StomachFogAnim;           // x = Escala Ruido, y = Vel Vento X, z = Vel Vento Z, w = Forca Dither PS1
half4  _StomachFogColorNear;      // Cor Linear esverdeada acida (medio alcance / vapores baixos)
half4  _StomachFogColorFar;       // Cor Linear esverdeada densa (longo alcance estilo Silent Hill 2, igual ao fundo da camera)
half4  _StomachFogColorHighlight; // Cor Linear de brilho sutil de gas gastrico nas nuvens baixas sobre o acido

float StomachHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float StomachValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    float a = StomachHash21(i + float2(0.0, 0.0));
    float b = StomachHash21(i + float2(1.0, 0.0));
    float c = StomachHash21(i + float2(0.0, 1.0));
    float d = StomachHash21(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Ruido FBM de 3 camadas em movimento para simular a neblina viva de Silent Hill 2
float SampleSilentHillGasNoise(float3 posWS, float scale, float2 windVel)
{
    float t = _Time.y;
    float2 uv1 = posWS.xz * scale + windVel * t;
    float2 uv2 = posWS.xz * (scale * 2.15) - float2(windVel.y * 1.25, -windVel.x * 1.1) * t + float2(posWS.y * 0.35, 17.3);
    float2 uv3 = posWS.xz * (scale * 4.10) + float2(-windVel.x * 1.6, windVel.y * 1.4) * t;

    float n1 = StomachValueNoise(uv1);
    float n2 = StomachValueNoise(uv2);
    float n3 = StomachValueNoise(uv3);

    return n1 * 0.55 + n2 * 0.30 + n3 * 0.15;
}

// Matriz Bayer 4x4 classica do hardware PS1 para dithering ordenado em baixa resolucao
float GetPS1BayerDither4x4(float2 screenPos)
{
    // Agrupa em blocos de 2x2 pixels da tela para simular resolucao retro PS1 (320x240)
    int2 p = int2(floor(screenPos * 0.5)) & 3;
    int index = p.y * 4 + p.x;

    float bayer = 0.0;
    if (index == 0)       bayer =  0.0 / 16.0;
    else if (index == 1)  bayer =  8.0 / 16.0;
    else if (index == 2)  bayer =  2.0 / 16.0;
    else if (index == 3)  bayer = 10.0 / 16.0;
    else if (index == 4)  bayer = 12.0 / 16.0;
    else if (index == 5)  bayer =  4.0 / 16.0;
    else if (index == 6)  bayer = 14.0 / 16.0;
    else if (index == 7)  bayer =  6.0 / 16.0;
    else if (index == 8)  bayer =  3.0 / 16.0;
    else if (index == 9)  bayer = 11.0 / 16.0;
    else if (index == 10) bayer =  1.0 / 16.0;
    else if (index == 11) bayer =  9.0 / 16.0;
    else if (index == 12) bayer = 15.0 / 16.0;
    else if (index == 13) bayer =  7.0 / 16.0;
    else if (index == 14) bayer = 13.0 / 16.0;
    else                  bayer =  5.0 / 16.0;

    return bayer - 0.5;
}

half3 ApplyPS1StomachFog(half3 sceneColor, float3 positionWS, float4 positionCS)
{
    // Se a neblina estiver desativada ou com densidade zero, retorna a cor original 100% limpa
    if (_StomachFogPlayerPos.w < 0.5 || _StomachFogParams.x <= 0.001)
        return sceneColor;

    float3 focusPos   = _StomachFogPlayerPos.xyz;
    float clearRadius = _StomachFogRadii.x;
    float midRadius   = _StomachFogRadii.y;
    float maxRadius   = _StomachFogRadii.z;
    float midDensity  = _StomachFogRadii.w;

    float maxDensity    = _StomachFogParams.x;
    float heightBase    = _StomachFogParams.y;
    float heightFalloff = max(_StomachFogParams.z, 0.2);
    float noiseAmp      = _StomachFogParams.w;

    float noiseScale    = _StomachFogAnim.x;
    float2 windVel      = _StomachFogAnim.yz;
    float ditherStr     = _StomachFogAnim.w;

    // Valores ja em espaco Linear (sincronizados com cam.backgroundColor)
    half3 colNear = _StomachFogColorNear.rgb;
    half3 colFar  = _StomachFogColorFar.rgb;
    half3 colGlow = _StomachFogColorHighlight.rgb;

    // 1. Distancia radial centrada no Jogador (bolha limpa ao redor do player)
    float2 dxz = positionWS.xz - focusPos.xz;
    float dy = (positionWS.y - focusPos.y) * 0.30;
    float distFromPlayer = sqrt(dot(dxz, dxz) + dy * dy);

    // 2. Ruido volumétrico 2.5D de gases estomacais (estilo Silent Hill 2)
    float gasNoise = SampleSilentHillGasNoise(positionWS, noiseScale, windVel); // [0..1]
    float centeredNoise = (gasNoise - 0.5) * 2.0;                               // [-1..1]

    // Perturba a borda da zona limpa para parecer gas vivo se movendo,
    // mantendo 100% sem neblina perto do jogador.
    float borderMask = smoothstep(clearRadius * 0.78, clearRadius * 1.30, distFromPlayer);
    float effectiveDist = distFromPlayer + centeredNoise * (noiseAmp * 3.0) * borderMask;

    // 3. Curva progressiva em 2 estagios (Zero perto -> Medio alcance -> Longo alcance denso)
    float midT = smoothstep(clearRadius, midRadius, effectiveDist);
    float farT = smoothstep(midRadius, maxRadius, effectiveDist);

    float baseFog = 0.0;
    if (effectiveDist > clearRadius)
    {
        float stageMid = midT * midDensity;
        baseFog = lerp(stageMid, maxDensity, farT);
    }

    // 4. Vapor acido rasteiro (mais denso perto do nivel do lago de acido, fora da bolha do player)
    float relHeight = max(0.0, positionWS.y - heightBase);
    float lowVapor = exp(-relHeight / heightFalloff);
    float outsidePlayerBubble = smoothstep(clearRadius * 0.95, clearRadius + 4.0, effectiveDist);
    float vaporBoost = lowVapor * outsidePlayerBubble * (0.20 + 0.22 * gasNoise) * (1.0 - farT * 0.5);

    // Modula a densidade no medio alcance com bolsões de gas em movimento (Silent Hill 2)
    float billowMod = lerp(1.0 + centeredNoise * noiseAmp, 1.0, farT * 0.85);
    float fogFactor = saturate(baseFog * billowMod + vaporBoost);

    // Garantia absoluta: ao redor imediato do jogador nao ha neblina
    fogFactor *= smoothstep(clearRadius * 0.85, clearRadius * 1.08, distFromPlayer);

    // 5. Dithering Ordenado PS1 (Bayer 4x4) + Quantizacao de Niveis de Neblina (Estetica Retro PS1)
    float dither = GetPS1BayerDither4x4(positionCS.xy) * ditherStr;
    if (fogFactor > 0.001 && fogFactor < 0.995)
    {
        float ditheredFog = saturate(fogFactor + dither * 0.075);
        fogFactor = floor(ditheredFog * 16.0 + 0.5) / 16.0;
    }

    // 6. Cor esverdeada dos gases do estomago (em espaco Linear, convergindo para colFar = cam.backgroundColor)
    half3 gasColor = lerp(colNear, colFar, saturate(farT * 0.92 + (1.0 - lowVapor) * 0.22));
    half3 fumeHighlight = colGlow * (half)(saturate((gasNoise - 0.48) * 2.0) * lowVapor * (1.0 - farT) * 0.40);
    gasColor = lerp(gasColor + fumeHighlight, colFar, farT * farT);

    return lerp(sceneColor, gasColor, (half)fogFactor);
}

#endif
