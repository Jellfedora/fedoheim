# Changelog

## 1.0.0

- Initial release.
- Adds the Gold Rabbit: a rare, Hare-model creature with its own entry in the world's ambient spawn table, so it can appear in any biome.
- Shouts a fleeing excuse (configurable text) the moment it notices a player and starts running.
- Drops a few coins every 2-3 seconds while alive (configurable interval and amount).
- Pays out a large pile of coins on death instead of its usual meat/pelt (configurable amount range).
- Despawns in a puff of smoke with no loot if not killed within a configurable time limit.
- On-screen message and sound when one spawns nearby (configurable).
- Registers a dedicated always-golden prefab (`Fedo_GoldRabbit`) spawnable on demand via the console or a prefab-listing mod like Easy Spawner, for testing.
- All settings are synced from the server and locked ([ServerSync](https://github.com/blaxxun-boop/ServerSync)) -- a connecting player can't override them from their own local `.cfg`.
