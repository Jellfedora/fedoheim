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
- Fixed: every real Gold Rabbit spawned in the world (including ones already in an
  existing save) came out invisible and never turned golden, and could crash the game
  with a `NullReferenceException` loop in `ZNetScene.RemoveObjects` that got WORSE every
  frame. Cause: the registered `Fedo_GoldRabbit` template was deliberately disabled
  (`SetActive(false)`, so it never acted like a real entity while just sitting as a
  lookup target) -- but `ZNetScene.CreateObject` instantiates every real spawn straight
  from that template with `Object.Instantiate`, which also copies its disabled
  `activeSelf` state onto every real spawn. Reactivating the result right after creation
  (an earlier attempt at this fix) turned out to be too late: `ZNetView.Awake()` needs to
  run *during* `Instantiate()` itself to claim the ZDO `CreateObject` is trying to attach
  it to, and a disabled `GameObject` defers `Awake` until reactivated -- by then the
  claim window had already closed, so the ZDO never got marked created and the game kept
  retrying the same broken instantiation every single frame forever. Fixed properly by
  never disabling the template itself: it's now parented under a permanently-disabled
  container instead (same technique as `FedoGuardian`'s `TemplateRoot`), which keeps its
  own `activeSelf` at `true` (so a real spawn -- created parentless, at the world root --
  activates and runs its `Awake` immediately and correctly) while still preventing it
  from acting like a real entity as long as it sits there.
