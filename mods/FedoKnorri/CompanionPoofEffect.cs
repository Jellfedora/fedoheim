using System;
using UnityEngine;

namespace FedoKnorri
{
    // Petite explosion de particules générée à la volée (pas d'asset externe) -- même technique
    // que FedoGoldRabbit.GoldRabbitBehaviour.PlayDespawnSmoke, reprise et rendue configurable en
    // couleur pour servir à la fois le nuage de fumée gris (invocation/rangement) et l'impact
    // vert du soin (voir CompanionHealOrb).
    internal static class CompanionPoofEffect
    {
        private static readonly Color DefaultSmokeColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);
        private static readonly Color DefaultSmokeFadeColor = new Color(0.6f, 0.6f, 0.6f);

        public static void Show(Vector3 position)
        {
            Show(position, DefaultSmokeColor, DefaultSmokeFadeColor);
        }

        public static void Show(Vector3 position, Color startColor, Color fadeColor)
        {
            try
            {
                var poofObj = new GameObject("FedoKnorri_Poof");
                poofObj.transform.position = position + Vector3.up * 0.3f;

                var ps = poofObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = false;
                main.duration = 0.5f;
                main.startLifetime = 0.8f;
                main.startSpeed = 1.2f;
                main.startSize = 0.5f;
                main.startColor = startColor;
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
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(fadeColor, 1f) },
                    new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = gradient;

                var renderer = poofObj.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                var shader = Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Legacy Shaders/Particles/Additive")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    renderer.material = new Material(shader);
                }

                ps.Play();
                UnityEngine.Object.Destroy(poofObj, 2f);
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionPoofEffect.Show failed: {e}");
            }
        }
    }
}
