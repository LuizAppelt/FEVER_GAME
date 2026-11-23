using System.Collections.Generic;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Boneco de treino anatomico (pustula carnuda) para testar o Chicote Mini Tenia:
    /// - Implementa IAcidDamageable (recebe dano de bolhas acidas e pocas de 3s)
    /// - Expoe TakeWhipDamage(amount, isMelee) para feedback diferenciado de chicote vs acido
    /// - Exibe barra de vida / popup de dano em tela e regenera HP ao chegar a 0
    /// </summary>
    public class WhipTrainingDummy : MonoBehaviour, IAcidDamageable
    {
        [Header("Vida e Regeneracao")]
        public float maxHealth = 120f;
        public float currentHealth = 120f;
        public float respawnRegenDelay = 2.0f;

        [Header("Feedback Visual")]
        public float hitFlashDuration = 0.16f;
        public Color meleeHitColor = new Color(1.0f, 0.35f, 0.28f, 1f);
        public Color acidHitColor = new Color(0.35f, 1.0f, 0.35f, 1f);
        public string dummyLabel = "Pustula Treino";

        private struct FloatingHitText
        {
            public string text;
            public Color color;
            public float timer;
            public float duration;
            public Vector3 worldOffset;
        }

        private readonly List<FloatingHitText> _floatingTexts = new List<FloatingHitText>(8);
        private Renderer[] _renderers;
        private MaterialPropertyBlock _mpb;
        private Vector3 _baseScale;
        private float _hitFlashTimer;
        private Color _activeFlashColor;
        private float _regenTimer;
        private bool _isDefeated;
        private Camera _cam;

        private static readonly int PropHitFlash = Shader.PropertyToID("_HitFlash");
        private static readonly int PropHitFlashColor = Shader.PropertyToID("_HitFlashColor");
        private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            currentHealth = maxHealth;
            _baseScale = transform.localScale;
            _mpb = new MaterialPropertyBlock();
            _cam = Camera.main;
            EnsureVisualsIfEmpty();
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_isDefeated)
            {
                _regenTimer -= dt;
                if (_regenTimer <= 0f)
                {
                    _isDefeated = false;
                    currentHealth = maxHealth;
                    transform.localScale = _baseScale;
                    PushFloatingText("REGENEROU!", new Color(0.55f, 1f, 0.65f, 1f), 0.9f);
                }
            }

            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - dt);
                float t = _hitFlashTimer / Mathf.Max(0.01f, hitFlashDuration);
                ApplyHitVisual(t);
            }
            else if (!_isDefeated)
            {
                float idlePulse = 1f + Mathf.Sin(Time.time * 3.2f + transform.position.x) * 0.025f;
                transform.localScale = new Vector3(
                    _baseScale.x * idlePulse,
                    _baseScale.y * (2f - idlePulse),
                    _baseScale.z * idlePulse
                );
            }

            for (int i = _floatingTexts.Count - 1; i >= 0; i--)
            {
                FloatingHitText item = _floatingTexts[i];
                item.timer -= dt;
                item.worldOffset.y += dt * 1.15f;
                if (item.timer <= 0f)
                {
                    _floatingTexts.RemoveAt(i);
                }
                else
                {
                    _floatingTexts[i] = item;
                }
            }
        }

        public void TakeAcidDamage(float amount)
        {
            TakeWhipDamage(amount, isMelee: false);
        }

        public void TakeWhipDamage(float amount, bool isMelee)
        {
            if (amount <= 0f) return;

            if (_isDefeated)
            {
                // Mesmo derrotado, mostra impacto visual leve para feedback continuo
                _hitFlashTimer = hitFlashDuration * 0.5f;
                _activeFlashColor = isMelee ? meleeHitColor : acidHitColor;
                return;
            }

            currentHealth = Mathf.Max(0f, currentHealth - amount);
            _hitFlashTimer = hitFlashDuration;
            _activeFlashColor = isMelee ? meleeHitColor : acidHitColor;

            string tag = isMelee ? "CHICOTE" : "ACIDO";
            PushFloatingText($"-{Mathf.RoundToInt(amount)} ({tag})", _activeFlashColor, 0.75f);

            if (currentHealth <= 0f)
            {
                _isDefeated = true;
                _regenTimer = respawnRegenDelay;
                transform.localScale = new Vector3(_baseScale.x * 1.15f, _baseScale.y * 0.35f, _baseScale.z * 1.15f);
                PushFloatingText("ESTOURADA!", new Color(1f, 0.85f, 0.3f, 1f), 1.1f);
            }
        }

        private void ApplyHitVisual(float normalizedFlash)
        {
            float squash = 1f + normalizedFlash * 0.16f;
            if (!_isDefeated)
            {
                transform.localScale = new Vector3(_baseScale.x * squash, _baseScale.y / squash, _baseScale.z * squash);
            }

            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(PropHitFlash, normalizedFlash);
                _mpb.SetColor(PropHitFlashColor, _activeFlashColor);
                _mpb.SetColor(PropBaseColor, Color.Lerp(Color.white, _activeFlashColor, normalizedFlash * 0.75f));
                r.SetPropertyBlock(_mpb);
            }
        }

        private void PushFloatingText(string text, Color color, float duration)
        {
            _floatingTexts.Add(new FloatingHitText
            {
                text = text,
                color = color,
                timer = duration,
                duration = duration,
                worldOffset = new Vector3(Random.Range(-0.25f, 0.25f), 2.1f, 0f)
            });
        }

        private void EnsureVisualsIfEmpty()
        {
            if (GetComponentInChildren<Renderer>(true) != null) return;

            Shader shader = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            Material fallbackMat = new Material(shader);
            fallbackMat.color = new Color(0.76f, 0.24f, 0.26f, 1f);

            GameObject stalk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stalk.name = "Dummy_Stalk";
            stalk.transform.SetParent(transform, false);
            stalk.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            stalk.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            Destroy(stalk.GetComponent<Collider>());
            stalk.GetComponent<Renderer>().sharedMaterial = fallbackMat;

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Dummy_PustuleHead";
            head.transform.SetParent(transform, false);
            head.transform.localPosition = new Vector3(0f, 1.32f, 0f);
            head.transform.localScale = new Vector3(0.95f, 0.85f, 0.95f);
            Destroy(head.GetComponent<Collider>());
            head.GetComponent<Renderer>().sharedMaterial = fallbackMat;

            CapsuleCollider col = GetComponent<CapsuleCollider>();
            if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.95f, 0f);
            col.radius = 0.52f;
            col.height = 1.9f;
        }

        private void OnGUI()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Vector3 headWorld = transform.position + Vector3.up * 2.25f;
            Vector3 screenPos = _cam.WorldToScreenPoint(headWorld);
            if (screenPos.z <= 0.1f) return;

            float guiX = screenPos.x;
            float guiY = Screen.height - screenPos.y;

            float barW = 78f;
            float barH = 8f;
            float ratio = Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));

            Color prev = GUI.color;
            GUI.color = new Color(0.08f, 0.05f, 0.06f, 0.85f);
            GUI.DrawTexture(new Rect(guiX - barW * 0.5f - 1f, guiY - 1f, barW + 2f, barH + 2f), Texture2D.whiteTexture);

            GUI.color = _isDefeated
                ? new Color(0.55f, 0.55f, 0.55f, 0.85f)
                : Color.Lerp(new Color(0.95f, 0.22f, 0.18f), new Color(0.45f, 0.95f, 0.35f), ratio);
            GUI.DrawTexture(new Rect(guiX - barW * 0.5f, guiY, barW * ratio, barH), Texture2D.whiteTexture);

            GUI.color = Color.white;
            string status = _isDefeated ? $"{dummyLabel} (Regen {_regenTimer:0.0}s)" : $"{dummyLabel} {Mathf.CeilToInt(currentHealth)}";
            GUI.Label(new Rect(guiX - 64f, guiY - 19f, 140f, 18f), status);

            for (int i = 0; i < _floatingTexts.Count; i++)
            {
                FloatingHitText item = _floatingTexts[i];
                Vector3 txtScreen = _cam.WorldToScreenPoint(transform.position + item.worldOffset);
                if (txtScreen.z <= 0.1f) continue;

                float alpha = Mathf.Clamp01(item.timer / Mathf.Max(0.01f, item.duration));
                GUI.color = new Color(item.color.r, item.color.g, item.color.b, alpha);
                GUI.Label(new Rect(txtScreen.x - 55f, Screen.height - txtScreen.y - 10f, 130f, 20f), item.text);
            }

            GUI.color = prev;
        }
    }
}
