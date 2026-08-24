using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace FedoServerTools
{
    // Appelle POST /modpacks/character-check (voir CharacterOwnershipPatch.cs) pour
    // savoir si un joueur qui se connecte a le droit d'utiliser ce nom de personnage --
    // protection contre l'usurpation d'un nom déjà lié à un AUTRE compte Fedoheim (voir
    // CLAUDE.md, "premier arrivé, premier servi").
    //
    // Bloquant volontairement (comme FedoServerToolsPlugin.ReportBlocking) : la décision
    // doit être connue avant de laisser la connexion continuer, un Postfix Harmony ne
    // peut pas être async. Timeout délibérément court (3s, plus court que le rapport
    // périodique) car ça bloque le thread principal du serveur -- donc TOUT le monde --
    // pendant l'appel, pas seulement le joueur qui se connecte. Échoue toujours
    // "ouvert" (autorisé) sur erreur/timeout : un souci réseau/API ne doit jamais
    // empêcher une vraie connexion.
    internal static class CharacterOwnershipCheck
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        public static bool IsAllowed(string apiBaseUrl, string serverToken, string characterName, string steamId)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(serverToken))
            {
                return true;
            }

            try
            {
                return CheckAsync(apiBaseUrl, serverToken, characterName, steamId).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogWarning($"FedoServerTools: character ownership check failed, allowing by default: {e.Message}");
                return true;
            }
        }

        private static async Task<bool> CheckAsync(string apiBaseUrl, string serverToken, string characterName, string steamId)
        {
            string url = apiBaseUrl.TrimEnd('/') + "/modpacks/character-check";
            string payload = "{\"characterName\":" + OnlinePlayersReporter.JsonEscape(characterName) + ",\"steamId\":" +
                (steamId != null ? OnlinePlayersReporter.JsonEscape(steamId) : "null") + "}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Server-Token", serverToken);

            var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return false;
            }
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new Exception($"API responded {(int)response.StatusCode}: {body}");
            }
            return true;
        }
    }
}
