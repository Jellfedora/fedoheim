export const SERVER_NAME = "Fedoheim";

// Le modpack "production" (isDefault côté API) reçu par tout joueur normal, fixe côté
// launcher — jamais changé dynamiquement. Un admin peut créer d'autres profils de
// modpack (voir ProfilesPage) pour tester avant de répliquer en production, mais ce
// slug-ci reste toujours celui utilisé par défaut et par tout joueur non-admin.
export const PRODUCTION_MODPACK_SLUG = "default";
