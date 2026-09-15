namespace FedoHud
{
    // Nom d'une compétence traduit dans la langue actuellement configurée par le joueur
    // -- même formule que le panneau de compétences natif du jeu (décompilé depuis
    // SkillsDialog.Setup) : une clé "$skill_xxx" traduite via GameLocalization.cs (qui
    // porte l'appel à Localization.instance, classe globale vivant dans
    // assembly_guiutils.dll, référencé dans le .csproj).
    internal static class SkillLocalization
    {
        public static string GetName(Skills.SkillType type)
        {
            return GameLocalization.LocalizeOrRaw("$skill_" + type.ToString().ToLowerInvariant());
        }
    }
}
