import { useState } from "react";
import type { ComponentProps } from "react";
import { ProfilesPage } from "./ProfilesPage";
import { SettingsPage } from "./SettingsPage";
import { ServerPage } from "./ServerPage";
import "./AdminPage.css";

type ProfilesPageProps = ComponentProps<typeof ProfilesPage>;
type SettingsPageProps = ComponentProps<typeof SettingsPage>;

interface AdminPageProps extends ProfilesPageProps {
  onSettingsSaved: SettingsPageProps["onSaved"];
}

type AdminTab = "profile" | "server" | "settings";

export function AdminPage({
  activeSlug,
  onSelect,
  onModpackUpdated,
  onSettingsSaved,
}: AdminPageProps) {
  const [tab, setTab] = useState<AdminTab>("profile");

  return (
    <div className="admin-page">
      <div className="admin-page__tabs">
        <button
          type="button"
          className={`admin-page__tab ${tab === "profile" ? "is-active" : ""}`}
          onClick={() => setTab("profile")}
        >
          Profil
        </button>
        <button
          type="button"
          className={`admin-page__tab ${tab === "server" ? "is-active" : ""}`}
          onClick={() => setTab("server")}
        >
          Serveur
        </button>
        <button
          type="button"
          className={`admin-page__tab ${tab === "settings" ? "is-active" : ""}`}
          onClick={() => setTab("settings")}
        >
          Paramètres
        </button>
      </div>

      {tab === "profile" && (
        <ProfilesPage
          activeSlug={activeSlug}
          onSelect={onSelect}
          onModpackUpdated={onModpackUpdated}
        />
      )}
      {tab === "server" && <ServerPage activeSlug={activeSlug} />}
      {tab === "settings" && <SettingsPage onSaved={onSettingsSaved} />}
    </div>
  );
}
