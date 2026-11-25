using System;
using System.Collections.Generic;
using System.IO;
using ImmuneEnemies;
using MapaEstomago;
using StomachAbyss.Combat;
using UnityEngine;

namespace StomachAbyss.Bonfire
{
    /// <summary>
    /// Registro global e persistencia em disco da rede de Portas Vivas (Bonfire + Teleporte).
    /// Mapeia as 4 portas nas 3 ilhas em sequencia:
    /// - Slot 0 (Ilha Esquerda):      Flesh_Eye_Door_CardiaExit
    /// - Slot 1 (Ilha do Meio - Sul): Flesh_Eye_Door_OssuaryArrival
    /// - Slot 2 (Ilha do Meio - Norte): Flesh_Eye_Door_CentralArena
    /// - Slot 3 (Ilha Direita):       Flesh_Eye_Door_CathedralBridge
    /// </summary>
    public static class BonfireDoorNetwork
    {
        public const int TotalMapSlots = 4;

        private const string PrefsKey = "HELLFLESH_BonfireDoors_v1";
        private const string SaveFileName = "hellflesh_bonfire_doors.json";

        public const string IdCardiaExit = "cardia_exit";
        public const string IdOssuaryArrival = "ossuary_arrival";
        public const string IdCentralArena = "central_arena";
        public const string IdCathedralBridge = "cathedral_bridge";

        [Serializable]
        private class BonfireSaveData
        {
            public List<string> unlockedDoorIds = new List<string>();
        }

        private static readonly HashSet<string> s_unlockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, DoorTransitionTrigger> s_slotToTrigger = new Dictionary<int, DoorTransitionTrigger>();
        private static readonly Dictionary<string, DoorTransitionTrigger> s_idToTrigger = new Dictionary<string, DoorTransitionTrigger>(StringComparer.OrdinalIgnoreCase);
        private static bool s_loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayMode()
        {
            s_loaded = false;
            s_unlockedIds.Clear();
            s_slotToTrigger.Clear();
            s_idToTrigger.Clear();
        }

        private static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            s_unlockedIds.Clear();

            string json = null;
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    json = File.ReadAllText(SaveFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BonfireDoorNetwork] Falha ao ler save em disco: {ex.Message}");
            }

            if (string.IsNullOrEmpty(json) && PlayerPrefs.HasKey(PrefsKey))
            {
                json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    BonfireSaveData data = JsonUtility.FromJson<BonfireSaveData>(json);
                    if (data != null && data.unlockedDoorIds != null)
                    {
                        for (int i = 0; i < data.unlockedDoorIds.Count; i++)
                        {
                            if (!string.IsNullOrEmpty(data.unlockedDoorIds[i]))
                            {
                                s_unlockedIds.Add(data.unlockedDoorIds[i]);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BonfireDoorNetwork] Save corrompido, iniciando limpo: {ex.Message}");
                }
            }
        }

        private static void SaveToDisk()
        {
            BonfireSaveData data = new BonfireSaveData();
            foreach (string id in s_unlockedIds)
            {
                data.unlockedDoorIds.Add(id);
            }

            string json = JsonUtility.ToJson(data, true);
            PlayerPrefs.SetString(PrefsKey, json);
            PlayerPrefs.Save();

            try
            {
                string dir = Path.GetDirectoryName(SaveFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(SaveFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BonfireDoorNetwork] Falha ao gravar save em disco: {ex.Message}");
            }
        }

        public static void ResetProgress()
        {
            s_unlockedIds.Clear();
            s_loaded = true;
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    File.Delete(SaveFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BonfireDoorNetwork] Falha ao apagar arquivo de save: {ex.Message}");
            }
            Debug.Log("[BonfireDoorNetwork] Progresso de portas descobertas resetado.");
        }

