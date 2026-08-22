# Changelog

## 1.0.0

- Initial release.
- Reports the connected player list (via the game's own `ZNet.GetPlayerList()`, so it
  includes the host in a solo/hosted game) to the Fedoheim API every
  `SyncIntervalSeconds` (default 30s, renamed from `ReportIntervalSeconds` -- this mod
  is meant to grow into the general sync channel with the API, not just a player-list
  reporter), along with each player's current biome and armor.
- Biome is resolved via `Heightmap.FindBiome` first (the biome actually displayed to
  the player, post edge-blending), falling back to `WorldGenerator.GetBiome` (the raw
  procedural computation used to generate the terrain) if that returns nothing.
- Armor is `Humanoid.GetBodyArmor()` (rounded), read from the matching `Player`
  instance found via `Player.GetAllPlayers()`.
- `ForcePublicPosition` (default on) forces "Public position" (Options > Game) on for
  the session, so players show up on each other's map and a biome can be reported for
  everyone regardless of what they've set locally: writes directly to the server's copy
  of the flag for each connected peer (`ZNetPeer.m_publicRefPos`) on the server side,
  and simulates a real click on the in-game checkbox
  (`Minimap.OnTogglePublicPosition()`) on a regular connecting client.
- `ForcePublicPosition` is synced and locked via [ServerSync](https://github.com/blaxxun-boop/ServerSync)
  -- a connecting player can no longer disable it by editing their own local `.cfg`,
  only the server admin's own copy controls it.
- The final `online: false` report on server shutdown is awaited synchronously (bounded
  by a short HTTP timeout) rather than fired-and-forgotten, so it reliably reaches the
  API even though the game process can exit within moments of a clean shutdown.
- Biome display names are configurable per-biome in the `.cfg` (`[Biomes]` section,
  default English, e.g. `MeadowsName = Prairies` for a French translation) — sent as-is
  to the API, no translation happens in the launcher.
- Authenticated per modpack profile with a shared `ServerToken` (identifies the profile
  on its own, alongside `ApiBaseUrl`) — no separate slug setting needed.
