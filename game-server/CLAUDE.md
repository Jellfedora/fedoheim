# Fedoheim — Configuration du serveur Valheim

Documentation des configurations du serveur de jeu Valheim. À alimenter au fur et à mesure.

## Contenu de ce dossier

- `CLAUDE.md` — cette documentation
- `backup/world/` — backup du monde Valheim (`worlds_local/Fedoheimv12`)
- `backup/bepinex/config/` — backup des configs BepInEx (Marketplace, mods...)
- `backup/bepinex/plugins/` — backup des plugins installés

Les backups sont à maintenir à jour manuellement avant chaque grosse modification du serveur.

## Environnement

- **Serveur dédié local** : `C:\Users\julie\Desktop\valheim-server\`
- **Profil joueur/admin (Gale)** : `C:\Users\julie\AppData\Roaming\com.kesomannen.gale\valheim\profiles\fedoheim admin\`
- **Monde** : `C:\Users\julie\AppData\LocalLow\IronGate\Valheim\worlds_local\Fedoheimv12`
- Pour tester en local : lancer via Gale (profil `fedoheim admin`) avec l'option "locally" cochée — le client fait aussi office de serveur, les configs Gale sont lues directement.

## Mods installés (serveur)

| Mod | Version | Rôle |
|---|---|---|
| KG-Marketplace_And_Server_NPCs_Revamped | 10.0.3 | Marketplace, quêtes, NPCs, territoires |
| Smoothbrain-Professions | 1.4.7 | Système de professions |
| Smoothbrain-Guilds | 1.1.14 | Guildes (Blaxxun) |
| Smoothbrain-Groups | 1.2.12 | Groupes/parties |
| Smoothbrain-Farming/Mining/Blacksmithing/etc. | — | Compétences métier |
| Smoothbrain-ServerCharacters | 1.4.17 | Personnages côté serveur |
| shudnal-Seasons | 1.10.4 | Saisons dynamiques |
| JereKuusela-Expand_World_Size | 1.43.0 | Taille de monde étendue |
| JereKuusela-Server_devcommands | 1.115.0 | Commandes admin avancées |
| JereKuusela-Cron_Job | 1.14.0 | Tâches planifiées |
| warpalicious-More_World_Locations_AIO | — | Lieux supplémentaires |
| warpalicious-Procedural_Roads | — | Routes procédurales |
| Geckuss-DeathRoulette | 1.6.0 | Effets à la mort |
| blacks7ar-Hunting | 1.4.5 | Chasse |
| M2Valheim-VikingHorse | 1.0.6 | Chevaux |
| Mods maison Fedoheim | — | FedoDeathGif, FedoHud, FedoSignColor, FedoSkillSafety, FedoServerTools |

## Marketplace & Server NPCs Revamped (KG)

Config principale : `BepInEx/config/Marketplace/MarketPlace.cfg`
Configs détaillées : `BepInEx/config/Marketplace/Configs/`

Steam ID admin déclaré dans `MarketPlace.cfg` → `OverrideDebug = <SteamID64>`

### Territoires

Dossier : `BepInEx/config/Marketplace/Configs/Territories/`  
Un fichier `.cfg` par groupe de zones (plusieurs zones possibles dans un même fichier).

**Format d'une zone :**
```
[NomZone@priorité]
Shape
Coordonnées
Couleur RGB
Flags
Access
```

**Formes et coordonnées :**
```
# Cercle — centre_X, centre_Z, rayon
Circle
-559, -662, 50

# Rectangle — origine_X, origine_Z, largeur, hauteur
# Origine = coin SUD-OUEST (X et Z les plus négatifs)
# Largeur = NE_X - SO_X, Hauteur = NE_Z - SO_Z (toujours positifs)
Rectangle
-608, -779, 132, 239

# Carré — centre_X, centre_Z, demi-côté
Square
0, 0, 30
```

**Coords depuis `pos` en jeu** : format affiché `x,z,y` (y = altitude, ignorée pour les territoires).

**Flags utiles :**
- `NoBuild` — interdit la construction
- `NoBuildDamage` — interdit de détruire/endommager les structures (≠ "NoDestroy" qui n'existe pas)
- `NoAttack` — pas d'attaque
- `NoMonsters` — pas de monstres
- `PeriodicHeal = N` — soin périodique
- `ForceBiome = N` — force un biome visuel

**Access :**
- `None` — accessible à tout le monde
- Liste de SteamID ou noms de guildes pour restreindre

**Commandes utiles :**
- `zonevisualizer` — affiche les bordures des zones en jeu
- `zonevisualizeralpha` — ajuste la transparence des bordures

**Reload :** les modifications d'un fichier existant sont prises en compte à chaud. Un nouveau fichier ou un changement de forme (`Circle` → `Rectangle`) peut nécessiter un redémarrage.

### Zones configurées

| Zone | Forme | Coords | Notes |
|---|---|---|---|
| QuestZone | Rectangle | SO=-608,-779 / NE=-476,-540 | Zone de quête principale |
| Spawn | Circle | 0, 0, r=50 | À placer |
| Village_Meadows | Circle | 0, 0, r=60 | À placer |
| Village_BlackForest | Circle | 0, 0, r=60 | À placer |
| Village_Swamp | Circle | 0, 0, r=60 | À placer |
| Village_Mountains | Circle | 0, 0, r=60 | À placer |
| Village_Plains | Circle | 0, 0, r=60 | À placer |
| Village_Mistlands | Circle | 0, 0, r=60 | À placer |
| Village_Ashlands | Circle | 0, 0, r=60 | À placer |
| Village_DeepNorth | Circle | 0, 0, r=60 | À placer |
| Pecheur_1/2/3 | Circle | 0, 0, r=25 | À placer |
| BossStone_Eikthyr | Circle | 0, 0, r=20 | À placer |
| BossStone_Elder | Circle | 0, 0, r=20 | À placer |
| BossStone_Bonemass | Circle | 0, 0, r=20 | À placer |
| BossStone_Moder | Circle | 0, 0, r=20 | À placer |
| BossStone_Yagluth | Circle | 0, 0, r=20 | À placer |
| BossStone_Queen | Circle | 0, 0, r=20 | À placer |
| BossStone_Fader | Circle | 0, 0, r=20 | À placer |
