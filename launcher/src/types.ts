export interface UserInfo {
  id: number;
  discordUsername: string;
  discordAvatar: string | null;
  isAdmin: boolean;
  hasAcceptedRules: boolean;
  // Brut, peut correspondre à une version dépassée du règlement — ne se fier qu'à ce
  // champ combiné à `hasAcceptedRules` pour afficher "signé le ...", pas à lui seul.
  rulesAcceptedAt: string | null;
  steamId: string | null;
}
