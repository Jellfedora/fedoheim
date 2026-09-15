# Changelog

## 1.0.0

- Reports who's currently connected (with biome and armor) to the Fedoheim API every
  ~30s, so the launcher can show server status without anyone needing to log in.
- Reports the current in-game season (if the Seasons mod is installed) and the current
  in-game clock, also shown by the launcher.
- Biome and season names are translatable per server (edit the `.cfg`).
- Reflects the server's real state (starting up / online / shutting down) instead of a
  plain online/offline flag, so the launcher shows an accurate status even while a
  heavily modded server is still booting.
- Lets an admin set the time of day, force a season, or broadcast a message to every
  connected player, all from the launcher — no need to touch the server console.
- Kicks a connecting player if their character name is already claimed by a different
  Fedoheim account, closing an identity-theft loophole.
- Includes a small stability fix for a known Valheim bug that could otherwise crash a
  heavily modded server's world-sync every frame.
- Server-only mod. For the client-side companion (auto-connect, in-game clock), see
  FedoClientTools.
