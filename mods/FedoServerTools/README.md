# FedoServerTools

*By Fedo*

Mostly a server-side mod: talks to the Fedoheim API on behalf of this game server.
Today that means reporting who's currently online (with biome and armor) and the
current in-game season (if the [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/)
mod is also installed) so the launcher's home page can show it to every player — no
login required to see it — but this mod is meant to grow into the general channel
between this server and the API, in both directions (the API telling the game to do
something is planned), not just a player-list reporter. It also has a couple of small
client-side effects (see `ForcePublicPosition` and "Auto-join" below) — safe to install
on a regular player's client too, see Notes — and a generic game-stability patch (see
below) unrelated to any of that, kept here for now since this is the closest thing to a
"misc utilities" mod.

## How it works

1. The moment this plugin loads — before even knowing whether this instance will turn
   out to be a server or a regular client — it sends one `status: "starting"` report
   (harmless no-op on a regular client, since `ServerToken` is blank there). This is
   the only report that can happen before the world/mods have finished loading, which
   is exactly the window a heavily modded server can spend a while in.
2. Once this instance becomes an actual server (dedicated server, or the host of a
   solo/co-op game), the mod starts talking to the API every `SyncIntervalSeconds`
   (default 30s): the list of connected players, using the game's own player list
   (`ZNet.GetPlayerList()`) — this includes the host in a solo/hosted game, not just
   remote peers. Each entry also carries the player's current biome (`Heightmap.
   FindBiome`, falling back to `WorldGenerator.GetBiome` if that returns nothing for
   the zone) and current armor (`Humanoid.GetBodyArmor()`, rounded). Each player's own
   "Public position" setting (Options > Game) normally has to be enabled for their
   position to be usable at all — see `ForcePublicPosition` below to force it on for
   everyone on this server. Reports still say `status: "starting"` (not `"online"`)
   until `StartingGracePeriodSeconds` has passed since the plugin loaded (default 60s)
   — increase this if your server has a lot of mods and takes longer to actually
   become reachable. Each report also carries the current season (`Spring`/`Summer`/
   `Fall`/`Winter`) if the [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/)
   mod is installed on this server — a soft dependency, entirely optional: this mod
   works exactly the same without it, it just won't have a season to report. Unlike
   biome/armor this is one value per report, not per player, since the season is a
   server-wide setting. Each report also carries the current in-game clock (`HH:MM`,
   see "In-game clock" below) — same one-value-per-report principle as the season.
3. Each report is authenticated with a shared secret (`ServerToken`) tied to one
   modpack profile (see Configuration below) — without it, reports are rejected by
   the API and only a warning is logged locally.
4. As soon as a shutdown is requested (`OnApplicationQuit`, best-effort/fire-and-forget
   — there's usually still a moment before the process actually exits), one
   `status: "stopping"` report is sent. Then, right before `ZNet` is actually torn down
   (`ZNet.OnDestroy`), a final one is sent — this one waited on synchronously (bounded
   by a short HTTP timeout) rather than fired-and-forgotten, since the process can exit
   within moments of a clean shutdown too. Together these make the launcher reflect a
   shutdown as it happens instead of waiting for the report to simply go stale (which
   still happens on its own after ~90s if the process is killed outright, e.g. a real
   crash with no chance to report anything).

Besides API reporting, this mod also posts session events (player connect/disconnect/
death, server start/stop, world saved) straight to a Discord webhook — see "Discord
webhook logging" below, entirely independent of the API-reporting feature (no
`ServerToken` involved).

Besides reporting, `ForcePublicPosition` (see below, on by default) forces "Public
position" (Options > Game) on for the session, two ways at once, belt-and-suspenders:
- **Server side**: writes directly to the server's copy of that flag for each connected
  *remote* peer (`ZNetPeer.m_publicRefPos`) every sync cycle — never includes the host
  itself in a solo/hosted game, since the host isn't its own "peer".
- **Client side, on every installation with a local player** (not just remote clients —
  the host of a solo/hosted game needs this too, exactly because the server-side write
  above never reaches them): simulates a real click on the in-game checkbox
  (`Minimap.OnTogglePublicPosition()`), going through the game's own normal path
  (whatever networking that triggers) instead of relying on the server-side write
  alone. Checked every frame rather than once on a specific lifecycle event — nothing
  guarantees `Minimap.instance` already exists at any single fixed point (e.g.
  `Game.Start()`), so a one-shot check could silently do nothing forever if it ran too
  early. Naturally does nothing on a headless dedicated server (no local `Minimap` to
  click) and stops re-clicking once the checkbox is already on.

## Configuration

Settings live in `BepInEx/config/fedo.servertools.cfg`.

**[Api]**
- `ApiBaseUrl` — base URL of the Fedoheim API, no trailing slash (default
  `http://127.0.0.1:3000`).
