# Changelog

## 2.0.0

- Merged the former standalone `FedoDeathGif` mod into `FedoDeath` — one mod now covers the whole death experience, guardian included. `FedoDeathGif` no longer exists as a separate mod; its icon is now used for `FedoDeath`.
- Death gif capture, Discord webhook posting, on-screen message and chat line on death are unchanged in behavior, just carried over as-is.
- Gif/webhook/message settings remain local to each client and are never synced (unlike the guardian settings), since a webhook URL is a secret.

## 1.0.0

- Initial release.
- On death, a hostile guardian (configurable, defaults to Skeleton) spawns in place of the tombstone, named after the dead player.
- The guardian stays frozen until a player comes within range (configurable), then hunts them specifically -- it never engages other creatures.
- It's ignored by every other creature (Boss faction) and its state (loot, owner) is stored in its persistent world data, so it survives disconnects, zone reloads, and server restarts.
- Defeating the guardian spawns the tombstone, with the player's items, where it died.
- A removable map pin tracks the guardian and follows it if it moves.
- On-screen messages for guardian spawn/defeat (configurable).
- All settings are synced from the server and locked ([ServerSync](https://github.com/blaxxun-boop/ServerSync)) -- a connecting player can't override them from their own local `.cfg`.
