# FedoDeathGif

*By Fedo*

Died in Valheim? Now there's proof. FedoDeathGif keeps a rolling recording of your last moments and, the instant you die, turns it into a gif and posts it straight to a Discord channel via a webhook — no external capture software needed.

## Features

- Continuously records a short, rolling window of gameplay in the background.
- The instant you die, waits just long enough for the death animation to play, then freezes and exports the clip as a gif.
- Posts the gif to a Discord channel through a webhook, with a customizable message that includes the player's name.
- Shows an on-screen message on death, styled like the game's own messages (e.g. "The gods are merciful").
- Makes your character shout a line in chat on death (visible to other players nearby), fully configurable.
- Everything is tunable: resolution, framerate, recording length, post-death delay, and every piece of text.

## Setup

1. Install the mod (BepInEx pack required, listed as a dependency).
2. Launch the game once so the mod generates its config file (`BepInEx/config/fedo.deathgif.cfg`).
3. Create a Discord webhook: in your Discord server, go to **Server Settings > Integrations > Webhooks**, create one, and copy its URL.
4. Paste that URL into the `WebhookUrl` setting under `[Discord]` in `fedo.deathgif.cfg`.
5. Die. Check Discord.

Keep your webhook URL private — anyone who has it can post messages to that channel.

## Configuration

All settings live in `BepInEx/config/fedo.deathgif.cfg` and can also be edited with a BepInEx config manager mod.

**[Capture]**
- `Fps` — frames captured per second in the background (default 12).
- `Width` / `Height` — gif resolution in pixels (default 640x360). Keep the final gif under ~8 MB or Discord will reject the upload.
- `BufferSeconds` — how many seconds before death are kept (default 5).
- `PostDeathDelay` — how long to wait after death before freezing the gif, so the death animation has time to play out (default 1.5s).

**[Discord]**
- `WebhookUrl` — your Discord webhook URL.
- `MessageTemplate` — text posted with the gif. `{player}` is replaced with the dead player's name.

**[Message]**
- `ShowDeathMessage` / `DeathMessageText` — on-screen death message (on/off + text).
- `ShowDeathChatMessage` / `DeathChatMessageText` — chat line said by the player on death (on/off + text).

## Notes

- Capturing happens continuously while you're in the world, so there's a small, constant performance cost. Lower `Fps`/`Width`/`Height` if you notice a hit.
- The gif uses a compact, dependency-free encoder tuned for short clips, not archival-quality footage.
