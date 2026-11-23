using BossTenia;
using StomachAbyss.Cathedral;
using StomachAbyss.Tutorial;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StomachAbyss.Audio
{
    /// <summary>
    /// Controls background music across Stomach_Abyss:
    /// - Tutorial (Cardia) & Standard Level: BGM_01_Abyssal_Ambient (Som 1)
    /// - Cathedral Bridge Checkpoint (before Cathedral): BGM_02_Cathedral_Bridge_Dirge (Som 2)
    /// - Inside Cathedral / Boss Encounter: Fades out level music completely so CathedralPipeOrganBossMusic plays cleanly.
    /// Auto-spawns when Stomach_Abyss loads so no manual scene wiring is required.
    /// </summary>
    public class StomachAbyssMusicDirector : MonoBehaviour
    {
        private enum MusicZone
        {
            LevelDefault,
            BridgeCheckpoint,
            CathedralSilent
        }

        private static StomachAbyssMusicDirector _instance;
        private static bool _sceneHookRegistered;

        [Header("Faixas de Música (carregadas automaticamente de Resources/Audio/Music)")]
        public AudioClip levelAmbientClip;
        public AudioClip bridgeCheckpointClip;

        [Header("Volumes & Transições")]
        [Range(0f, 1f)] public float levelMusicVolume = 0.42f;
        [Range(0f, 1f)] public float bridgeMusicVolume = 0.48f;
        public float crossfadeSpeed = 0.55f;
        public float cathedralFadeOutSpeed = 1.15f;

        [Header("Detecção do Checkpoint da Ponte (antes da Catedral)")]
        [Tooltip("Raio ao redor do Virgílio/Checkpoint da Ponte para iniciar o Som 2.")]
        public float bridgeEnterRadius = 28f;
        [Tooltip("Raio de saída (histerese) para voltar ao Som 1 ao se afastar da ponte.")]
        public float bridgeExitRadius = 34f;

        private AudioSource _levelSource;
        private AudioSource _bridgeSource;

        private Transform _playerTransform;
        private Transform _bridgeAnchor;
        private TeniaArenaBounds _arenaBounds;

        private MusicZone _currentZone = MusicZone.LevelDefault;
        private float _resolveTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!_sceneHookRegistered)
            {
                _sceneHookRegistered = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            TrySpawnForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TrySpawnForScene(scene);
        }

        private static void TrySpawnForScene(Scene scene)
        {
            if (scene.name != "Stomach_Abyss")
            {
                return;
            }

            if (_instance != null)
            {
                return;
            }

            StomachAbyssMusicDirector existing = FindFirstObjectByType<StomachAbyssMusicDirector>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            GameObject go = new GameObject("StomachAbyss_MusicDirector");
            _instance = go.AddComponent<StomachAbyssMusicDirector>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            LoadClipsIfNeeded();
            SetupAudioSources();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void LoadClipsIfNeeded()
        {
            if (levelAmbientClip == null)
            {
                levelAmbientClip = Resources.Load<AudioClip>("Audio/Music/BGM_01_Abyssal_Ambient");
            }

            if (bridgeCheckpointClip == null)
            {
                bridgeCheckpointClip = Resources.Load<AudioClip>("Audio/Music/BGM_02_Cathedral_Bridge_Dirge");
            }
        }

        private void SetupAudioSources()
        {
            _levelSource = gameObject.AddComponent<AudioSource>();
            _levelSource.clip = levelAmbientClip;
            _levelSource.loop = true;
            _levelSource.playOnAwake = false;
            _levelSource.spatialBlend = 0f;
            _levelSource.volume = 0f;

            _bridgeSource = gameObject.AddComponent<AudioSource>();
            _bridgeSource.clip = bridgeCheckpointClip;
            _bridgeSource.loop = true;
            _bridgeSource.playOnAwake = false;
            _bridgeSource.spatialBlend = 0f;
            _bridgeSource.volume = 0f;

            if (_levelSource.clip != null)
            {
                _levelSource.Play();
            }
        }

        private void Update()
        {
            float unscaledDt = Time.unscaledDeltaTime;
            float dt = Time.deltaTime;

            if (dt > 0f)
            {
                _resolveTimer -= dt;
                if (_resolveTimer <= 0f || _playerTransform == null)
                {
                    _resolveTimer = 1.0f;
                    ResolveSceneReferences();
                }

                UpdateCurrentZone();
            }

            UpdateAudioFades(unscaledDt > 0f ? unscaledDt : 0.016f);
        }

        private void ResolveSceneReferences()
        {
            if (_playerTransform == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _playerTransform = p.transform;
            }

            if (_arenaBounds == null)
            {
                _arenaBounds = FindFirstObjectByType<TeniaArenaBounds>();
            }

            if (_bridgeAnchor == null)
            {
                // Primary anchor: Virgil at the Cathedral Bridge checkpoint
                VirgilNpcController[] virgils = FindObjectsByType<VirgilNpcController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < virgils.Length; i++)
                {
                    if (virgils[i] != null && virgils[i].role == VirgilAppearanceRole.CathedralBridge)
                    {
                        _bridgeAnchor = virgils[i].transform;
                        break;
                    }
                }

                // Fallback anchor: Cathedral Gate trigger
                if (_bridgeAnchor == null)
                {
                    CathedralGateDramaticTrigger gate = FindFirstObjectByType<CathedralGateDramaticTrigger>();
                    if (gate != null) _bridgeAnchor = gate.transform;
                }
            }
        }

        private void UpdateCurrentZone()
        {
            if (_playerTransform == null)
            {
                _currentZone = MusicZone.LevelDefault;
                return;
            }

            Vector3 playerPos = _playerTransform.position;

            // 1. Inside Cathedral -> silence level music so Boss Music takes over cleanly
            if (_arenaBounds != null && _arenaBounds.IsPlayerInsideCathedral(playerPos))
            {
                _currentZone = MusicZone.CathedralSilent;
                return;
            }

            // 2. Near Cathedral Bridge Checkpoint -> play Som 2 (Gothic Cathedral Dirge)
            if (_bridgeAnchor != null)
            {
                float dist = Vector3.Distance(playerPos, _bridgeAnchor.position);
                if (_currentZone == MusicZone.BridgeCheckpoint)
                {
                    if (dist <= bridgeExitRadius)
                    {
                        return;
                    }
                }
                else if (dist <= bridgeEnterRadius)
                {
                    _currentZone = MusicZone.BridgeCheckpoint;
                    return;
                }
            }

            // 3. Default (Tutorial Cardia & Standard Stomach Abyss) -> play Som 1
            _currentZone = MusicZone.LevelDefault;
        }

        private void UpdateAudioFades(float dt)
        {
            float musicMult = StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Music);
            float targetLevelVol = (_currentZone == MusicZone.LevelDefault) ? levelMusicVolume * musicMult : 0f;
            float targetBridgeVol = (_currentZone == MusicZone.BridgeCheckpoint) ? bridgeMusicVolume * musicMult : 0f;
            float speed = (_currentZone == MusicZone.CathedralSilent) ? cathedralFadeOutSpeed : crossfadeSpeed;
            if (Time.timeScale <= 0f) speed = 10f; // resposta imediata ao arrastar o slider no Pause

            if (_levelSource != null && _levelSource.clip != null)
            {
                if (targetLevelVol > 0.001f && !_levelSource.isPlaying)
                {
                    _levelSource.Play();
                }

                _levelSource.volume = Mathf.MoveTowards(_levelSource.volume, targetLevelVol, speed * dt);

                if (_levelSource.volume <= 0.001f && targetLevelVol <= 0f && _levelSource.isPlaying)
                {
                    _levelSource.Pause();
                }
            }

            if (_bridgeSource != null && _bridgeSource.clip != null)
            {
                if (targetBridgeVol > 0.001f && !_bridgeSource.isPlaying)
                {
                    _bridgeSource.Play();
                }

                _bridgeSource.volume = Mathf.MoveTowards(_bridgeSource.volume, targetBridgeVol, speed * dt);

                if (_bridgeSource.volume <= 0.001f && targetBridgeVol <= 0f && _bridgeSource.isPlaying)
                {
                    _bridgeSource.Pause();
                }
            }
        }
    }
}