        public static string GetCanonicalIdForSlot(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return IdCardiaExit;
                case 1: return IdOssuaryArrival;
                case 2: return IdCentralArena;
                case 3: return IdCathedralBridge;
                default: return $"door_slot_{slotIndex}";
            }
        }

        public static void InferDoorIdentity(FleshEyeDoorController door, out string doorId, out int slotIndex)
        {
            string name = door != null ? door.gameObject.name : string.Empty;

            if (name.IndexOf("CardiaExit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                doorId = IdCardiaExit;
                slotIndex = 0;
                return;
            }
            if (name.IndexOf("OssuaryArrival", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                doorId = IdOssuaryArrival;
                slotIndex = 1;
                return;
            }
            if (name.IndexOf("CentralArena", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                doorId = IdCentralArena;
                slotIndex = 2;
                return;
            }
            if (name.IndexOf("CathedralBridge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Cathedral", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                doorId = IdCathedralBridge;
                slotIndex = 3;
                return;
            }

            doorId = string.IsNullOrEmpty(name) ? "door_unknown" : name.ToLowerInvariant();
            slotIndex = -1;
        }

        public static void RegisterDoor(DoorTransitionTrigger trigger)
        {
            if (trigger == null) return;
            EnsureLoaded();

            string id = trigger.GetResolvedDoorId();
            int slot = trigger.GetResolvedMapSlotIndex();

            if (!string.IsNullOrEmpty(id))
            {
                s_idToTrigger[id] = trigger;
            }
            if (slot >= 0 && slot < TotalMapSlots)
            {
                s_slotToTrigger[slot] = trigger;
            }
        }

        public static void UnregisterDoor(DoorTransitionTrigger trigger)
        {
            if (trigger == null) return;

            string id = trigger.GetResolvedDoorId();
            if (!string.IsNullOrEmpty(id) && s_idToTrigger.TryGetValue(id, out DoorTransitionTrigger existingId) && existingId == trigger)
            {
                s_idToTrigger.Remove(id);
            }

            int slot = trigger.GetResolvedMapSlotIndex();
            if (s_slotToTrigger.TryGetValue(slot, out DoorTransitionTrigger existingSlot) && existingSlot == trigger)
            {
                s_slotToTrigger.Remove(slot);
            }
        }

        public static void RefreshSceneDoors()
        {
            EnsureLoaded();
            s_slotToTrigger.Clear();
            s_idToTrigger.Clear();

            DoorTransitionTrigger[] triggers = UnityEngine.Object.FindObjectsByType<DoorTransitionTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < triggers.Length; i++)
            {
                if (triggers[i] != null)
                {
                    RegisterDoor(triggers[i]);
                }
            }
        }

        public static bool IsDoorUnlocked(string doorId)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(doorId) && s_unlockedIds.Contains(doorId);
        }

        public static bool IsSlotUnlocked(int slotIndex)
        {
            EnsureLoaded();
            if (s_slotToTrigger.TryGetValue(slotIndex, out DoorTransitionTrigger trig) && trig != null)
            {
                return IsDoorUnlocked(trig.GetResolvedDoorId());
            }
            return IsDoorUnlocked(GetCanonicalIdForSlot(slotIndex));
        }

        public static void UnlockDoor(string doorId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(doorId)) return;
            if (s_unlockedIds.Add(doorId))
            {
                SaveToDisk();
                Debug.Log($"[BonfireDoorNetwork] Porta desbloqueada e salva: {doorId}");
            }
        }

        public static void UnlockDoor(FleshEyeDoorController door)
        {
            if (door == null) return;
            DoorTransitionTrigger trig = door.GetComponentInChildren<DoorTransitionTrigger>(true);
            if (trig != null)
            {
                UnlockDoor(trig.GetResolvedDoorId());
                return;
            }
            InferDoorIdentity(door, out string inferredId, out _);
            UnlockDoor(inferredId);
        }

        public static FleshEyeDoorController GetDoorForSlot(int slotIndex)
        {
            if (!s_slotToTrigger.TryGetValue(slotIndex, out DoorTransitionTrigger trig) || trig == null)
            {
                RefreshSceneDoors();
                s_slotToTrigger.TryGetValue(slotIndex, out trig);
            }

            if (trig != null)
            {
                FleshEyeDoorController ctrl = trig.GetComponentInParent<FleshEyeDoorController>();
                if (ctrl != null) return ctrl;
            }

            // Fallback: varre todas as portas na cena e infere pelo nome
            FleshEyeDoorController[] allDoors = UnityEngine.Object.FindObjectsByType<FleshEyeDoorController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allDoors.Length; i++)
            {
                if (allDoors[i] == null) continue;
                InferDoorIdentity(allDoors[i], out _, out int inferredSlot);
                if (inferredSlot == slotIndex)
                {
                    return allDoors[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Aplica todos os efeitos de descanso estilo Bonfire (Dark Souls):
        /// - Cura 100% da vida e limpa condicoes
        /// - Recarrega 100% do Estus
        /// - Atualiza respawnPosition para a porta atual
        /// - Renasce todos os inimigos comuns (Macrophage, Eosinophil)
        /// - Som + feedback de camera
        /// </summary>
        public static void PerformBonfireRest(SimplePlayerController player, Vector3 respawnWorldPos, bool playFeedback = true)
        {
            if (player != null)
            {
                player.respawnPosition = respawnWorldPos;
                player.ClearAllConditions();
                player.Heal(player.maxHealth);
                player.RefillEstus();
            }

            ImmuneEnemyBase.RespawnAllEnemies();

            if (playFeedback)
            {
                CombatCameraJuice.PunchFov(-2.0f, 0.35f);
                CombatSfxBank.Play(CombatSfxId.CheckpointLit, 0.85f, 0.96f, 1.04f, 0.5f);
            }
        }
    }
}
