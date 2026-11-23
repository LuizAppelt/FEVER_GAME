using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Anima ilhas flutuantes de cartilagem/massa calcificada (Dome_Floating_Island_01, Dome_Floating_Island_02)
    /// boiando no lago de acido gastrico com oscilacao vertical suave e inclinacao em 2 eixos.
    /// </summary>
    public class FloatingFleshPlatform : MonoBehaviour
    {
        [Header("Flutuacao Vertical (Bobbing)")]
        [Tooltip("Amplitude vertical da oscilacao no lago de acido.")]
        public float bobAmplitude = 0.14f;

        [Tooltip("Frequencia do movimento vertical (ciclos por segundo).")]
        public float bobFrequency = 0.55f;

        [Tooltip("Deslocamento de fase para dessincronizar multiplas ilhas flutuantes.")]
        public float phaseOffset = 0f;

        [Header("Inclinacao em 2 Eixos (Rocking)")]
        [Tooltip("Amplitude maxima de inclinacao em graus nos eixos X e Z.")]
        public float tiltAmplitudeDeg = 2.8f;

        [Tooltip("Frequencia do balanco de inclinacao (ciclos por segundo).")]
        public float tiltFrequency = 0.42f;

        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;

        private void Awake()
        {
            _baseLocalPos = transform.localPosition;
            _baseLocalRot = transform.localRotation;
        }

        private void Update()
        {
            float bobAngle = Time.time * bobFrequency * Mathf.PI * 2f + phaseOffset;
            float verticalOffset = Mathf.Sin(bobAngle) * bobAmplitude;
            transform.localPosition = _baseLocalPos + new Vector3(0f, verticalOffset, 0f);

            float tiltAngle = Time.time * tiltFrequency * Mathf.PI * 2f + phaseOffset;
            float tiltX = Mathf.Sin(tiltAngle) * tiltAmplitudeDeg;
            float tiltZ = Mathf.Cos(tiltAngle * 0.85f + 1.15f) * tiltAmplitudeDeg;
            transform.localRotation = _baseLocalRot * Quaternion.Euler(tiltX, 0f, tiltZ);
        }
    }
}
