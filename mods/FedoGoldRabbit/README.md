# FedoGoldRabbit

*By Fedo*

A Diablo-style "treasure goblin" for Valheim — except it's a rabbit, and it's late for a very important date.

## How it works

The Gold Rabbit is a rare variant of the vanilla Hare, spawned by its own entry in the world's ambient spawn table — so it can show up in *any* biome, not just wherever normal Hares roam.

1. As soon as it notices a player and bolts, it shouts an excuse straight out of Alice in Wonderland ("En retard, en retard, j'ai un rendez-vous très important !") in a speech bubble above its head.
2. While it's alive and fleeing, it drops a few coins every 2-3 seconds.
3. Kill it, and it pays out a big pile of coins instead of its usual meat/pelt.
4. If nobody catches it within 30 seconds, it despawns in a puff of smoke like any dying creature — no loot dropped.

It's still a Hare under the hood: same model, same flee-on-sight behavior as vanilla. Only its name, loot table, and lifespan are different.

### Testing it on demand

The mod registers a dedicated prefab, `Fedo_GoldRabbit`, in `ZNetScene` — it's always a Gold Rabbit (no dice roll), so you can spawn it directly:

- Console: `spawn Fedo_GoldRabbit`
- Or search for it in a prefab-listing mod like Easy Spawner.

## Configuration

Settings live in `BepInEx/config/fedo.goldrabbit.cfg`.

**[GoldRabbit]**
- `GoldenName` — display name given to the Gold Rabbit (default `Lièvre Doré`).
- `SpawnMaxPerZone` / `SpawnIntervalSeconds` / `SpawnChancePercent` — control how rare it is in the ambient world spawn table.
- `CoinPrefabName` — item prefab used as currency (default `Coins`).
- `CoinDropIntervalMin` / `CoinDropIntervalMax` — delay range (seconds) between coin drops while alive.
- `CoinDropAmountMin` / `CoinDropAmountMax` — coins dropped per tick while alive (default `1`-`3`).
- `DeathCoinAmountMin` / `DeathCoinAmountMax` — coins dropped on death (default `75`-`200`).
- `LifetimeSeconds` — time before an uncaught Gold Rabbit despawns with no loot (default `30`).
- `FleeShoutText` / `FleeShoutCooldown` — the speech bubble text and its minimum repeat delay.
- `ShowSpawnMessage` / `SpawnMessageText` — on-screen notification (with the vanilla message chime) when one appears.

## Notes

- The periodic coin-drop sound reuses whatever audio clip is already attached to the `Coins` prefab, so no custom sound assets are bundled with the mod.
- Only affects Hare's golden variant; ordinary Hares are untouched.
