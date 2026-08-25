import { useEffect, useState } from "react";
import type { ReactElement } from "react";
import { getVersion } from "@tauri-apps/api/app";
import "./Sidebar.css";

export type Page = "home" | "players" | "mods" | "rules" | "faq" | "admin";

interface NavItem {
  page: Page;
  label: string;
  icon: ReactElement;
  adminOnly?: boolean;
}

const NAV_ITEMS: NavItem[] = [
  {
    page: "home",
    label: "Accueil",
    icon: (
      <path d="M4 11.5 12 4l8 7.5M6 10v9h5v-5h2v5h5v-9" />
    ),
  },
  {
    page: "players",
    label: "Joueurs",
    icon: (
      <path d="M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm8 1a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM2 20v-1a5 5 0 0 1 5-5h2a5 5 0 0 1 5 5v1M15 20v-.5a4 4 0 0 0-2.5-3.7" />
    ),
  },
  {
    page: "mods",
    label: "Mods",
    icon: (
      <path d="M12 3 4 7v10l8 4 8-4V7l-8-4Zm0 0v18M4 7l8 4 8-4" />
    ),
  },
  {
    page: "rules",
    label: "Règlement",
    icon: (
      <path d="M6 4h9l3 3v13a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Zm8 5h4M9 12h6M9 15h6M9 18h4" />
    ),
  },
  {
    page: "faq",
    label: "FAQ",
    icon: (
      <path d="M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Zm0-5.5v.01M9.5 9.75a2.5 2.5 0 0 1 4.9-.75c0 1.5-2.4 1.75-2.4 3.5" />
    ),
  },
  {
    page: "admin",
    label: "Admin",
    adminOnly: true,
    icon: (
      <path d="M12 3 5 6v6c0 4.4 3 7.6 7 9 4-1.4 7-4.6 7-9V6l-7-3Zm0 5.2v7.3M9 10.5l3-1.8 3 1.8" />
    ),
  },
];

interface SidebarProps {
  current: Page;
  onNavigate: (page: Page) => void;
  onSupport: () => void;
  isAdmin: boolean;
  // Vrai quand l'API est injoignable : seul "Accueil" reste cliquable (voir App.tsx),
  // le reste dépend de données qui ne peuvent de toute façon pas charger.
  navLocked: boolean;
  // Bouton de déconnexion en bas de la sidebar (voir App.tsx) — absent tant que
  // personne n'est connecté, plutôt que désactivé.
  isLoggedIn: boolean;
  onLogout: () => void;
}

export function Sidebar({
  current,
  onNavigate,
  onSupport,
  isAdmin,
  navLocked,
  isLoggedIn,
  onLogout,
}: SidebarProps) {
  const visibleItems = NAV_ITEMS.filter((item) => !item.adminOnly || isAdmin);
  const [version, setVersion] = useState("");

  useEffect(() => {
    getVersion().then(setVersion).catch(() => {});
  }, []);

  return (
    <nav className="sidebar" aria-label="Navigation principale">
      <img className="sidebar__mark" src="/logo_fedoheim.png" alt="Fedoheim" title="Fedoheim" />
      {version && <span className="sidebar__version">v{version}</span>}

      <ul className="sidebar__nav">
        {visibleItems.map((item) => {
          const disabled = navLocked && item.page !== "home";
          return (
            <li key={item.page}>
              <button
                type="button"
                className={`sidebar__item ${current === item.page ? "is-active" : ""}`}
                onClick={() => onNavigate(item.page)}
                aria-current={current === item.page}
                disabled={disabled}
                title={disabled ? "Indisponible hors ligne" : item.label}
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6">
                  {item.icon}
                </svg>
                <span className="sidebar__label">{item.label}</span>
              </button>
            </li>
          );
        })}
      </ul>

      <div className="sidebar__spacer" />

      <button
        type="button"
        className="sidebar__item sidebar__support"
        onClick={onSupport}
        title="Soutenir le serveur"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6">
          <path d="M5 9h13a1 1 0 0 1 1 1v2a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4V9Z" />
          <path d="M18 11h1.5a1.5 1.5 0 0 1 0 3H18M8 3.5C8 4.5 7 5 7 6M12 3.5c0 1-1 1.5-1 2.5" />
          <path d="M4 20h13" />
        </svg>
        <span className="sidebar__label">Soutenir</span>
      </button>

      {isLoggedIn && (
        <button
          type="button"
          className="sidebar__item sidebar__logout"
          onClick={onLogout}
          title="Se déconnecter"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6">
            <path d="M15 3h4a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1h-4M10 17l5-5-5-5M15 12H3" />
          </svg>
          <span className="sidebar__label">Déco</span>
        </button>
      )}
    </nav>
  );
}
