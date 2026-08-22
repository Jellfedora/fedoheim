# Changelog

## 1.0.0

- Initial release.
- Reports the connected player list (via the game's own `ZNet.GetPlayerList()`, so it
  includes the host in a solo/hosted game) to the Fedoheim API every
  `ReportIntervalSeconds` (default 30s), along with each player's current biome.
- `ForcePublicPosition` (default on) makes the server treat every player's position as
  public, regardless of their own "Public position" setting, so biome is always
  reported without asking each player to enable that setting themselves.
- Biome display names are configurable per-biome in the `.cfg` (`[Biomes]` section,
  default English, e.g. `MeadowsName = Prairies` for a French translation) — sent as-is
  to the API, no translation happens in the launcher.
- Authenticated per modpack profile with a shared `ServerToken` (identifies the profile
  on its own, alongside `ApiBaseUrl`) — no separate slug setting needed.
- Sends one last report with `online: false` on a clean server shutdown.
