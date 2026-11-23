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
            if (respawnPoint != null)
            {
                player.respawnPosition = respawnPoint.position;
            }
        }
    }
}
