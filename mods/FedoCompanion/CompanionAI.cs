using System;
using UnityEngine;

namespace FedoCompanion
{
    // IA du compagnon, écrite par-dessus BaseAI comme FedoGuardian.GuardAI -- mais ici on garde
    // le Character/Animator d'origine du Greyling cloné (voir CompanionPrefabPatch), donc pas
    // besoin de recréer un Humanoid nu : cette classe ne fait QUE le suivi/soin/ramassage,
    // jamais de combat (le compagnon est pacifiste, en faction Boss pour être ignoré des
    // monstres sauvages, et invulnérable -- voir CompanionInvulnerabilityPatch).
    public class CompanionAI : BaseAI
    {
        // Posée sur le ZDO du COMPAGNON : PlayerID stable du propriétaire (Player.GetPlayerID(),
        // lié au profil/à la sauvegarde du personnage), PAS son ZDOID de session -- vécu : le
        // ZDOID d'un joueur change à chaque reconnexion, ce qui laissait le compagnon figé pour
        // toujours après une déco/reco (ResolveOwner ne retrouvait plus jamais personne).
        private const string ZdoOwnerPlayerId = "FedoCompanion_OwnerPlayerId";

        // Nom personnalisé (Maj+E, voir CompanionInteract) persisté sur le ZDO du compagnon --
        // sinon un renommage serait perdu au prochain Awake (CompanionPrefabPatch réapplique le
        // nom par défaut du .cfg sur le gabarit à chaque instanciation).
        internal const string ZdoCustomName = "FedoCompanion_Name";

        // Pour un renommage (Maj+E) : le ZDO du compagnon (ZdoCustomName) est détruit dès
        // qu'il est rangé (le charme le supprime, voir SummonItemUsePatch) -- sans une copie sur
        // le PROPRIÉTAIRE, le prochain compagnon invoqué repartait sur le nom par défaut du .cfg.
        private const string CustomDataCompanionName = "FedoCompanion_CompanionName";

        private const float ArrivalDistance = 0.5f;
        private const float PickupArrivalDistance = 1f;
        private const float FullHealthEpsilon = 0.01f;
        private const float OwnershipCheckIntervalSeconds = 2f;

        // Character n'expose aucun "IsOnGround()" public (seul le champ privé m_groundContact
        // existe) -- une vitesse verticale quasi nulle est le meilleur signal disponible pour
        // distinguer un joueur posé au sol (ou immobile) d'un joueur en chute/en saut.
        private const float GroundedVerticalVelocityThreshold = 0.3f;

        private Player _owner;
        private float _healCooldownTimer;
        private float _pickupSearchTimer;
        private float _ownershipCheckTimer;
        private float _chatCooldownTimer;
        private ItemDrop _pickupTarget;

        public static void LinkToOwner(GameObject companion, Player owner)
        {
            if (owner == null)
            {
                return;
            }

            var companionView = companion.GetComponent<ZNetView>();
            ZDO companionZdo = companionView != null ? companionView.GetZDO() : null;
            companionZdo?.Set(ZdoOwnerPlayerId, owner.GetPlayerID());
        }

        // Réapplique le dernier nom personnalisé connu (Maj+E) à un compagnon fraîchement
        // invoqué -- appelé par CompanionSpawner juste après la création, avant que le joueur
        // ait eu l'occasion de le voir avec le nom par défaut du .cfg.
        public static void ApplySavedName(GameObject companion, Player owner)
        {
            if (owner?.m_customData == null ||
                !owner.m_customData.TryGetValue(CustomDataCompanionName, out string savedName) ||
                string.IsNullOrEmpty(savedName))
            {
                return;
            }

            var character = companion.GetComponent<Character>();
            if (character != null)
            {
                character.m_name = savedName;
            }

            var nview = companion.GetComponent<ZNetView>();
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            zdo?.Set(ZdoCustomName, savedName);
        }

        // Utilisé par SummonItemUsePatch pour savoir si ce joueur a déjà un compagnon vivant
        // (donc s'il faut le ranger plutôt que d'en invoquer un nouveau). Scanne directement les
        // Character actuellement chargés plutôt que de suivre un pointeur stocké côté joueur
        // (Player.m_customData) : vécu après une déco/reco, un compagnon toujours présent dans la
        // zone n'était plus retrouvé (le pointeur ne semble pas survivre de façon fiable à une
        // reconnexion), laissant croire à tort qu'aucun compagnon n'existait. La seule donnée dont
        // on est certain qu'elle persiste correctement est le ZDO du compagnon lui-même (un objet
        // du monde comme un autre) -- ne dépend donc plus que de ça. Ne trouve, comme avant, que
        // les objets actuellement chargés : un compagnon resté dans une zone déchargée loin du
        // joueur (ex: téléportation/portail) ne sera pas détecté et un second pourra apparaître à
        // côté du premier ; limitation connue, pas résolue ici.
        public static GameObject FindExistingCompanion(Player owner)
        {
            if (owner == null)
            {
                return null;
            }

            long ownerId = owner.GetPlayerID();

            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character.GetComponent<CompanionAI>() == null)
                {
                    continue;
                }

