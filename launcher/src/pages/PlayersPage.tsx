import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { ShieldIcon } from "../components/ShieldIcon";
import { SkullIcon } from "../components/SkullIcon";
import { formatDate } from "../utils/date";
import "./PlayersPage.css";

interface PlayersPageProps {
  // Profil ciblé — voir App.tsx::effectiveModpackSlug (toujours la production pour un
  // joueur normal, le profil actif pour un admin en train d'en tester un autre).
  slug: string;
}

interface PlayerStat {
  name: string;
  // Dernier biome/armure connus, tels que rapportés par FedoServerTools -- restent
  // affichés même une fois le joueur déconnecté (voir player-stats côté API), `null` si
  // jamais résolus (position non publique, personnage introuvable côté serveur...).
  biome: string | null;
  armor: number | null;
  // Total de morts rapportées pour ce joueur sur ce profil (voir
  // onlinePlayers.ts::upsertPlayerStats côté API) -- jamais remis à zéro.
  deaths: number;
  online: boolean;
  lastSeenAt: string;
  // Compte Fedoheim lié à ce nom de perso (voir onlinePlayers.ts::linkCharacterName) --
  // `null` pour un perso vu avant l'existence de cette fonctionnalité, ou jamais lié.
  discordUsername: string | null;
  discordAvatar: string | null;
}

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };

// Même cadence que le rapport le plus rapide possible côté mod (voir HomePage.tsx) --
// pas la peine d'ajouter un délai d'affichage supplémentaire au-dessus.
const POLL_MS = 10_000;

export function PlayersPage({ slug }: PlayersPageProps) {
  const [players, setPlayers] = useState<PlayerStat[]>([]);
  const [state, setState] = useState<LoadState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    function load() {
      invoke<{ players: PlayerStat[] }>("fetch_player_stats", { slug })
        .then((fetched) => {
          if (cancelled) return;
          setPlayers(fetched.players);
          setState({ kind: "loaded" });
        })
        .catch((err) => {
          if (!cancelled) setState({ kind: "error", message: String(err) });
        });
    }

    load();
    const interval = setInterval(load, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [slug]);

  return (
    <div className="players-page">
      <header className="players-page__header">
        <h1>Nos intrépides Vikings</h1>
      </header>

      {state.kind === "loading" && <p className="players-page__status">Chargement...</p>}
      {state.kind === "error" && (
        <p className="players-page__status is-error">{state.message}</p>
      )}
      {state.kind === "loaded" && players.length === 0 && (
        <p className="players-page__status">Aucun joueur vu pour le moment.</p>
      )}

      {players.length > 0 && (
        <ul className="players-list">
          {players.map((player) => (
            <li key={player.name} className="players-list__item">
              <span className={`players-list__dot ${player.online ? "is-online" : ""}`} />
              <span className="players-list__avatar" aria-hidden="true">
                {player.discordAvatar ? (
                  <img className="players-list__avatar-img" src={player.discordAvatar} alt="" />
                ) : (
                  (player.discordUsername ?? player.name).slice(0, 1).toUpperCase()
                )}
              </span>
              <span className="players-list__name">{player.name}</span>
              {player.armor !== null && (
                <span className="players-list__armor" title="Armure">
                  <ShieldIcon className="players-list__shield-icon" />
                  {player.armor}
                </span>
              )}
              {player.deaths > 0 && (
                <span className="players-list__deaths" title="Morts">
                  <SkullIcon className="players-list__skull-icon" />
                  {player.deaths}
                </span>
              )}
              {player.biome && <span className="players-list__biome">{player.biome}</span>}
              <span className="players-list__seen">
                {player.online ? "En ligne" : `Vu le ${formatDate(player.lastSeenAt)}`}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
