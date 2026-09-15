using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FedoHud
{
    // Grossit et anime le chiffre de dégâts natif du jeu (DamageText) quand c'est le
    // JOUEUR LOCAL qui vient d'infliger ce coup -- jamais les dégâts reçus, ni ceux
    // infligés par un allié/une autre source.
    //
    // `Character.RPC_Damage` (privée, décompilée) tourne localement chez TOUT LE MONDE
    // qui reçoit le RPC de dégâts -- y compris l'attaquant lui-même, indépendamment de
    // qui possède la ZDO de la cible (hôte, serveur dédié...) : c'est justement pour ça
    // que le jeu s'en sert pour ses propres statistiques (`m_localPlayerHasHit`,
    // incrémenté AVANT la vérification `m_nview.IsOwner()`). C'est donc le seul point
    // fiable pour savoir "c'est moi qui viens de frapper", même sur un serveur dédié où
    // le client du joueur ne possède jamais les mobs.
    //
    // Le texte flottant lui-même (`DamageText.AddInworldText`, privée) est diffusé
    // séparément par le propriétaire de la cible et ne transporte AUCUNE identité
    // d'attaquant dans son RPC (`RPC_DamageText` : type/position/texte/mySelf
    // seulement, vérifié par décompilation) -- on corrèle donc les deux par une petite
    // fenêtre de temps plutôt que par un identifiant partagé (qui n'existe pas).
    // Purement cosmétique : un léger décalage de fenêtre n'affecte rien d'autre que
    // l'effet visuel, jamais les vrais calculs de dégâts.
    internal static class PlayerDamageTextBoost
    {
        private const float CorrelationWindowSeconds = 0.35f;
        private const float PunchDurationSeconds = 0.18f;

        // Historique glissant de mes propres dégâts affichés (voir RollingAverageWindow)
        // -- sert de référence pour juger si LE COUP ACTUEL est petit/normal/gros par
        // rapport à ce que je fais d'habitude avec mon arme actuelle, plutôt qu'un seuil
        // absolu qui n'aurait aucun sens entre le début et la fin de partie.
        private const int RollingAverageWindow = 15;
        private const int RollingAverageMinSamples = 5;

        private static readonly FieldInfo WorldTextsField = AccessTools.Field(typeof(DamageText), "m_worldTexts");
        private static readonly Queue<float> RecentDamage = new Queue<float>(RollingAverageWindow);

        private static float _lastLocalAttackTime = -999f;

        private readonly struct Tier
        {
            public readonly float MaxRatio;
            public readonly float SizeFactor;
            public readonly float PunchOvershoot;
            public readonly Color? Tint;

            public Tier(float maxRatio, float sizeFactor, float punchOvershoot, Color? tint = null)
            {
                MaxRatio = maxRatio;
                SizeFactor = sizeFactor;
                PunchOvershoot = punchOvershoot;
                Tint = tint;
            }
        }

        // Triés du plus petit au plus grand ratio -- le dernier (MaxRatio infini) sert de
        // filet de sécurité pour tout ce qui dépasse le seuil précédent.
        private static readonly Tier[] Tiers =
        {
            new Tier(0.6f, 0.85f, 1.35f),
            new Tier(1.4f, 1f, 1.7f),
            new Tier(2.2f, 1.4f, 2f),
            new Tier(float.MaxValue, 1.9f, 2.5f, new Color(1f, 0.35f, 0.1f)),
        };

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class RpcDamagePatch
        {
            private static void Postfix(HitData hit)
            {
                try
                {
                    if (hit != null && hit.GetAttacker() == Player.m_localPlayer)
                    {
                        _lastLocalAttackTime = Time.time;
                    }
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: player damage text boost (attack tracking) failed: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(DamageText), "AddInworldText")]
        private static class AddInworldTextPatch
        {
            private static void Postfix(DamageText.TextType type, string text, bool mySelf)
            {
                try
                {
                    if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowPlayerDamageTextBoost)
                    {
                        return;
                    }

                    // `mySelf` (voir DamageText.AddInworldText) veut dire "cette cible
                    // m'appartient" (mon perso ou un animal apprivoisé par moi) --
                    // l'inverse exact de ce qu'on veut ici (dégâts INFLIGÉS par moi).
                    if (mySelf || !IsBoostableType(type))
                    {
                        return;
                    }

                    if (Time.time - _lastLocalAttackTime > CorrelationWindowSeconds)
                    {
                        return;
                    }

                    Boost(text);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: player damage text boost failed: {e.Message}");
                }
            }
        }

        private static bool IsBoostableType(DamageText.TextType type)
        {
            return type == DamageText.TextType.Normal
                || type == DamageText.TextType.Weak
                || type == DamageText.TextType.Resistant
                || type == DamageText.TextType.Bonus;
        }

        // `DamageText.WorldTextInstance` est un type privé (malgré des champs publics,
        // même piège que `InventoryGui.RecipeDataPair` -- voir CraftingRecipeTracker.cs) :
        // lu par réflexion sur l'objet boîté. La dernière entrée de la liste est
        // forcément celle tout juste créée par l'appel qu'on postfixe.
        private static void Boost(string text)
        {
            var list = WorldTextsField?.GetValue(DamageText.instance) as IList;
            if (list == null || list.Count == 0)
            {
                return;
            }

            object lastEntry = list[list.Count - 1];
            var entryType = lastEntry.GetType();
            var textField = entryType.GetField("m_textField")?.GetValue(lastEntry) as TMP_Text;
            var gui = entryType.GetField("m_gui")?.GetValue(lastEntry) as GameObject;
            if (textField == null || gui == null)
            {
                return;
            }

            var tier = ResolveTier(text);

            textField.fontSize *= FedoHudPlugin.Instance.PlayerDamageTextSizeMultiplier * tier.SizeFactor;
            textField.fontStyle |= FontStyles.Bold;
            if (tier.Tint.HasValue)
            {
                var tint = tier.Tint.Value;
                textField.color = new Color(tint.r, tint.g, tint.b, textField.color.a);
            }

            gui.AddComponent<DamageTextPunch>().Play(PunchDurationSeconds, tier.PunchOvershoot);
        }

        // Compare ce coup à la moyenne de mes coups récents (même arme/situation en
        // pratique) pour choisir un palier petit/normal/gros/énorme -- un seuil absolu
        // n'aurait aucun sens entre une hache en pierre et une épée en fer. Repli sur le
        // palier "normal" tant qu'on n'a pas assez d'historique (tout début de partie)
        // ou si le texte n'est pas un nombre (ne devrait pas arriver pour les types
        // filtrés par IsBoostableType, mais on ne throw jamais sur une valeur inattendue).
        private static Tier ResolveTier(string text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float damage) || damage <= 0f)
            {
                return Tiers[1];
            }

            Tier tier;
            if (RecentDamage.Count < RollingAverageMinSamples)
            {
                tier = Tiers[1];
            }
            else
            {
                float average = 0f;
                foreach (float value in RecentDamage)
                {
                    average += value;
                }

                average /= RecentDamage.Count;

                float ratio = average > 0f ? damage / average : 1f;
                tier = Tiers[Tiers.Length - 1];
                foreach (var candidate in Tiers)
                {
                    if (ratio <= candidate.MaxRatio)
                    {
                        tier = candidate;
                        break;
                    }
                }
            }

            RecentDamage.Enqueue(damage);
            if (RecentDamage.Count > RollingAverageWindow)
            {
                RecentDamage.Dequeue();
            }

            return tier;
        }
    }
}