- `ServerToken` — shared secret for the modpack profile this server runs, generated/
  regenerated from the launcher's "Profils" page (admin only, "Régénérer le jeton").
  The token already identifies which profile it belongs to, so there's nothing else
  to configure here — no separate slug setting. Required; keep it secret, anyone with
  it could post a fake player list for that profile. **Never let this end up in a
  player-facing modpack with a real value filled in** — see Notes below.
- `SyncIntervalSeconds` — how often this mod talks to the API (default `30`, between
  `10` and `300`).
- `StartingGracePeriodSeconds` — how long after the plugin loads to keep reporting
  `"starting"` instead of `"online"` (default `60`, between `0` and `600`). Raise this
  for a heavily modded server that takes a while to actually finish loading.

**[Players]**
- `ForcePublicPosition` (default `true`) — forces "Public position" (Options > Game) on
  for the duration of the session (server-side write + client-side simulated click, see
  above). Players show up on each other's map regardless of what they've set locally,
  and a biome can be reported for everyone. Their own local setting is untouched — it
  reverts the moment they leave this server. **Synced and locked** (see ServerSync
  below): only the server admin's own `.cfg` controls this — a connecting player can't
  disable it by editing their own local copy, it's overridden the moment they connect.
  Read on both a server and a client install, so it needs no `ServerToken` to take
  effect on the client side.

### ServerSync

