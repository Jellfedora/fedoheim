using System;
using System.Collections;
using UnityEngine;

namespace FedoKnorri
{
    // Palier d'évolution du compagnon -- Knorri (Greyling, la graine de base) et Shaman
    // (Greydwarf_Shaman, obtenu par la recette graine + 5 miels, voir ShamanSummonItemPrefabPatch)
    // partagent tous deux exactement ce même script CompanionAI : seul le soin (montant/cooldown,
    // voir TryHeal) diffère par palier, tout le reste (suivi, ramassage, invulnérabilité,
    // renommage, un seul compagnon par joueur, despawn à la déconnexion...) marche déjà
    // identiquement pour les deux sans code supplémentaire, puisque tout ce système s'appuie sur
    // la présence du composant CompanionAI, jamais sur le prefab précis dessous. L'ordre de
    // l'enum sert aussi de hiérarchie ("Shaman > Knorri") pour bloquer un retour en arrière --
    // voir SummonItemUsePatch.ShouldSummon.
    public enum CompanionTier
    {
        Knorri = 0,
        Shaman = 1,
    }

    // Comportement par défaut du compagnon face au danger, réglable au survol (clic simple sur
    // E, voir CompanionInteract) et persisté comme le nom (ZdoBehaviorMode/CustomDataBehaviorMode
    // ci-dessous). Défensif est la valeur par défaut d'un compagnon fraîchement invoqué -- cohérent
    // avec son comportement d'origine (pacifiste, jamais de combat) avant l'ajout de ce réglage.
    public enum CompanionBehaviorMode
    {
        Defensive = 0,
        Aggressive = 1,
    }

    // IA du compagnon, écrite par-dessus BaseAI comme FedoGuardian.GuardAI -- mais ici on garde
    // le Character/Animator d'origine du Greyling cloné (voir CompanionPrefabPatch), donc pas
    // besoin de recréer un Humanoid nu. Suivi/soin/ramassage dans tous les cas, plus, selon
    // CompanionBehaviorMode : en Défensif, il essaie de s'écarter d'une menace qui LE cible
    // (TryEvade) ; en Agressif, il se bat pour de vrai contre ce qui cible le propriétaire ou
    // lui-même (TryFight) -- ce dernier utilise Humanoid.StartAttack(target, ...), qui vise une
    // cible précise plutôt qu'un coup à l'aveugle dans l'arc de la vraie attaque, donc sans
    // risque de toucher le propriétaire par accident au passage.
    public class CompanionAI : BaseAI
    {
        // Posée sur le ZDO du COMPAGNON : PlayerID stable du propriétaire (Player.GetPlayerID(),
        // lié au profil/à la sauvegarde du personnage), PAS son ZDOID de session -- vécu : le
        // ZDOID d'un joueur change à chaque reconnexion, ce qui laissait le compagnon figé pour
        // toujours après une déco/reco (ResolveOwner ne retrouvait plus jamais personne).
        private const string ZdoOwnerPlayerId = "FedoKnorri_OwnerPlayerId";

        // Nom personnalisé (Maj+E, voir CompanionInteract) persisté sur le ZDO du compagnon --
        // sinon un renommage serait perdu au prochain Awake (CompanionPrefabPatch réapplique le
        // nom par défaut du .cfg sur le gabarit à chaque instanciation).
        internal const string ZdoCustomName = "FedoKnorri_Name";

        // Pour un renommage (Maj+E) : le ZDO du compagnon (ZdoCustomName) est détruit dès
        // qu'il est rangé (le charme le supprime, voir SummonItemUsePatch) -- sans une copie sur
        // le PROPRIÉTAIRE, le prochain compagnon invoqué repartait sur le nom par défaut du .cfg.
        private const string CustomDataCompanionName = "FedoKnorri_CompanionName";

