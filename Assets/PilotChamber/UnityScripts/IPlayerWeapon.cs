using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Contrato unificado para qualquer arma equipada pelo SimplePlayerController.
    /// Permite trocar ou adicionar novas armas mantendo controles, esquiva e cancelamento consistentes.
    /// </summary>
    public interface IPlayerWeapon
    {
        string WeaponDisplayName { get; }
        string HudStatusText { get; }
        bool IsBusy { get; }
        bool IsAiming { get; }
        bool SuppressesDefaultClawAttack { get; }
        float MoveSpeedMultiplier { get; }

        void OnWeaponEquipped(SimplePlayerController owner);
        void OnWeaponUnequipped();
        void OnPrimaryAttackPressed();
        void OnSecondaryAimState(bool held, bool justReleased);
        void CancelActiveAttack();
    }
}
