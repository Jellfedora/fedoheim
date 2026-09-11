using System;

namespace FedoHud
{
    // Formatage partagé pour les comptes à rebours de tooltip (voir
    // GrowthTooltip.cs/BeehiveTooltip.cs) -- une seule implémentation pour ne pas
    // diverger entre les deux.
    internal static class HudTimeFormat
    {
        // Sous la minute : affiche en secondes ("45s") plutôt qu'un trompeur "0h00m".
        // Sous l'heure : masque le "0h" ("45m" plutôt que "0h45m"). Arrondi à l'unité
        // supérieure dans les deux cas, pour ne jamais afficher "prêt" en avance.
        public static string FormatRemaining(double remainingSeconds)
        {
            if (remainingSeconds < 60)
            {
                int seconds = (int)Math.Ceiling(remainingSeconds);
                return $"{seconds}s";
            }

            int totalMinutes = (int)Math.Ceiling(remainingSeconds / 60.0);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            return hours > 0 ? $"{hours}h{minutes:D2}m" : $"{minutes}m";
        }
    }
}
