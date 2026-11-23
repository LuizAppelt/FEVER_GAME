using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Controla o corte dinamico estilo Baldur's Gate 3 (Opcao A):
    /// Projeta uma capsula/cone da Camera ate o Jogador. Tudo que estiver na parede frontal
    /// ou cupula entre a Camera e o Jogador e recortado com Dithering PS1 Bayer 4x4,
    /// mantendo as bordas da cupula visiveis ao redor da tela.
    /// </summary>
    [ExecuteAlways]
    public class BG3WallCutoutController : MonoBehaviour
    {
        [Header("Alvo do Corte (Jogador)")]
        [Tooltip("Se vazio, localiza automaticamente o Test_Player (tag Player).")]
        public Transform playerTarget;

        [Tooltip("Altura do ponto focal no jogador (peito/olho do Virus).")]
        public float playerFocusHeight = 1.0f;

        [Header("Geometria da Janela BG3 (Estilo PS1 Dither)")]
        public bool enableCutout = true;

        [Tooltip("Raio da janela limpa ao redor do jogador (em metros).")]
        [Range(1.5f, 12f)]
        public float cutoutRadiusAtPlayer = 4.8f;

        [Tooltip("Expansao do cone na direcao da camera (1.0 = cilindro, >1.0 = cone aberto).")]
        [Range(1.0f, 2.5f)]
        public float cameraConeMultiplier = 1.45f;

        [Tooltip("Largura da faixa de transicao com dithering Bayer 4x4 PS1.")]
        [Range(0.4f, 6.0f)]
        public float ditherFeatherWidth = 2.4f;

        private Camera _cam;

        private static readonly int PropCamPos = Shader.PropertyToID("_BG3CamPos");
        private static readonly int PropPlayerPos = Shader.PropertyToID("_BG3PlayerPos");
        private static readonly int PropParams = Shader.PropertyToID("_BG3CutoutParams");

        private void OnEnable()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;
            UpdateShaderGlobals();
        }

        private void OnDisable()
        {
            Shader.SetGlobalVector(PropParams, Vector4.zero);
        }

        private void LateUpdate()
        {
            UpdateShaderGlobals();
        }

        public void UpdateShaderGlobals()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;

            if (!enableCutout || _cam == null)
            {
                Shader.SetGlobalVector(PropParams, Vector4.zero);
                return;
            }

            if (playerTarget == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) p = GameObject.Find("Test_Player");
                if (p != null) playerTarget = p.transform;
            }

            Vector3 camPos = _cam.transform.position;
            Vector3 focusPos;

            if (playerTarget != null)
            {
                focusPos = playerTarget.position + Vector3.up * playerFocusHeight;
            }
            else
            {
                IsometricCameraFollow iso = GetComponent<IsometricCameraFollow>();
                focusPos = (iso != null ? iso.FreePivot : Vector3.zero) + Vector3.up * playerFocusHeight;
            }

            Shader.SetGlobalVector(PropCamPos, new Vector4(camPos.x, camPos.y, camPos.z, 1.0f));
            Shader.SetGlobalVector(PropPlayerPos, new Vector4(focusPos.x, focusPos.y, focusPos.z, Mathf.Max(0.1f, playerFocusHeight)));
            Shader.SetGlobalVector(PropParams, new Vector4(
                cutoutRadiusAtPlayer,
                cameraConeMultiplier,
                ditherFeatherWidth,
                1.0f
            ));
        }
    }
}
