# Changelog

## 1.0.0

- Initial release.
- On death, a hostile guardian (configurable, defaults to Skeleton) spawns in place of the tombstone, named after the dead player.
- The guardian stays frozen until a player comes within range (configurable), then hunts them specifically -- it never engages other creatures.
- It's ignored by every other creature (Boss faction) and its state (loot, owner) is stored in its persistent world data, so it survives disconnects, zone reloads, and server restarts.
- Defeating the guardian spawns the tombstone, with the player's items, where it died.
- A removable map pin tracks the guardian and follows it if it moves.
- On-screen messages for guardian spawn/defeat (configurable).
