using MapaEstomago;
using UnityEngine;

namespace StomachAbyss.Tutorial
{
    /// <summary>
    /// Arrival checkpoint trigger that optionally updates the player's respawn position
    /// once (when respawnPoint is assigned) and displays an atmospheric area arrival banner.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CardiaArrivalCheckpoint : MonoBehaviour
    {
        public Transform respawnPoint;
        private bool _triggered;

        private void Awake()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered || other == null) return;

            SimplePlayerController player = null;
            if (other.CompareTag("Player"))
            {
                player = other.GetComponent<SimplePlayerController>() ?? other.GetComponentInParent<SimplePlayerController>();
            }
            else
            {
                SimplePlayerController candidate = other.GetComponentInParent<SimplePlayerController>();
                if (candidate != null && candidate.CompareTag("Player"))
                {
                    player = candidate;
                }
            }

            if (player == null) return;

            _triggered = true;
            player.respawnPosition = respawnPoint != null ? respawnPoint.position : transform.position;
            player.RefillEstus();
            player.Heal(player.maxHealth);
            StomachAbyss.Combat.CombatCameraJuice.PunchFov(-2.0f, 0.35f);
            StomachAbyss.Combat.CombatSfxBank.Play(StomachAbyss.Combat.CombatSfxId.CheckpointLit, 0.85f, 0.97f, 1.03f, 0.5f);
        }
    }
}
