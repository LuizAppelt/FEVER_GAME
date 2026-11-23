using System.Collections;
using BossTenia;
using UnityEngine;

namespace StomachAbyss.Tutorial
{
    /// <summary>
    /// Keeps the post-boss Cathedral placement of Virgil inactive until Malphas (TeniaBossHealth)
    /// is defeated, then reveals Virgil at the center of the Cathedral floor eye medallion.
    /// Attach to an always-active GameObject in the Cathedral.
    /// </summary>
    public class CathedralPostBossVirgilSpawner : MonoBehaviour
    {
        [Header("References")]
        public GameObject virgilObject;
        public TeniaBossHealth bossHealth;
        public GameObject bossObject;

        [Header("Reveal Timing")]
        [Min(0f)]
        public float revealDelaySeconds = 1.2f;

        [Min(0.05f)]
        public float revealDurationSeconds = 0.9f;

        private bool _bossExisted;
        private bool _spawnTriggered;
        private bool _subscribed;
        private Vector3 _virgilBaseScale = Vector3.one;
        private Light _virgilLight;
        private float _virgilBaseLightRange;

        private void Awake()
        {
            ResolveBossReferences();
            CacheVirgilInitialState();

            if (virgilObject != null && (bossHealth == null || !bossHealth.IsDead))
            {
                virgilObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            SubscribeBossEvent();
        }

        private void OnDisable()
        {
            UnsubscribeBossEvent();
        }

        private void Start()
        {
            ResolveBossReferences();
            SubscribeBossEvent();

            if (!_spawnTriggered)
            {
                if (IsBossDefeated())
                {
                    TriggerVirgilReveal();
                }
                else if (virgilObject != null && virgilObject.activeSelf)
                {
                    virgilObject.SetActive(false);
                }
            }
        }

        private void Update()
        {
            if (_spawnTriggered)
            {
                return;
            }

            if (IsBossDefeated())
            {
                TriggerVirgilReveal();
            }
        }

        private void ResolveBossReferences()
        {
            if (bossHealth == null && bossObject != null)
            {
                bossHealth = bossObject.GetComponent<TeniaBossHealth>();
            }

            if (bossHealth == null && bossObject == null)
            {
                bossHealth = FindAnyObjectByType<TeniaBossHealth>();
            }

            if (bossHealth != null)
            {
                _bossExisted = true;
                if (bossObject == null)
                {
                    bossObject = bossHealth.gameObject;
                }
            }
            else if (bossObject != null)
            {
                _bossExisted = true;
            }
        }

        private void CacheVirgilInitialState()
        {
            if (virgilObject == null)
            {
                return;
            }

            Vector3 scale = virgilObject.transform.localScale;
            _virgilBaseScale = scale.sqrMagnitude > 0.0001f ? scale : Vector3.one;

            VirgilNpcController ctrl = virgilObject.GetComponent<VirgilNpcController>();
            if (ctrl != null && ctrl.biolumLight != null)
            {
                _virgilLight = ctrl.biolumLight;
                _virgilBaseLightRange = _virgilLight.range;
            }
        }

        private void SubscribeBossEvent()
        {
            if (_subscribed || bossHealth == null)
            {
                return;
            }

            bossHealth.OnDefeated += HandleBossDefeated;
            _subscribed = true;
        }

        private void UnsubscribeBossEvent()
        {
            if (!_subscribed || bossHealth == null)
            {
                return;
            }

            bossHealth.OnDefeated -= HandleBossDefeated;
            _subscribed = false;
        }

        private bool IsBossDefeated()
        {
            if (bossHealth != null)
            {
                return bossHealth.IsDead || bossHealth.currentHealth <= 0f;
            }

            if (_bossExisted)
            {
                return bossObject == null || !bossObject.activeInHierarchy;
            }

            return false;
        }

        private void HandleBossDefeated()
        {
            TriggerVirgilReveal();
        }

        private void TriggerVirgilReveal()
        {
            if (_spawnTriggered)
            {
                return;
            }

            _spawnTriggered = true;
            UnsubscribeBossEvent();

            if (virgilObject == null)
            {
                return;
            }

            StartCoroutine(RevealVirgilRoutine());
        }

        private IEnumerator RevealVirgilRoutine()
        {
            if (revealDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(revealDelaySeconds);
            }

            if (virgilObject == null)
            {
                yield break;
            }

            float duration = Mathf.Max(0.05f, revealDurationSeconds);
            Vector3 startScale = _virgilBaseScale * 0.05f;
            virgilObject.transform.localScale = startScale;

            if (_virgilLight != null)
            {
                _virgilLight.range = 0f;
            }

            virgilObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);

                if (virgilObject != null)
                {
                    virgilObject.transform.localScale = Vector3.LerpUnclamped(startScale, _virgilBaseScale, ease);
                }

                if (_virgilLight != null)
                {
                    _virgilLight.range = Mathf.Lerp(0f, _virgilBaseLightRange, ease);
                }

                yield return null;
            }

            if (virgilObject != null)
            {
                virgilObject.transform.localScale = _virgilBaseScale;
            }

            if (_virgilLight != null)
            {
                _virgilLight.range = _virgilBaseLightRange;
            }
        }
    }
}
