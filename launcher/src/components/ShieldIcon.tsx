interface ShieldIconProps {
  className?: string;
}

// Icône (shield_icon.png, launcher/public/) plutôt qu'un dessin maison -- partagée
// entre HomePage (widget "État du serveur") et PlayersPage (page "Joueurs"), toutes
// deux affichées devant une valeur d'armure. Taille par défaut alignée sur l'ancien
// SVG (12px) ; les deux appelants peuvent l'agrandir via leur className.
export function ShieldIcon({ className }: ShieldIconProps) {
  return (
    <img
      className={className}
      src="/shield_icon.png"
      alt=""
      aria-hidden="true"
      width={12}
      height={12}
    />
  );
}
