# FedoDeath

*By Fedo*

Death shouldn't be free. Instead of your tombstone appearing the instant you die, a hostile guardian spawns where you fell and stands over your loot. Kill it to reveal your grave.

## How it works

1. You die. Your tombstone is *not* created yet — instead, a guardian creature (a Skeleton by default) spawns exactly where your grave would have been.
2. The guardian stays completely still (AI disabled) until a player comes within `ActivationRange` (20m by default) -- so it can't wander off and get tangled up with other creatures. Once awake, it hunts you specifically. It's also assigned to the game's `Boss` faction, which in vanilla Valheim is allied with every other faction except players -- so no other mob will ever fight it (or get attacked by it). It does *not* get a boss health bar or boss music; that's a separate flag the mod leaves untouched.
3. Kill the guardian, and your tombstone (with everything it was holding) appears where it fell.

If you die with an empty inventory, no guardian spawns — there's nothing to protect.

The guardian's loot and owner info are stored in its own persistent world data (its ZDO), not just in memory -- so it survives disconnecting/reconnecting, zone unload/reload, or a server restart. Whenever you come back and finish it off, your grave still appears correctly.

A map pin tracks the guardian and follows it if it moves while hunting you, so you never lose track of where your loot is. It's a normal, removable pin, unlike the game's own death marker (which this mod replaces, since it wouldn't be updated while the guardian moves).

## Configuration

Settings live in `BepInEx/config/fedo.death.cfg`.

**[Guardian]**
- `CreaturePrefab` — the creature spawned as guardian (default `Skeleton`). Must be a valid Valheim prefab name (e.g. `Skeleton`, `Wraith`, `Troll`, `Draugr`...).
- `GuardianNameTemplate` — display name given to the guardian (default `Dead {player}`). `{player}` is replaced with the dead player's name.
- `ActivationRange` — distance in meters a player must approach before the guardian wakes up (default `20`).
- `ShowMessages` / `SpawnMessageText` / `DefeatMessageText` — on-screen messages when the guardian appears and when it's defeated.

## Notes

- Only affects your own death/tombstone flow; it doesn't change how other players' graves work unless they also have the mod installed.
- If the guardian somehow can't be spawned (invalid prefab name), the tombstone is created immediately instead, as a fallback.
