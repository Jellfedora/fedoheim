using UnityEngine;

namespace FedoKnorri
{
    // Deux interactions au survol, distinguées par le modifier "alt" d'Interact (Maj par défaut,
    // "AltPlace") : Maj+E renomme (même mécanique que le renommage d'une créature apprivoisée
    // vanilla via Tameable, mais sans embarquer tout Tameable -- apprivoisement, faim... -- qui
    // ne s'applique pas ici), un simple E bascule le mode de comportement (voir
    // CompanionAI.ToggleBehaviorMode). Aucune vérification que "user" est le propriétaire pour
    // ni l'une ni l'autre -- cohérence entre les deux plutôt qu'une asymétrie arbitraire.
    public class CompanionInteract : MonoBehaviour, Interactable, TextReceiver
    {
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
            {
                return false;
            }

            if (alt)
            {
                // Texte littéral plutôt qu'un token de localisation vanilla deviné (ex:
                // "$hud_rename") -- une mauvaise supposition afficherait le token brut non
                // résolu dans la popup.
                TextInput.instance.RequestText(this, FedoKnorriPlugin.Instance.RenamePromptText.Value, 20);
                return true;
            }

            GetComponent<CompanionAI>()?.ToggleBehaviorMode();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        public string GetText()
        {
            var character = GetComponent<Character>();
            return character != null ? character.m_name : string.Empty;
        }

        public void SetText(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var character = GetComponent<Character>();
            if (character != null)
            {
                character.m_name = text;
            }

            var nview = GetComponent<ZNetView>();
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            zdo?.Set(CompanionAI.ZdoCustomName, text);

            // Copie côté propriétaire aussi (voir CompanionAI.PersistRename) : le ZDO ci-dessus
            // disparaît avec le compagnon dès qu'il est rangé, ce qui ferait perdre le nom au
            // prochain "invoquer" sans cette seconde copie persistante.
            GetComponent<CompanionAI>()?.PersistRename(text);

            // EnemyHud (l'étiquette nom+vie flottante au-dessus d'une créature) met en cache le
            // nom dans un TextMeshProUGUI créé une seule fois par personnage -- jamais réécrit
            // tant que l'entrée existe, donc un renommage en cours de partie n'y apparaissait
            // jamais (vécu en jeu : le popup montrait bien le nouveau nom, l'étiquette au-dessus
            // restait sur l'ancien). RemoveCharacterHud force sa suppression ; EnemyHud la
            // recrée automatiquement (avec le nom à jour) au prochain passage de LateUpdate tant
            // que le compagnon reste visible.
            if (character != null)
            {
                EnemyHud.instance?.RemoveCharacterHud(character);
            }
        }
    }
}
