using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace FedoServerTools
{
    public static class DiscordWebhook
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task PostMessageAsync(string webhookUrl, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                throw new InvalidOperationException("Discord webhook not configured (see fedo.servertools.cfg).");
            }

            string payload = "{\"content\":" + JsonEscape(message) + "}";
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await Http.PostAsync(webhookUrl, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new Exception($"Discord responded {(int)response.StatusCode}: {body}");
            }
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
