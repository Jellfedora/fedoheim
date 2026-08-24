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
  and simulates a real click on the in-game checkbox (`Minimap.OnTogglePublicPosition()`)
  on every installation with a local player -- checked every frame rather than once on a
  fixed lifecycle event, since nothing guarantees `Minimap.instance` already exists at
  any single point in time.
- `ForcePublicPosition` is synced and locked via [ServerSync](https://github.com/blaxxun-boop/ServerSync)
  -- a connecting player can no longer disable it by editing their own local `.cfg`,
  only the server admin's own copy controls it.
- Reports now carry a `status` (`"starting"`, `"online"`, or `"stopping"`) instead of a
  plain online/offline flag: `"starting"` from the moment the plugin loads (even before
  knowing if this instance will be a server) until `StartingGracePeriodSeconds` has
  passed (default 60s, useful for a heavily modded server that takes a while to boot),
  `"stopping"` from `OnApplicationQuit` (best-effort) and once more, synchronously
  (bounded by a short HTTP timeout so it reliably reaches the API even though the game
  process can exit within moments of a clean shutdown), right before `ZNet` is
  destroyed.
- Biome display names are configurable per-biome in the `.cfg` (`[Biomes]` section,
  default English, e.g. `MeadowsName = Prairies` for a French translation) — sent as-is
  to the API, no translation happens in the launcher.
- Authenticated per modpack profile with a shared `ServerToken` (identifies the profile
  on its own, alongside `ApiBaseUrl`) — no separate slug setting needed.
- Reports now also carry the current in-game season (`Spring`/`Summer`/`Fall`/`Winter`)
  when the [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/) mod is also
  installed on the server -- a soft dependency detected at runtime via BepInEx's plugin
  list, never a hard reference: this mod works exactly the same without Seasons, it
  just won't have a season to report. One value per report (not per player, unlike
  biome/armor). Display names configurable in the `.cfg` (`[Seasons]` section, same
  translation convention as `[Biomes]`).
- Added a generic stability patch (not specific to any one mod or prefab): repairs any
  `ZNetScene` instance whose `ZNetView` has been destroyed or lost its ZDO without being
  properly deregistered, before `ZNetScene.RemoveObjects` gets a chance to crash on it
  every single frame with a `NullReferenceException` that never resolves on its own
  (observed in the wild on a heavily-modded, long-lived save). Logs the offending
  `GameObject`'s name and prefab hash to help track down the actual root cause, and
  resets the ZDO's `Created` flag so the game gets a chance to recreate it properly on
  the next pass instead of leaving it permanently broken.
- Stopped spamming the log every `SyncIntervalSeconds` when the periodic report can't
  reach the API (wrong `ApiBaseUrl`, API down...) or `ServerToken` is empty: the full
  error/warning is now logged once, with a terse reminder every 20 consecutive failures
  for the API case (and a one-line info log once it recovers), instead of a fresh full
  exception on every single attempt.
- Absorbed the former standalone `FedoDiscordLogs` mod: posts player connect/disconnect/
  death, server start/stop, and world-saved events to a Discord webhook (`[Discord]`
  section, `WebhookUrl`) -- entirely independent of the API-reporting feature above (no
  `ServerToken` involved). Unlike `ServerToken`, `WebhookUrl` is meant to be filled in on
  every installation (including players') if you want death events -- which only fire on
  that player's own client -- to be logged for everyone; see the README. Each event has
  its own on/off toggle and customizable message template, same as the old mod.
- Added an in-game clock overlay (`HH:MM`, top-center of the screen), following the
  server's day/night cycle via `EnvMan.GetDayFraction()` -- purely local, no
  `ServerToken` needed (`[Time]` section: `ShowClockOverlay`, `TimeOffsetHours`).
  Draggable with Shift+mouse, position saved per-installation
  (`ClockPositionX`/`ClockPositionY`) and restored on every future launch. The same
  clock value is now also sent in the periodic API report, shown by the launcher's
  home page next to the season.
- Player reports now also carry each player's resolved SteamID64 (`PeerSteamId.cs`,
  via `ZNetPeer.m_socket.GetHostName()`) so the API can link a character name to the
  Fedoheim account that played it, first-come first-served -- never displayed, used
  only for that link.
- Absorbed the former standalone `FedoAutoJoin` mod: a client-only patch on
  `FejdStartup` (the main menu) that skips straight to character creation or
  auto-connect based on a per-profile auto-connect target and the character↔account
  link above -- see the "Auto-join" section in the README.
- Fixed the main menu remaining visible underneath the character creation screen during
  auto-join (found in an actual in-game test): now calls the game's own `OnStartGame()`
  (the real "start game" button handler, which hides the main menu first) instead of
  invoking `ShowCharacterSelection()` directly.
- The new-character name field is now pre-filled with the player's Discord username when
  auto-join jumps straight to character creation, appending a number (`Name2`, `Name3`,
  ...) if that name is already taken by a local character on this machine -- and locked
  read-only right after, so the player can't retype a different name.
- Hid the "Cancel" button on that same character creation screen during auto-join: it
  only led back to the character-select list (never to the hidden main menu), but would
  let the player back out of an otherwise automatic flow.
- Fixed auto-join never actually connecting after creating a brand new character (found
  in an actual in-game test): the connection attempt ran before the game's own
  `OnCharacterStart()` (which sets the active profile and populates the local world
  list), so hosting a local world silently failed ("could not read the local world
  list") and the player was left stranded on the character-select screen. Now calls
  `OnCharacterStart()` first, same as the "already-linked character" path.
- Fixed the character↔account link never happening at all when playing solo or hosting
  (found in an actual in-game test): SteamID resolution (`PeerSteamId.cs`) went through
  `ZNet.GetPeerByPlayerName`, which only searches actual network connections
  (`ZNetPeer`, populated solely by incoming connections from other players) -- the host
  itself is added to the player list directly from its own profile, with no `ZNetPeer`
  to be found. `PeerSteamId.Resolve` now checks the local player's own name first and,
  if it matches, reads the SteamID64 straight from the platform layer
  (`Splatform.PlatformManager`/`IDistributionPlatform.LocalUser.PlatformUserID`)
  instead.
- The configured auto-connect world name is now matched case-insensitively against
  local worlds -- it's a free-text field on the launcher's "Profils" page, not a picker
  over the real world list, so a slightly different casing (`Fedodev3` vs `fedodev3`)
  used to silently fail to match.
- Added a server-side check (`CharacterOwnershipPatch.cs`, `POST /modpacks/character-
  check`) that kicks a connecting player if their character name is already linked to a
  *different* Fedoheim account -- protection against someone recreating a local
  character with an already-claimed name and joining under that identity. Fails open
  (allows the connection) on any API/network error so an outage never locks out
  legitimate players; never triggered for the host's own character, only remote
  connections.
- Added a "Chargement de Fedoheim..." text overlay (`LoadingOverlay.cs`), shown right
  before auto-join skips straight to connecting (already-linked character, or right
  after creating a new one) and hidden once the in-game HUD actually appears. Auto-join
  never shows the vanilla menu panels that normally display Valheim's own loading
  screen, so without this the whole connection/world-load time was a plain black
  screen with no text -- easy to mistake for a crash. Self-destructs after 30s as a
  safety net in case the HUD never shows up (a failed connection).
