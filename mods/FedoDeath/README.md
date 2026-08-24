# FedoDeath

*By Fedo*

Death shouldn't be free — and it shouldn't go unnoticed either. Instead of your tombstone appearing the instant you die, a hostile guardian spawns where you fell and stands over your loot. And the instant you die, a gif of your last moments gets posted straight to a Discord channel via a webhook.

## How it works

### Guardian

1. You die. Your tombstone is *not* created yet — instead, a guardian creature (a Skeleton by default) spawns exactly where your grave would have been.
2. The guardian stays completely still (AI disabled) until a player comes within `ActivationRange` (20m by default) -- so it can't wander off and get tangled up with other creatures. Once awake, it hunts you specifically. It's also assigned to the game's `Boss` faction, which in vanilla Valheim is allied with every other faction except players -- so no other mob will ever fight it (or get attacked by it). It does *not* get a boss health bar or boss music; that's a separate flag the mod leaves untouched.
3. Kill the guardian, and your tombstone (with everything it was holding) appears where it fell.

If you die with an empty inventory, no guardian spawns — there's nothing to protect.

The guardian's loot and owner info are stored in its own persistent world data (its ZDO), not just in memory -- so it survives disconnecting/reconnecting, zone unload/reload, or a server restart. Whenever you come back and finish it off, your grave still appears correctly.

A map pin tracks the guardian and follows it if it moves while hunting you, so you never lose track of where your loot is. It's a normal, removable pin, unlike the game's own death marker (which this mod replaces, since it wouldn't be updated while the guardian moves).

### Death gif

Independently of the guardian, FedoDeath keeps a rolling recording of your last moments and, the instant you die, turns it into a gif and posts it straight to a Discord channel via a webhook — no external capture software needed.

- Continuously records a short, rolling window of gameplay in the background.
- The instant you die, waits just long enough for the death animation to play, then freezes and exports the clip as a gif.
- Posts the gif to a Discord channel through a webhook, with a customizable message that includes the player's name.
- Shows an on-screen message on death, styled like the game's own messages.
- Makes your character shout a line in chat on death (visible to other players nearby), fully configurable.
- Everything is tunable: resolution, framerate, recording length, post-death delay, and every piece of text.

## Setup (death gif)

1. Install the mod (BepInEx pack required, listed as a dependency).
2. Launch the game once so the mod generates its config file (`BepInEx/config/fedo.death.cfg`).
3. Create a Discord webhook: in your Discord server, go to **Server Settings > Integrations > Webhooks**, create one, and copy its URL.
4. Paste that URL into the `WebhookUrl` setting under `[Discord]` in `fedo.death.cfg`.
5. Die. Check Discord.

Keep your webhook URL private — anyone who has it can post messages to that channel.

## Configuration

Settings live in `BepInEx/config/fedo.death.cfg`.

**[Guardian]** (synced from the server, locked — see Notes)
- `CreaturePrefab` — the creature spawned as guardian (default `Skeleton`). Must be a valid Valheim prefab name (e.g. `Skeleton`, `Wraith`, `Troll`, `Draugr`...).
- `GuardianNameTemplate` — display name given to the guardian (default `Dead {player}`). `{player}` is replaced with the dead player's name.
- `ActivationRange` — distance in meters a player must approach before the guardian wakes up (default `20`).
- `ShowMessages` / `SpawnMessageText` / `DefeatMessageText` — on-screen messages when the guardian appears and when it's defeated.

**[Capture]** (local to each client)
- `Fps` — frames captured per second in the background (default 12).
- `Width` / `Height` — gif resolution in pixels (default 640x360). Keep the final gif under ~8 MB or Discord will reject the upload.
- `BufferSeconds` — how many seconds before death are kept (default 5).
- `PostDeathDelay` — how long to wait after death before freezing the gif, so the death animation has time to play out (default 1.5s).

**[Discord]** (local to each client — never synced, it's a secret)
- `WebhookUrl` — your Discord webhook URL.
- `MessageTemplate` — text posted with the gif. `{player}` is replaced with the dead player's name.

**[Message]** (local to each client)
- `ShowGifMessage` / `GifMessageText` — on-screen death message shown alongside the gif capture (on/off + text).
- `ShowDeathChatMessage` / `DeathChatMessageText` — chat line said by the player on death (on/off + text).

## Notes

- Only the `[Guardian]` settings are synced from the server and locked ([ServerSync](https://github.com/blaxxun-boop/ServerSync)) -- a connecting player can't override them from their own local `.cfg`, since they affect what everyone on the server sees. The gif/webhook/message settings stay purely local to each client and are never synced (a webhook URL is a secret, never something to broadcast).
- The guardian only affects your own death/tombstone flow; it doesn't change how other players' graves work unless they also have the mod installed.
- If the guardian somehow can't be spawned (invalid prefab name), the tombstone is created immediately instead, as a fallback.
- Gif capturing happens continuously while you're in the world, so there's a small, constant performance cost. Lower `Fps`/`Width`/`Height` if you notice a hit.
- The gif uses a compact, dependency-free encoder tuned for short clips, not archival-quality footage.
