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
- Adapted to use [Jotunn](https://github.com/Valheim-Modding/Jotunn) (`PrefabManager`/
  `ItemManager`, new hard dependency — `[BepInDependency(Jotunn.Main.ModGuid)]`,
  `ValheimModding-Jotunn-2.29.1` added to `manifest.json`) instead of hand-rolled Harmony
  patches for registering the two custom prefabs: `CompanionPrefabPatch`/
  `SummonItemPrefabPatch` now clone `Greyling`/`SummonItemSourceItem` from
  `PrefabManager.OnVanillaPrefabsAvailable` and let Jotunn register them into
  `ZNetScene`/`ObjectDB` (including `ItemManager.AddItem`'s replay of `ObjectDB`'s internal
  by-hash/by-name/by-`SharedData` lookups) — the several self-repairing Postfix patches on
  `ZNetScene.Awake`/`GetPrefab`/`HasPrefab` and `ObjectDB.Awake`/`GetItemPrefab` (four
  overloads) are gone, along with `FedoKnorriPlugin.TemplateRoot` (Jotunn's own internal
  disabled container replaces it). Also switched off `ItemManager.OnVanillaItemsAvailable`
  once noticed it's marked `[Obsolete]` by Jotunn in favor of
  `PrefabManager.OnVanillaPrefabsAvailable` (used for both prefabs now).
- Renamed the summoning charm to **Knorri Seed** (`SummonItemName`, was `Greyling Charm`) with
  a matching description (`SummonItemDescription`, `A strange seed that summons a tame
  Greyling companion when used.`), and gave it a real custom inventory icon
  (`knorri_seed.jpg`, loaded via `AssetUtils.LoadSpriteFromFile`) instead of inheriting
  `SummonItemSourceItem`'s vanilla one — only the icon changes, the in-world/in-hand 3D model
  is still the vanilla source item until a real model exists.
- Fixed: the icon file was originally named `knorri_seed.jpeg` — `AssetUtils.LoadSpriteFromFile`
  checks the file *extension*, not its content, and threw (`LoadTexture can only load png or
  jpg textures`) for anything other than `.png`/`.jpg`. The exception wasn't caught inside icon
  loading, so it aborted `CreateItem` entirely (caught by its outer try/catch) — no
  `Fedo_KnorriCharm` at all got registered, not just a missing icon (found in testing).
  Renamed the file to `.jpg` and gave `LoadIcon` its own try/catch, so a future icon problem
  can only ever cost the icon, never the item itself.
- Added a small looping purple particle effect (`SummonItemSparkleEffect`, code-generated,
  same technique as `CompanionPoofEffect` but looping instead of a one-shot burst) attached
  once to the seed's prefab template, so every real-world instance of it (dropped on the
  ground, etc.) shows it automatically — no effect while it's just sitting in an inventory
  slot.
- Fixed: the cooldown overlay (darkened icon + countdown) only ever applied to the inventory
  grid — the seed still showed as available in the hotbar for the whole cooldown (found in
  testing). Added `SummonItemHotkeyBarCooldownPatch` (`HotkeyBar.UpdateIcons`), the hotbar's
  own separate vanilla UI system with its own private `HotkeyBar.ElementData` nested class,
  reconstructed the same way as the existing inventory-grid patch.
- Each seed now binds to the first player who successfully uses it
  (`SummonItemOwnershipPatch`) — anyone else trying to use that exact seed afterwards is
  blocked (`SummonItemNotOwnerMessage`) instead of summoning/storing away the owner's
  companion. Reuses `ItemDrop.ItemData.m_crafterID`/`m_crafterName` (vanilla "crafted by"
  fields, already saved per item instance for free) rather than a made-up custom-data
  mechanism, since this seed is never actually craftable. Shown in the tooltip
  (`SummonItemOwnerLabel`, default `Belongs to: {0}`) via a Postfix on the static
  `ItemDrop.ItemData.GetTooltip(...)` overload.
- Fixed: `IsSummonItem` compared the item's display name against the live `SummonItemName`
  config value, which reloads without a server restart — renaming the item while the server
  was running silently broke every seed already in the world (cooldown, use-toggle, ownership
  lock, all stopped recognizing them, with no error). First changed to compare by
  `ItemData.m_shared` object identity instead — but that broke something worse in testing: a
  seed obtained via Easy Spawner (or `additem`) didn't share the same `SharedData` instance
  created in `CreateItem`, so the comparison always failed, the seed fell through to real
  vanilla `Consumable` handling, and got silently *eaten* on use without ever summoning the
  companion. Settled on caching the display name itself (a plain string) at prefab creation
  time instead — stable across however the item was obtained, still immune to a later `.cfg`
  rename.
