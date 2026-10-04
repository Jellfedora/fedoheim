# FedoKnorri

*By Fedo*

Use a seed to summon a tame Greyling companion that follows you around, heals you when
you're hurt, and picks up nearby loot straight into your inventory.

**Requires [Jotunn](https://github.com/Valheim-Modding/Jotunn)** (Thunderstore dependency, see
`manifest.json`) — used to register the companion/seed as custom prefabs, see Notes below.

## How it works

1. Obtain the summoning item (**Knorri Seed** by default) and click "Use" on it in your
   inventory to summon a companion a couple meters ahead (with a little poof of smoke), or
   store it away with another click if one's already out — one companion per player at a
   time, also with a poof. The first player to use a given seed becomes its owner (shown in
   its tooltip from then on) — no one else can use that exact seed afterwards.
2. The companion follows you, keeping a short distance — it walks normally, but runs to catch
   up if you get too far ahead, and teleports next to you if it falls way behind (only while
   you're standing on solid ground — it won't teleport to you mid-fall or mid-jump).
3. Whenever your health isn't full (and it's close enough) it periodically heals you by
   launching a small glowing orb your way — a little burst of green particles plays when it
   lands. Since it isn't invulnerable anymore, it can end up hurt too, and it'll pick whoever
   needs it more (by health percentage, not raw missing HP) — including itself.
4. It also notices nearby items lying on the ground, runs over to them (showing one of two
   random lines above its head, at most once every `PickupChatCooldownSeconds`), and adds
   them straight to your inventory — no need to walk over them yourself. Picking up Coins
   specifically plays a little chime.
5. The companion is pacifist and never fights back, but it isn't invulnerable — it has real
   health (`CompanionMaxHealth`/`ShamanCompanionMaxHealth`) and wild creatures will actually
   target and attack it, though noticeably less often than you (`CompanionAggroChance`). It's
   still fully safe from players, including its own owner (an accidental or deliberate hit
   never lands). Its health bar shows green, like a tamed animal, not red. If it's killed, both
   seeds go on a `DeathCooldownSeconds` cooldown (default `120`) shown the same way as the
   normal one-summon-at-a-time cooldown — darkened icon, countdown.
6. Hover over it and press Shift+E to rename it — the new name persists across reloads.
7. Hover over it and press E (no modifier) to switch its behavior mode, shown next to its name:
   **Defensive** (default) heals/follows/picks up loot and tries to step away from anything
   currently targeting it; **Aggressive** does all of that too, but also actually fights back
   against whatever targets you or it, within `CombatEngageRange`. Both settings persist the
   same way as its name.

## Evolution

