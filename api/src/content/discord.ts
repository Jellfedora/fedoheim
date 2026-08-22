import { config } from "../config.js";

const DISCORD_API = "https://discord.com/api/v10";
const EMBED_COLOR = 0x25d3e4; // accent teal du launcher
const DESCRIPTION_LIMIT = 4096;

function discordHeaders() {
  return {
    Authorization: `Bot ${config.DISCORD_BOT_TOKEN}`,
    "Content-Type": "application/json",
  };
}

function buildRulesEmbed(rules: string[]) {
  const body = rules.map((rule, i) => `${i + 1}. ${rule}`).join("\n");
  const description =
    body.length > DESCRIPTION_LIMIT ? `${body.slice(0, DESCRIPTION_LIMIT - 1)}…` : body;
  return [{ title: "Règlement du serveur", color: EMBED_COLOR, description }];
}

async function postRulesToDiscord(rules: string[]): Promise<string | null> {
  if (!config.DISCORD_RULES_CHANNEL_ID) return null;

  try {
    const res = await fetch(`${DISCORD_API}/channels/${config.DISCORD_RULES_CHANNEL_ID}/messages`, {
      method: "POST",
      headers: discordHeaders(),
      body: JSON.stringify({ content: "", embeds: buildRulesEmbed(rules) }),
    });

    if (!res.ok) {
      console.error("Failed to post rules to Discord:", res.status, await res.text());
      return null;
    }

    const data = (await res.json()) as { id: string };
    return data.id;
  } catch (err) {
    console.error("Failed to post rules to Discord:", err);
    return null;
  }
}

async function editRulesOnDiscord(messageId: string, rules: string[]): Promise<boolean> {
  if (!config.DISCORD_RULES_CHANNEL_ID) return false;

  try {
    const res = await fetch(
      `${DISCORD_API}/channels/${config.DISCORD_RULES_CHANNEL_ID}/messages/${messageId}`,
      {
        method: "PATCH",
        headers: discordHeaders(),
        body: JSON.stringify({ content: "", embeds: buildRulesEmbed(rules) }),
      },
    );

    if (!res.ok) {
      console.error("Failed to edit rules on Discord:", res.status, await res.text());
      return false;
    }
    return true;
  } catch (err) {
    console.error("Failed to edit rules on Discord:", err);
    return false;
  }
}

// Best-effort, comme announcements/discord.ts : n'a jamais d'impact sur la réussite
// de PUT /content/rules côté API. Édite le message existant en place s'il y en a un
// (pas de spam d'un nouveau message à chaque modification) ; en reposte un nouveau
// si l'édition échoue (ex: message supprimé manuellement sur Discord) ou s'il n'y en
// avait pas encore. Renvoie l'ID à persister dans rules_meta.discordMessageId.
export async function syncRulesToDiscord(
  rules: string[],
  existingMessageId: string | null,
): Promise<string | null> {
  if (existingMessageId) {
    const edited = await editRulesOnDiscord(existingMessageId, rules);
    if (edited) return existingMessageId;
  }
  return postRulesToDiscord(rules);
}