                ZNetView view = character.GetComponent<ZNetView>();
                ZDO zdo = view != null ? view.GetZDO() : null;
                if (zdo != null && zdo.GetLong(ZdoOwnerPlayerId, 0) == ownerId)
                {
                    return character.gameObject;
                }
            }

            return null;
        }

        protected override void Awake()
        {
            base.Awake();

            // Jamais initialisé sinon (reste à 0, une valeur d'enum invalide -- cf. commentaire
            // équivalent dans GuardAI.Awake) : Pathfinding.GetPath ne trouve alors pas de chemin
            // correct, d'où des déplacements erratiques/bloqués.
            m_pathAgentType = Pathfinding.AgentType.Humanoid;

            ZDO zdo = m_nview != null ? m_nview.GetZDO() : null;
            string customName = zdo != null ? zdo.GetString(ZdoCustomName, string.Empty) : string.Empty;
            if (!string.IsNullOrEmpty(customName) && m_character != null)
            {
                m_character.m_name = customName;
            }
        }

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt))
            {
                return false;
            }

            // UpdateAI tourne dans MonoUpdaters, la boucle centrale qui met aussi à jour TOUS
            // les autres personnages (le joueur compris) au même endroit dans la frame. Une
            // exception non rattrapée ici casse le reste de cette itération pour cette frame.
            try
            {
                RunAI(dt);
            }
            catch (Exception e)
            {
                FedoCompanionPlugin.Log?.LogError($"FedoCompanion: CompanionAI.UpdateAI a levé une exception : {e}");
            }

            return true;
        }

        // Update() (MonoBehaviour brut) tourne sur TOUS les clients ayant cet objet chargé,
        // contrairement à UpdateAI ci-dessus qui ne s'exécute (via BaseAI) QUE sur le pair
        // propriétaire du ZDO -- c'est le seul endroit capable de réclamer la propriété si elle
        // est restée bloquée sur un pair déconnecté, sans quoi le compagnon resterait figé
        // indéfiniment (plus personne n'exécutant jamais RunAI pour lui).
        private void Update()
        {
            _ownershipCheckTimer -= Time.deltaTime;
            if (_ownershipCheckTimer > 0f)
            {
                return;
            }

            _ownershipCheckTimer = OwnershipCheckIntervalSeconds;

            try
            {
                TryReclaimOwnership();
            }
            catch (Exception e)
            {
                FedoCompanionPlugin.Log?.LogError($"FedoCompanion: CompanionAI.Update a levé une exception : {e}");
            }
        }

        private void TryReclaimOwnership()
        {
            if (m_nview == null || m_nview.IsOwner())
            {
                return;
            }

            long ownerPlayerId = m_nview.GetZDO()?.GetLong(ZdoOwnerPlayerId, 0) ?? 0;
            if (ownerPlayerId == 0)
            {
                return;
            }

            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == ownerPlayerId)
            {
                m_nview.ClaimOwnership();
            }
        }

        private void RunAI(float dt)
        {
            if (_owner == null || _owner.IsDead())
            {
                _owner = ResolveOwner();
                if (_owner == null)
                {
                    StopMoving();
                    return;
                }
            }

            // Trop loin du joueur (a décroché en route, téléportation...) : on abandonne
            // n'importe quelle poursuite d'objet en cours et on rejoint directement, avant même
            // de retenter un soin/ramassage ce tick-ci. Jamais tant que le joueur est en l'air
            // (chute, saut) : le téléporter à côté d'un point en plein vol pourrait l'envoyer
            // dans le vide ou en pleine chute lui aussi -- on attend qu'il retouche le sol.
            float ownerDistance = Vector3.Distance(base.transform.position, _owner.transform.position);
            if (ownerDistance > FedoCompanionPlugin.Instance.TeleportDistance.Value && IsOwnerGrounded())
            {
                _pickupTarget = null;
                Vector3 behindOwner = _owner.transform.position - _owner.transform.forward * ArrivalDistance;
                base.transform.position = behindOwner;
                return;
            }

            TryHeal(dt);

            // Le ramassage prend la main sur le déplacement de ce tick (le compagnon marche
            // jusqu'à l'objet) -- Follow ne reprend que quand rien n'est à ramasser.
            if (!TryPickup(dt))
            {
                Follow(dt);
            }
        }

        private bool IsOwnerGrounded()
        {
            return Mathf.Abs(_owner.GetVelocity().y) <= GroundedVerticalVelocityThreshold;
        }

        private Player ResolveOwner()
        {
            ZDO zdo = m_nview != null ? m_nview.GetZDO() : null;
            long ownerPlayerId = zdo != null ? zdo.GetLong(ZdoOwnerPlayerId, 0) : 0;
            if (ownerPlayerId == 0)
            {
                return null;
            }

            foreach (Player candidate in Player.GetAllPlayers())
            {
                if (candidate != null && candidate.GetPlayerID() == ownerPlayerId)
                {
                    return candidate;
                }
            }

            return null;
        }

        // Appelée par CompanionInteract au moment du renommage (Maj+E) : met à jour le nom
        // localement + son propre ZDO (déjà fait avant, voir CompanionInteract.SetText) et, en
        // plus, la copie persistée côté propriétaire (voir ApplySavedName/CustomDataCompanionName)
        // pour qu'un renommage survive à un ranger/réinvoquer du compagnon.
        public void PersistRename(string newName)
        {
            Player owner = _owner ?? ResolveOwner();
            if (owner?.m_customData != null)
            {
                owner.m_customData[CustomDataCompanionName] = newName;
            }
        }

        private void Follow(float dt)
        {
            float distance = Vector3.Distance(base.transform.position, _owner.transform.position);

            float followDistance = FedoCompanionPlugin.Instance.FollowDistance.Value;
            if (distance <= followDistance)
            {
                StopMoving();
                return;
            }

            bool run = distance > FedoCompanionPlugin.Instance.RunDistance.Value;
            MoveTo(dt, _owner.transform.position, followDistance, run);
        }

        private void TryHeal(float dt)
        {
            _healCooldownTimer -= dt;
            if (_healCooldownTimer > 0f)
            {
                return;
            }

            if (_owner.GetHealth() >= _owner.GetMaxHealth() - FullHealthEpsilon)
            {
                return;
            }

            float distance = Vector3.Distance(base.transform.position, _owner.transform.position);
            if (distance > FedoCompanionPlugin.Instance.HealRange.Value)
            {
                return;
            }

            _owner.Heal(FedoCompanionPlugin.Instance.HealAmount.Value, showText: true);
            _healCooldownTimer = FedoCompanionPlugin.Instance.HealCooldownSeconds.Value;
        }

        // Renvoie true si le ramassage a pris la main sur le déplacement ce tick (objet en vue
        // ou en cours d'approche), false si rien à ramasser (Follow doit reprendre la main).
        private bool TryPickup(float dt)
        {
            // Décrémenté ici (pas seulement dans SayPickupPhrase) pour courir en continu, que le
            // compagnon soit en train de chercher ou déjà en train de ramasser un objet.
            _chatCooldownTimer -= dt;

            if (_pickupTarget != null && !_pickupTarget.CanPickup())
            {
                _pickupTarget = null;
            }

            if (_pickupTarget == null)
            {
                _pickupSearchTimer -= dt;
                if (_pickupSearchTimer > 0f)
                {
                    return false;
                }

                _pickupSearchTimer = FedoCompanionPlugin.Instance.PickupIntervalSeconds.Value;
                _pickupTarget = FindNearestItem();
                if (_pickupTarget != null)
                {
                    TrySayPickupPhrase();
                }
            }

            if (_pickupTarget == null)
            {
                return false;
            }

            bool inRange = MoveTo(dt, _pickupTarget.transform.position, PickupArrivalDistance, run: true);
            if (inRange)
            {
                bool isCoins = IsCoins(_pickupTarget);
                Vector3 pickupPosition = _pickupTarget.transform.position;

                _pickupTarget.Pickup(_owner);
                _pickupTarget = null;

                if (isCoins)
                {
                    FedoCompanionPlugin.Instance.PlayCoinPickupSound(pickupPosition);
                }
            }

            return true;
        }

        // ZNetView.GetPrefabName() est privée (cf. CLAUDE.md) -- on compare donc le hash de
        // prefab de la ZDO au hash stable de "Coins" (nom du prefab vanilla de la monnaie,
        // déjà utilisé comme tel par FedoGoldRabbit).
        private static readonly int CoinsPrefabHash = "Coins".GetStableHashCode();

        private static bool IsCoins(ItemDrop item)
        {
            var nview = item.GetComponent<ZNetView>();
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            return zdo != null && zdo.GetPrefab() == CoinsPrefabHash;
        }

        // Un objet à ramasser peut se présenter très souvent (plusieurs par minute) -- dire une
        // phrase à chaque fois serait vite envahissant. Un cooldown dédié, séparé de
        // _pickupSearchTimer, limite ça à une phrase de temps en temps plutôt qu'à chaque objet.
        private void TrySayPickupPhrase()
        {
            if (_chatCooldownTimer > 0f)
            {
                return;
            }

            _chatCooldownTimer = FedoCompanionPlugin.Instance.PickupChatCooldownSeconds.Value;

            string phrase = UnityEngine.Random.Range(0, 2) == 0
                ? FedoCompanionPlugin.Instance.PickupPhrase1.Value
                : FedoCompanionPlugin.Instance.PickupPhrase2.Value;

            FloatingSpeechBubble.Show(base.transform, phrase);
        }

        private ItemDrop FindNearestItem()
        {
            float range = FedoCompanionPlugin.Instance.PickupRange.Value;
            Collider[] hits = Physics.OverlapSphere(base.transform.position, range);

            ItemDrop nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Collider hit in hits)
            {
                ItemDrop item = hit.GetComponentInParent<ItemDrop>();
                if (item == null || !item.CanPickup())
                {
                    continue;
                }

                float distance = Vector3.Distance(base.transform.position, item.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = item;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }
    }
}
