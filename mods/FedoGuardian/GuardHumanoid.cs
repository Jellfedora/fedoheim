namespace FedoGuardian
{
    // Humanoid nu, sans aucune des particularités de Player (animation de réveil, liste statique
    // des joueurs, messages HUD tournés vers un humain aux commandes, montée de compétences...).
    // Character.Message/RaiseSkill sont déjà des no-op par défaut pour un Humanoid non-joueur et
    // non-apprivoisé -- rien à couper explicitement ici, contrairement à l'ancienne approche à base
    // de patches Harmony sur Player.
    public class GuardHumanoid : Humanoid
    {
        protected override void Awake()
        {
            base.Awake();

            // Faction.Players : hostile aux monstres sauvages comme un vrai joueur, jamais aux
            // autres personnages de faction Players (son propriétaire, d'autres joueurs, d'autres
            // gardes) -- cf. BaseAI.IsEnemy, qui court-circuite dès que les deux factions sont
            // identiques. Pas persisté par le jeu : réappliqué à chaque Awake (rechargement de
            // zone, redémarrage serveur...).
            m_faction = Faction.Players;
            m_name = FedoGuardianPlugin.Instance.GuardianNameTemplate.Value;
        }

        public override string GetHoverText()
        {
            return m_name + "\n" + FedoGuardianPlugin.Instance.HoverHintText.Value;
        }

        public override string GetHoverName()
        {
            return m_name;
        }

        // Le corps physique (collider/rigidbody) reste celui du joueur cloné, pas d'un monstre --
        // Character.GetRadius() a un branchement différent selon IsPlayer() (renvoie directement
        // le rayon du collider pour un joueur, ou le rayon multiplié par l'échelle sinon). En
        // renvoyant false par défaut (Humanoid non-joueur), le garde annonçait un rayon
        // ~1.73x trop grand (échelle (1,1,1) : magnitude = racine de 3) aux autres personnages,
        // faussant leurs calculs de distance/évitement -- vécu : les monstres approchant le garde
        // glissaient au sol au lieu de marcher normalement. Comme le corps physique EST bien celui
        // d'un joueur, il faut garder cette branche.
        public override bool IsPlayer()
        {
            return true;
        }
    }
}
