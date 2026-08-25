using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FedoServerTools
{
    public static class OnlinePlayersReporter
    {
        // Timeout court : le rapport d'arrêt du serveur (voir
        // FedoServerToolsPlugin.ReportBlocking) attend cet appel de façon bloquante,
        // il ne doit jamais retarder la fermeture du jeu de plus de quelques secondes
        // si l'API est injoignable.
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Renvoie le corps brut de la réponse (voir onlinePlayers.ts) -- contient
        // notamment la commande serveur en attente pour ce profil (voir ServerCommands.
        // ApplyFromReportResponse), consommée ici même si l'appelant ne s'en sert pas
        // (ex: ReportBlocking, dernier rapport avant l'arrêt).
        public static async Task<string> ReportAsync(
            string apiBaseUrl,
            string serverToken,
            IReadOnlyList<PlayerReport> players,
            string status,
            string season = null,
            string time = null)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(serverToken))
            {
                throw new InvalidOperationException("FedoServerTools not configured (see fedo.servertools.cfg).");
            }

            // Pas de slug de modpack dans l'URL : le jeton identifie déjà le profil de
            // façon unique côté API (voir onlinePlayers.ts::findModpackByToken), une
            // seule valeur à recopier dans ce .cfg plutôt que deux qui doivent
            // correspondre entre elles.
            string url = apiBaseUrl.TrimEnd('/') + "/modpacks/online-players";
            // `season`/`time` : une seule valeur par rapport (pas par joueur, contrairement
            // à biome/armor) -- `season` est `null` si le mod Seasons n'est pas installé sur
            // ce serveur (voir SeasonReporting), `time` si EnvMan n'est pas encore chargé.
            string payload = "{\"players\":" + JsonPlayersArray(players) + ",\"status\":" + JsonEscape(status) +
                ",\"season\":" + (season != null ? JsonEscape(season) : "null") +
                ",\"time\":" + (time != null ? JsonEscape(time) : "null") + "}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            // Header custom, pas un Bearer JWT : ce mod n'a pas d'identité joueur, seul
            // le jeton partagé par profil (voir fedo.servertools.cfg) tient lieu d'auth.
            request.Headers.Add("X-Server-Token", serverToken);

            var response = await Http.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API responded {(int)response.StatusCode}: {responseBody}");
            }

            return responseBody;
        }

        private static string JsonPlayersArray(IReadOnlyList<PlayerReport> players)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < players.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append("{\"name\":").Append(JsonEscape(players[i].Name)).Append(",\"biome\":");
                sb.Append(players[i].Biome != null ? JsonEscape(players[i].Biome) : "null");
                sb.Append(",\"armor\":");
                sb.Append(players[i].Armor.HasValue ? players[i].Armor.Value.ToString(CultureInfo.InvariantCulture) : "null");
                sb.Append(",\"steamId\":");
                sb.Append(players[i].SteamId != null ? JsonEscape(players[i].SteamId) : "null");
                sb.Append(",\"died\":").Append(players[i].Died ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Interne (pas private) : réutilisé tel quel par CharacterOwnershipCheck.cs,
        // seul autre endroit de ce mod qui construit du JSON à la main.
        internal static string JsonEscape(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // Extracteur minimal, pas un vrai parseur JSON (voir CLAUDE.md -- aucun mod de ce
        // repo n'a de dépendance de parsing JSON) : ne comprend que des champs de premier
        // niveau à valeur simple (chaîne entre guillemets ou entier), suffisant pour le
        // format de réponse plat renvoyé par l'API (voir onlinePlayers.ts, réponse de
        // POST /modpacks/online-players). Utilisé par ServerCommands.cs.
        internal static string ExtractJsonString(string json, string key)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        internal static int? ExtractJsonInt(string json, string key)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
            return match.Success ? (int?)int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }
    }
}
