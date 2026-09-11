# FedoHud

*By Fedo*

**Alpha**

Small HUD additions for players: a clock, and hints about crops, beehives, fermenters,
smelters and charcoal kilns.

## What it does

- Shows a small clock at the top of the screen, following the day/night cycle. Hold
  **Left Shift** and drag it anywhere — it remembers where you leave it.
- Hovering over a planted crop shows how long until it's ready to harvest, and a
  floating **"!"** appears above it once it is.
- Hovering over a beehive shows how long until it produces one more honey, and the same
  floating **"!"** appears above it once it's full.
- Hovering over a fermenter shows how long until it's done fermenting.
- Hovering over a smelter or a charcoal kiln shows how long until everything queued is
  done smelting/burning.

## Configuration

Settings live in `BepInEx/config/fedo.hud.cfg`.

**[Time]**
- `ShowClockOverlay` — turn the clock on/off.
- `ClockPositionX` / `ClockPositionY` — where the clock sits on screen (set
  automatically when you drag it).

**[Growth]**
- `ShowGrowthTooltip` — turn the "time until ready to harvest" hint on/off.
- `GrowthRemainingPrefix` / `GrowthReadyText` — the wording used for that hint.
- `ShowGrowthReadyIcon` — turn the floating "!" above a ready crop on/off.

**[Beehive]**
- `ShowBeehiveTooltip` — turn the "time until next honey" hint on/off.
- `HoneyRemainingPrefix` / `HoneyFullText` / `HoneyPausedText` — the wording used for
  that hint.
- `ShowBeehiveFullIcon` — turn the floating "!" above a full beehive on/off.

**[Fermenter]**
- `ShowFermenterTooltip` — turn the "time until done fermenting" hint on/off.
- `FermenterRemainingPrefix` / `FermenterReadyText` — the wording used for that hint.

**[Smelter]**
- `ShowSmelterTooltip` — turn the "time until done smelting" hint on/off (also covers
  charcoal kilns).
- `SmelterRemainingPrefix` / `SmelterReadyText` / `SmelterPausedText` — the wording used
  for that hint.
