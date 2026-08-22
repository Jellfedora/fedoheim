# Changelog

Une entrée par version publiée du launcher. Le texte de chaque section est repris
automatiquement comme description de la release GitHub correspondante (voir
`.github/workflows/launcher-release.yml`).

## 0.0.5

- Le launcher retient maintenant la taille, la position et l'état agrandi/plein écran
  de la fenêtre d'une ouverture à l'autre.

## 0.0.4

- Correction : la fenêtre s'agrandit maintenant automatiquement quand un bandeau
  (API injoignable / mise à jour disponible) s'affiche, au lieu d'écraser le contenu.

## 0.0.3

- Le numéro de version s'affiche maintenant sous le logo, dans la sidebar.
- La vérification de mise à jour se refait toutes les 5 minutes tant que le launcher
  reste ouvert, plutôt qu'une seule fois au lancement.

## 0.0.2

- Mise à jour automatique du launcher (signée avec une clé propre à Tauri, gratuite —
  pas de certificat Apple/Windows payant).

## 0.0.1

- Premier build de test du launcher, macOS et Windows.
