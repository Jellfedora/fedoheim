export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString("fr-FR", {
    day: "numeric",
    month: "long",
    hour: "2-digit",
    minute: "2-digit",
  });
}
