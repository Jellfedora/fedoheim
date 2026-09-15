# FedoSignColor

*By Fedo*

Signs let you color your text, but only by typing a code like `<color=#ff0000>Hello`
by hand. This mod adds a full color picker next to the text box whenever you write on
a sign — pick any color visually (or paste a hex code) and apply it to your whole
message, no typing a color code by hand required.

## What it does

- Whenever you open a sign to write on it, a color picker appears next to the text box:
  a saturation/brightness square, a hue bar, a live preview, and a hex code field you
  can type or paste into directly.
- A row of quick color swatches is also there for one-click access to your favorite
  colors.
- Any color change (moving the square/bar, clicking a swatch, typing a hex code) colors
  everything you've typed right away — no extra button to click. Picking another color
  afterward replaces the previous one instead of stacking.

## Configuration

Settings live in `BepInEx/config/fedo.signcolor.cfg`.

**[SignColor]**
- `EnableColorPicker` — turn the color picker on/off.
- `Palette` — the list of quick-access swatch colors, as hex codes separated by commas
  (e.g. `ffffff,ff0000,00ff00`).
