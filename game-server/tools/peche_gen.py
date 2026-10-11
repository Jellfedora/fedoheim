# Génère peche.cfg + dialogues olaf/eirik/torvald (remise via FinishQuest -> récompenses du fichier de quête)
import os
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(out, exist_ok=True)

# id, titre, texte, prefab, nombre, récompenses (hors XP/compétence), xp, niveaux Pêche, objets donnés à l'acceptation
# Biomes/appâts : voir la mémoire reference_valheim_peche (liste fournie par l'utilisateur)
Q = {
 "olaf": [
  ("peche_perches", "Les premières perches",
   "Une canne ? Pas si vite. D'abord, montre-moi que tu es débrouillard : ramasse-moi 3 perches de la prairie, sans canne. Comment ? Ça, c'est ton problème. Même un bourgeon y arriverait.",
   "Fish1", 3, "Item: FishingRod, 1 | Item: FishingBait, 40 | Item: FishCooked, 5", 50, 0, []),
  ("peche_perches2", "Dix perches",
   "Pas mal pour un débutant ! Maintenant, prends la canne que je t'ai donnée : lance la ligne dans l'eau, attends, tire. Pêche-moi 10 perches. Et ne les mange pas en route.",
   "Fish1", 10, "Item: Coins, 10 | Item: FishingBait, 40", 75, 3, []),
  ("peche_brochets", "Les brochets",
   "Les brochets vivent dans les eaux profondes de la prairie et de la Forêt noire. Tes appâts standards feront l'affaire. Pêche-m'en 10. Ils mordent aussi les doigts, fais attention.",
   "Fish2", 10, "Item: FishWraps, 3 | Item: FishingBaitForest, 20", 150, 3, []),
 ],
 "eirik": [
  ("peche_trolls", "Le poisson-troll",
   "Olaf t'a donné des appâts mousseux ? Parfait. Le poisson-troll vit dans les eaux de la Forêt noire, et il ne mord qu'à ça. Il est moche, mais il est bon. Rapporte-m'en 2.",
   "Fish5", 2, "Item: HelmetFishingHat, 1 | Item: MeadStaminaMinor, 3 | Item: TankardAnniversary, 1 | Item: FishingBaitSwamp, 20", 250, 3, []),
  ("peche_harengs", "Le hareng géant",
   "Le hareng géant vit dans les eaux des Marais. Prends les appâts gluants que je t'ai donnés. Rapporte-m'en 2. Oui, géant. Non, il ne mange pas les gens. Normalement.",
   "Fish6", 2, "Item: FishAndBread, 5", 400, 4, []),
  ("peche_tetras", "Les tétras",
   "Les tétras vivent dans les lacs des grottes glacées des Montagnes. Oui, des poissons dans des grottes. Ne me demande pas pourquoi. Il te faudra des appâts froids. Rapporte-m'en 2.",
   "Fish4_cave", 2, "Item: Coins, 15 | Item: MeadFrostResist, 2", 500, 4, []),
  ("peche_merous", "Les mérous",
   "Les mérous vivent dans les eaux des Plaines, et ils ne mordent qu'aux appâts brûlants. Rapporte-m'en 2. Attention aux moustiques pendant que tu pêches.",
   "Fish7", 2, "Item: FishAndBread, 3", 600, 4, []),
  ("peche_thons", "Les thons",
   "Le thon vit en pleine mer. Prends un bateau, des appâts lourds, et ne tombe pas à l'eau. Rapporte-m'en 2.",
   "Fish3", 2, "Item: Coins, 20 | Item: FishWraps, 3", 700, 5, []),
 ],
 "torvald": [
  ("peche_morues", "Les perches coralliennes",
   "Les perches coralliennes vivent en mer, comme les thons, et mordent aux mêmes appâts lourds. Rapporte-m'en 2. Et si tu croises un serpent de mer, rame vite.",
   "Fish8", 2, "Item: Ruby, 1 | Item: MeadHealthMedium, 2", 800, 5, []),
  ("peche_globes", "Les poissons-globes",
   "Le poisson-globe vit en pleine mer et dans les eaux profondes des Terres brumeuses. Il gonfle quand il a peur. Comme Eirik. Il te faudra des appâts brumeux. Rapporte-m'en 2.",
   "Fish12", 2, "Item: Coins, 25 | Item: MeadEitrMinor, 2", 900, 5, []),
  ("peche_baudroies", "La baudroie",
   "La baudroie vit dans les eaux des Terres brumeuses. Elle a une petite lumière sur la tête. Appâts brumeux, là aussi. Rapporte-m'en 2.",
   "Fish9", 2, "Item: Coins, 30 | Item: MeadHealthMajor, 2", 1000, 6, []),
  ("peche_magma", "Le poisson de magma",
   "Le poisson de magma vit dans les eaux brûlantes des Terres de cendres. Oui, il y a des poissons dans la lave. Il ne mord qu'aux appâts ardents. Rapporte-m'en 1.",
   "Fish11", 1, "Item: Coins, 30 | Item: AmberPearl, 2 | Item: FeastAshlands, 1", 2000, 8, []),
 ],
}

