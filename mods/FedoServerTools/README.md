# FedoServerTools

*By Fedo*

Server-only: talks to the Fedoheim API on behalf of this game server, and gives an admin
a few remote controls over it (time of day, season, a broadcast message) from the
launcher. Only install this on the actual dedicated server -- for the client-side
companion (auto-connect, in-game clock, reconnect screen), see
[FedoClientTools](../FedoClientTools/README.md).

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
   the zone) and current armor (`Humanoid.GetBodyArmor()`, rounded). Reports still say
   `status: "starting"` (not `"online"`) until `StartingGracePeriodSeconds` has passed
   since the plugin loaded (default 60s) — increase this if your server has a lot of
   mods and takes longer to actually become reachable. Each report also carries the
   current season (`Spring`/`Summer`/`Fall`/`Winter`) if the
   [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/) mod is installed on
   this server — a soft dependency, entirely optional: this mod works exactly the same
   without it, it just won't have a season to report. Unlike biome/armor this is one
   value per report, not per player, since the season is a server-wide setting. Each
   report also carries the current in-game clock (`HH:MM`, from `EnvMan.
   GetDayFraction()`) — same one-value-per-report principle as the season.
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

Player reports also carry each player's resolved SteamID64 (`PeerSteamId.cs`) so the
API can link a character name to the Fedoheim account that played it, first-come
first-served — never displayed, used only for that link.

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
  player-facing modpack with a real value filled in** — this mod is server-only, so it
  should never be in that modpack in the first place, but see Notes below just in case.
- `SyncIntervalSeconds` — how often this mod talks to the API (default `30`, between
  `10` and `300`).
- `StartingGracePeriodSeconds` — how long after the plugin loads to keep reporting
  `"starting"` instead of `"online"` (default `60`, between `0` and `600`). Raise this
  for a heavily modded server that takes a while to actually finish loading.

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
- `TimeOffsetHours` (default `0`, between `-12` and `12`) — shifts the in-game clock
  value sent to the API/launcher if it doesn't match what the sky looks like. Purely
  cosmetic, no effect on the actual day/night cycle. Independent of FedoClientTools'
  own clock overlay setting of the same name (not synced between the two mods).

## Admin server commands

Server-side only (`ServerCommands.cs`) — lets an admin change the current time of day or
force a season from the launcher (Admin > Serveur), without touching the server console
directly. Follows a "poll, never push" principle: an admin action doesn't call the game,
it queues a one-shot command on the API (`POST /modpacks/:slug/server-command`) that
gets picked up and applied the next time this mod reports (`POST
/modpacks/online-players`, whose response now also carries the pending command for this
profile, if any) — so it can take up to `SyncIntervalSeconds` (30s default) to actually
happen, and only while the server is online to poll for it.

- **Time of day**: sets `ZNet.instance.SetNetTime(...)` to the next occurrence of the
  requested hour (6h/12h/18h/24h — always jumping forward, never backward, based on
  `EnvMan.GetDayFraction()`), then forces an immediate broadcast to already-connected
  clients (`ZNet.SendNetTime()`, private — invoked via reflection) instead of waiting for
  the engine's own periodic time sync.
- **Season**: forces a season via the [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/)
  mod's own public config entries (`Seasons.Seasons.overrideSeason`/`seasonOverrided`,
  both `ConfigEntry<T>` — this mod just sets `.Value` on them, exactly as if an admin had
  edited Seasons' own `.cfg`, so its own ServerSync already handles propagating the
  change to clients). Choosing "Automatique" turns the override back off and lets the
  season resume its natural progression. Silently ignored if Seasons isn't installed on
  the server (soft dependency, same `SeasonReporting.IsLoaded` guard as season
  reporting).
- Server-only (`ZNet.instance.IsServer()`) — a client applying this would just get
  overwritten by the next sync anyway.
- The response parsing/application runs off the main Unity thread (the periodic report is
  fire-and-forget over HTTP) — dispatched back onto the main thread via a small queue
  drained from `Update()` rather than touching `ZNet`/`EnvMan`/Seasons' config directly
  from a background thread.
- **Broadcast message** (`mods/_shared/BroadcastMessage.cs`, shared with FedoClientTools):
  posts a short admin message (Admin > Serveur, same one-shot polling mechanism as
  above) that shows up, on every connected player's own client, at the center of the
  screen in yellow and in their in-game chat. The RPC send side lives here; the
  receive/display side lives in FedoClientTools — see that mod's README.

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
- `ServerToken` empty disables this check entirely, same as the periodic reporting
  above.

## Stability patch (unrelated to reporting)

Shared with FedoClientTools (`mods/_shared/ZNetSceneStabilityPatch.cs`) — a small,
generic Harmony patch on `ZNetScene.RemoveObjects`, active on either mod (client or
server). It repairs a class of Valheim bug seen on heavily-tested/modded saves: if a
`ZNetView` instance gets destroyed (or loses its ZDO) without being properly removed
from `ZNetScene`'s internal instance registry, that vanilla method throws a
`NullReferenceException` on it *every single frame, forever* — never resolves on its
own, floods the log, and (depending on what else is affected) can visibly break the
game. This isn't specific to any one mod or prefab; see e.g.
[ASharpPen/Valheim.LessZdoZoneCorruption](https://github.com/ASharpPen/Valheim.LessZdoZoneCorruption)
for the same class of issue in other modded setups.

## Notes

- **Seasons integration is a soft dependency, detected at runtime** (BepInEx's plugin
  list is checked for `shudnal.Seasons` before touching any of its API, see
  `SeasonReporting.IsLoaded`) — this mod loads and works fine whether or not Seasons is
  part of the modpack, it just won't have a season to report/force if it isn't. Still
  listed in `manifest.json`'s `dependencies` so the launcher's editor can warn if an
  admin configures this mod without also adding Seasons to the modpack.
- **This mod is server-only.** It should never be part of a player-facing modpack — if
  it ever ended up there by mistake, `ServerToken` must stay blank in that shared
  `.cfg`: the `.cfg` is resynced identically to everyone who has this modpack, and a
  player hosting their own solo/co-op game becomes a server too (`ZNet.IsServer()` is
  true for them), which would start posting reports under the community's real token,
  corrupting the "who's online" display with their private session. With `ServerToken`
  left blank (the default), reporting is simply skipped with a local log warning.
