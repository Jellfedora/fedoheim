# FedoAchievements

*By Fedo*

Valheim's in-game achievements panel hides a lot: secret achievements only show up as
"Secret achievement" until you unlock them, and every unlock condition you haven't
completed yet reads "??? / ???" — so you never know how close you are. This mod lifts
both restrictions.

## What it does

- **Secret achievements revealed**: their real name and description are shown in
  the achievements panel, and you can open their details like any other achievement.
- **Progress shown for every condition**: the details of an achievement list each
  condition by name with your current progress (e.g. `3 / 10`), not only the ones you've
  already completed. Completed conditions stay green, the others stay grey.

Display only: nothing gets unlocked, no statistic is changed, and achievements are still
earned exactly as in the vanilla game. It only affects the in-game panel — the Steam
overlay and Steam's achievement page still follow Steam's own rules for hidden
achievements.

Client-side only: no need to install it on the server, and other players aren't
affected.

## Configuration

Settings live in `BepInEx/config/fedo.achievements.cfg`.

**[Achievements]**
- `RevealSecretAchievements` — show secret achievements in full before they're unlocked.
- `ShowProgress` — show the name and progress of conditions not completed yet.

Changes apply the next time you open the achievements panel.
