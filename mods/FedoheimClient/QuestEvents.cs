using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FedoheimClient
{
    // Lancer un vrai event de raid vanilla (ex. `army_theelder`, "la forêt bouge...")
    // depuis un dialogue de quête Marketplace.
    //
    // Pourquoi un mod : la commande vanilla `event` est marquée triche + serveur
    // seulement (Terminal.InitTerminal, vérifié par décompilation), et
    // `Terminal.ConsoleCommand.RunAction` refuse toute commande triche tant que le
    // joueur n'a jamais activé les triches (`Achievements.IsCheatedAtAll`) -- donc la
    // commande `ConsoleCommand` d'un dialogue Marketplace ne peut pas la lancer pour un
    // joueur normal. Le seul point d'entrée qui démarre un event est
    // `RandEventSystem.SetRandomEventByName(name, pos)`, appelé sur le serveur.
    //
    // Fonctionnement :
    // - client : commande console NON triche `fedoevent <nom>` (que Marketplace peut
    //   lancer via `Command: ConsoleCommand, fedoevent army_theelder`), qui envoie une
    //   demande au serveur avec la position du joueur ;
    // - serveur : valide la demande (liste blanche d'events, zones autorisées, délai
    //   entre deux lancements, aucun event déjà en cours) puis démarre l'event.
    //
    // Toute la décision est prise côté serveur avec SA config -- un joueur qui édite
    // son .cfg local n'y gagne rien, donc pas besoin de ServerSync ici. N'importe qui
    // peut taper la commande à la main : c'est justement pour ça que le serveur filtre.
    internal static class QuestEvents
    {
        private const string RpcName = "FedoheimClient_RequestEvent";
        private const string CommandName = "fedoevent";

        private static ConfigEntry<string> _allowedEvents;
        private static ConfigEntry<string> _allowedAreas;
        private static ConfigEntry<float> _cooldownSeconds;

        // Time.realtimeSinceStartup du dernier event lancé par ce mod (serveur seulement).
        private static float _lastStartRealtime = float.NegativeInfinity;

        public static void BindConfig(ConfigFile config)
        {
            _allowedEvents = config.Bind(
                "QuestEvents",
                "AllowedEvents",
                "",
                "Server only. Comma-separated list of vanilla random event names players may start with the 'fedoevent <name>' console command (e.g. army_theelder). Empty = feature disabled.");
            _allowedAreas = config.Bind(
                "QuestEvents",
                "AllowedAreas",
                "",
                "Server only. Where the requesting player must stand, as 'x,z,radius' entries separated by ';' (e.g. -520,-575,150). Empty = anywhere.");
            _cooldownSeconds = config.Bind(
                "QuestEvents",
                "CooldownSeconds",
                300f,
                new ConfigDescription(
                    "Server only. Minimum delay (in seconds) between two events started through this command.",
                    new AcceptableValueRange<float>(0f, 86400f)));
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class RegisterCommandPatch
        {
            private static void Postfix()
            {
                try
                {
                    new Terminal.ConsoleCommand(CommandName, "[event] - asks the server to start an allowed event at your position", args =>
                    {
                        if (args.Length < 2 || ZRoutedRpc.instance == null || Player.m_localPlayer == null)
                        {
                            return;
                        }

                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, args[1], Player.m_localPlayer.transform.position);
                    });
                }
                catch (Exception e)
                {
                    FedoheimClientPlugin.Log?.LogWarning($"FedoheimClient: failed to register '{CommandName}' command: {e.Message}");
                }
            }
        }

        // Envoyé à ZRoutedRpc.Everybody (relayé par le serveur) : seul le serveur agit,
        // les clients ignorent la demande dans OnRequest (garde IsServer).
        // Game.Start enregistre lui-même ses propres RPC (ZRoutedRpc déjà créé par
        // ZNet.Awake à ce moment-là) -- même point d'accroche que le jeu.
        [HarmonyPatch(typeof(Game), "Start")]
        private static class RegisterRpcPatch
        {
            private static void Postfix()
            {
                try
                {
                    ZRoutedRpc.instance?.Register<string, Vector3>(RpcName, OnRequest);
                    LogAvailableEvents();
                }
                catch (Exception e)
                {
                    FedoheimClientPlugin.Log?.LogWarning($"FedoheimClient: failed to register event RPC: {e.Message}");
                }
            }
        }

        // Aide à remplir AllowedEvents : les noms d'events ne sont lisibles nulle part
        // ailleurs (définis dans les assets compressés du jeu).
        private static void LogAvailableEvents()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || RandEventSystem.instance == null)
            {
                return;
            }

            var names = RandEventSystem.instance.m_events.Where(e => e != null).Select(e => e.m_name);
            FedoheimClientPlugin.Log?.LogInfo($"FedoheimClient: available events: {string.Join(", ", names)}");
        }

        private static void OnRequest(long sender, string eventName, Vector3 position)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || RandEventSystem.instance == null)
            {
                return;
            }

            string who = ZNet.instance.GetPeer(sender)?.m_playerName ?? sender.ToString(CultureInfo.InvariantCulture);

            try
            {
                if (!ParseList(_allowedEvents.Value).Contains(eventName))
                {
                    FedoheimClientPlugin.Log?.LogInfo($"FedoheimClient: {who} requested event '{eventName}', not in AllowedEvents -- ignored.");
                    return;
                }

                if (!IsInAllowedArea(position))
                {
                    FedoheimClientPlugin.Log?.LogInfo($"FedoheimClient: {who} requested event '{eventName}' at {position}, outside AllowedAreas -- ignored.");
                    return;
                }

                if (RandEventSystem.instance.GetCurrentRandomEvent() != null)
                {
                    FedoheimClientPlugin.Log?.LogInfo($"FedoheimClient: {who} requested event '{eventName}', but an event is already running -- ignored.");
                    return;
                }

                float elapsed = Time.realtimeSinceStartup - _lastStartRealtime;
                if (elapsed < _cooldownSeconds.Value)
                {
                    FedoheimClientPlugin.Log?.LogInfo($"FedoheimClient: {who} requested event '{eventName}', cooldown active ({_cooldownSeconds.Value - elapsed:F0}s left) -- ignored.");
                    return;
                }

                if (!RandEventSystem.instance.HaveEvent(eventName))
                {
                    FedoheimClientPlugin.Log?.LogWarning($"FedoheimClient: {who} requested event '{eventName}', which does not exist in this game version.");
                    return;
                }

                RandEventSystem.instance.SetRandomEventByName(eventName, position);
                _lastStartRealtime = Time.realtimeSinceStartup;
                FedoheimClientPlugin.Log?.LogInfo($"FedoheimClient: event '{eventName}' started at {position}, requested by {who}.");
            }
            catch (Exception e)
            {
                FedoheimClientPlugin.Log?.LogWarning($"FedoheimClient: failed to start event '{eventName}' for {who}: {e.Message}");
            }
        }

        private static HashSet<string> ParseList(string value)
        {
            return new HashSet<string>(
                (value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        // Une entrée invalide est ignorée (avec un avertissement) plutôt que d'ouvrir ou
        // de fermer toute la fonctionnalité.
        private static bool IsInAllowedArea(Vector3 position)
        {
            var entries = (_allowedAreas.Value ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (entries.Count == 0)
            {
                return true;
            }

            foreach (var entry in entries)
            {
                var parts = entry.Split(',');
                if (parts.Length != 3
                    || !float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                    || !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                {
                    FedoheimClientPlugin.Log?.LogWarning($"FedoheimClient: invalid AllowedAreas entry '{entry}' (expected x,z,radius), ignored.");
                    continue;
                }

                float dx = position.x - x;
                float dz = position.z - z;
                if (dx * dx + dz * dz <= radius * radius)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
