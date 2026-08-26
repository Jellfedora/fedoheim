# Changelog

## 1.0.0

- Initial release: summoning charm (clone of a vanilla item, `TrophyGreydwarf` by default —
  Greylings don't drop their own trophy) spawns a tame Greyling companion (clone of the vanilla
  `Greyling` prefab, its own MonsterAI swapped for a custom follow/heal/pickup AI).
- Companion follows its owner (walk/run/teleport depending on distance, never teleporting
  while the owner is airborne), heals them periodically (`HealAmount` `15`,
  `HealCooldownSeconds` `10`) when hurt and in range by launching a small glowing orb their
  way (`CompanionHealOrb`, a code-generated sphere, `GameObject.CreatePrimitive`), turning to
  face its owner and playing its real vanilla throw animation first
  (`Animator.SetTrigger("throw")`, the exact parameter name confirmed in-game — the
  companion's cloned `Character` turns out to really be a `Humanoid`, same as the vanilla
  Greydwarf/Greyling rock-throw). Deliberately only the animation:
  `Humanoid.StartAttack` (the real vanilla attack call) was avoided since it would go through
  the actual damage pipeline, risking the companion hurting its own owner for what's supposed
  to be a harmless heal gesture. Fixed: the orb was launching at the same time as the
  animation instead of waiting for it to finish, looking desynced — `LaunchHealOrbAfterThrow`
  (a coroutine) now reads the real clip length off the Animator's current state and waits for
  it before launching, rather than a guessed delay. Followed on arrival by a green particle
  burst on the owner
  (`CompanionPoofEffect`, generalized to take a color instead of only the grey spawn/despawn
  smoke) and, if `healing.mp3` is provided (optional, dropped next to the DLL — silent otherwise,
  particles only), a matching impact sound. Also runs over to pick up nearby ground items
  into their inventory — picking up Coins specifically plays a chime (`shiny.mp3`, bundled
  next to the DLL, loaded via `UnityWebRequestMultimedia` like `FedoGoldRabbit`'s spawn
  sound, throttled by `CoinPickupSoundCooldownSeconds` so grabbing several coin stacks in a
  row doesn't spam it) — detected by comparing the picked-up item's prefab hash against
  `"Coins"` (`ZNetView.GetPrefabName()` being private).
- Companion is scaled down (`CompanionScale`, `0.7` by default) relative to a vanilla Greyling.
- One companion per player: the charm toggles it (summon/store away, each with a little poof
  of smoke — `CompanionPoofEffect`, a code-generated particle burst reusing `FedoGoldRabbit`'s
  despawn-smoke technique, no sound attached yet) instead of spawning a new one on every use.
  Found by scanning currently-loaded `Character`s for one carrying a `CompanionAI` whose
  stored owner ZDO field matches the player's stable `Player.GetPlayerID()` — not a pointer
  kept on the player themselves: `Player.m_customData` was tried first, but didn't reliably
  survive a reconnect in testing (the companion was still there, un-frozen, yet undetected,
  letting a second one be summoned). A plain `Update()` (which runs regardless of ZDO
  ownership, unlike `UpdateAI`) also reclaims ownership of the companion's own networked
  object on behalf of the local owning player if it ends up stuck on a disconnected peer —
  otherwise its AI would simply stop running (`BaseAI.UpdateAI` only executes for whichever
  peer owns the ZDO) and it would sit frozen in place after a reconnect.
- Companion is faction `Boss` and fully invulnerable (`Character.Damage` blocked outright,
  including from its own owner) — no combat AI, can't fight and can't be hurt. Marked tamed
  (`Character.SetTamed(true)`, applied per-instance at spawn, not on the template which has
  no valid ZNetView yet) so its health bar shows green like a tamed animal instead of red —
  `Faction.Boss` alone reads as an enemy to the player (see `mods/CLAUDE.md`: allied to
  everything except players). Fixed: `Faction.Boss` alone didn't reliably stop wild creatures
  from still attacking it in testing (harmless — it can't be damaged — but not meant to
  happen). `BaseAI.IsEnemy` (both overloads) is now patched directly (`CompanionNeverEnemyPatch`)
  so no AI ever considers it a valid enemy, regardless of the exact reason the faction check
  alone wasn't enough.
- Companion shows one of two random lines above its head (`PickupPhrase1`/`PickupPhrase2`,
  throttled by `PickupChatCooldownSeconds` so it doesn't comment on every single item) when it
  spots an item to fetch — via a small local `FloatingSpeechBubble` (3D `TextMeshPro`, no
  network RPC involved), not vanilla's `Talker.Say()`, which crashed
  (`NullReferenceException` in `Character.GetHeadPoint()`, on `m_head`, a field never set up
  for a Greyling that doesn't talk natively) once the message came back through its RPC
  round-trip. Fixed: the bubble showed nothing at all at first — a `TextMeshPro` created at
  runtime (not from an editor-configured scene/prefab) has no font assigned by default, same
  pitfall already hit and documented in `FedoServerTools.LoadingOverlay`; now borrows one from
  `Hud.instance.m_hoverName.font` instead of shipping a font asset.
- Rename support (Shift+E, `CompanionInteract` implementing `Interactable`/`TextReceiver`)
  with a hover hint. Persisted in two places: the companion's own ZDO (survives a reload) and
  the owner's `Player.m_customData` (survives storing the companion away and summoning a new
  one, since that destroys its ZDO — the copy on the companion alone isn't enough, a fresh
  spawn applies the saved name *before* `SetTamed(true)`, see below). Two separate display
  caches had to be defeated to make a rename actually show up:
  - `Character.GetHoverName()`/`GetHoverText()` are fully replaced (Harmony Prefix, not just
    appended to) so the crosshair tooltip always reflects the live `m_name`.
  - `EnemyHud` — the floating name+health-bar label shown above a nearby creature — is a
    *separate* system from the above, caching the name once per character in a dictionary the
    game itself never refreshes. `EnemyHud.RemoveCharacterHud` is called right after a rename
    to force it to rebuild that entry (with the new name) next time the companion is visible.
    Applying a saved name at spawn also has to happen *before* `SetTamed(true)`, not after —
    `SetTamed` is what seems to trigger `EnemyHud` to register the character in the first
    place, so doing it in the other order baked the old/default name into that cache
    immediately at spawn.
- `SummonCooldownSeconds` (default `3`) is now shown visually
  (`SummonItemCooldownOverlayPatch`): the charm's icon darkens with a countdown number in the
  inventory grid while on cooldown, reconstructed from scratch since vanilla has no generic
  per-item cooldown UI (checked: no such field on `ItemDrop.ItemData`/`HotkeyBar.ElementData`/
  `InventoryGrid.Element`) — a Harmony Postfix on `InventoryGrid.UpdateGui` reaching into that
  private nested `Element` class's icon/amount fields through cached reflection. (Was briefly
  lowered to `0.5` when clicking twice to toggle felt unresponsive with no visual feedback;
  restored to `3` now that the cooldown is visible.)
- Settings synced server-wide via ServerSync (`mods/_shared/ConfigSync.cs`).
- Custom prefabs registered as `Fedo_Knorri` / `Fedo_KnorriCharm` (`Fedo_` prefix, same
  convention as `Fedo_GoldRabbit`) — the charm's registration retries from `ZNetScene.Awake` in
  addition to `ObjectDB.Awake`, since the latter alone can run once at the main menu (before
  `ZNetScene.instance` exists) and cache a template that never made it into
  `ZNetScene.m_prefabs`, leaving it invisible to spawners like Easy Spawner.
- Renamed the mod from `FedoCompanion` to `FedoKnorri` (plugin GUID `fedo.knorri`, custom
  prefabs, `.cfg` filename all follow) — `Knorri` is now the companion's default name
  (`CompanionName`), instead of the generic `Companion`.
- Added `SummonItemDescription`, making the charm's tooltip text configurable instead of
  hardcoded (default `Summons a tame Greyling companion when used.`).
