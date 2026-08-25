interface SkullIconProps {
  className?: string;
}

// Icône (death_icon.png, launcher/public/), même principe que ShieldIcon.tsx --
// affichée devant le compteur de morts sur la page "Joueurs".
export function SkullIcon({ className }: SkullIconProps) {
  return (
    <img
      className={className}
      src="/death_icon.png"
      alt=""
      aria-hidden="true"
      width={12}
      height={12}
    />
  );
}
