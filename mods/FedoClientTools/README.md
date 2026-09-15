# FedoClientTools

*By Fedo*

Client-only companion to [FedoServerTools](../FedoServerTools/README.md) — split out so
that a regular player only installs what actually runs on their own machine: skipping
the main menu straight to auto-connect, and a reconnect/quit screen after a lost
connection. No `ServerToken`, no admin routes, nothing that talks to the API with a
secret — safe to install on any player's client (and it's part of the player-facing
modpack, unlike FedoServerTools).

The in-game clock previously shown here has moved to
[FedoHud](../FedoHud/README.md), which now owns all custom HUD overlays.

## Auto-join (client menu skip)

A client-only Harmony patch on `FejdStartup` (the Valheim main menu) that skips it
entirely when the active modpack profile has an auto-connect target configured (see the
Fedoheim launcher's "Profils" page, admin only). At boot, it reads a small
`fedoheim-session.txt` file dropped by the launcher next to `BepInEx/` — never part of
this mod's own package, never synced like the rest of a modpack.

- If the profile has no auto-connect target configured, this does nothing — the menu
  behaves exactly like vanilla Valheim.
- If the account has no character linked yet, it jumps straight to the "new character"
  screen, with the name field pre-filled to the player's Discord username as a
  suggestion — freely editable, not locked. Once the character is created, it connects
  automatically to the configured target (a local world to host, or a dedicated server
  to join).
- If the account already has a linked character (and it exists locally), the whole menu
  is skipped entirely: the character is selected and the game connects immediately.

The character↔account link itself is decided server-side (see FedoServerTools'
`PeerSteamId.cs` and the Fedoheim API's `linkCharacterName`), not by this patch — it
only reacts to what the launcher tells it, and never restricts what name a player
chooses.

Since none of the vanilla menu panels are shown once auto-join takes over, Valheim's own
loading screen never appears either — the whole connection/world-load time would
otherwise be a plain black screen with no text. `LoadingOverlay.cs` shows the Fedoheim
logo and a "Chargement de Fedoheim" line below it, both centered in the middle of the
screen, hidden as soon as the in-game HUD actually appears (or after 30s regardless, as
a safety net if the connection fails).

### Reconnect / quit screen

If auto-join brings the player back to this menu after a failed/lost connection
(`ZNet.GetConnectionStatus()` not `None`) rather than a first launch, `DisconnectChoiceOverlay.cs`
shows two buttons — "Connexion" (retry the same target) and "Quitter le jeu" — instead
of silently retrying in a loop. For a dedicated-server target, it also polls `GET
/modpacks/:slug/online-players` (public route, no token) every 10s to show a live
online/offline status and disable the reconnect button while the server is confirmed
offline.

## Configuration

Settings live in `BepInEx/config/fedo.clienttools.cfg`.

**[Api]**
- `ApiBaseUrl` — base URL of the Fedoheim API, no trailing slash (default
  `http://127.0.0.1:3000`). Only used to poll the public online-players route for the
  reconnect screen above — no login/token involved.

## Admin broadcast message (receiving side)

Receives and displays the admin broadcast message sent from FedoServerTools (see that
mod's README, "Admin server commands") — shows up centered on screen in yellow and in
the in-game chat. The RPC protocol itself (`mods/_shared/BroadcastMessage.cs`) is shared
between the two mods so they never drift on the RPC name/format.

## Stability patch (unrelated to any of the above)

Shared with FedoServerTools (`mods/_shared/ZNetSceneStabilityPatch.cs`) — a small,
generic Harmony patch on `ZNetScene.RemoveObjects` that repairs a class of Valheim bug
seen on heavily-tested/modded saves (a `ZNetView` destroyed without being properly
deregistered, which would otherwise crash that vanilla method every single frame,
forever). See FedoServerTools' README for the full explanation.
