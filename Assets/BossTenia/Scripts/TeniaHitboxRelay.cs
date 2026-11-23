using MapaEstomago;
using UnityEngine;

namespace BossTenia
{
    /// <summary>
    /// Lightweight hitbox relay placed on child boss segment colliders
    /// (Head, Body_Segment_XX, Tail, AcidOrgan_XX) to forward damage
    /// with per-part multipliers and weak-spot flags to TeniaBossHealth.
    /// </summary>
    public class TeniaHitboxRelay : MonoBehaviour, IAcidDamageable
    {
        [Tooltip("Boss health component on the root. Auto-resolved from parents if left null.")]
        public TeniaBossHealth ownerHealth;

        [Tooltip("Damage multiplier for this hitbox (e.g. 1.25 Head, 1.75 AcidOrgan weak-spot, 1.0 Body).")]
        [Min(0f)]
        public float damageMultiplier = 1.0f;

        [Tooltip("True for critical weak-spot hitboxes such as AcidOrgan sacs.")]
        public bool isWeakSpot = false;

        private void Awake()
        {
            if (ownerHealth == null)
            {
                ownerHealth = GetComponentInParent<TeniaBossHealth>();
            }
        }

        public void TakeAcidDamage(float amount)
        {
            if (ownerHealth == null)
            {
                ownerHealth = GetComponentInParent<TeniaBossHealth>();
            }

            if (ownerHealth != null)
            {
                ownerHealth.ApplySegmentDamage(amount, transform.position, damageMultiplier, isWeakSpot);
            }
        }
    }
}