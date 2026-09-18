namespace FedoSkillSafety
{
    // Le "meilleur palier atteint" par compétence est stocké dans Player.m_customData
    // (Dictionary<string,string>, public) -- même mécanique que le nom/mode de
    // comportement d'un compagnon dans FedoKnorri (CompanionAI.cs) : sauvegardé avec le
    // profil du personnage, donc disponible même hors ligne/en solo, sans dépendre du
    // serveur ni d'aucune ZDO. Ne peut jamais diminuer : une fois un palier atteint, il
    // reste acquis pour ce personnage tant que sa sauvegarde existe.
    internal static class PalierTracker
    {
        private const string KeyPrefix = "FedoSkillSafety_BestPalier_";

        public static int GetBestPalier(Player player, Skills.SkillType type)
        {
            if (player?.m_customData == null)
            {
                return 0;
            }

            if (player.m_customData.TryGetValue(KeyPrefix + type, out var raw) && int.TryParse(raw, out var value))
            {
                return value;
            }

            return 0;
        }

        // Si le niveau actuel vient de faire franchir un nouveau palier (jamais atteint
        // avant pour cette compétence), l'enregistre et renvoie sa valeur. Renvoie null
        // sinon (aucun nouveau palier, ou palierSize invalide) -- le plancher lui-même
        // n'a besoin d'être mis à jour qu'à ce moment précis, pas à chaque appel.
        public static int? CheckAndRecord(Player player, Skills.SkillType type, int currentLevel, int palierSize)
        {
            if (player?.m_customData == null || palierSize <= 0)
            {
                return null;
            }

            int candidate = (currentLevel / palierSize) * palierSize;
            if (candidate <= 0)
            {
                return null;
            }

            int stored = GetBestPalier(player, type);
            if (candidate <= stored)
            {
                return null;
            }

            player.m_customData[KeyPrefix + type] = candidate.ToString();
            return candidate;
        }
    }
}
