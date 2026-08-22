import { config } from "../config.js";

const DISCORD_API = "https://discord.com/api/v10";
const EMBED_COLOR = 0x25d3e4; // accent teal du launcher
const DESCRIPTION_LIMIT = 4096;
const TITLE_LIMIT = 256;

// Discord auto-intègre une image dans un embed à condition que l'URL soit
// publiquement joignable par les serveurs Discord (pas 127.0.0.1) — d'où
// PUBLIC_API_URL, distinct de l'URL locale utilisée par le launcher.
function resolveImageUrls(images: string[]): string[] {
  if (!config.PUBLIC_API_URL) return [];
  return images.map((image) => `${config.PUBLIC_API_URL}${image}`);
}

// Un embed (plutôt que du texte brut en **gras**) donne un vrai rendu de titre
// distinct sur Discord : plus gros, en gras, avec une barre de couleur à gauche.
// La 1ère image devient l'image principale de l'embed ; les suivantes sont ajoutées
// en embeds séparés (chacun affiché sous le principal dans le même message).
function buildEmbeds(title: string | null, message: string, images: string[]) {
  const imageUrls = resolveImageUrls(images);

  const mainEmbed: Record<string, unknown> = {
    color: EMBED_COLOR,
    description:
      message.length > DESCRIPTION_LIMIT
        ? `${message.slice(0, DESCRIPTION_LIMIT - 1)}…`
        : message,
  };
  if (title) {
    mainEmbed.title = title.length > TITLE_LIMIT ? `${title.slice(0, TITLE_LIMIT - 1)}…` : title;
  }
  if (imageUrls.length > 0) {
    mainEmbed.image = { url: imageUrls[0] };
  }

  const extraEmbeds = imageUrls.slice(1).map((url) => ({ color: EMBED_COLOR, image: { url } }));

  return [mainEmbed, ...extraEmbeds];
}

function discordHeaders() {
  return {
    Authorization: `Bot ${config.DISCORD_BOT_TOKEN}`,
    "Content-Type": "application/json",
  };
}

// Toutes les fonctions ci-dessous sont "best-effort" : si le salon n'est pas
// configuré ou si l'appel à Discord échoue, on log et on continue sans jamais faire
// échouer la requête API — le règlement, la FAQ, les mods etc. ne dépendent pas de
// Discord pour fonctionner, les annonces non plus.
export async function postAnnouncementToDiscord(
  title: string | null,
  message: string,
  images: string[],
): Promise<string | null> {
  if (!config.DISCORD_ANNOUNCEMENT_CHANNEL_ID) return null;

  try {
    const res = await fetch(
      `${DISCORD_API}/channels/${config.DISCORD_ANNOUNCEMENT_CHANNEL_ID}/messages`,
      {
        method: "POST",
        headers: discordHeaders(),
        body: JSON.stringify({ content: "", embeds: buildEmbeds(title, message, images) }),
      },
    );

    if (!res.ok) {
      console.error("Failed to post announcement to Discord:", res.status, await res.text());
      return null;
    }

    const data = (await res.json()) as { id: string };
    return data.id;
  } catch (err) {
    console.error("Failed to post announcement to Discord:", err);
    return null;
  }
}

export async function editAnnouncementOnDiscord(
  messageId: string,
  title: string | null,
  message: string,
  images: string[],
): Promise<void> {
  if (!config.DISCORD_ANNOUNCEMENT_CHANNEL_ID) return;

  try {
    const res = await fetch(
      `${DISCORD_API}/channels/${config.DISCORD_ANNOUNCEMENT_CHANNEL_ID}/messages/${messageId}`,
      {
        method: "PATCH",
        headers: discordHeaders(),
        // `content: ""` est important ici : sans lui, un ancien message posté avant le
        // passage aux embeds garderait son texte brut affiché en double à côté de l'embed.
        body: JSON.stringify({ content: "", embeds: buildEmbeds(title, message, images) }),
      },
    );

    if (!res.ok) {
      console.error("Failed to edit announcement on Discord:", res.status, await res.text());
    }
  } catch (err) {
    console.error("Failed to edit announcement on Discord:", err);
  }
}

export async function deleteAnnouncementOnDiscord(messageId: string): Promise<void> {
  if (!config.DISCORD_ANNOUNCEMENT_CHANNEL_ID) return;

  try {
    const res = await fetch(
      `${DISCORD_API}/channels/${config.DISCORD_ANNOUNCEMENT_CHANNEL_ID}/messages/${messageId}`,
      { method: "DELETE", headers: discordHeaders() },
    );

    // 404 = déjà supprimé côté Discord (manuellement) : rien d'anormal.
    if (!res.ok && res.status !== 404) {
      console.error("Failed to delete announcement on Discord:", res.status, await res.text());
    }
  } catch (err) {
    console.error("Failed to delete announcement on Discord:", err);
  }
}
