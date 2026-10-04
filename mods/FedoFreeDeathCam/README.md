# FedoFreeDeathCam

*By Fedo*

**Alpha**

A tiny fix for one annoyance: when you die, the camera normally stays locked in place,
forced to keep staring at your character's body until you respawn. This mod lets you
move the camera freely instead, so you can look around while you wait.

## What it does

- The moment you die, you can fly the camera around freely: WASD/mouse to move and look,
  scroll wheel to change speed.
- It goes back to normal automatically the moment you respawn.
- If the camera happened to already be in that free-roaming mode before you died (only
  possible through the game's own debug mode), this mod leaves it alone either way.

## Configuration

Settings live in `BepInEx/config/fedo.freedeathcam.cfg`.

**[FreeDeathCam]**
- `EnableFreeDeathCam` — turn the free camera on death on/off.
