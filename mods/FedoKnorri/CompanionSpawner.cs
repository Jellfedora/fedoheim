using UnityEngine;

namespace FedoKnorri
{
    // Cf. commentaire équivalent dans FedoGuardian.GuardianSpawner : le prefab gabarit
    // (CompanionPrefabPatch) est volontairement inactif -- Object.Instantiate() d'un objet
    // inactif produit un clone lui aussi inactif, sur lequel Awake() ne se déclenche jamais
    // tant qu'on ne l'active pas explicitement. Un vrai spawn doit donc impérativement
    // rappeler SetActive(true) après Instantiate().
    internal static class CompanionSpawner
    {
        // tier choisit lequel des deux prefabs instancier (voir CompanionTier) -- tout le reste
        // de cette méthode (propriété, nom sauvegardé, apprivoisement, poof) est déjà partagé
        // entre les deux paliers sans rien à distinguer ici, puisque CompanionAI l'est aussi.
        public static GameObject Spawn(Vector3 position, Quaternion rotation, Player owner, CompanionTier tier)
        {
            GameObject prefab = tier == CompanionTier.Shaman
                ? ShamanCompanionPrefabPatch.GetPrefab()
                : CompanionPrefabPatch.GetPrefab();
            if (prefab == null)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: impossible de créer le prefab du compagnon ({tier}), abandon du spawn.");
                return null;
            }

            var instance = Object.Instantiate(prefab, position, rotation);
            instance.SetActive(true);
            instance.GetComponent<ZNetView>()?.ClaimOwnership();

            CompanionAI.LinkToOwner(instance, owner);

            // Nom réappliqué AVANT SetTamed : EnemyHud (l'étiquette nom+vie flottante au-dessus
            // d'une créature, voir CompanionInteract.SetText) capture le nom courant au moment où
            // elle enregistre l'objet -- probablement déclenché par SetTamed(true) ci-dessous.
            // Appeler ApplySavedName après aurait laissé l'étiquette figée sur le nom par défaut
            // du .cfg (vécu en jeu).
            CompanionAI.ApplySavedName(instance, owner);

            // Mode de comportement (Défensif/Agressif) réappliqué de la même façon -- pas de
            // contrainte d'ordre connue ici contrairement au nom, EnemyHud ne mettant rien en
            // cache pour ça.
            CompanionAI.ApplySavedMode(instance, owner);

            // Character.SetTamed(true) (pas juste m_tamed, un champ protégé) plutôt que de
            // changer la faction : c'est le vrai déclencheur vanilla d'une barre de vie verte
            // (loups/sangliers apprivoisés) -- Faction.Boss seul affiche rouge, "ennemi" du point
            // de vue du joueur (cf. CLAUDE.md : allié à tout SAUF aux joueurs). Appelé ici plutôt
            // que sur le gabarit dans CompanionPrefabPatch : SetTamed a probablement besoin d'un
            // ZNetView/ZDO valide (RPC_SetTamed), inexistant tant que l'objet reste inerte sous
            // le conteneur désactivé de Jotunn.
            var character = instance.GetComponent<Character>();
            if (character != null)
            {
                character.SetTamed(true);

                // Plus invulnérable (voir CompanionInvulnerabilityPatch) : de vrais PV, par
                // palier -- SetMaxHealth seul ne remplit pas m_health à fond, d'où le SetHealth
                // explicite juste après pour qu'un compagnon fraîchement invoqué démarre
                // toujours plein de vie plutôt qu'à l'ancienne valeur du prefab source.
                float maxHealth = tier == CompanionTier.Shaman
                    ? FedoKnorriPlugin.Instance.ShamanCompanionMaxHealth.Value
                    : FedoKnorriPlugin.Instance.CompanionMaxHealth.Value;
                character.SetMaxHealth(maxHealth);
                character.SetHealth(maxHealth);

                // Character.m_onDeath (Action public, pas un événement .NET classique) --
                // s'exécute côté pair propriétaire de la ZDO au moment où le compagnon meurt
                // pour de vrai (dégâts d'un monstre, voir CompanionInvulnerabilityPatch), donc
                // normalement sur la machine du propriétaire lui-même (ClaimOwnership ci-dessus
                // + la reprise de propriété continue de CompanionAI). ownerId capturé ici plutôt
                // que relu depuis `owner` au moment de la mort : `owner` pourrait avoir changé
                // de session/objet Player entre-temps, ownerId (stable) non.
                long ownerId = owner.GetPlayerID();
                character.m_onDeath += () => SummonItemUsePatch.ApplyDeathCooldown(ownerId);
            }

            CompanionPoofEffect.Show(position);

            return instance;
        }
    }
}
