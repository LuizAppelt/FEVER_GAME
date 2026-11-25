using System.Collections.Generic;
using StomachAbyss.Combat;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Projetil bumerangue de uma das 3 laminas de barbear da Espada Cruz.
    /// Avanca girando na direcao mirada e retorna em arco exatamente ao slot de origem.
    /// </summary>
    public class CrossRazorBoomerangProjectile : MonoBehaviour
    {
        private CrossRazorGreatswordController _owner;
        private Transform _ownerRoot;
        private int _slotIndex;
        private Vector3 _launchDir;
        private Vector3 _startPos;

        private float _outboundSpeed = 20f;
        private float _returnSpeed = 14.5f;
        private float _maxRange = 16.5f;
        private float _damage = 38f;
        private float _apexHoverDuration = 0.42f;
        private float _spinSpeed = 1680f;
        private float _hitRadius = 1.15f;

        private bool _hoveringAtApex;
        private float _apexHoverTimer;
        private float _apexTickTimer;
        private bool _returning;
        private float _traveled;
        private float _flightTimer;
        private float _curveSideSign = 1f;
        private Vector3 _currentMoveDir;
        private Transform _visualSpinNode;
        private readonly HashSet<GameObject> _hitOutbound = new HashSet<GameObject>();
        private readonly HashSet<GameObject> _hitReturn = new HashSet<GameObject>();

        public void Launch(
            CrossRazorGreatswordController owner,
            Transform ownerRoot,
            int slotIndex,
            Vector3 spawnPos,
            Vector3 direction,
            Mesh bladeMesh,
            Material bladeMaterial,
            Vector3 visualScale,
            float outboundSpeed = 20f,
            float returnSpeed = 14.5f,
            float maxRange = 16.5f,
            float damage = 38f,
            float apexHoverDuration = 0.42f)
        {
            _owner = owner;
            _ownerRoot = ownerRoot;
            _slotIndex = slotIndex;
            _startPos = spawnPos;
            _launchDir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            _currentMoveDir = _launchDir;
            _outboundSpeed = outboundSpeed;
            _returnSpeed = returnSpeed;
            _maxRange = maxRange;
            _damage = damage;
            _apexHoverDuration = apexHoverDuration;
            _curveSideSign = (slotIndex % 2 == 0) ? 1f : -1f;

            transform.position = spawnPos;
            transform.rotation = Quaternion.LookRotation(_launchDir, Vector3.up);

            // Monta o visual identico da lamina de barbear girando deitado como disco cortante
            GameObject visualGo = new GameObject("Spinning_Razor_Visual");
            visualGo.transform.SetParent(transform, false);
            visualGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visualGo.transform.localScale = visualScale;

            MeshFilter mf = visualGo.AddComponent<MeshFilter>();
            mf.sharedMesh = bladeMesh;
            MeshRenderer mr = visualGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = bladeMaterial;

            _visualSpinNode = visualGo.transform;

            // Rastro cortante acompanhando o voo do bumerangue
            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
            CrossRazorGreatswordController.ConfigureBladeTrail(
                trail,
                0.22f,
                0.24f,
                CrossRazorGreatswordController.CreateOrReuseTrailMaterial()
            );
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _flightTimer += dt;

            if (_visualSpinNode != null)
            {
                _visualSpinNode.Rotate(0f, 0f, _spinSpeed * dt, Space.Self);
            }

            if (_owner == null || _flightTimer > 10.5f)
            {
                FinishReturn();
                return;
            }

            if (_hoveringAtApex)
            {
                _apexHoverTimer -= dt;

                // Enquanto a espada estiver totalmente sem laminas no cooldown de recarga,
                // segura as laminas no apice girando apenas visualmente (sem dano continuo infinito)
                if (_apexHoverTimer <= 0f && !_owner.ShouldDelayBladeReturn)
                {
                    _hoveringAtApex = false;
                    BeginReturn();
                }
                return;
            }

            if (!_returning)
            {
                float step = _outboundSpeed * dt;
                // Checa colisao com paredes solidas antes de avancar
                if (Physics.SphereCast(transform.position, 0.28f, _launchDir, out RaycastHit hit, step, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (_ownerRoot == null || !hit.transform.IsChildOf(_ownerRoot))
                    {
                        // Nao bate no chao plano se estiver voando paralelo
                        if (hit.normal.y < 0.7f)
                        {
                            transform.position = hit.point + hit.normal * 0.35f;
                            EvaluateHitAround(transform.position, _hitOutbound);
                            _owner?.NotifyBladeImpact(hit.point, false);
                            StartApexHover();
                            return;
                        }
                    }
                }

                transform.position += _launchDir * step;
                _currentMoveDir = _launchDir;
                _traveled += step;
                EvaluateHitAround(transform.position, _hitOutbound);

                if (_traveled >= _maxRange)
                {
                    StartApexHover();
                }
            }
            else
            {
                Vector3 targetPos = _owner.GetSlotWorldPosition(_slotIndex);
                Vector3 toSlot = targetPos - transform.position;
                float dist = toSlot.magnitude;

                if (_owner.ShouldDelayBladeReturn)
                {
                    // Caso ja estivesse voltando perto do jogador quando a ultima lamina saiu, mantem espera em distancia segura
                    if (dist < 4.5f)
                    {
                        Vector3 awayDir = dist > 0.05f ? (-toSlot / dist) : -_launchDir;
                        transform.position += awayDir * (_returnSpeed * 0.45f * dt);
                    }
                    return;
                }

                if (dist <= Mathf.Max(0.85f, _returnSpeed * dt * 1.4f))
                {
                    FinishReturn();
                    return;
                }

                Vector3 dir = toSlot / dist;
                // Leve arco lateral de bumerangue enquanto distante do retorno
                float arcBlend = Mathf.Clamp01((dist - 1.5f) / 8f);
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * _curveSideSign;
                Vector3 moveDir = (dir + side * (0.28f * arcBlend)).normalized;
                _currentMoveDir = moveDir;

                transform.position += moveDir * (_returnSpeed * dt);
                EvaluateHitAround(transform.position, _hitReturn);
            }
        }

        private void StartApexHover()
        {
            if (_apexHoverDuration <= 0.01f)
            {
                BeginReturn();
                return;
            }
            _hoveringAtApex = true;
            _apexHoverTimer = _apexHoverDuration;
            _apexTickTimer = 0.14f;
        }

        private void BeginReturn()
        {
            _returning = true;
            _outboundSpeed *= 0.8f;
        }

        private void FinishReturn()
        {
            if (_owner != null)
            {
                _owner.ReturnBladeToSlot(_slotIndex);
            }
            Destroy(gameObject);
        }

        private void EvaluateHitAround(Vector3 center, HashSet<GameObject> alreadyHit)
        {
            bool hitDamageable = false;
            Vector3 attackDir = _currentMoveDir.sqrMagnitude > 0.001f ? _currentMoveDir : _launchDir;

            Collider[] cols = Physics.OverlapSphere(center, _hitRadius);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null) continue;
                    if (_ownerRoot != null && c.transform.IsChildOf(_ownerRoot)) continue;

                    if (!alreadyHit.Add(c.gameObject)) continue;

                    OrganPulse op = c.GetComponentInParent<OrganPulse>();
                    if (op != null)
                    {
                        op.TriggerHitRecoil();
                        _owner?.NotifyBladeImpact(c.ClosestPoint(center), true);
                    }

                    IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                    if (dmg != null && (_ownerRoot == null || !ReferenceEquals(dmg, _ownerRoot.GetComponent<SimplePlayerController>())))
                    {
                        Vector3 impactPt = c.ClosestPoint(center);
                        BloodSplatterSystem.BeginAttackBurstContext(attackDir, impactPt, 0.92f, false);
                        CombatSfxBank.BeginImpactScope(false);
                        try
                        {
                            dmg.TakeAcidDamage(_damage);
                        }
                        finally
                        {
                            if (!BloodSplatterSystem.WasEntityHandledInContext)
                            {
                                BloodSplatterSystem.SpawnBloodBurst(impactPt, attackDir, 0.92f, null, false);
                            }
                            if (!CombatSfxBank.WasImpactClaimedInScope)
                            {
                                CombatSfxBank.PlayImpact(false, impactPt, 0.78f);
                            }
                            BloodSplatterSystem.EndAttackBurstContext();
                            CombatSfxBank.EndImpactScope(false);
                        }

                        hitDamageable = true;
                        _owner?.NotifyBladeImpact(impactPt, true);
                    }
                }
            }

            if (hitDamageable)
            {
                CombatHitstopManager.TriggerHitstop(0.036f, 0.04f, false);
            }

            // Interacao direta com bolhas, ulceras e portas viscerais do mapa
            AcidBubblePopper[] bubbles = FindObjectsByType<AcidBubblePopper>(FindObjectsSortMode.None);
            foreach (AcidBubblePopper b in bubbles)
            {
                if (b != null && !alreadyHit.Contains(b.gameObject) &&
                    Vector3.Distance(center, b.transform.position) <= _hitRadius + 0.6f)
                {
                    alreadyHit.Add(b.gameObject);
                    b.PopByAttack();
                    _owner?.NotifyBladeImpact(b.transform.position, true);
                }
            }

            UlcerEruption[] ulcers = FindObjectsByType<UlcerEruption>(FindObjectsSortMode.None);
            foreach (UlcerEruption u in ulcers)
            {
                if (u != null && !alreadyHit.Contains(u.gameObject) &&
                    Vector3.Distance(center, u.transform.position) <= _hitRadius + 0.9f)
                {
                    alreadyHit.Add(u.gameObject);
                    u.TriggerEruption();
                    _owner?.NotifyBladeImpact(u.transform.position, true);
                }
            }
        }
    }
}
