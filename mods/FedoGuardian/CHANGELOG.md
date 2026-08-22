# Changelog

## 1.0.0

- Initial release.
- Summoning wand (configurable) spawns a permanent guard a few meters ahead of the player.
- The guard engages hostile creatures within its detection range and returns to its post
  afterward.
- Right-click to dress the guard in your currently worn gear; alt + right-click to take it back.
- Guard state (position, equipment, owner) persists across disconnects, zone reloads, and server
  restarts.
- All settings are synced from the server and locked ([ServerSync](https://github.com/blaxxun-boop/ServerSync))
  -- a connecting player can't override them from their own local `.cfg`.
