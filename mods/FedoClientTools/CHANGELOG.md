# Changelog

## 1.1.0

- Removed the in-game clock overlay — moved to the new [FedoHud](../FedoHud/README.md)
  mod, which now owns all custom HUD overlays. `ShowClockOverlay`/`TimeOffsetHours`/
  `ClockPositionX`/`ClockPositionY` no longer exist in this mod's `.cfg`.

## 1.0.0

- Skips the main menu straight to auto-connect when the active modpack profile has an
  auto-connect target configured (a local world to host, or a dedicated server to
  join) — no clicking through menus.
- The new-character name field is pre-filled with your Discord username as a
  suggestion, but you're always free to type your own instead.
- Shows a Fedoheim loading screen during that automatic connection, instead of a plain
  black screen.
- If a previous connection attempt failed, shows a clear "Reconnect" / "Quit" choice
  with a live server status, instead of silently retrying in a loop.
- Adds a small, draggable in-game clock (hold Shift and drag it) at the top of the
  screen, following the day/night cycle.
- Displays an admin's broadcast message (sent from the launcher) centered on screen and
  in the chat.
- Client-side companion to FedoServerTools — no admin routes, safe to install on any
  player's game.
