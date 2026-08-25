# Changelog

Une entrée par version publiée du launcher. Le texte de chaque section est repris
automatiquement comme description de la release GitHub correspondante (voir
`.github/workflows/launcher-release.yml`).

## 0.0.7

- La page "Profils" et la page "Paramètres" (admin) sont fusionnées en une seule page
  "Admin", avec un nouvel onglet "Serveur" pour régler l'heure du jeu, forcer une saison,
  ou diffuser un message ponctuel sur l'écran de chaque joueur connecté — appliqué par le
  mod FedoServerTools au prochain rapport du serveur (jusqu'à 30s de latence).
- L'éditeur de mods a maintenant ses propres onglets de catégorie et une recherche
  utilisables aussi en dehors du mode édition ; les boutons d'action (Éditer, Ajouter des
  mods, Enregistrer...) sont désormais flottants en haut de page plutôt qu'en bas de
  liste.
- La fenêtre du launcher a une taille fixe plus grande (1200×800, non redimensionnable)
  pour mieux accueillir ces nouveaux écrans.

## 0.0.6

- Un build distribué (release) pointe maintenant vers l'API de production
  (`https://fedoheim.hopto.org`) par défaut, au lieu de l'API locale de dev — jusqu'ici
  il fallait positionner `VALHEIM_API_URL` à la main pour qu'un build autre que
  `tauri dev` fonctionne vraiment.

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