`ForcePublicPosition` is registered with [ServerSync](https://github.com/blaxxun-boop/ServerSync)
(embedded from `mods/_shared/ConfigSync.cs`, see that folder's README) and locked
(`ConfigSync.IsLocked = true`): the server pushes its own current value to every
connecting client and refuses to let the client's local `.cfg` override it in memory,
for as long as they stay connected to this server. Only this one setting is
registered — never `ServerToken` or `ApiBaseUrl`, since `AddConfigEntry` broadcasts a
setting's value to every connected client the moment it changes, which would leak the
real token to every player.

**[Biomes]**
- `MeadowsName`, `BlackForestName`, `SwampName`, `MountainName`, `PlainsName`,
  `AshLandsName`, `DeepNorthName`, `OceanName`, `MistlandsName` — display name sent for
  each biome, shown as-is by the launcher (no translation happens outside this .cfg).
  Default to the English name; edit this file to use your own translation, e.g. French
  (`MeadowsName = Prairies`).

**[Seasons]**
- `SpringName`, `SummerName`, `FallName`, `WinterName` — display name sent for each
  season, same principle as `[Biomes]` above. Only used (and only sent to the API) if
  the [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/) mod is installed
  on this server; harmless if it isn't.

**[Time]**
- `ShowClockOverlay` (default `true`) — shows the in-game clock overlay (see "In-game
  clock" below). Read on both a server and a client install, so it needs no
  `ServerToken` to take effect on the client side.
- `TimeOffsetHours` (default `0`, between `-12` and `12`) — shifts the displayed clock
  (both the overlay and the value sent to the API/launcher) if it doesn't match what
  the sky looks like. Purely cosmetic, no effect on the actual day/night cycle.
- `ClockPositionX` / `ClockPositionY` (default `0` / `-18`) — where the overlay sits on
  screen, in UI pixels from the top-center. Written automatically when a player drags
  the clock (see below); not meant to be hand-edited, but resettable here.

## In-game clock

Independent of the API/Discord features above: shows a small clock (`HH:MM`) at the
top-center of the screen, following the server's day/night cycle
(`EnvMan.GetDayFraction()` — the same value the game itself uses for lighting, so it
stays correctly synced with what the sky looks like). Works on any installation with a
local player (client or host), no `ServerToken` or network call involved — purely
local. The same value is also sent in the periodic API report (see "How it works"
above) so the launcher's home page can show it next to the season.

**Draggable, position saved locally**: hold **Left Shift** and drag the clock with the
mouse to move it anywhere on screen. The clock is normally "click-through" (it never
intercepts a gameplay click at that spot on screen) — holding Shift is what makes it
draggable, so it never gets in the way otherwise. The new position is written to
`ClockPositionX`/`ClockPositionY` in this installation's own `.cfg` as soon as you
release the drag, and is restored on every future launch — this is a per-player, purely
local preference, never synced through the modpack or the server (unlike
`ForcePublicPosition`, which is a shared setting on purpose).

## Discord webhook logging

Independent of the API reporting above: posts a running log of session events to a
Discord channel via a webhook — who connected, who disconnected, who died, when the
server started/stopped, and when the world was saved.

- **Player connected / disconnected / server started / server stopped / world saved**
  only fire on the machine acting as the server (a dedicated server, or a client
  hosting the game).
- **Player died** fires on whichever machine actually simulates that player's
  character — normally that player's own client (or the host, for the host's own
  character). For every player's death to show up, every player needs `WebhookUrl`
  (below) filled in — it can be the same webhook for everyone.

Settings live under `[Discord]` in `BepInEx/config/fedo.servertools.cfg`:

- `WebhookUrl` — your Discord webhook URL (Server Settings > Integrations > Webhooks).
  Keep it private — anyone who has it can post in that channel. **Unlike `ServerToken`
  above, it's fine to fill this in on every installation, including players'** — it
  doesn't grant access to the Fedoheim API, and death logging (see above) needs it set
  on every client to work for every player. Blank by default (the safe no-op), same as
  `ServerToken` — an admin has to explicitly decide to hand it out.
- `LogPlayerConnected` / `PlayerConnectedTemplate`
- `LogPlayerDisconnected` / `PlayerDisconnectedTemplate`
- `LogPlayerDeath` / `PlayerDeathTemplate`
- `LogServerStarted` / `ServerStartedTemplate`
- `LogServerStopped` / `ServerStoppedTemplate`
- `LogWorldSaved` / `WorldSavedTemplate`

Each `*Template` supports `{player}` (connected/disconnected/death), `{world}` (server
started), or `{cause}` (death only). `{cause}` describes what killed the player:
`drowning`, `fall damage`, `fire`, `the cold`, `poison`, `the edge of the world`, the
name of an attacking creature/player (e.g. `Greydwarf`), or a few other environmental
causes (falling tree, cart, boat, turret, catapult, stalactite, the sea, smoke
inhalation, unknown causes).

The "server stopped" message is best-effort: it's sent right as the server shuts down
(`OnApplicationQuit`), so it won't arrive if the process is force-killed instead of shut
down normally — unlike the API's own `"stopping"` report, this one isn't waited on
synchronously.

## Stability patch (unrelated to reporting)

Not part of the API-reporting feature above — a small, generic Harmony patch on
`ZNetScene.RemoveObjects`, active on any install of this mod (client or server, no
`ServerToken` needed). It repairs a class of Valheim bug seen on heavily-tested/modded
saves: if a `ZNetView` instance gets destroyed (or loses its ZDO) without being properly
removed from `ZNetScene`'s internal instance registry, that vanilla method throws a
`NullReferenceException` on it *every single frame, forever* — never resolves on its
own, floods the log, and (depending on what else is affected) can visibly break the
game. This isn't specific to any one mod or prefab; see e.g.
[ASharpPen/Valheim.LessZdoZoneCorruption](https://github.com/ASharpPen/Valheim.LessZdoZoneCorruption)
for the same class of issue in other modded setups.

This patch runs just before `RemoveObjects` each frame, finds any such broken entry,
logs the affected `GameObject`'s name and prefab hash (`FedoServerTools: repaired a
broken ZNetScene instance (...)`, useful to track down what actually caused it), removes
it from the registry, and resets its ZDO's `Created` flag so the game gets a chance to
recreate it properly on the next pass instead of leaving it permanently broken.

## Auto-join (client menu skip)

Also unrelated to reporting — a client-only Harmony patch on `FejdStartup` (the
Valheim main menu) that skips it entirely when the active modpack profile has an
auto-connect target configured (see the Fedoheim launcher's "Profils" page, admin
only). At boot, it reads a small `fedoheim-session.txt` file dropped by the launcher
next to `BepInEx/` — never part of this mod's own package, never synced like the rest
of a modpack (same idea as `ServerToken` above).

- If the profile has no auto-connect target configured, this does nothing — the menu
  behaves exactly like vanilla Valheim.
- If the account has no character linked yet, it jumps straight to the "new character"
  screen. Once the character is created, it connects automatically to the configured
  target (a local world to host, or a dedicated server to join).
- If the account already has a linked character (and it exists locally), the whole menu
  is skipped entirely: the character is selected and the game connects immediately.

The character↔account link itself is decided server-side (see `PeerSteamId.cs` and the
Fedoheim API's `linkCharacterName`), not by this patch — it only reacts to what the
launcher tells it.

Since none of the vanilla menu panels are shown once auto-join takes over, Valheim's own
loading screen never appears either — the whole connection/world-load time would
otherwise be a plain black screen with no text. `LoadingOverlay.cs` shows a small
"Chargement de Fedoheim..." banner at the top of the screen for that duration, hidden as
soon as the in-game HUD actually appears (or after 30s regardless, as a safety net if the
connection fails).

## Character ownership check

Server-side only (`CharacterOwnershipPatch.cs`) — when a remote player connects, this
mod asks the Fedoheim API (`POST /modpacks/character-check`) whether their character
name is already linked to a *different* Fedoheim account, and kicks them immediately if
so. This closes an impersonation gap: without it, anyone could create a local character
with an already-claimed name and connect under that identity, showing up on the map and
in reports as if they were the rightful owner.

- Never triggered for the host's own character in a solo/hosted game — the host has no
  `ZNetPeer` representing themselves (see `PeerSteamId.cs`), so this check simply never
  runs for them.
- A character name not yet linked to anyone is always allowed — this check only blocks
  stealing an already-claimed name, it never performs the first-time link itself (that
  stays `linkCharacterName`, on the normal periodic report).
- Blocking by design: the connecting player has to wait for this one HTTP round-trip
  (3s timeout) before joining, which also briefly blocks the whole server's main thread
  (Harmony patches can't be async) — kept deliberately short for that reason. Fails
  open (allows the connection) on any error or timeout, same philosophy as the rest of
  this mod's API calls: a network hiccup should never lock out a legitimate player.
- `ServerToken` empty (the default on a player install) disables this check entirely,
  same as the periodic reporting above.

## Notes

- **Seasons integration is a soft dependency, detected at runtime** (BepInEx's plugin
  list is checked for `shudnal.Seasons` before touching any of its API) — this mod
  loads and works fine whether or not Seasons is part of the modpack, it just won't
  have a season to report if it isn't. Not listed in `manifest.json`'s `dependencies`
  for this reason (that field is for hard requirements only).
- **Reporting is server-only. `ForcePublicPosition`'s client-side half runs everywhere
  there's a local player**, including the host of a solo/hosted game — not gated on
  `!ZNet.IsServer()` like the rest of this mod, since the server-side write only
  reaches *remote* peers and would otherwise never force the host's own checkbox.
- **If this mod ends up in the player-facing modpack (for the client-side effect),
  `ServerToken` must stay blank there.** The `.cfg` is resynced identically to everyone
  who has this modpack — including `ServerToken`. A player hosting their own solo/co-op
  game becomes a server too (`ZNet.IsServer()` is true for them), and would start
  posting reports under the community's real token, corrupting the "who's online"
  display with their private session. With `ServerToken` left blank (the default),
  reporting is simply skipped with a local log warning — safe, and doesn't affect
  `ForcePublicPosition` either way. Only the actual dedicated server's own copy,
  installed manually (not through the modpack sync), should ever have the real token.
- **`WebhookUrl` does not have this restriction** — see "Discord webhook logging"
  above. It's a separate secret with a separate risk (someone could post fake messages
  to that one Discord channel, nothing more), and death logging actually needs it
  filled in on every player's client to work for everyone. Still blank by default, so
  nothing is posted anywhere until an admin deliberately fills it in — on the server
  only (for connect/disconnect/start/stop/saved), on every client (to also get death
  logging), or both.
