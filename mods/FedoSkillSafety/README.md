# FedoSkillSafety

*By Fedo*

Softens vanilla's skill loss on death, without removing it entirely: once a skill
reaches a **tier**, it can never drop below that level again for that character, no
matter how many times you die afterward.

## How it works

- Tiers happen every 5 skill levels by default (5, 10, 15, 20...). Reaching one for the
  first time permanently unlocks it as a floor for that skill, on that character.
- Death still lowers your skills exactly like vanilla (whatever the world's "Death
  penalty" setting says) -- this mod only steps in *after* that loss to bring back up
  anything that fell below your last unlocked tier.
- Example: level 10 in Swords, 58% of the way to 11. Level 10 is a tier you already
  unlocked, so a death can never bring Swords below 10 again -- but if you were level 13
  and hadn't reached tier 15 yet, a death can still knock you back down to 11 or 12, just
  never below 10.
- Progress toward your next level (the in-progress %) is never protected, only whole
  tier levels are -- dying always costs you something.
- A tier reached is a tier reached forever, even if a later death (or several) brings the
  skill back down closer to it -- the floor itself never moves down.
- The first time a skill reaches a new tier, a short on-screen message announces it.
- The unlocked tier is shown in blue next to the level in the game's own skills panel
  (Tab), e.g. `10 (5)`.
- If [FedoHud](https://valheim.hexium.gg/mods/Fedo/FedoHud) is installed and its pinned skills block is shown, the same
  blue tier is also displayed next to the level of each pinned skill, e.g.
  `Swords 13 (58%) (10)`. Purely cosmetic -- FedoSkillSafety works exactly the same without
  FedoHud.

## Configuration

Settings live in `BepInEx/config/fedo.skillsafety.cfg`.

**[Skills]**
- `PalierSize` — number of levels between two tiers (default `5`). Synced from the
  server and locked ([ServerSync](https://github.com/blaxxun-boop/ServerSync)) -- a
  connecting player can't override it from their own local `.cfg`.
- `ShowPalierUnlockedMessage` — turn the "tier unlocked" on-screen message on/off
  (default `true`).
- `PalierUnlockedMessageText` — wording of that message. `{skill}` is replaced with the
  skill's (localized) name, `{level}` with the tier level reached.

The blue tier display itself (game's own skills panel, and FedoHud's pinned skills block
if installed) is always on and not configurable -- there's nothing to turn off or
reword there.

## Notes

- Works entirely client-side: skill data in Valheim is only ever tracked by the client
  that owns the character, so this mod protects your tiers the same way whether you're
  playing solo, hosting, or connected to any server -- modded or not. `PalierSize` is
  only synced/locked when the server you're on also runs this mod; otherwise your own
  `.cfg` value applies.
- The best tier reached per skill is stored on the character's own save data, so it
  survives disconnects and server restarts and travels with that character, not with a
  specific world or server.
