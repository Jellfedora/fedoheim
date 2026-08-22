using System;
using HarmonyLib;

namespace FedoDeath
{
    // Character.Awake() tourne à chaque fois qu'une instance de ce personnage est chargée --
    // que ce soit la création initiale ou un rechargement depuis la ZDO sauvegardée (zone
    // rechargée, joueur reconnecté, serveur redémarré). Comme la faction/le nom/l'agressivité
    // qu'on impose ne sont pas des données persistées par le jeu, il faut les réappliquer à
    // chaque fois plutôt qu'une seule fois à la création.
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class GuardianStatePatch
    {
        private static void Postfix(Character __instance)
        {
            try
            {
                var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
                if (zdo == null || !zdo.GetBool(FedoDeathPlugin.ZdoIsGuardian, false))
                {
                    return;
                }

                string ownerName = zdo.GetString(FedoDeathPlugin.ZdoOwnerName, "");
                ApplyGuardianState(__instance, ownerName);

                // Reste figé (IA désactivée) tant qu'aucun joueur n'est assez proche -- évite
                // qu'il ne s'éloigne combattre d'autres créatures avant même d'avoir vu le
                // joueur, et garantit que la tombe réapparaîtra bien là où il est tombé.
                if (__instance.GetComponent<GraveGuardianActivator>() == null)
                {
                    __instance.gameObject.AddComponent<GraveGuardianActivator>();
                }
            }
            catch (Exception e)
            {
                FedoDeathPlugin.Log?.LogError($"FedoDeath: guardian state patch failed: {e}");
            }
        }

        public static void ApplyGuardianState(Character character, string ownerName)
        {
            if (character == null)
            {
                return;
            }

            // Boss : allié à toutes les factions sauf celle des joueurs en vanilla -- ignoré par
            // tous les autres monstres, hostile uniquement aux joueurs. m_boss (barre de vie /
            // musique de boss) est un champ séparé qu'on laisse à false.
            character.m_faction = Character.Faction.Boss;
            character.SetTamed(false);
            character.m_name = FedoDeathPlugin.Instance.GuardianNameTemplate.Value.Replace("{player}", ownerName);
        }
    }

    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class GuardianDeathPatch
    {
        // Prefix, pas Postfix : le jeu libère/décroche la ZDO de son ZNetView avant ou pendant
        // le corps de OnDeath (confirmé par les logs -- GetZDO() renvoie déjà null en Postfix
        // pour N'IMPORTE QUELLE créature). Il faut donc lire nos données AVANT l'appel original.
        // Un Prefix void n'empêche pas la méthode d'origine de s'exécuter normalement ensuite.
        private static void Prefix(Character __instance)
        {
            try
            {
                var nview = __instance.GetComponent<ZNetView>();
                var zdo = nview?.GetZDO();
                if (zdo == null || !zdo.GetBool(FedoDeathPlugin.ZdoIsGuardian, false))
                {
                    return;
                }

                // Comme pour le loot des monstres classiques, une seule instance doit faire
                // apparaître la tombe -- sinon chaque client verrait sa propre copie apparaître.
                if (!nview.IsOwner())
                {
                    FedoDeathPlugin.Log?.LogWarning("FedoDeath: guardian died but this client doesn't own it, skipping tombstone (another client/the server should handle it).");
                    return;
                }

                FedoDeathPlugin.Instance.OnGuardianDefeated(zdo, __instance.transform.position, __instance.transform.rotation);
            }
            catch (Exception e)
            {
                FedoDeathPlugin.Log?.LogError($"FedoDeath: guardian death patch failed: {e}");
            }
        }
    }
}
