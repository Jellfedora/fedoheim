using System;
using UnityEngine;

namespace FedoGuardian
{
    // IA du garde, écrite à la main par-dessus BaseAI plutôt que de réutiliser MonsterAI :
    // MonsterAI embarque plein de comportements de monstre sauvage qu'on ne veut surtout pas ici
    // (fuite si la vie est basse, fuite hors des zones "no monster" -- ce qui ferait fuir un garde
    // de la base même qu'il est censé protéger --, despawn de jour, consommation d'objets au sol,
    // monte d'une selle...). On garde uniquement ce qui est réellement partagé et utile :
    // détection/portée (m_viewRange/m_hearRange), notion d'ennemi par faction (IsEnemy/FindEnemy),
    // pathfinding (MoveTo/LookAt/StopMoving), tous hérités de BaseAI.
    public class GuardAI : BaseAI
    {
        private const string ZdoHomeYaw = "FedoGuardian_HomeYaw";
        private const float ArrivalDistance = 0.3f;
        private const float DefaultAttackRange = 2f;
        private const float LookAngleTolerance = 90f;

        private float _homeYaw;
        private Character _currentTarget;

        protected override void Awake()
        {
            base.Awake();

            // Jamais initialisé sinon (reste à 0, une valeur d'enum invalide -- les vrais types
            // commencent à 1) : Pathfinding.GetPath ne trouve alors pas de chemin correct,
            // d'où des déplacements erratiques/bloqués.
            m_pathAgentType = Pathfinding.AgentType.Humanoid;

            m_viewAngle = 360f;

            var zdo = m_nview.GetZDO();
            if (zdo != null)
            {
                _homeYaw = zdo.GetFloat(ZdoHomeYaw, base.transform.eulerAngles.y);
                if (m_nview.IsOwner())
                {
                    zdo.Set(ZdoHomeYaw, _homeYaw);
                }
            }
        }

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt))
            {
                return false;
            }

            // UpdateAI tourne dans MonoUpdaters, la boucle centrale qui met aussi à jour TOUS les
            // autres personnages (le joueur compris) au même endroit dans la frame. Une exception
            // non rattrapée ici casse le reste de cette itération pour cette frame -- vécu : plus
            // aucun personnage (dont le joueur) ne bouge tant que ce bug se reproduit à chaque tick.
            try
            {
                RunAI(dt);
            }
            catch (Exception e)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: GuardAI.UpdateAI a levé une exception : {e}");
            }

            return true;
        }

        private void RunAI(float dt)
        {
            m_viewRange = FedoGuardianPlugin.Instance.DetectionRange.Value;
            m_hearRange = FedoGuardianPlugin.Instance.DetectionRange.Value;

            var humanoid = m_character as Humanoid;
            if (humanoid == null)
            {
                return;
            }

            // Ne revient au poste que quand plus aucun ennemi n'est détectable (pas de notion de
            // laisse/distance au poste) : tant qu'il reste au moins un ennemi à portée de vue/ouïe,
            // le garde continue à se battre. On garde la cible déjà engagée tant qu'elle reste
            // détectable, pour éviter de sauter d'un ennemi à l'autre à chaque tick.
            if (_currentTarget != null && (_currentTarget.IsDead() || !CanSenseTarget(_currentTarget)))
            {
                _currentTarget = null;
            }

            if (_currentTarget == null)
            {
                _currentTarget = FindEnemy();
            }

            if (_currentTarget != null)
            {
                AttackTarget(humanoid, _currentTarget, dt);
            }
            else
            {
                ReturnToPost(dt);
            }
        }

        private void AttackTarget(Humanoid humanoid, Character target, float dt)
        {
            ItemDrop.ItemData weapon = humanoid.GetCurrentWeapon();
            float attackRange = weapon != null ? weapon.m_shared.m_aiAttackRange : DefaultAttackRange;

            bool inRange = MoveTo(dt, target.transform.position, attackRange, run: true);
            LookAt(target.GetTopPoint());

            if (inRange && !humanoid.InAttack() && IsLookingAt(target.GetTopPoint(), LookAngleTolerance))
            {
                humanoid.StartAttack(target, secondaryAttack: false);
            }
        }

        private void ReturnToPost(float dt)
        {
            bool arrived = MoveTo(dt, m_spawnPoint, ArrivalDistance, run: false);
            if (arrived)
            {
                StopMoving();
                m_character.SetLookDir(Quaternion.Euler(0f, _homeYaw, 0f) * Vector3.forward);
            }
        }
    }
}
