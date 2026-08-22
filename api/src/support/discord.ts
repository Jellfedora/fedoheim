import { config } from "../config.js";

const DISCORD_API = "https://discord.com/api/v10";

// Contrairement à announcements/discord.ts et content/discord.ts, ce n'est PAS
// best-effort : Discord est ici l'unique destination du log (pas de copie en base à
// côté), donc un échec doit être remonté à l'appelant pour que le joueur sache que son
// log n'est jamais parti, au lieu d'un faux message de succès.
export async function postLogToDiscord(
  buffer: Buffer,
  filename: string,
  discordUsername: string,
  discordId: string,
): Promise<boolean> {
  if (!config.DISCORD_LOG_CHANNEL_ID) return false;

  try {
    const form = new FormData();
    form.append(
      "payload_json",
      JSON.stringify({ content: `Log envoyé par **${discordUsername}** (\`${discordId}\`)` }),
    );
    form.append("files[0]", new Blob([Uint8Array.from(buffer)]), filename);

    const res = await fetch(`${DISCORD_API}/channels/${config.DISCORD_LOG_CHANNEL_ID}/messages`, {
      method: "POST",
      // Pas de Content-Type manuel ici : fetch pose lui-même le boundary multipart
      // pour un body FormData, contrairement à discordHeaders() (JSON) des autres fichiers.
      headers: { Authorization: `Bot ${config.DISCORD_BOT_TOKEN}` },
      body: form,
    });

    if (!res.ok) {
      console.error("Failed to post log to Discord:", res.status, await res.text());
      return false;
    }
    return true;
  } catch (err) {
    console.error("Failed to post log to Discord:", err);
    return false;
  }
}