Craft a **Shaman Seed** at a Workbench (base seed + `ShamanRecipeHoneyAmount` Honey, see
Configuration) and use it: your existing companion is replaced on the spot by an evolved
Greydwarf Shaman — same follow/pickup behavior, but a much stronger heal
(`ShamanHealAmount`/`ShamanHealCooldownSeconds`, `40` HP every `15`s by default vs the base
seed's `15` HP every `10`s). If you'd already renamed your companion, the Shaman keeps that
same name — it's meant to be the same companion, evolved, not a second one. Using the base seed
again once you have a Shaman doesn't undo the evolution — it shows `CannotDowngradeMessage`
and leaves the Shaman as-is. Using the Shaman Seed again while the Shaman is already out stores
it away, same toggle as the base seed. See Notes for how this is implemented (one shared
`CompanionAI` script for both tiers, distinguished only by which prefab it's attached to).

## Configuration

Settings live in `BepInEx/config/fedo.knorri.cfg`.

**[Companion]**
- `CompanionName` — display name shown when hovering over the companion (default
  `Knorri`).
- `CompanionScale` — uniform scale applied to the companion's model, `1` being the size of a
  vanilla Greyling (default `0.7`).
- `FollowDistance` — distance (meters) the companion tries to keep from its owner (default
  `3`).
- `RunDistance` — distance (meters) beyond which the companion runs instead of walking to
  catch up (default `6`).
- `TeleportDistance` — distance (meters) beyond which the companion teleports next to its
  owner instead of pathing (default `25`); never triggers while the owner is airborne
  (falling/jumping) — it waits for them to land.
- `RenameHintText` — hover hint shown under the companion's name (default `[Shift+E]
  Rename`).
- `RenamePromptText` — title of the text box opened by Shift+E (default `Rename companion`).
- `BehaviorModeHintText` — hover hint telling players how to switch behavior mode (default
  `[E] Toggle Combat Mode`).
- `DefensiveModeLabel` / `AggressiveModeLabel` — labels shown next to the companion's name in
  its tooltip for each mode (defaults `Defensive` / `Aggressive`).
- `DefensiveModeMessage` / `AggressiveModeMessage` — lines the companion says when switched to
  each mode (defaults `Playing it safe now.` / `Ready to fight!`).
- `CombatEngageRange` — distance (meters) within which the companion looks for a wild creature
  currently targeting the owner or itself, used by both modes (default `12`).
- `CombatAttackCooldownSeconds` — minimum delay between two attacks in Aggressive mode (default
  `2`).
- `ShamanCompanionScale` — uniform scale applied to the evolved companion's model, `1` being the
  size of a vanilla Greydwarf Shaman (default `1`).
- `CompanionMaxHealth` / `ShamanCompanionMaxHealth` — maximum health of the base/evolved
  companion (defaults `50` / `150`) — no longer invulnerable, see How it works above.
- `CompanionAggroChance` — chance (`0`-`1`, default `0.4`) that the companion counts as a valid
  target when a wild creature without a target yet is looking for one; the owner is always a
  100% valid candidate, so this makes the companion draw noticeably less aggro without ever
  making it fully untargetable. Never applies once a creature already has a target locked, so
  it can't cause a monster to lose track of the companion mid-fight.

**[Healing]**
- `HealAmount` — health points restored per heal (default `15`).
- `HealCooldownSeconds` — minimum delay between two heals (default `10`).
- `HealRange` — distance (meters) within which the companion can heal its owner (default `8`).
- `HealThrowDelaySeconds` — delay (seconds) between the throw animation starting and the heal
  orb actually launching (default `0.8`) — tune by eye in game until the orb leaves right at
  (or a touch before) the end of the throw motion.
- `HealImpactSoundVolume` / `HealImpactSoundMaxDistance` — loudness and audible range of the
  heal impact sound — defaults `1.5` / `20`. Optional: drop a file named `healing.mp3` next to
  the DLL (`BepInEx/plugins/FedoKnorri/`) to enable it; without it, the impact stays
  silent (particles only).
- `ShamanHealAmount` / `ShamanHealCooldownSeconds` — same as `HealAmount`/`HealCooldownSeconds`
  above, but for the evolved companion (defaults `40` / `15`). Everything else about healing
  (range, throw delay, impact sound) is shared between both tiers.

**[Pickup]**
- `PickupRange` — distance (meters) within which the companion notices ground items and walks
  over to pick them up (default `10`).
- `PickupIntervalSeconds` — how often it scans for a new item once it has none to fetch
  (default `0.3`).
- `PickupPhrase1` / `PickupPhrase2` — lines the companion may say (picked at random) when it
  spots an item to fetch (defaults `Ooh, shiny!` / `Look, something shiny!`).
- `PickupChatCooldownSeconds` — minimum delay (seconds) between two pickup lines, so it
  doesn't comment on every single item (default `20`).
- `CoinPickupSoundVolume` / `CoinPickupSoundMaxDistance` — loudness and audible range of the
  coin pickup chime (`shiny.mp3`, bundled next to the DLL) — defaults `1.5` / `20`.
- `CoinPickupSoundCooldownSeconds` — minimum delay (seconds) between two coin pickup chimes,
  so grabbing several coin stacks in a row doesn't spam it (default `20`).

**[SummonItem]**
- `SummonItemSourceItem` — vanilla item prefab used as the summoning item's base, placeholder
  until a custom model exists (default `TrophyGreydwarf` — Greylings don't drop their own
  trophy in vanilla, only the adult Greydwarf does). Still governs the in-world/in-hand 3D
  model, but no longer the inventory icon — see `knorri_seed.jpg` below.
- `SummonItemName` — display name of the summoning item (default `Knorri Seed`).
- `SummonItemDescription` — tooltip description of the summoning item (default `A strange
  seed that summons a tame Greyling companion when used.`).
- `SummonCooldownSeconds` — minimum delay between two summons/store-aways (default `3`).
  Shown visually — the item's icon in the inventory darkens with a countdown number while
  on cooldown.
- `SummonDistance` — distance in front of the player at which the companion is summoned
  (default `2`).
- `SummonItemOwnerLabel` — line appended to the seed's tooltip once it's bound to an owner,
  `{0}` replaced with their name (default `Belongs to: {0}`).
- `SummonItemNotOwnerMessage` — message shown to a player who tries to use a seed already
  bound to someone else (default `This seed doesn't answer to you.`).
- `CannotDowngradeMessage` — message shown when using the base seed while an evolved (Shaman)
  companion is already out (default `Your companion has already evolved beyond this seed.`).
- `DeathCooldownSeconds` — cooldown before either seed can be used again after the companion is
  killed (default `120`) — shown on the seed's icon the same way as the normal one-summon
  cooldown (darkened, countdown).

**[ShamanSummonItem]**
- `ShamanSummonItemSourceItem` — vanilla item prefab used as the evolved item's base (default
  `AncientSeed`) — same role as `SummonItemSourceItem` above.
- `ShamanSummonItemName` — display name of the evolved summoning item (default `Shaman Seed`).
- `ShamanSummonItemDescription` — tooltip description of the evolved summoning item.
- `ShamanRecipeHoneyItem` — vanilla item prefab required (alongside the base seed) to craft the
  evolved item (default `Honey`).
- `ShamanRecipeHoneyAmount` — how many of `ShamanRecipeHoneyItem` the recipe requires (default
  `5`).
- `ShamanRecipeCraftingStation` — crafting station piece required (default `piece_workbench`,
  the base Workbench).
- `ShamanRecipeMinStationLevel` — minimum upgrade level of that station required (default `1`,
  no upgrades needed).

## Notes

- The heal orb (`CompanionHealOrb`) is a code-generated sphere (`GameObject.CreatePrimitive`,
  no game asset) that flies from the companion to its owner over ~0.6s before the heal is
  actually applied. The companion turns to face its owner and plays its real vanilla throw
  animation first (`Animator.SetTrigger("throw")`, the exact parameter name confirmed in-game
  — turns out the companion's cloned `Character` really is a `Humanoid`, same as the vanilla
  Greydwarf/Greyling rock-throw). Deliberately only the animation, though:
  `Humanoid.StartAttack` (the real vanilla attack call) was avoided since it would go through
  the actual damage pipeline — a real risk of the companion hurting its own owner (or anyone
  else) for what's supposed to be a harmless heal gesture. The orb launches
  `HealThrowDelaySeconds` (default `0.8`, tuned by eye in game) after the throw animation starts
  (`LaunchHealOrbAfterThrow`) — a plain fixed, tunable delay, after three separate attempts at
  reading the Animator's real transition/state timing to derive it automatically each landed on
  a *different* visible desync instead: reading the current state's clip length right after a
  single frame's wait picked up the *previous* state's length (often idle's, a much longer
  loop) for as long as the Controller was still transitioning into "throw", launching the orb
  up to ~1.5s late; waiting out that transition (`Animator.IsInTransition(0)`) first, then
  sleeping for the *full* clip length, still landed ~0.5s late, since a crossfade already plays
  the destination clip *during* the transition — its internal clock had advanced by the time
  the transition ended, so adding the full clip length on top counted that overlap twice;
  polling `AnimatorStateInfo.normalizedTime >= 1f` every frame landed *later* still, apparently
  because the "throw" state has its own exit transition back to idle configured before the clip
  actually ends, re-triggering `IsInTransition` a second time. Inspecting the real
  AnimatorController's transition graph to get this exactly right would mean decompiling a
  serialized Unity asset, not just the assembly — out of reach of the `MetadataLoadContext`
  technique used elsewhere in this repo (see `mods/CLAUDE.md`). On arrival, it triggers a green
  particle burst on the owner (reusing `CompanionPoofEffect`, generalized to accept a color
  instead of only the grey spawn/despawn smoke) and, if `healing.mp3` has been provided, a
  matching sound.
- One companion per player: the seed toggles it (summon if none exists, store away — despawn
  — if one already does). Found by scanning currently-loaded `Character`s for one carrying a
  `CompanionAI` whose stored owner matches the player's stable `Player.GetPlayerID()` — not a
  pointer kept on the player themselves (tried first via `Player.m_customData`, but that
  didn't reliably survive a reconnect in testing: the companion was still there, un-frozen,
  yet undetected, letting a second one be summoned). If the companion is left behind in an
  unloaded area (e.g. after a portal jump) it won't be found either way, and using the seed
  again will summon a second, orphaned one — known limitation, not solved here.
- The companion despawns (poof, like storing it away manually) the moment its owner
  disconnects (`CompanionDisconnectDespawnPatch`) — a remote player's `ZNetPeer` leaving
  (`ZNet.Disconnect`) or the host's own session ending (`ZNet.OnDestroy`, since the host has
  no `ZNetPeer` representing themselves — see `mods/CLAUDE.md`, "PeerSteamId.Resolve"). It no
  longer sits around in the world after someone logs off. As a safety net for whatever slips
  past those two hooks (a crash, a connection timeout instead of a clean disconnect...),
  ownership of the companion's own networked object is still re-claimed automatically (checked
  every ~2s) if it ever ends up stuck on a disconnected peer — otherwise its AI would simply
  stop running (`BaseAI.UpdateAI` only executes for whichever peer owns the ZDO) and it would
  sit frozen in place instead of resuming.
