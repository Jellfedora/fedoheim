// Validation de format uniquement — pas d'appel à l'API Steam (nécessiterait une clé
// dédiée non prévue pour ce projet). Un SteamID64 individuel fait 17 chiffres et
// commence toujours par "7656119" (offset fixe imposé par le format Steam).
const STEAM_ID64_REGEX = /^7656119\d{10}$/;

export function isValidSteamId64(value: string): boolean {
  return STEAM_ID64_REGEX.test(value);
}
