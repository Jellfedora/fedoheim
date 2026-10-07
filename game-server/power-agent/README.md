# Agent d'alimentation Fedoheim

Permet à un admin de **démarrer / arrêter / redémarrer** le serveur Valheim depuis le
launcher (page Admin > Serveur, section « Machine »), y compris quand le jeu est éteint —
ce que FedoServerTools ne peut pas faire, puisqu'il s'éteint avec le jeu.

## Fonctionnement

- `fedoheim-power-agent.sh` tourne en service systemd (utilisateur `valheim`) et sonde
  `POST /modpacks/power-agent` toutes les 10s, en envoyant l'état du service
  (`systemctl is-active valheim.service`). L'API répond en texte brut `start`, `stop`,
  `restart` ou `none` (action en attente consommée, jamais rejouée).
- Authentification : le même `ServerToken` que FedoServerTools, relu à chaque tour
  depuis `BepInEx/config/fedo.servertools.cfg` (avec `ApiBaseUrl`). Régénérer le jeton
  depuis la page « Profils » + mettre à jour ce `.cfg` suffit, rien d'autre à toucher.
- Aucun port ouvert : c'est la machine qui interroge l'API, jamais l'inverse.
- Droits : une règle sudoers limitée aux trois commandes `systemctl --no-block
  start|stop|restart valheim.service`, sans mot de passe.
- Arrêt propre : `valheim-graceful-stop.conf` fait envoyer SIGINT (Ctrl-C) au jeu au
  lieu de SIGTERM — le serveur dédié sauvegarde le monde avant de quitter.

## Installation (Ubuntu, en root, depuis ce dossier)

```sh
install -m 755 fedoheim-power-agent.sh /usr/local/bin/fedoheim-power-agent.sh
install -m 644 fedoheim-power-agent.service /etc/systemd/system/
install -m 440 fedoheim-power-agent.sudoers /etc/sudoers.d/fedoheim-power-agent
visudo -cf /etc/sudoers.d/fedoheim-power-agent
mkdir -p /etc/systemd/system/valheim.service.d
install -m 644 valheim-graceful-stop.conf /etc/systemd/system/valheim.service.d/
systemctl daemon-reload
systemctl enable --now fedoheim-power-agent.service
```

Le drop-in `valheim-graceful-stop.conf` ne s'applique au process déjà lancé qu'à son
prochain démarrage — inutile de redémarrer Valheim juste pour ça.

Logs : `journalctl -u fedoheim-power-agent -f`.

## Côté API

`POST /modpacks/power-agent` (jeton serveur), `POST /modpacks/:slug/power-command`
(admin), `GET /modpacks/:slug/power-status` (public) — voir
`api/src/modpacks/onlinePlayers.ts`. L'API doit être redéployée avec ces routes avant
que l'agent puisse s'y connecter (sinon 404, loggé une fois par l'agent).
