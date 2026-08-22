# FedoGuardian

*By Fedo*

Summon a loyal guard to protect your base — dress it in your spare gear, and it'll fight
anything hostile that comes near.

## How it works

1. Craft/obtain the summoning wand (**Enslavement Wand** by default) and use it in front of you
   to summon a guard a couple meters ahead.
2. The guard stands its ground and engages any hostile creature that comes within its detection
   range, then returns to its post once the fight is over.
3. Right-click the guard to dress it in whatever gear you're currently wearing (armor + weapon),
   transferred in one go. Alt + right-click does the reverse: it gives back everything it's
   wearing.
4. The guard is persistent — it survives disconnects, zone unloads, and server restarts, keeping
   its position, equipment, and owner.

## Configuration

Settings live in `BepInEx/config/fedo.guardian.cfg`.

**[Guardian]**
- `DetectionRange` — distance (meters) within which the guardian notices and engages hostile
  creatures (default `15`).
- `GuardianNameTemplate` — display name shown when hovering over the guardian (default
  `Guardian`).
- `HoverHintText` — hint shown under the guardian's name when hovering over it.

**[SummonWand]**
- `SummonWandSourceItem` — vanilla item prefab used as the wand's visual/base, placeholder until
  a custom model exists (default `Club`).
- `SummonWandName` — display name of the summoning wand (default `Enslavement Wand`).
- `SummonWandCooldownSeconds` — minimum delay between two summons (default `1.5`).
- `SummonDistance` — distance in front of the player at which the guardian is summoned (default
  `2`).

## Notes

- Only one guard can be equipped/stripped through the interact prompt at a time; summoning
  another does not despawn the previous one.
- The guardian is ignored by other hostile creatures only if it successfully engages them first
  through its own detection range — it does not passively "tank" aggro from across the map.
