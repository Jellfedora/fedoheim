# FedoDiscordLogs

*By Fedo*

Posts a running log of your Valheim session to a Discord channel via a webhook: who connected, who disconnected, who died, when the server started/stopped, and when the world was saved.

## Features

- Player connected / disconnected.
- Player died.
- Server started / server stopped.
- World saved.
- Every event has its own on/off toggle and a customizable message template (with `{player}` / `{world}` placeholders).
- No external dependency: posts straight to the Discord webhook over HTTP.

## Where does this need to be installed?

This is the same DLL whether you use it as a "server mod", a "client mod", or both — there's no separate build. What each installation actually sees depends on its role:

- **Player connected / disconnected / server started / server stopped / world saved** only fire on the machine acting as the server (a dedicated server, or a client hosting the game). Install it there for these events.
- **Player died** fires on whichever machine actually simulates that player's character — normally that player's own client (or the host, for the host's own character). For every player's death to show up, every player needs the mod installed with a webhook configured (it can be the same webhook for everyone).

## Setup

1. Install the mod (BepInEx pack required, listed as a dependency).
2. Launch the game once so the mod generates its config file (`BepInEx/config/fedo.discordlogs.cfg`).
3. Create a Discord webhook: in your Discord server, go to **Server Settings > Integrations > Webhooks**, create one, and copy its URL.
4. Paste that URL into the `WebhookUrl` setting under `[Discord]` in `fedo.discordlogs.cfg`.

Keep your webhook URL private — anyone who has it can post messages to that channel.

## Configuration

All settings live in `BepInEx/config/fedo.discordlogs.cfg` and can also be edited with a BepInEx config manager mod.

**[Discord]**
- `WebhookUrl` — your Discord webhook URL.

**[Events]**
- `LogPlayerConnected` / `PlayerConnectedTemplate`
- `LogPlayerDisconnected` / `PlayerDisconnectedTemplate`
- `LogPlayerDeath` / `PlayerDeathTemplate`
- `LogServerStarted` / `ServerStartedTemplate`
- `LogServerStopped` / `ServerStoppedTemplate`
- `LogWorldSaved` / `WorldSavedTemplate`

Each `*Template` supports `{player}` (connected/disconnected/death), `{world}` (server started), or `{cause}` (death only).

`{cause}` describes what killed the player: `drowning`, `fall damage`, `fire`, `the cold`, `poison`, `the edge of the world`, the name of an attacking creature/player (e.g. `Greydwarf`), or a few other environmental causes (falling tree, cart, boat, turret, catapult, stalactite, the sea, smoke inhalation, unknown causes).

## Notes

- The "server stopped" message is best-effort: it's sent right as the server shuts down, so it won't arrive if the process is force-killed instead of shut down normally.
