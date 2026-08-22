using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace FedoServerTools
{
    public static class OnlinePlayersReporter
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task ReportAsync(
            string apiBaseUrl,
            string serverToken,
            IReadOnlyList<PlayerReport> players,
            bool online)
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
            string payload = "{\"players\":" + JsonPlayersArray(players) + ",\"online\":" + (online ? "true" : "false") + "}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            // Header custom, pas un Bearer JWT : ce mod n'a pas d'identité joueur, seul
            // le jeton partagé par profil (voir fedo.servertools.cfg) tient lieu d'auth.
            request.Headers.Add("X-Server-Token", serverToken);

            var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new Exception($"API responded {(int)response.StatusCode}: {body}");
            }
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
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string JsonEscape(string value)
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
    }
}
