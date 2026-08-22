using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FedoDeath
{
    // Garde le gardien totalement inerte (IA désactivée, ne bouge pas, n'attaque personne) tant
    // qu'aucun joueur n'est entré dans son rayon d'activation. Une fois activé, il se met à
    // chasser spécifiquement le joueur (SetHuntPlayer), sans jamais s'en prendre à d'autres mobs.
    // Fait aussi suivre un pin de mort sur la carte tant qu'il est vivant, pour ne pas le perdre
    // s'il se déplace.
    public class GraveGuardianActivator : MonoBehaviour
    {
        private static readonly FieldInfo DeathPinField = AccessTools.Field(typeof(Minimap), "m_deathPin");

        private BaseAI _ai;
        private bool _activated;
        private Minimap.PinData _pin;

        private void Awake()
        {
            try
            {
                _ai = GetComponent<BaseAI>();
                if (_ai != null)
                {
                    _ai.enabled = false;
                }

                if (Minimap.instance != null)
                {
                    // Retire spécifiquement le pin de mort automatique du jeu (m_deathPin) --
                    // une suppression par position/rayon peut le manquer si sa position exacte
                    // diffère légèrement.
                    var vanillaDeathPin = DeathPinField?.GetValue(Minimap.instance) as Minimap.PinData;
                    if (vanillaDeathPin != null)
                    {
                        Minimap.instance.RemovePin(vanillaDeathPin);
                        DeathPinField.SetValue(Minimap.instance, null);
                    }

                    // save:true -- sans ça, le pin est traité comme un simple overlay système et
                    // n'a pas l'option "supprimer" dans le menu de la carte.
                    _pin = Minimap.instance.AddPin(transform.position, Minimap.PinType.Death, "", true, false, 0, default);
                }
                else
                {
                    FedoDeathPlugin.Log?.LogWarning("FedoDeath: Minimap.instance is null, no pin created for the guardian.");
                }

                InvokeRepeating(nameof(Tick), 1f, 1f);
            }
            catch (Exception e)
            {
                FedoDeathPlugin.Log?.LogError($"FedoDeath: GraveGuardianActivator.Awake failed: {e}");
            }
        }

        private void Tick()
        {
            try
            {
                UpdateDeathPin();

                if (!_activated)
                {
                    CheckActivation();
                }
            }
            catch (Exception e)
            {
                FedoDeathPlugin.Log?.LogError($"FedoDeath: GraveGuardianActivator.Tick failed: {e}");
            }
        }

        private void UpdateDeathPin()
        {
            if (Minimap.instance == null || _pin == null)
            {
                return;
            }

            float moved = Vector3.Distance(_pin.m_pos, transform.position);
            if (moved < 0.5f)
            {
                return;
            }

            // Retirer puis reposer le pin (plutôt que de muter sa position) pour être sûr que
            // l'icône se redessine bien au bon endroit sur la carte.
            Minimap.instance.RemovePin(_pin);
            _pin = Minimap.instance.AddPin(transform.position, Minimap.PinType.Death, "", true, false, 0, default);
        }

        private void CheckActivation()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            float range = FedoDeathPlugin.Instance != null ? FedoDeathPlugin.Instance.ActivationRange.Value : 20f;
            if (Vector3.Distance(transform.position, player.transform.position) > range)
            {
                return;
            }

            Activate();
        }

        private void Activate()
        {
            if (_activated)
            {
                return;
            }

            _activated = true;

            if (_ai != null)
            {
                _ai.enabled = true;
                _ai.SetHuntPlayer(true);
            }
        }
    }
}