        // Même raisonnement que ZdoCustomName/CustomDataCompanionName ci-dessus, mais pour le
        // mode de comportement (voir CompanionInteract, clic simple sur E) : persisté à la fois
        // sur le ZDO du compagnon (survit à un reload) et côté propriétaire (survit à un
        // ranger/réinvoquer, y compris à travers une évolution -- voir ApplySavedMode).
        private const string ZdoBehaviorMode = "FedoKnorri_BehaviorMode";
        private const string CustomDataBehaviorMode = "FedoKnorri_BehaviorMode";

        private const float ArrivalDistance = 0.5f;
        private const float PickupArrivalDistance = 1f;
        private const float FullHealthEpsilon = 0.01f;
        private const float OwnershipCheckIntervalSeconds = 2f;
        private const float AttackRange = 2f;
        private const float EvadeDistance = 6f;

        // Character n'expose aucun "IsOnGround()" public (seul le champ privé m_groundContact
        // existe) -- une vitesse verticale quasi nulle est le meilleur signal disponible pour
        // distinguer un joueur posé au sol (ou immobile) d'un joueur en chute/en saut.
        private const float GroundedVerticalVelocityThreshold = 0.3f;

        // Nom du paramètre Trigger de l'Animator vérifié en jeu (voir CompanionAI.Awake, ancien
        // diagnostic retiré) : le compagnon est en réalité un vrai Humanoid (GetComponent
        // <Humanoid>() réussit), pas un Character nu comme supposé au départ -- mais on
        // déclenche l'animation directement via l'Animator plutôt que Humanoid.StartAttack, qui
        // ferait passer par le vrai système de dégâts (risque réel de blesser le joueur).
        private const string ThrowAnimationTrigger = "throw";

        private Player _owner;
        private Animator _animator;
        private Humanoid _humanoid;
        private CompanionTier _tier;
        private CompanionBehaviorMode _mode = CompanionBehaviorMode.Defensive;
        private float _healCooldownTimer;
        private float _pickupSearchTimer;
        private float _ownershipCheckTimer;
        private float _chatCooldownTimer;
        private float _coinSoundCooldownTimer;
        private float _attackCooldownTimer;
        private ItemDrop _pickupTarget;

        public CompanionBehaviorMode Mode => _mode;

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

