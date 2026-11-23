using System.Collections.Generic;
using MapaEstomago;
using UnityEngine;

namespace BossTenia
{
    /// <summary>
    /// Orbe bioluminescente de cura na catedral. Dropa da Tenia na Fase 2 / Quebra de Pilar
    /// ou reaparece periodicamente em pontos seguros da nave para equilibrar lutas longas.
    /// </summary>
    public class TeniaSoulOrb : MonoBehaviour
    {
        public static readonly List<TeniaSoulOrb> ActiveOrbs = new List<TeniaSoulOrb>();

        private float _healAmount = 28f;
        private float _pickupRadius = 1.65f;
        private float _lifetime = 26f;
        private float _age;
        private Vector3 _baseWorldPos;
        private Transform _visualCore;
        private Transform _groundRing;
        private TeniaAudioController _audio;
        private bool _collected;

        public static TeniaSoulOrb Spawn(Vector3 floorPos, float healAmount = 28f, float lifetime = 26f, TeniaAudioController audio = null)
        {
            GameObject go = new GameObject("Tenia_SoulHealOrb");
            go.transform.position = floorPos;
            TeniaSoulOrb orb = go.AddComponent<TeniaSoulOrb>();
            orb.Initialize(floorPos, healAmount, lifetime, audio);
            return orb;
        }

        public static void ClearAllActiveOrbs()
        {
            for (int i = ActiveOrbs.Count - 1; i >= 0; i--)
            {
                if (ActiveOrbs[i] != null)
                {
                    Destroy(ActiveOrbs[i].gameObject);
                }
            }
            ActiveOrbs.Clear();
        }

        private void OnEnable()
        {
            if (!ActiveOrbs.Contains(this))
            {
                ActiveOrbs.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveOrbs.Remove(this);
        }

        private void Initialize(Vector3 floorPos, float healAmount, float lifetime, TeniaAudioController audio)
        {
            _baseWorldPos = floorPos;
            _healAmount = Mathf.Max(5f, healAmount);
            _lifetime = lifetime;
            _audio = audio;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            // Anel luminoso no chao para destaque imediato
            GameObject ringGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringGo.name = "GroundHalo";
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ringGo.transform.localScale = new Vector3(1.55f, 0.015f, 1.55f);
            Collider rc = ringGo.GetComponent<Collider>();
            if (rc != null) Destroy(rc);
            _groundRing = ringGo.transform;

            if (sh != null)
            {
                Material ringMat = new Material(sh);
                ringMat.color = new Color(0.18f, 0.92f, 1.0f, 0.72f);
                ringGo.GetComponent<Renderer>().sharedMaterial = ringMat;
            }

            // Nucleo flutuante ciano-dourado
            GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreGo.name = "VitalCore";
            coreGo.transform.SetParent(transform, false);
            coreGo.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            coreGo.transform.localScale = Vector3.one * 0.56f;
            Collider cc = coreGo.GetComponent<Collider>();
            if (cc != null) Destroy(cc);
            _visualCore = coreGo.transform;

            if (sh != null)
            {
                Material coreMat = new Material(sh);
                coreMat.color = new Color(0.35f, 1.0f, 0.96f, 1f);
                coreGo.GetComponent<Renderer>().sharedMaterial = coreMat;
            }

            Light glow = coreGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.28f, 0.96f, 1.0f, 1f);
            glow.intensity = 2.6f;
            glow.range = 4.5f;
        }

        private void Update()
        {
            if (_collected) return;

            float dt = Time.deltaTime;
            _age += dt;
            if (_lifetime > 0f && _age >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            if (_visualCore != null)
            {
                float bob = Mathf.Sin(_age * 3.4f) * 0.16f;
                _visualCore.localPosition = new Vector3(0f, 0.95f + bob, 0f);
                _visualCore.Rotate(Vector3.up, 110f * dt, Space.World);
                float pulse = 0.52f + 0.08f * Mathf.Sin(_age * 5.2f);
                _visualCore.localScale = Vector3.one * pulse;
            }

            if (_groundRing != null)
            {
                float rPulse = 1.45f + 0.18f * Mathf.Sin(_age * 4.5f);
                _groundRing.localScale = new Vector3(rPulse, 0.015f, rPulse);
            }

            GameObject playerObj = GameObject.FindGameObjectWithTag("Player") ?? GameObject.Find("Test_Player");
            if (playerObj != null)
            {
                Vector3 p = playerObj.transform.position;
                float dx = p.x - _baseWorldPos.x;
                float dz = p.z - _baseWorldPos.z;
                if ((dx * dx + dz * dz) <= _pickupRadius * _pickupRadius && Mathf.Abs(p.y - _baseWorldPos.y) <= 2.4f)
                {
                    SimplePlayerController player = playerObj.GetComponent<SimplePlayerController>();
                    if (player != null && !player.IsDead)
                    {
                        _collected = true;
                        player.Heal(_healAmount);
                        _audio?.PlayHealOrbPickup();
                        Destroy(gameObject);
                    }
                }
            }
        }
    }
}
