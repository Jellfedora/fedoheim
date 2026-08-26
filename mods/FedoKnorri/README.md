# FedoKnorri

*By Fedo*

Use a charm to summon a tame Greyling companion that follows you around, heals you when
you're hurt, and picks up nearby loot straight into your inventory.

## How it works

1. Obtain the summoning charm (**Greyling Charm** by default) and click "Use" on it in your
   inventory to summon a companion a couple meters ahead (with a little poof of smoke), or
   store it away with another click if one's already out — one companion per player at a
   time, also with a poof.
2. The companion follows you, keeping a short distance — it walks normally, but runs to catch
   up if you get too far ahead, and teleports next to you if it falls way behind (only while
   you're standing on solid ground — it won't teleport to you mid-fall or mid-jump).
3. Whenever your health isn't full and the companion is close enough, it periodically heals
   you by launching a small glowing orb your way — a little burst of green particles plays on
   you when it lands.
4. It also notices nearby items lying on the ground, runs over to them (showing one of two
   random lines above its head, at most once every `PickupChatCooldownSeconds`), and adds
   them straight to your inventory — no need to walk over them yourself. Picking up Coins
   specifically plays a little chime.
5. The companion is pacifist and can't fight — no hostile creature will ever target it, and
   it can't be damaged at all, including by its own owner. Its health bar shows green, like a
   tamed animal, not red.
6. Hover over it and press Shift+E to rename it — the new name persists across reloads.

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

**[Healing]**
- `HealAmount` — health points restored per heal (default `15`).
- `HealCooldownSeconds` — minimum delay between two heals (default `10`).
- `HealRange` — distance (meters) within which the companion can heal its owner (default `8`).
- `HealImpactSoundVolume` / `HealImpactSoundMaxDistance` — loudness and audible range of the
  heal impact sound — defaults `1.5` / `20`. Optional: drop a file named `healing.mp3` next to
  the DLL (`BepInEx/plugins/FedoKnorri/`) to enable it; without it, the impact stays
  silent (particles only).

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
- `SummonItemSourceItem` — vanilla item prefab used as the charm's visual/base, placeholder
  until a custom model exists (default `TrophyGreydwarf` — Greylings don't drop their own
  trophy in vanilla, only the adult Greydwarf does).
- `SummonItemName` — display name of the summoning charm (default `Greyling Charm`).
- `SummonItemDescription` — tooltip description of the summoning charm (default `Summons a
  tame Greyling companion when used.`).
- `SummonCooldownSeconds` — minimum delay between two summons/store-aways (default `3`).
  Shown visually — the charm's icon in the inventory darkens with a countdown number while
  on cooldown.
- `SummonDistance` — distance in front of the player at which the companion is summoned
  (default `2`).

## Notes

- The heal orb (`CompanionHealOrb`) is a code-generated sphere (`GameObject.CreatePrimitive`,
  no game asset) that flies from the companion to its owner over ~0.6s before the heal is
  actually applied. The companion turns to face its owner and plays its real vanilla throw
  animation first (`Animator.SetTrigger("throw")`, the exact parameter name confirmed in-game
  — turns out the companion's cloned `Character` really is a `Humanoid`, same as the vanilla
  Greydwarf/Greyling rock-throw). Deliberately only the animation, though:
  `Humanoid.StartAttack` (the real vanilla attack call) was avoided since it would go through
  the actual damage pipeline — a real risk of the companion hurting its own owner (or anyone
  else) for what's supposed to be a harmless heal gesture. The orb only launches once that
  throw animation actually finishes (`LaunchHealOrbAfterThrow`, a coroutine reading the real
  clip length off the Animator's current state rather than a guessed delay — launching it
  right away looked desynced from the animation in testing). On arrival, it triggers a green
  particle burst on the owner (reusing `CompanionPoofEffect`, generalized to accept a color
  instead of only the grey spawn/despawn smoke) and, if `healing.mp3` has been provided, a
  matching sound.
- One companion per player: the charm toggles it (summon if none exists, store away — despawn
  — if one already does). Found by scanning currently-loaded `Character`s for one carrying a
  `CompanionAI` whose stored owner matches the player's stable `Player.GetPlayerID()` — not a
  pointer kept on the player themselves (tried first via `Player.m_customData`, but that
  didn't reliably survive a reconnect in testing: the companion was still there, un-frozen,
  yet undetected, letting a second one be summoned). If the companion is left behind in an
  unloaded area (e.g. after a portal jump) it won't be found either way, and using the charm
  again will summon a second, orphaned one — known limitation, not solved here.
- Ownership of the companion's own networked object is re-claimed automatically (checked every
  ~2s) if it ends up stuck on a disconnected peer — otherwise its AI would simply stop running
  (`BaseAI.UpdateAI` only executes for whichever peer owns the ZDO) and it would sit frozen in
  place after a reconnect.
- The companion has no inventory of its own — picked-up items go straight into its owner's
  inventory (via the same code path a player walking over an item uses).
- Completely invulnerable (`Character.Damage` is blocked outright for it), not just ignored by
  wildlife — a Boss faction alone wouldn't have stopped its own owner from hitting it.
- Never targeted by any monster's AI either: `Faction.Boss` alone didn't reliably stop wild
  creatures from still attacking it in testing (harmless since it can't be damaged, but not
  meant to happen at all) — `BaseAI.IsEnemy` (both overloads) is patched directly so no AI
  ever considers it a valid enemy, regardless of the exact reason the faction check alone
  wasn't enough.
- The charm and the companion are registered as real prefabs (`Fedo_KnorriCharm` /
  `Fedo_Knorri`, same `Fedo_` naming convention as `Fedo_GoldRabbit`) — an admin can give
  themselves the charm directly via the debug console (`additem Fedo_KnorriCharm 1`)
  instead of hunting for a vanilla item name.
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
- The cooldown visual (`SummonItemCooldownOverlayPatch`) darkens the charm's icon and shows a
  countdown number in the inventory grid while it's on cooldown — vanilla has no generic
  per-item cooldown UI to hook into (checked: no such field on `ItemDrop.ItemData`,
  `HotkeyBar.ElementData`, or `InventoryGrid.Element`), so this reconstructs it via a Harmony
  Postfix on `InventoryGrid.UpdateGui` reading/writing that private nested `Element` class's
  icon/amount fields through cached reflection (`InventoryGrid.Element` itself is a private
  nested class, inaccessible any other way from outside the game's own assembly).
