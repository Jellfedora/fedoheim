using UnityEngine;

namespace FedoHud
{
    // Petit "pop" d'échelle (démarre survolté, se stabilise) ajouté sur un texte de
    // dégâts déjà créé par le jeu -- voir PlayerDamageTextBoost.cs, qui l'attache
    // seulement quand c'est le joueur local qui vient de frapper. Se désactive tout
    // seul une fois l'animation terminée ; le texte reste ensuite géré normalement par
    // DamageText.UpdateWorldTexts (déplacement à l'écran, fondu), qui ne touche jamais
    // à `localScale`.
    internal class DamageTextPunch : MonoBehaviour
    {
        private float _duration;
        private float _overshoot;
        private float _timer;
        private Vector3 _baseScale;
        private bool _playing;

        public void Play(float duration, float overshoot)
        {
            _duration = duration;
            _overshoot = overshoot;
            _timer = 0f;
            _baseScale = transform.localScale;
            _playing = true;
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);
            float scale = Mathf.Lerp(_overshoot, 1f, easeOut);
            transform.localScale = _baseScale * scale;

            if (t >= 1f)
            {
                transform.localScale = _baseScale;
                _playing = false;
            }
        }
    }
}
