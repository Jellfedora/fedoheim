using BepInEx;
using UnityEngine;

namespace HelloFedo
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class HelloFedoPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.hellofedo";
        public const string PluginName = "HelloFedo";
        public const string PluginVersion = "1.0.0";

        private GUIStyle _style;

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 32,
                    alignment = TextAnchor.UpperCenter,
                    fontStyle = FontStyle.Bold
                };
                _style.normal.textColor = Color.white;
            }

            var rect = new Rect(0, 20, Screen.width, 60);
            GUI.Label(rect, "Hello Fedo", _style);
        }
    }
}
