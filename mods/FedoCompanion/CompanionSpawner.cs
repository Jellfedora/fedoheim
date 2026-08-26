using UnityEngine;

namespace FedoCompanion
{
    // Cf. commentaire équivalent dans FedoGuardian.GuardianSpawner : le prefab gabarit
    // (CompanionPrefabPatch) est volontairement inactif -- Object.Instantiate() d'un objet
    // inactif produit un clone lui aussi inactif, sur lequel Awake() ne se déclenche jamais
    // tant qu'on ne l'active pas explicitement. Un vrai spawn doit donc impérativement
    // rappeler SetActive(true) après Instantiate().
    internal static class CompanionSpawner
    {
        public static GameObject Spawn(Vector3 position, Quaternion rotation, Player owner)
        {
            GameObject prefab = CompanionPrefabPatch.GetPrefab();
            if (prefab == null)
            {
                FedoCompanionPlugin.Log?.LogError("FedoCompanion: impossible de créer le prefab du compagnon, abandon du spawn.");
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

            // Character.SetTamed(true) (pas juste m_tamed, un champ protégé) plutôt que de
            // changer la faction : c'est le vrai déclencheur vanilla d'une barre de vie verte
            // (loups/sangliers apprivoisés) -- Faction.Boss seul affiche rouge, "ennemi" du point
            // de vue du joueur (cf. CLAUDE.md : allié à tout SAUF aux joueurs). Appelé ici plutôt
            // que sur le gabarit dans CompanionPrefabPatch : SetTamed a probablement besoin d'un
            // ZNetView/ZDO valide (RPC_SetTamed), inexistant tant que l'objet reste inerte sous
            // le TemplateRoot désactivé.
            instance.GetComponent<Character>()?.SetTamed(true);

            CompanionPoofEffect.Show(position);

            return instance;
        }
    }
}
