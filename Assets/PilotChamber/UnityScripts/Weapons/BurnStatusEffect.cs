using System.Collections.Generic;
using BossTenia;
using ImmuneEnemies;
using StomachAbyss.Combat;
using StomachAbyss.PillarSeals;
using StomachAbyss.Tutorial;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Efeito de Queimadura (Burn DoT) aplicado em inimigos pela Estocada de Fogo e rastro de fogo no chao.
    /// - Causa dano periodico a cada 0.40s (padrao: 18 DPS -> 7.2 por tick -> ~72 dano total em 4.0s).
    /// - Emite particulas pixeladas PS1 de fogo/brasas ascendentes e luz Point laranja quente.
    /// - Sinergia com golpes rapidos (Botao Esquerdo): ExtendBurn(+0.5s) e TriggerFlameFanBonus(+10 dano + 14 faiscas).
    /// </summary>
    public class BurnStatusEffect : MonoBehaviour
    {
        private const float TickInterval = 0.40f;
        private const float MaxExtendedDuration = 8.0f;

        private static Material s_sharedFallbackFireMat;
        private static Texture2D s_sharedFirePixelTex;

        private GameObject _attacker;
        private IAcidDamageable _damageable;
        private Collider _targetCollider;

        private float _remainingDuration;
        private float _dps = 18f;
        private float _tickTimer;
        private float _flareBoostTimer;

        private GameObject _fxRoot;
        private ParticleSystem _fireParticles;
        private ParticleSystem _sparkParticles;
        private Light _burnLight;
        private Material _particleMat;

        public bool IsBurning => _remainingDuration > 0f;
        public float RemainingDuration => Mathf.Max(0f, _remainingDuration);

        /// <summary>
        /// Aplica ou renova o estado de queimadura no alvo.
        /// </summary>
        public void ApplyBurn(GameObject attacker, float duration = 4.0f, float dps = 18f, Material pixelFireMat = null)
        {
            // Nunca incendeia o proprio jogador
            if (GetComponent<SimplePlayerController>() != null || GetComponentInParent<SimplePlayerController>() != null)
            {
                enabled = false;
                return;
            }

            if (duration <= 0f) return;

            ResolveTargetRefs();
            if (_damageable == null || IsTargetDefeated()) return;

            bool wasBurning = IsBurning;
            _attacker = attacker;
            _dps = wasBurning ? Mathf.Max(_dps, dps) : Mathf.Max(0f, dps);
            _remainingDuration = Mathf.Max(_remainingDuration, duration);

            if (!wasBurning)
            {
                _tickTimer = TickInterval;
            }

            EnsureBurnVisuals(pixelFireMat);
            SetVisualsActive(true);
            enabled = true;
        }

        /// <summary>
        /// Estende a duracao da queimadura ativa (sinergia com golpes rapidos da Lamina Ardente).
        /// </summary>
        public void ExtendBurn(float extraSeconds = 0.5f)
        {
            if (!IsBurning || extraSeconds <= 0f) return;
            _remainingDuration = Mathf.Min(MaxExtendedDuration, _remainingDuration + extraSeconds);
        }

        /// <summary>
        /// Faz as chamas subirem com faiscas (Emit(14)) e aplica dano bonus de fogo ao acertar um inimigo ja queimando.
        /// </summary>
        public void TriggerFlameFanBonus(float bonusDamage = 10f)
        {
            ResolveTargetRefs();
            EnsureBurnVisuals(_particleMat);

            _flareBoostTimer = 0.30f;
            UpdateFxAnchorPosition();

            if (_sparkParticles != null)
            {
                _sparkParticles.Emit(14);
            }
            if (_fireParticles != null)
            {
                _fireParticles.Emit(6);
            }

            if (bonusDamage > 0f && _damageable != null && !IsTargetDefeated())
            {
                _damageable.TakeAcidDamage(bonusDamage);
            }
        }

        private void Update()
        {
            if (_remainingDuration <= 0f)
            {
                Extinguish();
                return;
            }

            ResolveTargetRefs();
            if (_damageable == null || IsTargetDefeated())
            {
                Extinguish();
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateFxAnchorPosition();

            _remainingDuration -= dt;
            _tickTimer -= dt;
            if (_tickTimer <= 0f)
            {
                _tickTimer += TickInterval;
                ApplyBurnTickDamage();
            }

            if (_flareBoostTimer > 0f)
            {
                _flareBoostTimer = Mathf.Max(0f, _flareBoostTimer - dt);
            }

            if (_burnLight != null && _burnLight.enabled)
            {
                float fade = _remainingDuration < 0.5f ? Mathf.Clamp01(_remainingDuration / 0.5f) : 1f;
                float flicker = 1.15f + 0.28f * Mathf.Sin(Time.time * 19f) + 0.14f * Mathf.Cos(Time.time * 31f);
                float flare = (_flareBoostTimer / 0.30f) * 1.9f;
                _burnLight.intensity = (flicker + flare) * fade;
            }

            if (_remainingDuration <= 0f || IsTargetDefeated())
            {
                Extinguish();
            }
        }

        private void OnDisable()
        {
            SetVisualsActive(false);
        }

        private void OnDestroy()
        {
            if (_fxRoot != null)
            {
                Destroy(_fxRoot);
                _fxRoot = null;
            }
        }

        private void ApplyBurnTickDamage()
        {
            if (_damageable == null || IsTargetDefeated()) return;

            float tickDmg = _dps * TickInterval;
            if (tickDmg <= 0f) return;

            Vector3 center = GetTargetCenterWorld();
            Vector3 upDir = Vector3.up;

            // Contexto silencioso/leve para que o tick periodico nao gere sangue pesado ou som duplicado no mesmo frame
            BloodSplatterSystem.BeginAttackBurstContext(upDir, center, 0.35f, false);
            try
            {
                _damageable.TakeAcidDamage(tickDmg);
            }
            finally
            {
                BloodSplatterSystem.EndAttackBurstContext();
            }

            if (_fireParticles != null)
            {
                _fireParticles.Emit(5);
            }
        }

        private void Extinguish()
        {
            _remainingDuration = 0f;
            _tickTimer = 0f;
            _flareBoostTimer = 0f;
            SetVisualsActive(false);
            enabled = false;
        }

        private void ResolveTargetRefs()
        {
            if (_damageable == null)
            {
                _damageable = GetComponent<IAcidDamageable>();
                if (_damageable == null) _damageable = GetComponentInParent<IAcidDamageable>();
                if (_damageable == null) _damageable = GetComponentInChildren<IAcidDamageable>();
            }
            if (_targetCollider == null)
            {
                _targetCollider = GetComponentInChildren<Collider>();
            }
        }

        private bool IsTargetDefeated()
        {
            if (_damageable is ImmuneEnemyBase enemy && enemy.IsDead) return true;
            if (_damageable is TeniaBossHealth boss && boss.IsDead) return true;
            if (_damageable is TutorialCystTarget cyst && cyst.IsDestroyed) return true;
            if (_damageable is EyePillarController pillar && pillar.IsDestroyed) return true;
            if (_damageable is WhipTrainingDummy dummy && dummy.currentHealth <= 0f) return true;
            return false;
        }

        private Vector3 GetTargetCenterWorld()
        {
            if (_targetCollider != null && _targetCollider.enabled)
            {
                return _targetCollider.bounds.center;
            }
            return transform.position + Vector3.up * 0.9f;
        }

        private void UpdateFxAnchorPosition()
        {
            if (_fxRoot != null)
            {
                _fxRoot.transform.position = GetTargetCenterWorld();
            }
        }

        private void EnsureBurnVisuals(Material pixelFireMat)
        {
            Material resolvedMat = GetOrCreateSharedFireParticleMaterial(pixelFireMat);
            if (_particleMat == null || (pixelFireMat != null && _particleMat != pixelFireMat))
            {
                _particleMat = resolvedMat;
            }

            if (_fxRoot == null)
            {
                _fxRoot = new GameObject("BurnStatus_FireFX");
                _fxRoot.transform.SetParent(transform, false);
                _fxRoot.transform.position = GetTargetCenterWorld();

                // Emissor 1: Chamas e brasas PS1 subindo do corpo do inimigo
                GameObject fireGo = new GameObject("RisingFlames");
                fireGo.transform.SetParent(_fxRoot.transform, false);
                _fireParticles = fireGo.AddComponent<ParticleSystem>();

                var main = _fireParticles.main;
                main.playOnAwake = false;
                main.loop = true;
                main.duration = 1.0f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.62f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.95f, 2.35f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
                main.gravityModifier = -0.35f;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.0f, 0.72f, 0.16f, 1f),
                    new Color(1.0f, 0.28f, 0.06f, 1f)
                );
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = _fireParticles.emission;
                emission.enabled = true;
                emission.rateOverTime = 22f;

                var shape = _fireParticles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.42f;

                var sizeOverLife = _fireParticles.sizeOverLifetime;
                sizeOverLife.enabled = true;
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.9f, 1f, 0.1f));

                ParticleSystemRenderer fireRend = fireGo.GetComponent<ParticleSystemRenderer>();
                if (fireRend != null)
                {
                    fireRend.renderMode = ParticleSystemRenderMode.Billboard;
                    fireRend.sharedMaterial = _particleMat;
                }

                // Emissor 2: Explosao radial de faiscas ao estender/soprar as chamas
                GameObject sparkGo = new GameObject("FlameFanSparks");
                sparkGo.transform.SetParent(_fxRoot.transform, false);
                _sparkParticles = sparkGo.AddComponent<ParticleSystem>();

                var spMain = _sparkParticles.main;
                spMain.playOnAwake = false;
                spMain.loop = false;
                spMain.duration = 0.35f;
                spMain.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
                spMain.startSpeed = new ParticleSystem.MinMaxCurve(4.2f, 8.8f);
                spMain.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                spMain.gravityModifier = 0.65f;
                spMain.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.0f, 0.92f, 0.42f, 1f),
                    new Color(1.0f, 0.45f, 0.10f, 1f)
                );
                spMain.simulationSpace = ParticleSystemSimulationSpace.World;

                var spEmission = _sparkParticles.emission;
                spEmission.enabled = false;

                var spShape = _sparkParticles.shape;
                spShape.enabled = true;
                spShape.shapeType = ParticleSystemShapeType.Sphere;
                spShape.radius = 0.32f;

                ParticleSystemRenderer spRend = sparkGo.GetComponent<ParticleSystemRenderer>();
                if (spRend != null)
                {
                    spRend.renderMode = ParticleSystemRenderMode.Billboard;
                    spRend.sharedMaterial = _particleMat;
                }

                // Luz Point laranja quente
                GameObject lightGo = new GameObject("BurnGlowLight");
                lightGo.transform.SetParent(_fxRoot.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                _burnLight = lightGo.AddComponent<Light>();
                _burnLight.type = LightType.Point;
                _burnLight.color = new Color(1.0f, 0.48f, 0.12f, 1f);
                _burnLight.intensity = 1.25f;
                _burnLight.range = 3.6f;
                _burnLight.shadows = LightShadows.None;
            }
            else if (pixelFireMat != null)
            {
                ParticleSystemRenderer fireRend = _fireParticles != null ? _fireParticles.GetComponent<ParticleSystemRenderer>() : null;
                if (fireRend != null) fireRend.sharedMaterial = pixelFireMat;
                ParticleSystemRenderer spRend = _sparkParticles != null ? _sparkParticles.GetComponent<ParticleSystemRenderer>() : null;
                if (spRend != null) spRend.sharedMaterial = pixelFireMat;
            }
        }

        private void SetVisualsActive(bool active)
        {
            if (_fireParticles != null)
            {
                if (active)
                {
                    if (!_fireParticles.isPlaying) _fireParticles.Play();
                }
                else if (_fireParticles.isPlaying || _fireParticles.isEmitting)
                {
                    _fireParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (_burnLight != null)
            {
                _burnLight.enabled = active;
                if (!active) _burnLight.intensity = 0f;
            }
        }

        internal static Material GetOrCreateSharedFireParticleMaterial(Material preferred = null)
        {
            if (preferred != null) return preferred;
            if (s_sharedFallbackFireMat != null) return s_sharedFallbackFireMat;

            if (s_sharedFirePixelTex == null)
            {
                s_sharedFirePixelTex = BuildPixelEmberTexture();
            }

            Shader shader = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
            bool isFallback = false;
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                isFallback = true;
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
                isFallback = true;
            }

            s_sharedFallbackFireMat = new Material(shader);
            s_sharedFallbackFireMat.hideFlags = HideFlags.DontSave;

            if (s_sharedFallbackFireMat.HasProperty("_BaseMap")) s_sharedFallbackFireMat.SetTexture("_BaseMap", s_sharedFirePixelTex);
            if (s_sharedFallbackFireMat.HasProperty("_MainTex")) s_sharedFallbackFireMat.SetTexture("_MainTex", s_sharedFirePixelTex);
            if (s_sharedFallbackFireMat.HasProperty("_BaseColor")) s_sharedFallbackFireMat.SetColor("_BaseColor", Color.white);
            if (s_sharedFallbackFireMat.HasProperty("_Color")) s_sharedFallbackFireMat.SetColor("_Color", Color.white);
            if (s_sharedFallbackFireMat.HasProperty("_EmissionBoost")) s_sharedFallbackFireMat.SetFloat("_EmissionBoost", 2.0f);
            if (s_sharedFallbackFireMat.HasProperty("_Cutoff")) s_sharedFallbackFireMat.SetFloat("_Cutoff", 0.32f);

            if (isFallback)
            {
                if (s_sharedFallbackFireMat.HasProperty("_Surface")) s_sharedFallbackFireMat.SetFloat("_Surface", 1f);
                if (s_sharedFallbackFireMat.HasProperty("_SrcBlend")) s_sharedFallbackFireMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (s_sharedFallbackFireMat.HasProperty("_DstBlend")) s_sharedFallbackFireMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (s_sharedFallbackFireMat.HasProperty("_ZWrite")) s_sharedFallbackFireMat.SetInt("_ZWrite", 0);
                s_sharedFallbackFireMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            return s_sharedFallbackFireMat;
        }

        private static Texture2D BuildPixelEmberTexture()
        {
            const int size = 12;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontSave;
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - center) / center;
                    float dy = Mathf.Abs(y - center) / center;
                    float diamond = dx * 0.85f + dy * 0.72f;
                    if (diamond <= 0.42f)
                    {
                        pixels[y * size + x] = new Color(1f, 0.98f, 0.85f, 1f);
                    }
                    else if (diamond <= 0.88f)
                    {
                        pixels[y * size + x] = new Color(1f, 0.65f, 0.25f, 0.92f);
                    }
                    else
                    {
                        pixels[y * size + x] = new Color(1f, 0.35f, 0.05f, 0f);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
