#!/usr/bin/env bash
# Agent d'alimentation Fedoheim : sonde l'API toutes les POLL_INTERVAL secondes et
# démarre/arrête/redémarre le service systemd du serveur Valheim quand un admin le
# demande depuis le launcher (page Admin > Serveur). Même principe "sonde, jamais poussé"
# que FedoServerTools : aucun port à ouvrir sur cette machine.
#
# ApiBaseUrl et ServerToken sont relus à chaque tour depuis le .cfg de FedoServerTools
# installé sur ce même serveur -- une seule valeur à régénérer, prise en compte sans
# redémarrer l'agent.
set -u

CFG="${FEDO_CFG:-/home/valheim/valheim-server/BepInEx/config/fedo.servertools.cfg}"
SERVICE="${VALHEIM_SERVICE:-valheim.service}"
INTERVAL="${POLL_INTERVAL:-10}"

read_cfg() {
  sed -n "s/^$1[[:space:]]*=[[:space:]]*//p" "$CFG" 2>/dev/null | tail -n 1 | tr -d '\r'
}

failing=0

while true; do
  token="$(read_cfg ServerToken)"
  api="$(read_cfg ApiBaseUrl)"
  api="${api%/}"

  if [ -z "$token" ] || [ -z "$api" ]; then
    echo "ServerToken ou ApiBaseUrl absent de $CFG, nouvel essai dans 60s"
    sleep 60
    continue
  fi

  state="$(systemctl is-active "$SERVICE" 2>/dev/null)"
  [ -n "$state" ] || state="unknown"

  if action="$(curl -fsS -m 10 -X POST \
      -H "x-server-token: $token" \
      -H "content-type: application/json" \
      --data "{\"serviceState\":\"$state\"}" \
      "$api/modpacks/power-agent" 2>&1)"; then
    if [ "$failing" -eq 1 ]; then
      echo "API de nouveau joignable"
      failing=0
    fi

    # Liste blanche stricte : seule une de ces trois valeurs exactes déclenche quelque
    # chose, tout le reste est ignoré. --no-block pour ne pas geler la boucle pendant
    # l'arrêt du jeu (sauvegarde du monde), l'état "deactivating" étant rapporté au
    # tour suivant.
    case "$action" in
      start|stop|restart)
        echo "Action reçue : $action (état actuel : $state)"
        sudo -n /usr/bin/systemctl --no-block "$action" "$SERVICE" \
          || echo "Échec de systemctl $action (règle sudoers installée ?)"
        ;;
    esac
  elif [ "$failing" -eq 0 ]; then
    # Une seule ligne par panne, pas une toutes les 10s dans le journal.
    echo "API injoignable : $action"
    failing=1
  fi

  sleep "$INTERVAL"
done