INTRO = {
 "olaf": "Chut ! Tu fais fuir les poissons ! Bon, ils étaient déjà partis. Tu veux apprendre à pêcher ?",
 "eirik": "Olaf t'envoie ? Bienvenue au port ! Olaf pêche des sardines. Moi je pêche des monstres. Enfin, des gros poissons.",
 "torvald": "Hé hé hé... Un petit nouveau ? Eirik t'envoie ? Ce gamin ne sait pas ce qu'est une vraie pêche.",
}
FIN = {
 "olaf": "Tu as l'étoffe d'un pêcheur ! Pour aller plus loin, va trouver Eirik, le maître pêcheur. Il vit dans un petit port à l'est de la stèle d'Eikthyr, là où tu as posé son trophée. Il pêche des choses plus grosses. Beaucoup plus grosses.",
 "eirik": "Pas mal du tout ! Il ne te reste plus qu'à voir Torvald, le vieux loup de mer. Il pêche dans des eaux où même les poissons ont peur.",
 "torvald": "Tu as pêché ce que je cherchais ! Tu es maintenant un vrai maître pêcheur. Je n'ai plus rien à t'apprendre. Va, et que la mer te soit douce.",
}
BRAVO = "Belle prise ! Tiens, ta récompense. Et lave-toi les mains, tu sens le poisson."
BYE = "Reviens quand tu auras quelque chose au bout de ta ligne."

quests, step = [], 0
for npc in ("olaf", "eirik", "torvald"):
    d = [f"[{npc}_accueil]", INTRO[npc]]
    offers = []
    qs = Q[npc]
    for i, (qid, title, text, prefab, n, rew, xp, lvl, gifts) in enumerate(qs):
        quests += [f"[{qid}]", "Collect", title, text, f"{prefab}, {n}",
                   (f"{rew} | Skill: Fishing, {lvl} | EpicMMO_EXP: {xp}" if lvl else f"{rew} | EpicMMO_EXP: {xp}"), "99999999", f"NotHasQuest, {qid}", ""]
        cond = (f"Condition: !CustomValueMore, peche_etape, 1" if step == 0
                else f"Condition: CustomValueMore, peche_etape, {step} | Condition: !CustomValueMore, peche_etape, {step + 1}")
        d.append(f"Text: Tu as du travail pour moi ? | {cond} | Condition: NotHasQuest, {qid} | AlwaysVisible: false | Transition: {qid}_offre")
        nxt = f"{npc}_fin" if i == len(qs) - 1 else f"{npc}_bravo"
        d.append(f"Text: Voilà ce que tu m'as demandé ! | Condition: HasQuest, {qid} | Condition: HasItem, {prefab}, {n} | AlwaysVisible: false"
                 f" | Command: FinishQuest, {qid} | Command: SetCustomValue, peche_etape, {step + 1} | Transition: {nxt}")
        give = "".join(f" | Command: GiveItem, {p}, {c}, 1" for p, c in gifts)
        offers += [f"[{qid}_offre]", text, f"Text: C'est parti ! | Command: GiveQuest, {qid}{give}", "Text: Plus tard.", ""]
        step += 1
    d.append(f"Text: Et maintenant ? | Condition: CustomValueMore, peche_etape, {step} | AlwaysVisible: false | Transition: {npc}_fin")
    if npc == "olaf":  # option de test, visible seulement avec Easy Spawner (outil admin, Greylist)
        allq = [q[0] for n in Q for q in Q[n]]
        rm = "".join(f" | Command: RemoveQuest, {q}, false" for q in allq)
        d.append(f"Text: [Admin] Recommencer la pêche | Condition: ModInstalled, cooley.easyspawner | AlwaysVisible: false | Command: SetCustomValue, peche_etape, 0{rm} | Transition: olaf_reset")
    d += ["Text: Je ne fais que passer. | Transition: " + f"{npc}_bye", ""]
    d += offers
    d += [f"[{npc}_bravo]", BRAVO, "Text: Merci !", "", f"[{npc}_fin]", FIN[npc], "Text: Merci !", "",
          f"[{npc}_bye]", BYE, "Text: Au revoir", ""]
    if npc == "olaf":
        d += ["[olaf_reset]", "Une canne ? Quelle canne ? Je ne t'ai jamais vu de ma vie. On reprend tout depuis le début !", "Text: D'accord", ""]
    open(os.path.join(out, f"{npc}.cfg"), "w", encoding="utf-8", newline="\n").write("\n".join(d))
open(os.path.join(out, "peche.cfg"), "w", encoding="utf-8", newline="\n").write("\n".join(quests))
print("ok, étapes:", step)
