using System;
using System.Collections;
using System.Net.Http;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace FedoServerTools
{
    // Statut en ligne + nombre de joueurs connectés, affiché sur DisconnectChoiceOverlay
    // pour savoir si le serveur qu'on vient de quitter est de nouveau joignable avant de
    // cliquer "Se reconnecter" -- lu depuis GET /modpacks/:slug/online-players, le même
    // endpoint PUBLIC (pas de jeton nécessaire, comme /health) que le launcher utilise
    // déjà pour la page d'accueil. Parsing JSON minimal à la main (recherche de
    // sous-chaînes), comme OnlinePlayersReporter côté écriture -- aucun mod de ce repo
    // n'a de dépendance de parsing JSON, pas la peine d'en introduire une pour lire deux
    // champs.
    internal static class ServerStatusLine
    {
        // Même cadence que le sondage déjà utilisé côté launcher (HomePage/PlayersPage,
        // voir CLAUDE.md) pour ce même endpoint -- pas la peine de vérifier plus souvent
        // qu'une page qui fait exactement la même requête.
        private static readonly float PollIntervalSeconds = 10f;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // `host` doit être un MonoBehaviour vivant (voir DisconnectChoiceOverlay._root) --
        // c'est lui qui porte la coroutine, pas cette classe statique. Tourne en boucle
        // tant que l'overlay reste affiché (le joueur peut très bien rester sur cet écran
        // en attendant que le serveur redémarre) -- s'arrête toute seule dès que `host`
        // est détruit (Hide()), Unity coupant alors sa coroutine.
        // `onStatusUpdate` (optionnel) reçoit le statut confirmé de chaque sondage réussi
        // (`true`/`false`, jamais appelé sur un sondage en échec -- voir DisconnectChoiceOverlay,
        // qui s'en sert pour désactiver le bouton "Connexion" tant que le serveur est
        // confirmé hors ligne, plutôt que de tenter une connexion qui resterait bloquée
        // sans retour avant le timeout vanilla).
        public static void Fetch(MonoBehaviour host, string apiBaseUrl, string slug, TextMeshProUGUI label, Action<bool> onStatusUpdate = null)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(slug))
            {
                label.text = "Statut du serveur indisponible";
                return;
            }

            host.StartCoroutine(PollCoroutine(apiBaseUrl, slug, label, onStatusUpdate));
        }

        private static IEnumerator PollCoroutine(string apiBaseUrl, string slug, TextMeshProUGUI label, Action<bool> onStatusUpdate)
        {
            var wait = new WaitForSecondsRealtime(PollIntervalSeconds);
            while (label != null)
            {
                yield return FetchOnceCoroutine(apiBaseUrl, slug, label, onStatusUpdate);
                yield return wait;
            }
        }

        private static IEnumerator FetchOnceCoroutine(string apiBaseUrl, string slug, TextMeshProUGUI label, Action<bool> onStatusUpdate)
        {
            Task<string> task = Http.GetStringAsync(BuildUrl(apiBaseUrl, slug));

            // Attend la fin de la requête sans bloquer le thread principal -- la coroutine
            // reprend ici, sur le thread principal, une fois `task` terminée, donc la mise
            // à jour de `label` ci-dessous reste sûre (TextMeshProUGUI n'est pas
            // thread-safe).
            while (!task.IsCompleted)
            {
                yield return null;
            }

            // L'overlay peut avoir été fermé (reconnexion lancée, jeu fermé) pendant que
            // la requête était en vol -- `label` est alors un objet Unity détruit.
            if (label == null)
            {
                yield break;
            }

            if (task.IsFaulted || task.Result == null)
            {
                label.text = "Serveur hors ligne";
                yield break;
            }

            string json = task.Result;
            bool online = json.Contains("\"online\":true");
            int playerCount = CountOccurrences(json, "\"name\":\"");

            label.text = online
                ? $"Serveur en ligne -- {playerCount} joueur{(playerCount == 1 ? "" : "s")} connecté{(playerCount == 1 ? "" : "s")}"
                : "Serveur hors ligne";
            onStatusUpdate?.Invoke(online);
        }

        private static string BuildUrl(string apiBaseUrl, string slug)
        {
            return apiBaseUrl.TrimEnd('/') + "/modpacks/" + Uri.EscapeDataString(slug) + "/online-players";
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}
