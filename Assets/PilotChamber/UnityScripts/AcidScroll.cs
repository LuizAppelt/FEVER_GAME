using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Faz a textura do acido gastrico deslizar lentamente para simular
    /// correnteza toxica e ebulicao. Anexe ao objeto "Acid_Lake".
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class AcidScroll : MonoBehaviour
    {
        [Tooltip("Velocidade de deslocamento UV da textura do acido.")]
        public Vector2 scrollSpeed = new Vector2(0.06f, 0.04f);

        [Tooltip("Oscilacao sutil vertical simulando pulsacao do liquido gastrico.")]
        public float pulseAmplitude = 0.025f;
        public float pulseFrequency = 1.6f;

        private Renderer _renderer;
        private Material _instancedMat;
        private Vector2 _offset;
        private Vector3 _initialLocalPos;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int EmissiveMapId = Shader.PropertyToID("_EmissionMap");

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _instancedMat = _renderer.material;
            _initialLocalPos = transform.localPosition;
        }

        private void Update()
        {
            if (_instancedMat == null) return;

            _offset += scrollSpeed * Time.deltaTime;
            _offset.x %= 1f;
            _offset.y %= 1f;

            if (_instancedMat.HasProperty(BaseMapId))
            {
                _instancedMat.SetTextureOffset(BaseMapId, _offset);
            }
            if (_instancedMat.HasProperty(MainTexId))
            {
                _instancedMat.SetTextureOffset(MainTexId, _offset);
            }
            if (_instancedMat.HasProperty(EmissiveMapId))
            {
                _instancedMat.SetTextureOffset(EmissiveMapId, _offset);
            }

            if (pulseAmplitude > 0f)
            {
                float dy = Mathf.Sin(Time.time * pulseFrequency) * pulseAmplitude;
                transform.localPosition = _initialLocalPos + new Vector3(0f, dy, 0f);
            }
        }
    }
}
