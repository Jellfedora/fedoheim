using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace FedoDeathGif
{
    public static class DiscordUploader
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task UploadGifAsync(string webhookUrl, byte[] gifBytes, string fileName, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                throw new InvalidOperationException("Discord webhook not configured (see fedo.deathgif.cfg).");
            }

            using var content = new MultipartFormDataContent();

            if (!string.IsNullOrEmpty(message))
            {
                content.Add(new StringContent(message), "content");
            }

            var fileContent = new ByteArrayContent(gifBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/gif");
            content.Add(fileContent, "file", fileName);

            var response = await Http.PostAsync(webhookUrl, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new Exception($"Discord responded {(int)response.StatusCode}: {body}");
            }
        }
    }
}
