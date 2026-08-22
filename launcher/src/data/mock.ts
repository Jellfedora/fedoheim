// Données factices en attendant que l'API expose les vrais endpoints (joueurs en
// ligne). Les mods, le règlement, la FAQ, les annonces et les paramètres (dont le
// lien Buy Me a Coffee) viennent de l'API (voir
// invoke("fetch_mods"|"fetch_rules"|"fetch_faq"|"fetch_announcements"|"fetch_settings")).

export const SERVER_NAME = "Fedoheim";

// Le modpack "production" (isDefault côté API) reçu par tout joueur normal, fixe côté
// launcher — jamais changé dynamiquement. Un admin peut créer d'autres profils de
// modpack (voir ProfilesPage) pour tester avant de répliquer en production, mais ce
// slug-ci reste toujours celui utilisé par défaut et par tout joueur non-admin.
export const PRODUCTION_MODPACK_SLUG = "default";

export const MOCK_PLAYERS_ONLINE = [
  "Bjornolfr",
  "Ingrid_la_Hardie",
  "Sveinn",
  "Fenrisulfr",
];
