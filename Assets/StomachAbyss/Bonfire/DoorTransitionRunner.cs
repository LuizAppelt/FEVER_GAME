using MapaEstomago;
using UnityEngine;

namespace StomachAbyss.Bonfire
{
    /// <summary>
    /// Host persistente para rodar a coroutine de transicao das portas vivas,
    /// garantindo que o zone-culling de uma area nunca interrompa o teleporte no meio.
    /// </summary>
    public class DoorTransitionRunner : MonoBehaviour
    {
        private static DoorTransitionRunner s_instance;

        public static DoorTransitionRunner EnsureExists()
        {
            if (s_instance != null) return s_instance;
            GameObject go = new GameObject("DoorTransition_CoroutineRunner");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<DoorTransitionRunner>();
            return s_instance;
        }

        private void OnDestroy()
        {
            if (DoorTransitionTrigger.IsTransitionRunning)
            {
                DoorTransitionTrigger.ForceClearFade();
            }
            if (s_instance == this)
            {
                s_instance = null;
            }
        }
    }
}
