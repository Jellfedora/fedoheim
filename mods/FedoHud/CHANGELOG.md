# Changelog

## 0.0.1 (alpha)

- New mod, built for Valheim 1.0. Takes over the in-game clock previously shown by
  FedoClientTools (draggable, hold Shift and drag; position saved locally).
- Hover tooltip line on planted crops showing the remaining growth time (`Grows in Xh` /
  `Ready to harvest!`).
- Hover tooltip line on beehives showing the remaining time until the next honey
  (`Next honey in Xh` / `Storage full!` / `Production paused`).
- Floating "!" above a crop once it's ready to harvest (never above a planted tree
  sapling), and above a beehive once it holds as much honey as it can. Each icon has its
  own toggle, independent from the hover tooltip text.
- Hover tooltip line on fermenters showing the remaining time until done fermenting
  (`Ready in Xh` / `Ready!`).
- Hover tooltip line on smelters and charcoal kilns showing the remaining time until
  everything queued is done (`Done in Xh` / `Done!` / `Paused`).
- Removed `TimeOffsetHours` — unnecessary, the clock already matches the sky exactly
  (same value the game itself uses for lighting).
- Not yet fully tested in-game.
