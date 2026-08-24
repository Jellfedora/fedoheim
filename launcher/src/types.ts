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
  // Posé une seule fois côté API dès qu'un rapport FedoServerTools reconnaît ce compte
  // en jeu (steamId) -- `null` tant que ce compte n'a jamais été vu. Consommé par la
  // partie client de FedoServerTools (menu skip).
  characterName: string | null;
}
