using System;
using UnityEngine;

namespace FedoGoldRabbit
{
    // Ajouté uniquement sur les instances du prefab dédié "Fedo_GoldRabbit" (cf. GoldRabbitAwakePatch).
    // Gère le drop périodique de piastres pendant que la créature est vivante, le cri de fuite à la
    // Lapin Blanc, et le despawn sans loot si personne ne le tue à temps. Le loot de mort (butin
    // configurable, pas de viande/peau) est géré directement via la table CharacterDrop.m_drops, le
    // jeu s'en charge tout seul à la mort du personnage.
    public class GoldRabbitBehaviour : MonoBehaviour
    {
        private ItemDrop.ItemData _coinTemplate;
        private float _lastShoutTime = -999f;

        private void Awake()
        {
            try
            {
                _coinTemplate = FedoGoldRabbitPlugin.Instance.GetCoinItemTemplate();
                ScheduleNextDrop();

                if (FedoGoldRabbitPlugin.Instance.ShowGoldenAura.Value)
                {
                    AddGoldenAura();
                }

                if (FedoGoldRabbitPlugin.Instance.TintFurGolden.Value)
                {
                    ApplyGoldenTint();
                }

                float lifetime = FedoGoldRabbitPlugin.Instance.LifetimeSeconds.Value;
                if (lifetime > 0f)
                {
                    Invoke(nameof(AnnounceDespawn), lifetime);
                }
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: GoldRabbitBehaviour.Awake failed: {e}");
            }
        }

        // N'est jamais appelé si la créature a déjà été tuée entre-temps : sa mort détruit ce
        // GameObject (et donc ce component), ce qui annule automatiquement les Invoke en attente.
        // Laisse le temps de lire la bulle avant le poof (cf. DespawnWithoutLoot).
        private void AnnounceDespawn()
        {
            try
            {
                if (Chat.instance != null)
                {
                    Chat.instance.SetNpcText(gameObject, Vector3.up * 0.3f, 15f, 3f, "", FedoGoldRabbitPlugin.Instance.DespawnShoutText.Value, false);
                }
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: AnnounceDespawn failed: {e}");
            }

            Invoke(nameof(DespawnWithoutLoot), 2f);
        }

        // Pas d'effet de mort du jeu ici (m_deathEffects) -- il déclenchait une pose/anim de mort
        // sur le modèle, ce qui contredit le "j'ai trouvé mon terrier". Le nuage de fumée est un
        // ParticleSystem à nous (cf. PlayDespawnSmoke), qui ne touche jamais au personnage lui-même.
        private void DespawnWithoutLoot()
        {
            try
            {
                PlayDespawnSmoke();
                GetComponent<ZNetView>()?.Destroy();
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: DespawnWithoutLoot failed: {e}");
            }
        }

        // Bouffée de fumée en un coup (burst), grise, indépendante du personnage -- destinée à se
        // survivre à la destruction du lièvre le temps de finir de s'estomper.
        private void PlayDespawnSmoke()
        {
            try
            {
                var smokeObj = new GameObject("GoldRabbitDespawnSmoke");
                smokeObj.transform.position = transform.position + Vector3.up * 0.3f;

                var ps = smokeObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = false;
                main.duration = 0.5f;
                main.startLifetime = 0.8f;
                main.startSpeed = 1.2f;
                main.startSize = 0.5f;
                main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.25f;

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 1f) },
                    new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = gradient;

                var renderer = smokeObj.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                var shader = Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Legacy Shaders/Particles/Additive")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    renderer.material = new Material(shader);
                }

                ps.Play();
                UnityEngine.Object.Destroy(smokeObj, 2f);
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: PlayDespawnSmoke failed: {e}");
            }
        }

        // Petit nuage de particules dorées autour du lièvre -- pas d'asset custom : un
        // ParticleSystem généré au runtime avec un shader intégré au moteur, avec repli en
        // cascade si le premier choix a été retiré du build (shader stripping).
        private void AddGoldenAura()
        {
            try
            {
                var auraObj = new GameObject("GoldRabbitAura");
                auraObj.transform.SetParent(transform, false);
                auraObj.transform.localPosition = Vector3.up * 0.2f;

                var ps = auraObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.startLifetime = 1.5f;
                main.startSpeed = 0.25f;
                main.startSize = 0.06f;
                main.startColor = new Color(1f, 0.84f, 0.2f, 0.9f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 40;

                var emission = ps.emission;
                emission.rateOverTime = 10f;

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.35f;

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.65f, 0.05f), 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = gradient;

                var renderer = auraObj.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                var shader = Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Legacy Shaders/Particles/Additive")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    renderer.material = new Material(shader);
                }
                else
                {
                    FedoGoldRabbitPlugin.Log?.LogWarning("FedoGoldRabbit: no usable particle shader found, golden aura may not render correctly.");
                }
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: AddGoldenAura failed: {e}");
            }
        }

        // Teinte le modèle du lièvre lui-même en doré (pas juste les particules autour) --
        // instancie les matériaux de son visuel (via .materials, qui les clone automatiquement)
        // pour ne pas affecter les autres Hare du monde qui partagent le même prefab de base.
        private void ApplyGoldenTint()
        {
            try
            {
                var tint = new Color(1f, 0.8f, 0.2f);

                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    // Ignore le rendu de l'aura de particules (ajoutée séparément par
                    // AddGoldenAura) -- elle a déjà sa propre couleur/dégradé, pas question de
                    // l'écraser ici.
                    if (renderer is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    foreach (var material in renderer.materials)
                    {
                        if (material.HasProperty("_Color"))
                        {
                            material.color = tint;
                        }

                        if (material.HasProperty("_EmissionColor"))
                        {
                            material.EnableKeyword("_EMISSION");
                            material.SetColor("_EmissionColor", tint * 0.25f);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: ApplyGoldenTint failed: {e}");
            }
        }

        public void TryShout()
        {
            float cooldown = FedoGoldRabbitPlugin.Instance.FleeShoutCooldown.Value;
            if (Time.time - _lastShoutTime < cooldown)
            {
                return;
            }

            _lastShoutTime = Time.time;

            if (Chat.instance == null)
            {
                return;
            }

            Chat.instance.SetNpcText(gameObject, Vector3.up * 0.3f, 15f, 4f, "", FedoGoldRabbitPlugin.Instance.FleeShoutText.Value, false);
        }

        // Invoke (plutôt qu'InvokeRepeating) car l'intervalle doit varier aléatoirement à chaque
        // tick (2-3s configurables) et pas rester fixe.
        private void ScheduleNextDrop()
        {
            float min = FedoGoldRabbitPlugin.Instance.CoinDropIntervalMin.Value;
            float max = FedoGoldRabbitPlugin.Instance.CoinDropIntervalMax.Value;
            float delay = UnityEngine.Random.Range(Mathf.Min(min, max), Mathf.Max(min, max));
            Invoke(nameof(DropCoin), delay);
        }

        private void DropCoin()
        {
            try
            {
                if (_coinTemplate != null)
                {
                    int min = FedoGoldRabbitPlugin.Instance.CoinDropAmountMin.Value;
                    int max = FedoGoldRabbitPlugin.Instance.CoinDropAmountMax.Value;
                    int amount = UnityEngine.Random.Range(Mathf.Min(min, max), Mathf.Max(min, max) + 1);
                    ItemDrop.DropItem(_coinTemplate, amount, transform.position, transform.rotation);
                    FedoGoldRabbitPlugin.Instance.PlayCoinSound(transform.position);
                }
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: DropCoin failed: {e}");
            }

            ScheduleNextDrop();
        }
    }
}