- Fixed: the summon cooldown (`SummonItemUsePatch.LastUse`) was a plain `Dictionary<Humanoid,
  float>` that grew by one entry per player who ever used the seed, never cleared on
  disconnect — harmless in practice but a real (if tiny) memory leak on a long-running
  server with a lot of player turnover. Switched to a `ConditionalWeakTable`, whose entries
  are collected automatically once the corresponding `Humanoid` is (shortly after a
  disconnect, once nothing else references it).
- The companion now despawns the moment its owner disconnects
  (`CompanionDisconnectDespawnPatch`), instead of sitting frozen in the world until they
  reconnect. Two hooks: `ZNet.Disconnect` (a remote player's `ZNetPeer` leaving) and
  `ZNet.OnDestroy` (the host's own session ending — the host has no `ZNetPeer` of their own to
  catch via the first hook). The existing ~2s ownership-reclaim loop in `CompanionAI` stays as
  a fallback for whatever slips past both (a crash, a timeout instead of a clean disconnect).
- Fixed: `ParticleSystem.playOnAwake` alone wasn't reliably restarting the seed's sparkle loop
  once its GameObject actually went active (added by code to a template that starts out
  inactive under Jotunn's prefab container). Added a small `ForcePlayOnEnable` component that
  calls `Play()` explicitly from `OnEnable` instead, which is guaranteed to fire every time the
  object activates. Also made the effect more visibly obvious (bigger particles, faster
  emission) and gave the seed's inherited vanilla mesh a purple tint (`ApplyPurpleTint`) to
  match the icon/particles.
- Fixed: that purple tint had no visible effect at all in testing — neither `_Color` nor
  `_BaseColor` (tried first) exist on Valheim's own item shaders. `ApplyPurpleTint` now asks
  the shader itself for every property of type Color (`Shader.GetPropertyCount`/
  `GetPropertyType`) and tints all of them, skipping anything with "Emission" in its name to
  avoid an unwanted glow.
- Fixed: the hotbar cooldown countdown would stop decrementing at `1` and stay stuck there,
  icon still greyed, once the cooldown actually ended — moving the seed into the inventory grid
  fixed it instantly (found in testing). Cause: unlike `InventoryGrid.UpdateGui` (which rewrites
  icon color and amount text unconditionally every frame), `HotkeyBar.UpdateIcons` never touches
  icon color at all, and only rewrites the amount text if the item's stack count changed since
  last frame (an internal `m_stackText` cache) — a single seed's stack count never changes, so
  nothing in vanilla ever had a reason to undo our last cooldown-digit write.
  `SummonItemHotkeyBarCooldownPatch` now explicitly restores `icon.color` to white and resets
  `m_stackText` to `-1` once the cooldown ends, instead of just stopping and assuming vanilla
  would clean up after itself.
- Fixed: the companion's heal orb could launch up to ~1.5s later than the actual throw motion
  it's supposed to follow. `LaunchHealOrbAfterThrow` read `GetCurrentAnimatorStateInfo`
  right after a single `yield return null` following `SetTrigger` — but while the Animator is
  still transitioning into the "throw" state (any nonzero transition duration on the
  Controller), that call keeps reporting the *previous* state (often a much longer idle loop),
  so the coroutine waited for the wrong clip's length. First fixed by waiting out
  `IsInTransition(0)` before reading the clip length, then sleeping for that full length — cut
  the desync down but still landed the orb ~0.5s late, because a crossfade plays the
  destination clip *during* the transition (its internal clock is already partway through by
  the time the transition ends), so adding "transition time" and "full clip length" separately
  double-counted that overlap. Then tried polling `AnimatorStateInfo.normalizedTime >= 1f`
  every frame instead of computing a wait duration up front — landed *later* still: the
  "throw" state apparently has its own exit transition back to idle configured before the
  clip's natural end, re-triggering `IsInTransition` a second time and requiring a wait through
  that too. Gave up deriving the delay from the Animator's real transition/state timing after
  three different attempts landed on three different desyncs — `LaunchHealOrbAfterThrow` now
  just waits a plain configurable `HealThrowDelaySeconds` (default `0.8`, tuned by eye in game
  during testing, new `[Healing]` setting) instead, tunable further with no rebuild needed.
- Removed the `Shader.Find`/`new Material` call from `SummonItemSparkleEffect` — found
  `"Desired shader compiler platform ... is not available in shader blob"` in the log,
  meaning a shader can resolve as a non-null object without an actually usable compiled
  variant for this build. The particle renderer now keeps whatever default material Unity
  assigns it (an engine resource, never subject to a project's shader stripping), tinted the
  same way via `colorOverLifetime`. Also added `LogInfo` diagnostics to both this and
  `ApplyPurpleTint` (renderer count, which shader property got tinted and to what value) to
  make the next round of in-game testing conclusive instead of another guess.