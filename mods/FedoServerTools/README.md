# FedoServerTools

*By Fedo*

Mostly a server-side mod: talks to the Fedoheim API on behalf of this game server.
Today that means reporting who's currently online (with biome and armor) so the
launcher's home page can show it to every player — no login required to see it — but
this mod is meant to grow into the general channel between this server and the API, in
both directions (the API telling the game to do something is planned), not just a
player-list reporter. It also has one small client-side effect (see
`ForcePublicPosition`) — safe to install on a regular player's client too, see Notes.

## How it works

1. Once this instance becomes an actual server (dedicated server, or the host of a
   solo/co-op game), the mod starts talking to the API every `SyncIntervalSeconds`
   (default 30s). Today that means posting the list of connected players, using the
   game's own player list (`ZNet.GetPlayerList()`) — this includes the host in a
   solo/hosted game, not just remote peers. Each entry also carries the player's
   current biome (`Heightmap.FindBiome`, falling back to `WorldGenerator.GetBiome` if
   that returns nothing for the zone) and current armor (`Humanoid.GetBodyArmor()`,
   rounded). Each player's own "Public position" setting (Options > Game) normally has
   to be enabled for their position to be usable at all — see `ForcePublicPosition`
   below to force it on for everyone on this server.
2. Each report is authenticated with a shared secret (`ServerToken`) tied to one
   modpack profile (see Configuration below) — without it, reports are rejected by
   the API and only a warning is logged locally.
3. On a clean server shutdown, one last report is sent — waited on synchronously
   (bounded by a short HTTP timeout) rather than fired-and-forgotten, since the process
   can exit within moments of a clean shutdown too — with an empty player list and
   `online: false`, so the launcher reflects the server going down right away instead
   of waiting for the report to simply go stale (which still happens on its own after
   ~90s if the process is killed outright, e.g. a real crash).

Besides reporting, `ForcePublicPosition` (see below, on by default) forces "Public
position" (Options > Game) on for the session, two ways at once, belt-and-suspenders:
- **Server side**: writes directly to the server's copy of that flag for each connected
  peer (`ZNetPeer.m_publicRefPos`) every sync cycle.
- **Client side**: on a regular connecting client (not the server), simulates a real
  click on the in-game checkbox (`Minimap.OnTogglePublicPosition()`) once it loads in —
  going through the game's own normal path (whatever networking that triggers) instead
  of relying on the server-side write alone.

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

## Notes

- **Reporting is server-only, `ForcePublicPosition` runs on both.** The player-list
  reporting (and the server-side half of `ForcePublicPosition`) only ever activates
  when this instance becomes an actual server (`ZNet.IsServer()`) — dedicated server, or
  the host of a solo/co-op game; dormant on a regular connecting client. The
  client-side half of `ForcePublicPosition` (the simulated checkbox click) does the
  opposite: it only runs when this instance is *not* the server.
- **If this mod ends up in the player-facing modpack (for the client-side effect),
  `ServerToken` must stay blank there.** The `.cfg` is resynced identically to everyone
  who has this modpack — including `ServerToken`. A player hosting their own solo/co-op
  game becomes a server too (`ZNet.IsServer()` is true for them), and would start
  posting reports under the community's real token, corrupting the "who's online"
  display with their private session. With `ServerToken` left blank (the default),
  reporting is simply skipped with a local log warning — safe, and doesn't affect
  `ForcePublicPosition` either way. Only the actual dedicated server's own copy,
  installed manually (not through the modpack sync), should ever have the real token.