- The companion has no inventory of its own — picked-up items go straight into its owner's
  inventory (via the same code path a player walking over an item uses).
- Faction `Players` (`CompanionPrefabPatch`/`ShamanCompanionPrefabPatch`), not `Boss` — wild
  creatures read that faction as hostile to them, exactly like a real player, so they'll
  actually target and attack the companion without needing any `BaseAI.IsEnemy` patching (an
  earlier version used `Boss` + such a patch specifically to make sure *nothing* could ever
  target it; both are gone now that the opposite is wanted). `Character.Damage` is still
  patched (`CompanionInvulnerabilityPatch`), but only to block hits whose `HitData.GetAttacker()`
  is a `Player` — a wild creature's hit goes through normally. The companion has no attack
  logic at all (see `CompanionAI`), so "can be hurt" never turns into "can fight" — it just
  takes damage and, if its health reaches zero, dies for real (`Character.m_onDeath`, see the
  death-cooldown note below).
- On death, `Character.m_onDeath` (a public `Action` field, not a Harmony patch) triggers
  `SummonItemUsePatch.ApplyDeathCooldown` — subscribed once per spawn in `CompanionSpawner`,
  closing over the owner's stable `PlayerID` rather than the `Player`/`Humanoid` object itself.
  This starts a `DeathCooldownSeconds` cooldown that blocks *both* seeds (checked before the
  normal per-click cooldown in `ShouldSummon`) and is shown through the same icon-darkening
  overlay as that normal cooldown (`GetRemainingCooldown` returns whichever of the two is
  larger) — no separate UI needed. Keyed by `PlayerID` in a plain `Dictionary`, not the
  `ConditionalWeakTable` used for the per-click cooldown: this one is meant to survive a
  disconnect/reconnect (it's a penalty, not just an anti-spam-click debounce), and a `long` key
  isn't an object reference to worry about leaking in the first place.
- The seed and the companion are registered as real prefabs (`Fedo_KnorriCharm` /
  `Fedo_Knorri`, same `Fedo_` naming convention as `Fedo_GoldRabbit`) via Jotunn's
  `PrefabManager`/`ItemManager` (`CompanionPrefabPatch`/`SummonItemPrefabPatch`) rather than
  hand-rolled Harmony patches — Jotunn takes care of registering the clones into
  `ZNetScene`/`ObjectDB` at the right point of the loading sequence (main menu and real game
  load alike), including their internal by-hash/by-name/by-`SharedData` lookup dictionaries,
  which is exactly what used to need several self-repairing Postfix patches to get right by
  hand (see `mods/CLAUDE.md`, "Notes techniques de modding", for that older technique — still
  used elsewhere in this repo for mods that don't depend on Jotunn). An admin can give
  themselves the seed directly via the debug console (`additem Fedo_KnorriCharm 1`) instead
  of hunting for a vanilla item name.
- The seed's inventory icon is `knorri_seed.jpg`, loaded via Jotunn's
  `AssetUtils.LoadSpriteFromFile` (deployed next to the DLL by the `.csproj`, same mechanism
  as `shiny.mp3`/`healing.mp3`). **Must be `.jpg` or `.png`** — `LoadSpriteFromFile` checks the
  file *extension*, not its actual content, and throws (`LoadTexture can only load png or jpg
  textures`) for anything else, including `.jpeg` (found in testing: the exception wasn't
  caught inside icon loading itself, so it aborted the *entire* item creation — no
  `Fedo_KnorriCharm` at all, not just a missing icon; `LoadIcon` now has its own try/catch so a
  future icon problem only loses the icon, never the item). Only the icon is custom — the
  in-world/in-hand 3D model still comes from `SummonItemSourceItem` (`TrophyGreydwarf` by
  default) until a real model exists. If the file is missing, the seed silently falls back to
  that vanilla item's own icon (warning logged, not an error).
- The seed gives off a small loop of purple particles (`SummonItemSparkleEffect`, code-generated,
  no game asset — same technique as `CompanionPoofEffect` but looping instead of a one-shot
  burst) wherever it exists as a real object in the world (dropped on the ground, etc.) —
  attached once to the prefab template itself rather than spawned per-instance, since Unity
  copies child GameObjects on every `Instantiate()`. No effect at all while the seed just sits
  in an inventory slot, since only `ItemDrop.ItemData` (plain data, no live GameObject)
  represents it there. `ParticleSystem.playOnAwake` alone wasn't reliable for a system added by
  code to an object that starts out inactive (the template, parked under Jotunn's disabled
  prefab container) — a small dedicated `ForcePlayOnEnable` component calls `Play()`
  explicitly every time the object becomes active, which `OnEnable` guarantees and a
  once-per-object-lifetime `Awake` doesn't as reliably for a runtime-added component. No custom
  material either: `Shader.Find` + `new Material(...)` looked fine at compile time but logged
  `"Desired shader compiler platform ... is not available in shader blob"` in testing — a
  shader can resolve as a real object without an actually usable compiled variant for this
  particular build. The particle renderer just keeps whatever default material Unity assigns a
  freshly created `ParticleSystemRenderer` (an engine resource, never subject to a project's
  own shader stripping), tinted the same way via `colorOverLifetime`.
- The seed's visible mesh (inherited from `SummonItemSourceItem`, still a vanilla model) is
  tinted purple (`ApplyPurpleTint`, blends toward a fixed purple) to look more like it belongs
  with the icon/particles, in lieu of a real custom model. Uses `Renderer.material` (not
  `.sharedMaterial`), which instances a per-renderer copy on first access — never recolors the
  shared vanilla asset used by the untouched source item elsewhere in the game. Neither `_Color`
  nor `_BaseColor` (the two usual suspects for a Standard/URP shader) turned out to have any
  effect on Valheim's own item shaders in testing, so instead of guessing a third name,
  `ApplyPurpleTint` asks the shader itself which properties are of type Color
  (`Shader.GetPropertyCount`/`GetPropertyType`, Unity's own shader reflection, not .NET
  reflection) and tints all of them — skipping anything with "Emission" in its name, which
  would otherwise risk turning into an unwanted glow instead of a plain recolor.
- Renaming (Shift+E) updates the name locally, on the companion's own persisted ZDO, and on
  the owner's `Player.m_customData` — the latter is what makes a custom name survive storing
  the companion away and summoning it again (its ZDO gets destroyed each time, so it alone
  can't remember anything across that). Unlike the one-companion-per-player pointer above,
  this hasn't shown the same reconnect issue in testing, but hasn't been stress-tested for it
  either — worth watching. It isn't broadcast live to other clients via RPC, so a remote
  player might see the old name until their client re-reads the object (e.g. on reload). Also
  forces `EnemyHud` to rebuild its floating name+health-bar entry for the companion right
  after a rename (`EnemyHud.RemoveCharacterHud`) — that label is cached once per character in
  a dictionary the game itself never refreshes, so without this the popup would show the new
  name while the label above its head kept showing the old one.
- Marked tamed (`Character.SetTamed(true)`) purely for the green health bar — it doesn't add
  a `Tameable` component, so there's no feeding/hunger/breeding behavior to worry about.
- The spawn/despawn poof (`CompanionPoofEffect`) is a code-generated particle burst, same
  technique as `FedoGoldRabbit`'s despawn smoke — no sound attached to it (yet).
- Coin detection (`CompanionAI.IsCoins`) compares the picked-up item's prefab hash against
  `"Coins"` (the vanilla currency prefab name, `ZNetView.GetPrefabName()` being private) —
  won't recognize a renamed/custom currency prefab from another mod.
- The cooldown visual darkens the seed's icon and shows a countdown number while it's on
  cooldown — vanilla has no generic per-item cooldown UI to hook into (checked: no such field
  on `ItemDrop.ItemData`, `HotkeyBar.ElementData`, or `InventoryGrid.Element`), so this
  reconstructs it via Harmony Postfixes reading/writing those private nested classes'
  icon/amount fields through cached reflection (`InventoryGrid.Element`/`HotkeyBar.ElementData`
  are themselves private nested classes, inaccessible any other way from outside the game's own
  assembly). Two separate patches are needed for two separate vanilla UI systems with no shared
  rendering: `SummonItemCooldownOverlayPatch` (`InventoryGrid.UpdateGui`) for the inventory
  grid, and `SummonItemHotkeyBarCooldownPatch` (`HotkeyBar.UpdateIcons`) for the hotbar —
  without the second one, the seed showed as available (no tint, no countdown) in the hotbar
  for the whole cooldown even though it was correctly greyed out in the inventory (found in
  testing). The two aren't just two copies of the same patch, either: `InventoryGrid.UpdateGui`
  rewrites its icon color and amount text unconditionally on every single call (confirmed by
  decompiling it), so once the cooldown patch stops touching an element there, vanilla itself
  puts it back to normal on the very next frame. `HotkeyBar.UpdateIcons` never touches icon
  color at all, and only rewrites the amount text if the item's real stack count changed since
  the last frame (an internal `m_stackText` cache) — since a single seed's stack count never
  changes, without `RestoreDefaultVisual` explicitly resetting `icon.color` to white and that
  cache field to `-1` once the cooldown ends, the icon stayed grey and the countdown number
  stuck on its last digit forever (found in testing) — nothing in vanilla ever had a reason to
  touch either one again on its own.
- Each seed binds to the first player who successfully uses it (`SummonItemOwnershipPatch`) —
  every later use by anyone else is blocked (`SummonItemUsePatch.ShouldSummon` checks this
  before even looking at the cooldown), with `SummonItemNotOwnerMessage` shown to them instead.
  Ownership reuses `ItemDrop.ItemData.m_crafterID`/`m_crafterName` — vanilla fields normally
  meant for "crafted by" attribution — rather than a made-up custom-data mechanism:
  `ItemDrop.ItemData` has no generic per-instance data bag to begin with (unlike
  `Player.m_customData`), and these two are already saved/reloaded automatically with each
  item instance for free, exactly the "who owns this one exemplar" semantics needed here. This
  seed is never actually craftable (see `SummonItemPrefabPatch`), so nothing else reads or
  writes them for it. Shown in the tooltip (`SummonItemOwnerLabel`) via a Harmony Postfix on
  the static `ItemDrop.ItemData.GetTooltip(...)` overload — the only place a per-exemplar text
  can go, since `m_shared.m_name`/`m_description` are one `SharedData` object shared by every
  seed in existence (see `SummonItemPrefabPatch`), not something that could hold one player's
  name without showing it on everyone else's seed too.
- `SummonItemPrefabPatch.IsSummonItem` (used by the cooldown overlay, the use-toggle, and the
  ownership lock) identifies a seed by its display name **cached once at prefab creation
  time** (`_createdWithName`), not by re-reading the live `SummonItemName.Value` config on
  every call. `.cfg` values reload live without a restart (see `mods/CLAUDE.md`), so comparing
  directly against `SummonItemName.Value` used to silently break every seed already in the
  world the moment an admin renamed the item while the server was running (`m_shared.m_name`
  itself only gets set once, at prefab creation). Comparing `ItemData.m_shared` by *object
  identity* was tried first as a fix for that same problem, but broke something worse in
  testing: an exemplar obtained via Easy Spawner (or `additem`) didn't share the same
  `SharedData` instance created in `CreateItem` — some part of the vanilla item pipeline
  apparently copies it by value rather than by reference — so the comparison always failed,
  the seed fell through to real vanilla `Consumable` handling, and got silently eaten without
  ever summoning the companion. A cached *string* has neither problem: stable across
  whichever way the item was obtained, immune to a later `.cfg` rename.
- Evolution (base seed → Shaman Seed → evolved companion) is built on a single shared
  `CompanionAI` script for both tiers (`CompanionTier` enum) rather than a second class: follow,
  pickup, invulnerability, renaming, one-companion-per-player, disconnect despawn — none of it
  needed to change, since it all keys off the *presence* of a `CompanionAI` component, never off
  which specific prefab it's attached to. Only healing (`CompanionAI.HealAmount`/
  `HealCooldownSeconds`, private properties despite the same names as the public config
  entries) actually branches on tier, read from `CompanionAI.GetTier` — the ZDO's own prefab
  hash compared against `CompanionPrefabPatch.PrefabHash`/`ShamanCompanionPrefabPatch.PrefabHash`,
  no separate persisted "tier" field needed. `SummonItemUsePatch.ShouldSummon` uses that same
  tier comparison to decide what a seed does when a companion already exists: same tier stores
  it away (the original toggle), a *higher* tier replaces it in place (poof, then
  `CompanionSpawner.Spawn` at the same spot — `ApplySavedName` inside that call is what carries
  a custom name across the evolution, since it's keyed by player, not by companion instance),
  and a *lower* tier is a no-op (`CannotDowngradeMessage`) rather than destroying the better
  companion the player already has.
- The evolution recipe (`ShamanSummonItemPrefabPatch.RegisterRecipe`, `ItemManager.AddRecipe`)
  needs the base seed (`SummonItemPrefabPatch.PrefabName`) to already be a resolvable item at
  the moment it registers, since it's one of the recipe's ingredients — guaranteed by
  subscribing `ShamanCompanionPrefabPatch`/`ShamanSummonItemPrefabPatch` to
  `PrefabManager.OnVanillaPrefabsAvailable` *after* the base-tier ones in
  `FedoKnorriPlugin.Awake` (a multicast delegate invokes subscribers in subscription order), so
  the base seed is always built earlier in the same event firing.
- `TryHeal` picks between healing the owner and healing itself by comparing
  `Character.GetHealthPercentage()`, not raw missing HP — otherwise the Shaman's much bigger
  health pool (`150` vs the base tier's `50`) would make it look "more hurt" than the owner from
  a small scratch, stealing heals it doesn't really need more urgently. `CompanionHealOrb.Launch`
  already took a generic `Character` target rather than specifically `Player` (nothing to change
  there), so the only real change was widening `LaunchHealOrbAfterThrow`'s parameter type to
  match.
- Valheim has no real threat/aggro table — wild AI just picks the closest/most perceptible
  candidate it considers an enemy (`BaseAI.FindEnemy`/`FindClosestEnemy`, filtered through
  `BaseAI.IsEnemy`). `CompanionAggroPatch` can't lower a "threat value" that doesn't exist, so
  instead it probabilistically vetoes the companion as a valid `IsEnemy` candidate
  (`CompanionAggroChance`) — but *only* while `!BaseAI.HaveTarget()`, i.e. only during a fresh
  target search. Once some creature has already locked onto the companion, this patch stops
  touching `IsEnemy` for it entirely, on purpose: re-rolling every tick while the companion is
  the ongoing target would make monsters randomly "forget" it mid-fight and look bugged, not
  merely less popular a target than the owner (who's always a 100% valid candidate either way).
- Behavior mode (`CompanionBehaviorMode`, Defensive/Aggressive) toggles on a plain E hover
  interaction (`CompanionInteract`) — Shift+E was already taken by renaming, so this reuses the
  *other* modifier state of the same `Interactable.Interact(user, hold, alt)` callback rather
  than needing a second interaction system. Persisted exactly like the custom name (own ZDO +
  a copy in `Player.m_customData`, reapplied by `CompanionAI.ApplySavedMode` right after
  `ApplySavedName` in `CompanionSpawner`), so it survives storing the companion away,
  reconnecting, and evolving into the Shaman tier.
  - **Aggressive mode** actually fights: `CompanionAI.TryFight` moves into `AttackRange` of a
    threat, then calls `Humanoid.StartAttack(target, secondaryAttack: false)` — the overload
    that takes an explicit target rather than swinging blindly through whatever's in front of
    it, which is what makes this safe to use at all (no risk of clipping the owner standing
    nearby, unlike a real weapon swing). The companion turns out to still have its innate
    unarmed weapon (`Humanoid.m_unarmedWeapon`) intact, since prefab creation only ever removed
    `MonsterAI`, never the rest of the source creature's setup.
  - Both modes share `CompanionAI.FindThreat`, which only ever considers a wild creature that is
    *already* targeting the owner or the companion (via `BaseAI.GetTargetCreature()`) — never
    "any hostile thing nearby" on its own, so it won't drag the companion into a fight (or make
    it flee) over something that was never actually a threat to begin with.
  - **Defensive mode**'s `TryEvade` only reacts to a creature targeting the companion
    specifically (not one only targeting the owner) and just walks it away from that creature's
    position for `EvadeDistance` — no pathfinding-aware "find safety" logic, just putting
    distance between itself and the immediate threat.