        // Cf. commentaire équivalent sur ApplySavedName juste au-dessus -- même mécanique pour
        // le mode de comportement (Défensif/Agressif), survit lui aussi à un ranger/réinvoquer
        // et à une évolution (voir SummonItemUsePatch.ShouldSummon), puisque
        // CustomDataBehaviorMode est posé côté propriétaire, pas sur le ZDO du compagnon (détruit
        // à chaque rangement).
        public static void ApplySavedMode(GameObject companion, Player owner)
        {
            if (owner?.m_customData == null ||
                !owner.m_customData.TryGetValue(CustomDataBehaviorMode, out string savedMode) ||
                !Enum.TryParse(savedMode, out CompanionBehaviorMode mode))
            {
                return;
            }

            var ai = companion.GetComponent<CompanionAI>();
            if (ai != null)
            {
                ai._mode = mode;
            }

            var nview = companion.GetComponent<ZNetView>();
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            zdo?.Set(ZdoBehaviorMode, savedMode);
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

        // Déduit le palier d'un compagnon depuis le hash de prefab de sa ZDO plutôt qu'un champ
        // dédié persisté -- ce hash est déjà la source de vérité de "quel objet est-ce", pas
        // besoin de dupliquer l'info. Utilisé aussi bien en interne (Awake, ci-dessous) que par
        // SummonItemUsePatch pour savoir si un compagnon déjà présent doit être rangé, remplacé
        // (évolution) ou laissé tel quel (graine d'un palier inférieur à celui déjà invoqué).
        public static CompanionTier GetTier(GameObject companion)
        {
            ZNetView view = companion != null ? companion.GetComponent<ZNetView>() : null;
            int prefabHash = view != null ? view.GetZDO()?.GetPrefab() ?? 0 : 0;
            return prefabHash == ShamanCompanionPrefabPatch.PrefabHash ? CompanionTier.Shaman : CompanionTier.Knorri;
        }

        protected override void Awake()
        {
            base.Awake();

            // Jamais initialisé sinon (reste à 0, une valeur d'enum invalide -- cf. commentaire
            // équivalent dans GuardAI.Awake) : Pathfinding.GetPath ne trouve alors pas de chemin
            // correct, d'où des déplacements erratiques/bloqués.
            m_pathAgentType = Pathfinding.AgentType.Humanoid;

            _tier = GetTier(gameObject);

            ZDO zdo = m_nview != null ? m_nview.GetZDO() : null;
            string customName = zdo != null ? zdo.GetString(ZdoCustomName, string.Empty) : string.Empty;
            if (!string.IsNullOrEmpty(customName) && m_character != null)
            {
                m_character.m_name = customName;
            }

            string savedMode = zdo != null ? zdo.GetString(ZdoBehaviorMode, string.Empty) : string.Empty;
            if (!string.IsNullOrEmpty(savedMode) && Enum.TryParse(savedMode, out CompanionBehaviorMode mode))
            {
                _mode = mode;
            }

            _animator = GetComponentInChildren<Animator>();
            // Confirmé en jeu (voir README) : le compagnon cloné (Greyling/Greydwarf_Shaman) est
            // réellement un Humanoid, pas un Character nu -- StartAttack (mode Agressif, voir
            // TryFight) en a besoin, tout comme son arme "à mains nues" d'origine
            // (Humanoid.m_unarmedWeapon), jamais retirée puisque seul MonsterAI est supprimé du
            // clone (voir CompanionPrefabPatch), pas le reste de son équipement inné.
            _humanoid = GetComponent<Humanoid>();
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
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionAI.UpdateAI a levé une exception : {e}");
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
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionAI.Update a levé une exception : {e}");
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
            if (ownerDistance > FedoKnorriPlugin.Instance.TeleportDistance.Value && IsOwnerGrounded())
            {
                _pickupTarget = null;
                Vector3 behindOwner = _owner.transform.position - _owner.transform.forward * ArrivalDistance;
                base.transform.position = behindOwner;
                return;
            }

            TryHeal(dt);

            // Combat/esquive prennent la main sur le ramassage/suivi ce tick-ci, selon le mode
            // (voir CompanionBehaviorMode) -- mais jamais sur le soin ci-dessus, qui reste
            // prioritaire dans tous les cas.
            if (_mode == CompanionBehaviorMode.Aggressive && TryFight(dt))
            {
                return;
            }

            if (_mode == CompanionBehaviorMode.Defensive && TryEvade(dt))
            {
                return;
            }

            // Le ramassage prend la main sur le déplacement de ce tick (le compagnon marche
            // jusqu'à l'objet) -- Follow ne reprend que quand rien n'est à ramasser.
            if (!TryPickup(dt))
            {
                Follow(dt);
            }
        }

        // Cherche un adversaire à traiter comme une menace, parmi les Character non-joueurs
        // vivants dans CombatEngageRange dont l'IA cible actuellement le propriétaire (si
        // includeOwnerTargets) ou le compagnon lui-même. Ne cherche jamais "n'importe quel
        // monstre sauvage à portée" tout seul : ça l'aurait fait s'engager dans des combats qui
        // ne le concernent pas juste en marchant à travers une Forêt Noire, plutôt que de
        // réagir à une vraie menace déjà engagée contre le groupe.
        private Character FindThreat(bool includeOwnerTargets)
        {
            float range = FedoKnorriPlugin.Instance.CombatEngageRange.Value;
            Collider[] hits = Physics.OverlapSphere(base.transform.position, range);

            Character nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Collider hit in hits)
            {
                Character candidate = hit.GetComponentInParent<Character>();
                if (candidate == null || candidate == m_character || candidate.IsDead() || candidate.IsPlayer())
                {
                    continue;
                }

                BaseAI candidateAi = candidate.GetComponent<BaseAI>();
                Character target = candidateAi != null ? candidateAi.GetTargetCreature() : null;
                bool isThreat = target == m_character || (includeOwnerTargets && _owner != null && target == (Character)_owner);
                if (!isThreat)
                {
                    continue;
                }

                float distance = Vector3.Distance(base.transform.position, candidate.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        // Mode Agressif : se bat pour de vrai contre ce qui cible le propriétaire ou lui-même --
        // se rapproche si hors de portée, sinon attaque via Humanoid.StartAttack(target, ...), qui
        // vise explicitement "target" plutôt qu'un coup dans l'arc de la vraie attaque, donc sans
        // risque de toucher le propriétaire (ou n'importe qui d'autre) par accident au passage.
        // Renvoie true si un combat est en cours ce tick (prend la main sur ramassage/suivi).
        private bool TryFight(float dt)
        {
            _attackCooldownTimer -= dt;

            Character threat = FindThreat(includeOwnerTargets: true);
            if (threat == null)
            {
                return false;
            }

            float distance = Vector3.Distance(base.transform.position, threat.transform.position);
            if (distance > AttackRange)
            {
                MoveTo(dt, threat.transform.position, AttackRange * 0.6f, run: true);
                return true;
            }

            StopMoving();
            LookAt(threat.transform.position);

            if (_humanoid != null && _attackCooldownTimer <= 0f && !_humanoid.InAttack())
            {
                if (_humanoid.StartAttack(threat, secondaryAttack: false))
                {
                    _attackCooldownTimer = FedoKnorriPlugin.Instance.CombatAttackCooldownSeconds.Value;
                }
            }

            return true;
        }

        // Mode Défensif : essaie de s'écarter d'une menace qui LE cible spécifiquement (jamais
        // celles qui ciblent seulement le propriétaire -- fuir pour un danger qui n'est même pas
        // dirigé contre lui n'aurait aucun sens, et gênerait le combat du joueur pour rien).
        // Renvoie true si une esquive est en cours ce tick.
        private bool TryEvade(float dt)
        {
            Character threat = FindThreat(includeOwnerTargets: false);
            if (threat == null)
            {
                return false;
            }

            Vector3 away = base.transform.position - threat.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                // Menace exactement à la même position (cas limite) : direction aléatoire plutôt
                // que Normalize() sur un vecteur quasi nul, qui donnerait une direction imprévisible.
                away = UnityEngine.Random.insideUnitSphere;
                away.y = 0f;
            }
            away.Normalize();

            Vector3 fleeTarget = base.transform.position + away * EvadeDistance;
            MoveTo(dt, fleeTarget, 0f, run: true);
            return true;
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

        // Appelée par CompanionInteract (clic simple sur E, voir CompanionInteract.Interact) --
        // pas de vérification que "user" est bien le propriétaire, même absence de contrôle que
        // pour le renommage (Maj+E) déjà en place, pour rester cohérent avec lui plutôt que
        // d'introduire une asymétrie entre les deux seules interactions du compagnon.
        public void ToggleBehaviorMode()
        {
            _mode = _mode == CompanionBehaviorMode.Defensive ? CompanionBehaviorMode.Aggressive : CompanionBehaviorMode.Defensive;

            ZDO zdo = m_nview != null ? m_nview.GetZDO() : null;
            zdo?.Set(ZdoBehaviorMode, _mode.ToString());

            Player owner = _owner ?? ResolveOwner();
            if (owner?.m_customData != null)
            {
                owner.m_customData[CustomDataBehaviorMode] = _mode.ToString();
            }

            string message = _mode == CompanionBehaviorMode.Aggressive
                ? FedoKnorriPlugin.Instance.AggressiveModeMessage.Value
                : FedoKnorriPlugin.Instance.DefensiveModeMessage.Value;
            FloatingSpeechBubble.Show(base.transform, message);
        }

        private void Follow(float dt)
        {
            float distance = Vector3.Distance(base.transform.position, _owner.transform.position);

            float followDistance = FedoKnorriPlugin.Instance.FollowDistance.Value;
            if (distance <= followDistance)
            {
                StopMoving();
                return;
            }

            bool run = distance > FedoKnorriPlugin.Instance.RunDistance.Value;
            MoveTo(dt, _owner.transform.position, followDistance, run);
        }

        // Seul le montant/cooldown de soin diffère par palier (voir CompanionTier) -- le Shaman
        // (recette graine + 5 miels) soigne plus fort mais moins souvent, tout le reste de son
        // comportement (portée, suivi, ramassage...) reste identique et partagé.
        private float HealAmount => _tier == CompanionTier.Shaman
            ? FedoKnorriPlugin.Instance.ShamanHealAmount.Value
            : FedoKnorriPlugin.Instance.HealAmount.Value;

        private float HealCooldownSeconds => _tier == CompanionTier.Shaman
            ? FedoKnorriPlugin.Instance.ShamanHealCooldownSeconds.Value
            : FedoKnorriPlugin.Instance.HealCooldownSeconds.Value;

        private void TryHeal(float dt)
        {
            _healCooldownTimer -= dt;
            if (_healCooldownTimer > 0f)
            {
                return;
            }

            bool ownerHurt = _owner.GetHealth() < _owner.GetMaxHealth() - FullHealthEpsilon;
            // Plus invulnérable (voir CompanionInvulnerabilityPatch) : il peut maintenant avoir
            // besoin d'un soin lui aussi.
            bool selfHurt = m_character != null && m_character.GetHealth() < m_character.GetMaxHealth() - FullHealthEpsilon;
            if (!ownerHurt && !selfHurt)
            {
                return;
            }

            float distance = Vector3.Distance(base.transform.position, _owner.transform.position);
            if (distance > FedoKnorriPlugin.Instance.HealRange.Value)
            {
                return;
            }

            // "Il choisit" : soigne qui en a relativement le plus besoin (le plus bas pourcentage
            // de vie, GetHealthPercentage() -- pas la perte en valeur absolue, pour qu'un Shaman
            // à 150 PV max ne soit pas systématiquement jugé "plus mal en point" que le
            // propriétaire juste parce que sa jauge est plus grande). À égalité, ou si lui seul
            // est blessé, le propriétaire reste prioritaire -- comportement historique inchangé
            // tant que le compagnon lui-même n'est jamais blessé.
            Character healTarget = selfHurt && (!ownerHurt || m_character.GetHealthPercentage() < _owner.GetHealthPercentage())
                ? m_character
                : _owner;

            _healCooldownTimer = HealCooldownSeconds;

            // Petit geste d'"aim" avant le lancer : le compagnon se tourne vers le joueur, puis
            // joue sa vraie animation de jet (vérifiée en jeu -- Trigger "throw" sur son
            // Animator). Uniquement l'animation : SetTrigger ne passe jamais par
            // Humanoid.StartAttack/le système de dégâts, contrairement à un vrai jet de caillou.
            // Le vrai soin n'est appliqué qu'à l'arrivée de l'orbe (voir CompanionHealOrb), pas
            // instantanément ici -- voir LaunchHealOrbAfterThrow pour le timing du lancer. On
            // regarde toujours vers le joueur pour ce geste, même en cas d'auto-soin -- c'est sa
            // direction naturelle puisqu'il le suit en permanence, pas la peine de le faire se
            // tourner vers lui-même pour "viser".
            LookAt(_owner.GetTopPoint());
            _animator?.SetTrigger(ThrowAnimationTrigger);

            StartCoroutine(LaunchHealOrbAfterThrow(healTarget, HealAmount));
        }

        // L'orbe partait en même temps que l'animation plutôt qu'à la fin de son geste (vécu en
        // jeu, décalage visuel). Trois tentatives successives de lire l'état réel de l'Animator
        // pour caler l'orbe automatiquement dessus, trois décalages différents, toutes vécues en
        // jeu et abandonnées :
        // 1) Lecture immédiate de la durée de l'état courant après un "yield return null" :
        //    tant que l'AnimatorController est encore en transition vers "throw" (même une
        //    simple crossfade), GetCurrentAnimatorStateInfo continue de renvoyer l'état
        //    PRÉCÉDENT (souvent l'idle, une boucle bien plus longue) -- l'orbe partait ~1,5s
        //    trop tard, décalé de la durée de l'idle mal lue au lieu de celle du vrai clip.
        // 2) Attendre la fin de la transition (IsInTransition) avant de lire la durée, PUIS
        //    attendre cette durée en entier -- toujours ~0,5s trop tard : un crossfade joue déjà
        //    le clip "throw" EN PARALLÈLE de l'ancien état, donc son horloge interne avait déjà
        //    avancé pendant la transition ; additionner "durée de la transition" PUIS "durée
        //    totale du clip" comptait ce chevauchement deux fois.
        // 3) Surveiller AnimatorStateInfo.normalizedTime frame par frame jusqu'à 1 -- encore
        //    PLUS tard cette fois : l'état "throw" a vraisemblablement sa propre transition de
        //    SORTIE vers l'idle configurée avant la fin du clip (Exit Time < 1), qui remet
        //    IsInTransition à true une seconde fois avant que normalizedTime n'atteigne 1,
        //    obligeant à attendre cette transition de sortie en plus.
        // Inspecter le vrai AnimatorController pour comprendre son graphe de transitions exact
        // demanderait de le décompiler (pas juste l'assembly, un asset Unity sérialisé, hors de
        // portée de MetadataLoadContext) -- retenu à la place : un délai fixe, réglable dans le
        // .cfg (rechargé à chaud, aucun rebuild nécessaire pour l'ajuster) plutôt que deviné à
        // l'aveugle à travers d'autres cycles de test.
        // Character plutôt que Player : depuis que TryHeal peut choisir de soigner le compagnon
        // lui-même, la cible n'est plus systématiquement le propriétaire -- CompanionHealOrb.Launch
        // accepte déjà un Character générique, rien d'autre à changer côté orbe.
        private IEnumerator LaunchHealOrbAfterThrow(Character target, float healAmount)
        {
            yield return new WaitForSeconds(FedoKnorriPlugin.Instance.HealThrowDelaySeconds.Value);

            if (target == null)
            {
                yield break;
            }

            Vector3 launchPoint = base.transform.position + Vector3.up * 0.8f;
            CompanionHealOrb.Launch(launchPoint, target, healAmount);
        }

        // Renvoie true si le ramassage a pris la main sur le déplacement ce tick (objet en vue
        // ou en cours d'approche), false si rien à ramasser (Follow doit reprendre la main).
        private bool TryPickup(float dt)
        {
            // Décrémentés ici (pas seulement au moment où ils servent) pour courir en continu,
            // que le compagnon soit en train de chercher ou déjà en train de ramasser un objet.
            _chatCooldownTimer -= dt;
            _coinSoundCooldownTimer -= dt;

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

                _pickupSearchTimer = FedoKnorriPlugin.Instance.PickupIntervalSeconds.Value;
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

                if (isCoins && _coinSoundCooldownTimer <= 0f)
                {
                    _coinSoundCooldownTimer = FedoKnorriPlugin.Instance.CoinPickupSoundCooldownSeconds.Value;
                    FedoKnorriPlugin.Instance.PlayCoinPickupSound(pickupPosition);
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

            _chatCooldownTimer = FedoKnorriPlugin.Instance.PickupChatCooldownSeconds.Value;

            string phrase = UnityEngine.Random.Range(0, 2) == 0
                ? FedoKnorriPlugin.Instance.PickupPhrase1.Value
                : FedoKnorriPlugin.Instance.PickupPhrase2.Value;

            FloatingSpeechBubble.Show(base.transform, phrase);
        }

        private ItemDrop FindNearestItem()
        {
            float range = FedoKnorriPlugin.Instance.PickupRange.Value;
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
