# FedoServerTools

*By Fedo*

Server-side mod: reports who's currently online to the Fedoheim API, so the launcher's
home page can show it to every player — no login required to see it.

## How it works

1. Once this instance becomes an actual server (dedicated server, or the host of a
   solo/co-op game), the mod starts posting a report to the API every
   `ReportIntervalSeconds` (default 30s): the list of connected players, using the
   game's own player list (`ZNet.GetPlayerList()`) — this includes the host in a
   solo/hosted game, not just remote peers. Each entry also carries the player's
   current biome (`Heightmap.FindBiome`). Each player's own "Public position" setting
   (Options > Game) normally has to be enabled for their position to be usable at all —
   see `ForcePublicPosition` below to always report a biome regardless of that setting.
2. Each report is authenticated with a shared secret (`ServerToken`) tied to one
   modpack profile (see Configuration below) — without it, reports are rejected by
   the API and only a warning is logged locally.
3. On a clean server shutdown, one last report is sent immediately with an empty
   player list and `online: false`, so the launcher reflects the server going down
   right away instead of waiting for the report to simply go stale (which still
   happens on its own after ~90s if the process is killed without a clean shutdown,
   e.g. a crash).

Besides reporting, `ForcePublicPosition` (see below, on by default) makes this server
treat every player's position as public — the one deliberate effect this mod has beyond
reporting.

## Configuration

Settings live in `BepInEx/config/fedo.servertools.cfg`.

**[Api]**
- `ApiBaseUrl` — base URL of the Fedoheim API, no trailing slash (default
  `http://127.0.0.1:3000`).
- `ServerToken` — shared secret for the modpack profile this server runs, generated/
  regenerated from the launcher's "Profils" page (admin only, "Régénérer le jeton").
  The token already identifies which profile it belongs to, so there's nothing else
  to configure here — no separate slug setting. Required; keep it secret, anyone with
  it could post a fake player list for that profile.
- `ReportIntervalSeconds` — how often to report (default `30`, between `10` and
  `300`).

**[Players]**
- `ForcePublicPosition` (default `true`) — treats every connected player's position as
  public on this server, regardless of their own "Public position" setting (Options >
  Game). That setting lives in each player's local preferences and can't be changed
  from the server — this only overrides what this server itself sees when building the
  player list (`ZNet.GetPlayerList()`), so a biome is always reported for everyone.
  Disable if you'd rather respect each player's own choice, at the cost of never
  reporting a biome for players who haven't enabled it themselves.

**[Biomes]**
- `MeadowsName`, `BlackForestName`, `SwampName`, `MountainName`, `PlainsName`,
  `AshLandsName`, `DeepNorthName`, `OceanName`, `MistlandsName` — display name sent for
  each biome, shown as-is by the launcher (no translation happens outside this .cfg).
  Default to the English name; edit this file to use your own translation, e.g. French
  (`MeadowsName = Prairies`).

## Notes

- Install this only on the server side (dedicated server, or the machine hosting a
  solo/co-op game) — a regular client connecting to someone else's server has no use
  for it, since it never becomes the server itself (`ZNet.IsServer()` stays false).
