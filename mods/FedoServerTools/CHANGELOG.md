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
- Reworked that loading overlay: centered the whole block in the middle of the screen
  (was pinned to the top), added the Fedoheim logo above the loading line (bundled as a
  plain PNG next to the DLL, decoded at runtime via `Texture2D.LoadImage` --
  `LoadingLogo.cs`), and disabled word-wrap so the loading line always stays on one
  line. Briefly tried rendering the logo *and* a "Fedoheim" title in Cinzel SemiBold
  (the launcher home page's title font), registering the bundled `.ttf` with the OS for
  this process only (`AddFontResourceEx`/`CTFontManagerRegisterFontsForURL`) and
  resolving it via `Font.CreateDynamicFontFromOSFont` -- confirmed broken by an actual
  game launch (`Unable to load font face for [Cinzel SemiBold]`): the OS resolves the
  font by name, but Unity's own font engine can't load its glyph data from a
  process-only registration in a standalone player. Reverted to the font already
  borrowed from the game and dropped the title text entirely.
- Fixed: the host's own connect/disconnect never showed up in the Discord log (only
  "server started"/"server stopped" did), confirmed by an actual game launch (hosting
  solo). Same root cause already found for character↔account linking: the host has no
  `ZNetPeer` representing themselves, so `ZNetJoinLeaveAnnouncePatches.cs` (which hooks
  `RPC_PeerInfo`/`Disconnect`) never fires for them. Now announced explicitly
  (`AnnounceHostConnected`/`AnnounceHostDisconnected`, using
  `Game.instance.GetPlayerProfile()`'s name).
- Discord session log events (connect/disconnect/death/server started-stopped/world
  saved) are now posted as embeds (colored side bar, emoji + title, the templated
  message as the description, `Player`/`Cause` fields, a "Fedoheim · <world>" footer
  with a native timestamp) instead of a plain text message -- inspired by similar
  community mods. `DiscordWebhook.PostMessageAsync` replaced by `PostEmbedAsync`
  (`DiscordEmbed`/`DiscordEmbedField`), payload still hand-built (no JSON dependency,
  see mods/CLAUDE.md).
- Fixed, again, after a second game launch: "disconnected" showed up fine, but
  "connected" still didn't. `AnnounceHostConnected` was firing from `ZNet.SetServer`
  (right alongside "server started") -- too early, `Game.instance.GetPlayerProfile()`
  isn't usable yet at that point, so the announcement silently never went out. Moved to
  `Hud.Awake` instead (guarded on `ZNet.instance.IsServer()`), with a one-time guard so
  a mid-session scene reload (which re-triggers `Hud.Awake`) doesn't re-announce it.
- Loading screen text switched from `fejd.m_versionLabel.font` (too pixel/retro-looking
  in practice, per an actual game launch) to `fejd.m_csName.font` (the game's regular UI
  font, used for the character name on the character-selection screen) -- confirmed
  in-game to look right. Also dropped the trailing "..." from "Chargement de
  Fedoheim...".
- Two new Discord log events: **new day** (🌅, fires when `EnvMan.GetDay()` -- the
  game's own day counter, which ticks over at dawn -- returns a different value than
  last observed) and **season changed** (🍂, fires when `SeasonReporting.
  GetCurrentSeasonKey()` returns a different key than last observed; requires the
  Seasons mod, simply never fires without it, same as the rest of the season
  reporting). Both detected by polling (`CheckDayAndSeasonChange`, every 5s from
  `Update()`, independent of the clock overlay's own throttle/visibility toggle), not a
  game event hook -- no such hook was found for either. Both reset their "last known"
  value on every new session (`OnServerStarted`) so resuming on a different world never
  announces a false change left over from the previous session.
- Added admin server commands (`ServerCommands.cs`): the launcher's Admin > Serveur page
  can now set the time of day (6h/12h/18h/24h) or force a season, queued on the API
  (`POST /modpacks/:slug/server-command`) and picked up by the next periodic report --
  same "poll, never push" principle as the rest of this mod, never called into the game
  directly. Time is set via `ZNet.SetNetTime` (jumping forward to the next occurrence of
  the requested hour, based on `EnvMan.GetDayFraction()`) followed by a forced
  `ZNet.SendNetTime()` broadcast (private, invoked via reflection) so already-connected
  clients see it immediately. Season is forced through the
  [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/) mod's own public
  `overrideSeason`/`seasonOverrided` config entries (soft dependency, silently ignored if
  Seasons isn't installed) -- not a Harmony hack, this mod just sets `.Value` on Seasons'
  own ConfigEntry objects, exactly as if an admin had edited its `.cfg`. Server-only
  (`ZNet.instance.IsServer()`); the response is parsed and applied off the main Unity
  thread (fire-and-forget HTTP report), so it's dispatched onto the main thread via a
  small action queue drained from `Update()` rather than touching `ZNet`/`EnvMan`/Seasons'
  config from a background thread.
- Added a third admin server command (`BroadcastMessage.cs`): a short message posted from
  the launcher's Admin > Serveur page now shows up on every connected player's own client,
  centered on screen in yellow (`MessageHud.ShowMessage`) and in their in-game chat
  (`Chat.OnNewChatMessage`, styled as a Shout), plus a Discord log entry
  (`AnnounceAdminMessage`, `[Discord] LogAdminMessage`/`AdminMessageTemplate`). Unlike
  time/season above, this needs to reach every client rather than just apply on the
  server, so it's dispatched over a dedicated `ZRoutedRpc` (registered on both server and
  client from `ZNet.Awake`, same hook as ServerSync's own RPC registration) targeting
  `ZRoutedRpc.Everybody`. Deliberately calls `Chat.OnNewChatMessage` (the game's own
  internal display step for an incoming chat RPC) rather than `Chat.SendText` (used
  elsewhere in this repo, e.g. FedoDeath) -- `SendText` would itself broadcast a brand new
  RPC from every client that receives ours, causing a message storm.
- Fixed (found in an actual in-game test): a brand new `UserInfo` (empty `UserId`) passed
  as the chat message's sender made the game log
  `Failed to check permission CommunicateWithUsingText: UserID was invalid` on every
  broadcast -- non-fatal (the message still showed up), but noisy. Now reuses the local
  player's own `PlatformUserID` (`UserInfo.GetLocalUser().UserId`, always valid) while
  keeping "Fedoheim" as the displayed name, instead of a blank one.
- Fixed: the loading overlay's logo/text (`LoadingOverlay.cs`) looked tiny on a real
  high-resolution monitor -- the `CanvasScaler` was left on its default
  `ConstantPixelSize` mode, so the pixel sizes below stayed fixed regardless of screen
  resolution. Switched to `ScaleWithScreenSize` (1920x1080 reference, 0.5 width/height
  balance) and roughly doubled the base sizes (logo 160px→320px, text 40pt→56pt) for a
  more prominent loading screen.
