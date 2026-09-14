# FedoDeathGif

*By Fedo*

Death shouldn't go unnoticed. The instant you die, a gif of your last moments gets
posted straight to a Discord channel via a webhook — no external capture software
needed.

## What it does

- Continuously records a short window of your gameplay in the background.
- The instant you die, waits just long enough for the death animation to play, then
  captures that moment as a gif.
- Posts the gif to a Discord channel, with a message that includes your name.
- Shows an on-screen message when you die, styled like the game's own messages.
- Makes your character shout a line in chat when you die, visible to nearby players.
- Everything above is configurable: resolution, recording length, and every piece of
  text.

## Setup

1. Launch the game once so the mod creates its config file
   (`BepInEx/config/fedo.deathgif.cfg`).
2. Create a Discord webhook: in your Discord server, go to **Server Settings >
   Integrations > Webhooks**, create one, and copy its URL.
3. Paste that URL into the `WebhookUrl` setting under `[Discord]` in
   `fedo.deathgif.cfg`.
4. Die. Check Discord.

Once you're connected to a server with this mod, everyone's client automatically picks
up the admin's webhook URL — so every player's death gets posted to the same channel
without each of them having to configure anything.

## Configuration

Settings live in `BepInEx/config/fedo.deathgif.cfg`. The admin's values (webhook
included) apply to everyone — a player editing their own copy of these settings has no
effect once connected to the server.

**[Capture]**
- `Fps` — frames captured per second in the background.
- `Width` / `Height` — gif resolution in pixels. Keep the final gif reasonably small or
  Discord will reject the upload.
- `BufferSeconds` — how many seconds before death are kept.
- `PostDeathDelay` — how long to wait after death before capturing the gif, so the
  death animation has time to play out.

**[Discord]**
- `WebhookUrl` — your Discord webhook URL.
- `MessageTemplate` — text posted with the gif. `{player}` is replaced with your name.

**[Message]**
- `ShowGifMessage` / `GifMessageText` — the on-screen death message (on/off + text).
- `ShowDeathChatMessage` / `DeathChatMessageText` — the chat line said on death (on/off
  + text).
