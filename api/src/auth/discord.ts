import { config } from "../config.js";

const DISCORD_API = "https://discord.com/api/v10";

export class DiscordAuthError extends Error {
  constructor(message: string, readonly statusCode: number) {
    super(message);
  }
}

interface DiscordTokenResponse {
  access_token: string;
}

interface DiscordUser {
  id: string;
  username: string;
  avatar: string | null;
}

interface DiscordGuildMember {
  roles: string[];
}

// Échange le "code" OAuth2 (obtenu par le loopback local du launcher) contre un access
// token Discord. C'est la seule opération qui utilise le client_secret, donc elle ne
// peut avoir lieu que côté API.
export async function exchangeCodeForAccessToken(
  code: string,
  redirectUri: string,
): Promise<string> {
  const body = new URLSearchParams({
    client_id: config.DISCORD_CLIENT_ID,
    client_secret: config.DISCORD_CLIENT_SECRET,
    grant_type: "authorization_code",
    code,
    redirect_uri: redirectUri,
  });

  const res = await fetch(`${DISCORD_API}/oauth2/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body,
  });

  if (!res.ok) {
    throw new DiscordAuthError(`Discord token exchange failed: ${await res.text()}`, 401);
  }

  const data = (await res.json()) as DiscordTokenResponse;
  return data.access_token;
}

export async function getDiscordUser(accessToken: string): Promise<DiscordUser> {
  const res = await fetch(`${DISCORD_API}/users/@me`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });

  if (!res.ok) {
    throw new DiscordAuthError(`Failed to fetch Discord user: ${await res.text()}`, 401);
  }

  return (await res.json()) as DiscordUser;
}

// Utilise le token du bot (pas celui de l'utilisateur) pour lire les rôles du joueur
// sur le serveur Discord de la communauté.
export async function getGuildMemberRoles(discordUserId: string): Promise<string[]> {
  const res = await fetch(
    `${DISCORD_API}/guilds/${config.DISCORD_GUILD_ID}/members/${discordUserId}`,
    { headers: { Authorization: `Bot ${config.DISCORD_BOT_TOKEN}` } },
  );

  if (res.status === 404) {
    // L'utilisateur n'est pas membre du serveur Discord.
    return [];
  }

  if (!res.ok) {
    throw new DiscordAuthError(`Failed to fetch guild member: ${await res.text()}`, 502);
  }

  const member = (await res.json()) as DiscordGuildMember;
  return member.roles;
}

// Un admin doit toujours pouvoir se connecter, même s'il n'a pas (ou plus) le rôle de
// base — avoir le rôle admin suffit, pas besoin de cumuler les deux rôles sur Discord.
export function hasRequiredRole(roles: string[]): boolean {
  return roles.includes(config.DISCORD_REQUIRED_ROLE_ID) || hasAdminRole(roles);
}

export function hasAdminRole(roles: string[]): boolean {
  return roles.includes(config.DISCORD_ADMIN_ROLE_ID);
}
