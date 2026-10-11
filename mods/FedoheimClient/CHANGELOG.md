# Changelog

## 1.0.1

- Can replace the help pages of the More World Locations port interface (which that mod
  only ships in English) with your own text: drop `<page>.md` files in
  `BepInEx/config/FedoheimClient/mwl_tutorials/` (first line = tab title, the rest = page
  text). Pages without a file are left untouched.

## 1.0.0

- New `fedoevent <name>` console command: asks the server to start a vanilla raid event
  at your position. The server only accepts events, areas and timings its admin allowed
  (`AllowedEvents`, `AllowedAreas`, `CooldownSeconds`), so quest NPCs that can run console
  commands can trigger a real raid for regular players.
