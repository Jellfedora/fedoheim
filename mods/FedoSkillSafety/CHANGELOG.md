# Changelog

## 1.0.0

- Initial release.
- Skill levels are protected in tiers (every 5 levels by default, configurable): once a
  skill reaches a tier, death can never bring it below that level again for that
  character.
- Death still lowers skills exactly like vanilla otherwise -- only whatever fell below
  an already-unlocked tier is brought back up.
- On-screen message (configurable) the first time a skill reaches a new tier.
- The unlocked tier is shown in blue next to the level in the game's own skills panel
  (Tab).
- Optional integration with FedoHud: shows the same blue tier next to a pinned skill's
  level in its skills block, if FedoHud is installed.
- Tier size is synced from the server and locked ([ServerSync](https://github.com/blaxxun-boop/ServerSync))
  -- a connecting player can't override it from their own local `.cfg`. Works fully
  offline/solo too, since skill data itself is only ever tracked client-side.
