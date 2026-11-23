using System.Collections;
using System.Collections.Generic;
using StomachAbyss.Combat;
using UnityEngine;

namespace StomachAbyss.Tutorial
{
    /// <summary>
    /// Visceral tendril seal blocking the Living Flesh Door until all linked TutorialCystTarget
    /// nodes/glands have been destroyed.
    /// </summary>
    public class TutorialGateBarrier : MonoBehaviour
    {
        public List<TutorialCystTarget> requiredTargets = new List<TutorialCystTarget>();
        public Collider blockingCollider;
        public GameObject[] tendrilVisuals;
        public Light gateStatusLight;
        public Collider doorTriggerToEnable;

        private bool _isUnlocked;

        private void Awake()
        {
            if (blockingCollider != null)
            {
                blockingCollider.enabled = true;
            }

            if (doorTriggerToEnable != null)
            {
                doorTriggerToEnable.enabled = false;
            }

            if (gateStatusLight != null)
            {
                gateStatusLight.color = new Color(0.95f, 0.28f, 0.16f);
            }
        }

        public void NotifyTargetDestroyed(TutorialCystTarget target)
        {
            if (_isUnlocked) return;

            int totalCount = 0;
            int destroyedCount = 0;

            if (requiredTargets != null)
            {
                for (int i = 0; i < requiredTargets.Count; i++)
                {
                    TutorialCystTarget t = requiredTargets[i];
                    if (t == null) continue;

                    totalCount++;
                    if (t.IsDestroyed)
                    {
                        destroyedCount++;
                    }
                }
            }

            if (destroyedCount < totalCount)
            {
                Vector3 sfxPos = target != null ? target.transform.position : transform.position;
                CombatSfxBank.PlayAt(CombatSfxId.HitWeakspotCrit, sfxPos, 0.85f, 0.85f, 0.95f);
                VirgilDialogueUI.GetOrCreate().ShowBannerToast(
                    $"SELO ORGÂNICO ROMPIDO ({destroyedCount}/{totalCount})",
                    "Quebre os selos restantes nas arenas do Platô Ritual",
                    2.8f);
                return;
            }

            UnlockGate();
        }

        private void UnlockGate()
        {
            _isUnlocked = true;

            if (blockingCollider != null)
            {
                blockingCollider.enabled = false;
            }

            if (doorTriggerToEnable != null)
            {
                doorTriggerToEnable.enabled = true;
            }

            if (gateStatusLight != null)
            {
                gateStatusLight.color = new Color(0.24f, 0.96f, 0.76f);
            }

            CombatSfxBank.PlayAt(CombatSfxId.RazorReturnLock, transform.position, 0.92f, 0.85f, 0.95f);

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(ShrinkAndHideTendrilsRoutine());
            }
            else
            {
                HideTendrilsImmediate();
            }

            VirgilDialogueUI.GetOrCreate().ShowBannerToast(
                "PORTA VIVA DESBLOQUEADA",
                "Suba ao Altar Norte e entre na boca orgânica rumo ao Abismo Gástrico",
                3.5f);
        }

        private IEnumerator ShrinkAndHideTendrilsRoutine()
        {
            if (tendrilVisuals == null || tendrilVisuals.Length == 0) yield break;

            Vector3[] initialScales = new Vector3[tendrilVisuals.Length];
            for (int i = 0; i < tendrilVisuals.Length; i++)
            {
                if (tendrilVisuals[i] != null)
                {
                    initialScales[i] = tendrilVisuals[i].transform.localScale;
                }
            }

            const float shrinkDuration = 0.45f;
            float elapsed = 0f;
            while (elapsed < shrinkDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / shrinkDuration);
                float factor = 1f - t * t;
                for (int i = 0; i < tendrilVisuals.Length; i++)
                {
                    if (tendrilVisuals[i] != null)
                    {
                        tendrilVisuals[i].transform.localScale = initialScales[i] * factor;
                    }
                }
                yield return null;
            }

            HideTendrilsImmediate();
        }

        private void HideTendrilsImmediate()
        {
            if (tendrilVisuals == null) return;
            for (int i = 0; i < tendrilVisuals.Length; i++)
            {
                if (tendrilVisuals[i] != null)
                {
                    tendrilVisuals[i].SetActive(false);
                }
            }
        }
    }
}
