using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace FedoServerTools
{
    // Un champ nom/valeur affiché dans l'embed (voir DiscordEmbed ci-dessous) -- ex.
    // ("Joueur", "jellfedora2").
    public readonly struct DiscordEmbedField
    {
        public readonly string Name;
        public readonly string Value;

        public DiscordEmbedField(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    // Contenu d'un log de session Fedoheim mis en forme façon "embed" Discord (barre de
    // couleur, titre+émoji, description, champs, pied de page horodaté) -- même principe
    // que les annonces/le règlement côté API (voir api/src/announcements/discord.ts),
    // mais construit ici à la main (pas de dépendance JSON dans ce mod, voir
    // mods/CLAUDE.md) plutôt qu'un simple message texte brut comme avant.
    public sealed class DiscordEmbed
    {
        public string Title;
        public string Description;
        public int Color;
        public List<DiscordEmbedField> Fields = new List<DiscordEmbedField>();
        public string FooterText;
    }

    public static class DiscordWebhook
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task PostEmbedAsync(string webhookUrl, DiscordEmbed embed)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                throw new InvalidOperationException("Discord webhook not configured (see fedo.servertools.cfg).");
            }

            string payload = BuildPayload(embed);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await Http.PostAsync(webhookUrl, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new Exception($"Discord responded {(int)response.StatusCode}: {body}");
            }
        }

        private static string BuildPayload(DiscordEmbed embed)
        {
            var sb = new StringBuilder();
            sb.Append("{\"embeds\":[{");
            sb.Append("\"title\":").Append(JsonString(embed.Title)).Append(',');
            sb.Append("\"description\":").Append(JsonString(embed.Description)).Append(',');
            sb.Append("\"color\":").Append(embed.Color).Append(',');

            sb.Append("\"fields\":[");
            for (int i = 0; i < embed.Fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var field = embed.Fields[i];
                sb.Append("{\"name\":").Append(JsonString(field.Name))
                  .Append(",\"value\":").Append(JsonString(field.Value))
                  .Append(",\"inline\":true}");
            }
            sb.Append("],");

            sb.Append("\"footer\":{\"text\":").Append(JsonString(embed.FooterText)).Append("},");
            // ISO 8601 en UTC ("o") -- Discord affiche cette valeur déjà formatée/traduite
            // dans le fuseau horaire du lecteur, pas la peine de la formater nous-mêmes.
            sb.Append("\"timestamp\":").Append(JsonString(DateTime.UtcNow.ToString("o")));

            sb.Append("}]}");
            return sb.ToString();
        }

        private static string JsonString(string value)
        {
            value ??= "";
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
