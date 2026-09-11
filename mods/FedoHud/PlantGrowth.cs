using System;
using System.Reflection;
using HarmonyLib;

namespace FedoHud
{
    // Calcul du temps de pousse restant d'une `Plant`, partagé entre GrowthTooltip.cs
    // (texte au survol) et PlantReadyIndicator.cs (icône "!" une fois prête) -- une seule
    // implémentation de la formule pour ne pas diverger entre les deux. Signatures
    // vérifiées par réflexion contre assembly_valheim.dll (1.0), rien deviné -- voir
    // mods/CLAUDE.md.
    internal static class PlantGrowth
    {
        // Plant.GetGrowTime() est privée (temps de pousse total en secondes, tiré
        // aléatoirement mais de façon déterministe par instance via une seed interne
        // propre à chaque plante) -- pas d'autre moyen de l'obtenir sans réflexion ; elle
        // ne modifie aucun état global durable (sauvegarde/restaure Random.state en
        // interne avant de rendre la main).
        private static readonly MethodInfo GetGrowTimeMethod =
            AccessTools.Method(typeof(Plant), "GetGrowTime");

        // `null` si le statut n'est pas Healthy (pas de soleil, mauvais biome... -- pas de
        // pousse en cours dans ces cas) ou si la ZDO n'est pas accessible.
        public static double? GetRemainingSeconds(Plant plant)
        {
            if (plant.GetStatus() != Plant.Status.Healthy)
            {
                return null;
            }

            var nview = plant.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || ZNet.instance == null)
            {
                return null;
            }

            // Reproduit Plant.TimeSincePlanted() (privée) : ZNet.GetTime() et
            // ZDOVars.s_plantTime sont publics, donc reproductibles ici sans passer par la
            // méthode privée elle-même.
            long plantedTicks = zdo.GetLong(ZDOVars.s_plantTime, ZNet.instance.GetTime().Ticks);
            double elapsedSeconds = (ZNet.instance.GetTime() - new DateTime(plantedTicks)).TotalSeconds;

            float growTimeSeconds = (float)GetGrowTimeMethod.Invoke(plant, null);
            return growTimeSeconds - elapsedSeconds;
        }

        // Heuristique par nom de GameObject (`gameObject.name`, pas de réflexion privée
        // nécessaire contrairement à ZNetView.GetPrefabName()) : les jeunes arbres plantés
        // au cultivateur contiennent "sapling" dans leur nom vanilla (ex.
        // "FirTree_Sapling"). Pas vérifiable autrement sans lancer le jeu -- à confirmer
        // en survolant un jeune arbre planté, voir mods/FedoHud/README.md. Si jamais un
        // mod tiers ajoute une culture dont le nom contient aussi ce mot par coïncidence,
        // elle serait à tort exclue de l'icône/traitée comme un arbre.
        public static bool IsTreeSapling(Plant plant)
        {
            return plant.gameObject.name.ToLowerInvariant().Contains("sapling");
        }
    }
}
