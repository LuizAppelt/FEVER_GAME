using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Simula pulsacao cardiaca / contracao peristaltica viva nos esfincteres e pilares de carne.
    /// </summary>
    public class OrganPulse : MonoBehaviour
    {
        [Tooltip("Batimentos por segundo (ou respiracao lenta).")]
        public float beatsPerSecond = 0.85f;

        [Tooltip("Amplitude de expansao horizontal (XZ).")]
        public float lateralAmplitude = 0.055f;

        [Tooltip("Amplitude de expansao vertical (Y).")]
        public float verticalAmplitude = 0.035f;

        [Tooltip("Deslocamento de fase para cada orgao nao pulsar exatamente igual.")]
        public float phaseOffset = 0f;

        [Tooltip("Ativa batida dupla estilo 'tum-tum' de coracao/estomago.")]
        public bool doubleHeartbeat = true;

        private Vector3 _baseScale;
        private float _recoilTimer;

        private void Awake()
        {
            _baseScale = transform.localScale;
        }

        public void TriggerHitRecoil()
        {
            _recoilTimer = 0.38f;
        }

        private void Update()
        {
            float t = (Time.time * beatsPerSecond + phaseOffset) % 1f;
            float pulse;

            if (doubleHeartbeat)
            {
                // Batida dupla organica ("tum-tum... tum-tum...")
                float beat1 = Mathf.Exp(-Mathf.Pow((t - 0.16f) / 0.07f, 2f));
                float beat2 = 0.72f * Mathf.Exp(-Mathf.Pow((t - 0.34f) / 0.08f, 2f));
                pulse = beat1 + beat2;
            }
            else
            {
                pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f);
            }

            float recoil = 0f;
            if (_recoilTimer > 0f)
            {
                _recoilTimer -= Time.deltaTime;
                recoil = Mathf.Sin(_recoilTimer * 48f) * (_recoilTimer / 0.38f) * 0.22f;
            }

            float sx = _baseScale.x * (1f + pulse * lateralAmplitude + recoil);
            float sy = _baseScale.y * (1f - pulse * verticalAmplitude * 0.5f + pulse * verticalAmplitude - recoil * 0.6f);
            float sz = _baseScale.z * (1f + pulse * lateralAmplitude + recoil);
            transform.localScale = new Vector3(sx, sy, sz);
        }
    }
}
