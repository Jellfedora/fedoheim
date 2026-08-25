using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace FedoServerTools
{
    // Applique une commande one-shot posée par un admin depuis le launcher (page
    // Admin > Serveur, voir POST /modpacks/:slug/server-command côté API) -- consommée
    // depuis la réponse du rapport périodique (voir OnlinePlayersReporter.ReportAsync /
    // FedoServerToolsPlugin.Report), jamais poussée depuis l'API : le jeu n'expose aucun
    // serveur qu'on pourrait appeler de l'extérieur, seul le mod sonde (même principe que
    // la connexion automatique, voir CLAUDE.md). Serveur seulement -- jamais appliqué sur
    // un simple client, qui se ferait de toute façon écraser par la prochaine
    // synchronisation réseau/ServerSync.
    internal static class ServerCommands
    {
        public static void ApplyFromReportResponse(string responseBody, ManualLogSource log)
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                return;
            }

            string command = OnlinePlayersReporter.ExtractJsonString(responseBody, "command");
            if (command == null)
            {
                // Rien en attente -- le cas normal à chaque rapport, jamais loggué pour ne
                // pas spammer toutes les SyncIntervalSeconds.
                return;
            }

            // Loggué avant tout guard ci-dessous : preuve que ce serveur a bien reçu la
            // commande depuis l'API, même si elle finit par être rejetée juste après (pas
            // le serveur autoritaire, EnvMan pas encore chargé, Seasons absent...) --
            // permet de distinguer "jamais reçue" (rien dans ce log) de "reçue mais
            // rejetée" (voir le warning qui suit).
            log?.LogInfo($"FedoServerTools: server command received from API: '{command}' (raw: {responseBody}).");

            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                log?.LogWarning(
                    $"FedoServerTools: ignoring server command '{command}' -- this instance is not the authoritative server (ZNet.instance={(ZNet.instance == null ? "null" : "set")}, IsServer={(ZNet.instance != null && ZNet.instance.IsServer())}).");
                return;
            }

            try
            {
                switch (command)
                {
                    case "set-time":
                        int? hour = OnlinePlayersReporter.ExtractJsonInt(responseBody, "hour");
                        if (hour.HasValue)
                        {
                            ApplyTimeOfDay(hour.Value, log);
                        }
                        else
                        {
                            log?.LogWarning("FedoServerTools: 'set-time' command received without a valid 'hour' field, ignored.");
                        }
                        break;
                    case "set-season":
                        string season = OnlinePlayersReporter.ExtractJsonString(responseBody, "season");
                        if (season != null)
                        {
                            ApplySeason(season, log);
                        }
                        else
                        {
                            log?.LogWarning("FedoServerTools: 'set-season' command received without a valid 'season' field, ignored.");
                        }
                        break;
                    case "broadcast-message":
                        string message = OnlinePlayersReporter.ExtractJsonString(responseBody, "message");
                        if (message != null)
                        {
                            ApplyBroadcastMessage(message, log);
                        }
                        else
                        {
                            log?.LogWarning("FedoServerTools: 'broadcast-message' command received without a valid 'message' field, ignored.");
                        }
                        break;
                    default:
                        log?.LogWarning($"FedoServerTools: unknown server command '{command}' ignored.");
                        break;
                }
            }
            catch (Exception e)
            {
                log?.LogWarning($"FedoServerTools: failed to apply server command '{command}': {e.Message}");
            }
        }

        // Avance l'horloge réseau (ZNet.SetNetTime, publique -- voir mods/CLAUDE.md,
        // "Notes techniques de modding") jusqu'à la prochaine occurrence de l'heure
        // demandée, toujours vers l'avant, jamais en arrière (si l'heure visée est déjà
        // passée aujourd'hui, on saute au même moment demain plutôt que de reculer le
        // temps). `hour` va de 0 à 24 (24 = minuit du jour suivant, distinct de 0 = minuit
        // du jour courant, déjà passé). Basé sur EnvMan.GetDayFraction(), la même fraction
        // 0..1 déjà utilisée pour dériver l'horloge affichée (voir
        // FedoServerToolsPlugin.GetCurrentGameTime) -- pas de recalcul divergent.
        // SendNetTime (privée) force la diffusion immédiate aux clients déjà connectés,
        // plutôt que d'attendre le prochain cycle de synchronisation périodique de ZNet.
        private static void ApplyTimeOfDay(int hour, ManualLogSource log)
        {
            if (EnvMan.instance == null)
            {
                log?.LogWarning("FedoServerTools: cannot set time, EnvMan not loaded yet.");
                return;
            }

            float targetFraction = Mathf.Clamp(hour, 0, 24) / 24f;
            float currentFraction = EnvMan.instance.GetDayFraction();
            float deltaFraction = targetFraction - currentFraction;
            if (deltaFraction <= 0f)
            {
                deltaFraction += 1f;
            }

            double deltaSeconds = deltaFraction * EnvMan.instance.m_dayLengthSec;
            double oldTime = ZNet.instance.GetTimeSeconds();
            double newTime = oldTime + deltaSeconds;

            ZNet.instance.SetNetTime(newTime);
            bool broadcast = InvokeSendNetTime();

            log?.LogInfo(
                $"FedoServerTools: server time set to {hour}h (admin command) -- day fraction {currentFraction:F3} -> {targetFraction:F3}, net time {oldTime:F1}s -> {newTime:F1}s, broadcast={broadcast}.");
        }

        // Renvoie `false` si SendNetTime n'a pas pu être trouvée/invoquée par réflexion
        // (ex: signature différente sur une future version du jeu) -- ZNet.SetNetTime a
        // quand même été appelée dans ce cas, le changement finira par être visible au
        // prochain cycle de synchronisation périodique de ZNet, juste pas immédiatement.
        private static bool InvokeSendNetTime()
        {
            var method = typeof(ZNet).GetMethod("SendNetTime", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                return false;
            }

            method.Invoke(ZNet.instance, null);
            return true;
        }

        // Force la saison via l'API publique du mod tiers shudnal/Seasons (dépendance
        // douce, voir SeasonReporting.IsLoaded) -- jamais un hack Harmony : `overrideSeason`/
        // `seasonOverrided` sont ses propres ConfigEntry ServerSync (voir sa source,
        // Seasons.cs), donc écrire `.Value` ici depuis le process serveur a exactement le
        // même effet que si un admin éditait son .cfg (source de vérité côté ServerSync ;
        // la propagation aux clients est déjà gérée par ce mod tiers, rien à refaire ici).
        // "auto" désactive l'override et laisse la saison reprendre sa progression
        // naturelle. `Season` est un type imbriqué (`Seasons.Seasons.Season`, vérifié par
        // reflection dump contre le vrai Seasons.dll -- pas un type de premier niveau).
        private static void ApplySeason(string seasonKey, ManualLogSource log)
        {
            if (!SeasonReporting.IsLoaded)
            {
                log?.LogWarning("FedoServerTools: cannot set season, Seasons mod not installed.");
                return;
            }

            if (seasonKey == "auto")
            {
                global::Seasons.Seasons.overrideSeason.Value = false;
                log?.LogInfo("FedoServerTools: season override disabled (admin command).");
                return;
            }

            if (!Enum.TryParse(seasonKey, out global::Seasons.Seasons.Season season))
            {
                log?.LogWarning($"FedoServerTools: unknown season '{seasonKey}' ignored.");
                return;
            }

            global::Seasons.Seasons.seasonOverrided.Value = season;
            global::Seasons.Seasons.overrideSeason.Value = true;
            log?.LogInfo($"FedoServerTools: season forced to {season} (admin command).");
        }

        // Diffuse le message au centre de l'écran de chaque joueur connecté (voir
        // BroadcastMessage.cs, RPC dédiée -- ZRoutedRpc.Everybody inclut l'hôte lui-même
        // en partie solo/hébergée) et le poste aussi dans le salon Discord des logs
        // (même webhook/mécanique que les autres événements de session, voir
        // FedoServerToolsPlugin.AnnounceAdminMessage) -- ces deux effets sont
        // indépendants l'un de l'autre, chacun best-effort (une RPC sans client connecté,
        // ou un webhook non configuré, n'empêche jamais l'autre).
        private static void ApplyBroadcastMessage(string message, ManualLogSource log)
        {
            BroadcastMessage.Send(message);
            FedoServerToolsPlugin.Instance.AnnounceAdminMessage(message);
            log?.LogInfo($"FedoServerTools: admin message broadcast: \"{message}\".");
        }
    }
}
