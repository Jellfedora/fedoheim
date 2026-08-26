using UnityEngine;

namespace FedoKnorri
{
    // Petit orbe qui vole du compagnon vers le joueur avant qu'il ne soit vraiment soigné --
    // équivalent visuel du jet de caillou d'un nain gris, mais entièrement recréé en code
    // (`GameObject.CreatePrimitive`, pas d'asset externe) : le compagnon n'a pas d'animation
    // d'attaque à distance fiable à déclencher ici (Greyling clone un Character nu, pas un
    // Humanoid, et Attack.m_character est typé Humanoid -- le vrai système d'attaque vanilla ne
    // s'applique pas à lui), et deviner un nom de trigger d'Animator n'est pas vérifiable sans
    // décompiler l'AnimatorController (un asset, pas du code).
    public class CompanionHealOrb : MonoBehaviour
    {
        private const float FlightDurationSeconds = 0.6f;
        private const float OrbDiameter = 0.4f;
        private const float TargetHeightOffset = 1f;

        private static readonly Color ImpactColor = new Color(0.5f, 1f, 0.6f, 0.9f);
        private static readonly Color ImpactFadeColor = new Color(0.2f, 0.6f, 0.3f);

        private Vector3 _startPosition;
        private Transform _target;
        private Character _healTarget;
        private float _healAmount;
        private float _timer;

        public static void Launch(Vector3 startPosition, Character healTarget, float healAmount)
        {
            if (healTarget == null)
            {
                return;
            }

            var orbObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orbObj.name = "FedoKnorri_HealOrb";
            orbObj.transform.position = startPosition;
            orbObj.transform.localScale = Vector3.one * OrbDiameter;

            // Traverse tout, ne doit jamais physiquement percuter/pousser quoi que ce soit --
            // c'est un simple effet visuel, le vrai soin est appliqué directement par code.
            Object.Destroy(orbObj.GetComponent<Collider>());

            var renderer = orbObj.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null && renderer != null)
            {
                renderer.material = new Material(shader) { color = new Color(0.5f, 1f, 0.6f, 1f) };
            }

            var orb = orbObj.AddComponent<CompanionHealOrb>();
            orb._startPosition = startPosition;
            orb._target = healTarget.transform;
            orb._healTarget = healTarget;
            orb._healAmount = healAmount;
        }

        private void Update()
        {
            if (_target == null)
            {
                Destroy(gameObject);
                return;
            }

            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / FlightDurationSeconds);

            Vector3 targetPoint = _target.position + Vector3.up * TargetHeightOffset;
            transform.position = Vector3.Lerp(_startPosition, targetPoint, t);

            if (t < 1f)
            {
                return;
            }

            if (_healTarget != null && !_healTarget.IsDead())
            {
                _healTarget.Heal(_healAmount, showText: true);

                Vector3 impactPosition = _healTarget.transform.position + Vector3.up * TargetHeightOffset;
                CompanionPoofEffect.Show(impactPosition, ImpactColor, ImpactFadeColor);
                FedoKnorriPlugin.Instance.PlayHealImpactSound(impactPosition);
            }

            Destroy(gameObject);
        }
    }
}
