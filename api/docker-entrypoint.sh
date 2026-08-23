#!/bin/sh
# Applique les migrations Drizzle en attente avant chaque démarrage — un conteneur est
# jetable, donc "avoir déjà migré la dernière fois" ne peut jamais être supposé. Sans
# migration en attente, c'est un no-op rapide.
set -e

node_modules/.bin/drizzle-kit migrate

exec node dist/src/index.js
