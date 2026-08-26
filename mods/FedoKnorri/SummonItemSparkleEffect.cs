using System;
using UnityEngine;

namespace FedoKnorri
{
    // Petites particules violettes en boucle greffées à demeure sur le clone du charme (voir
    // SummonItemPrefabPatch), plutôt que déclenchées ponctuellement comme CompanionPoofEffect
    // (une explosion, jouée une fois à un instant précis) -- Unity copie les enfants d'un
    // GameObject à chaque Instantiate(), donc attacher ce ParticleSystem une seule fois sur le
    // clone-gabarit suffit à ce que TOUTE instance réelle de l'item (posé au sol, jeté...)
    // l'affiche automatiquement, sans code de spawn dédié. Aucun effet tant que l'item reste en
    // inventaire : seul ItemDrop.ItemData (une simple structure) le représente à ce moment, pas
    // de GameObject vivant sur lequel ce ParticleSystem pourrait tourner.
    internal static class SummonItemSparkleEffect
    {
        private static readonly Color SparkleColor = new Color(0.75f, 0.4f, 1f, 1f);
        private static readonly Color SparkleFadeColor = new Color(0.3f, 0.05f, 0.55f);

        // playOnAwake seul n'est pas fiable pour un ParticleSystem ajouté par code sur un
        // objet resté inactif un moment (le gabarit, sous le conteneur désactivé de Jotunn) --
        // Awake() est bien rappelé une fois l'objet activé pour de vrai (clone réel posé au
        // sol), mais rien ne garantit dans tous les cas que Play() soit effectivement relancé
        // à ce moment précis pour un composant ajouté à l'exécution plutôt que configuré dans
        // l'éditeur. Ce petit composant dédié force un Play() explicite dans OnEnable, qui se
        // déclenche à coup sûr à chaque fois que l'objet redevient actif (contrairement à
        // Awake, qui ne s'exécute qu'une seule fois dans la vie de l'objet).
        private sealed class ForcePlayOnEnable : MonoBehaviour
        {
            private ParticleSystem _ps;

            private void Awake()
            {
                _ps = GetComponent<ParticleSystem>();
            }

            private void OnEnable()
            {
                _ps?.Play();
            }
        }

        public static void Attach(GameObject target)
        {
            try
            {
                var sparkleObj = new GameObject("FedoKnorri_Sparkle");
                sparkleObj.transform.SetParent(target.transform, worldPositionStays: false);
                sparkleObj.transform.localPosition = Vector3.up * 0.15f;

                var ps = sparkleObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.startLifetime = 1.5f;
                main.startSpeed = 0.2f;
                main.startSize = 0.12f;
                main.startColor = SparkleColor;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                // Léger modificateur de gravité négatif : les particules dérivent doucement
                // vers le haut au lieu de tomber, comme une poussière magique.
                main.gravityModifier = -0.02f;

                var emission = ps.emission;
                emission.rateOverTime = 10f;

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.3f;

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(SparkleColor, 0f), new GradientColorKey(SparkleFadeColor, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = gradient;

                var renderer = sparkleObj.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                // Pas de Shader.Find/new Material ici : repéré en jeu, "Desired shader
                // compiler platform ... is not available in shader blob" -- ce build ne
                // contient pas forcément de variante compilée pour un shader cherché par nom,
                // même quand Shader.Find renvoie un objet Shader non nul (les métadonnées
                // existent, mais pas le programme compilé pour cette plateforme). On laisse
                // donc le ParticleSystemRenderer fraîchement créé sur le matériau que Unity lui
                // assigne par défaut -- une ressource du moteur, jamais sujette au stripping de
                // shaders d'un projet, garantie de fonctionner quel que soit le build. La
                // couleur/dégradé configurés plus haut (colorOverLifetime) s'appliquent par
                // multiplication de couleur de sommet, ce qui marche sur n'importe quel shader
                // de particules, y compris celui-ci.
                FedoKnorriPlugin.Log?.LogInfo($"FedoKnorri: particules du charme configurées, matériau par défaut '{renderer.material?.name ?? "aucun"}' (shader '{renderer.material?.shader?.name ?? "aucun"}').");

                sparkleObj.AddComponent<ForcePlayOnEnable>();
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: SummonItemSparkleEffect.Attach failed: {e}");
            }
        }
    }
}
