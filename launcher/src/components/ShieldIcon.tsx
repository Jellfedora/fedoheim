interface ShieldIconProps {
  className?: string;
}

// Icône bouclier minimale, en `currentColor` pour hériter de la couleur du texte
// environnant plutôt que d'ajouter une dépendance à une librairie d'icônes pour un
// usage aussi simple. Partagée entre HomePage (widget "État du serveur") et
// PlayersPage (page "Joueurs"), toutes deux affichées devant une valeur d'armure.
export function ShieldIcon({ className }: ShieldIconProps) {
  return (
    <svg className={className} viewBox="0 0 16 16" width="12" height="12" aria-hidden="true">
      <path
        fill="currentColor"
        d="M8 1 2.5 3v4.2c0 3.4 2.2 6.2 5.5 7.3 3.3-1.1 5.5-3.9 5.5-7.3V3L8 1Z"
      />
    </svg>
  );
}
