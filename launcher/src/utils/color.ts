// Convertit un hex "#rrggbb" (voir <input type="color">) en rgba() pour un fond
// translucide — même esprit que --accent-soft, mais dérivé d'une couleur arbitraire
// choisie par un admin pour un profil de modpack (voir ProfilesPage).
export function hexToRgba(hex: string, alpha: number): string {
  const match = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!match) return hex;
  const value = match[1];
  const r = parseInt(value.slice(0, 2), 16);
  const g = parseInt(value.slice(2, 4), 16);
  const b = parseInt(value.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
