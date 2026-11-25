#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace StomachAbyss.Bonfire.EditorTools
{
    public static class BonfireDoorEditorTools
    {
        [MenuItem("HELLFLESH/Bonfire/Resetar Portas Descobertas (Testar 1a Entrada)")]
        public static void ResetDiscoveredDoors()
        {
            BonfireDoorNetwork.ResetProgress();
        }

        [MenuItem("HELLFLESH/Bonfire/Desbloquear Todas as 4 Portas (Teste Rapido)")]
        public static void UnlockAllFourDoors()
        {
            BonfireDoorNetwork.UnlockDoor(BonfireDoorNetwork.IdCardiaExit);
            BonfireDoorNetwork.UnlockDoor(BonfireDoorNetwork.IdOssuaryArrival);
            BonfireDoorNetwork.UnlockDoor(BonfireDoorNetwork.IdCentralArena);
            BonfireDoorNetwork.UnlockDoor(BonfireDoorNetwork.IdCathedralBridge);
            Debug.Log("[BonfireDoorEditorTools] Todas as 4 portas foram desbloqueadas para teste.");
        }
    }
}
#endif
