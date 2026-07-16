# Revue d'architecture — WorldSim (état session 7)

## Contexte
Demande de l'utilisateur : audit de l'architecture existante et des règles CLAUDE.md,
avec conseils et warnings pour la suite (objectif : concurrent de WorldBox).
Aucune modification de code demandée — le livrable est la revue elle-même.

## Verdict global
Les fondations sont bonnes et les règles CLAUDE.md sont réellement appliquées dans le
code (vérifié : zéro alloc dans le tick + test qui le prouve, structs en tableaux plats,
double buffer feu, swap-with-last, data-driven, déterminisme testé multi-instances).
Trois mines sont à désamorcer AVANT la prochaine session (reproduction).

## 🔴 Mines à désamorcer maintenant

### 1. Identité des agents incompatible avec la compaction (bloquant pour la reproduction)
`CleanupDeadAgents` compacte par swap-with-last → l'index d'un agent change à chaque
mort. `MotherId`/`FatherId` (int) ne peuvent donc PAS être des index de tableau : ils
seraient corrompus dès la première mort. La reproduction est la prochaine étape du
journal → décider d'un schéma d'ID stable d'abord :
- Option recommandée : `uint Id` séquentiel unique par agent (compteur monotone dans
  World), MotherId/FatherId = ces Id. Lookup inverse seulement si nécessaire (les
  castes tracked du CLAUDE.md).
- Alternative : handle = slot + génération. Plus complexe, pas nécessaire ici.
Effet de bord du même problème : l'étalement de pensée `(i & 3)` change de groupe à
chaque compaction et a déjà produit les « morts groupées » notées en session 6 →
étaler par `Id & 3` une fois l'Id stable.

### 2. RNG unique partagé entre tous les systèmes
Feu, errance, végétation tirent tous sur le même `Rng`. Conséquence : ajouter,
retirer ou réordonner UN tirage dans UN système change la trajectoire de TOUT le
monde → chaque feature casse la comparabilité des seeds, les goldens, et rend les
bugs non-locaux. Correctif pas cher aujourd'hui, très cher plus tard :
un flux RNG par système (`new Rng(Mix(seed, systemId))`) : _rngFire, _rngAgents,
_rngVegetation, _rngWorldGen.

### 3. Hash() incomplet + aucun chemin de restauration
Le hash omet : `_burning`, `_activeCurrent` (feu en cours), `_tickCounter`,
l'état du RNG, et `_agentPaths` (les waypoints restants d'un agent Seeking sont
de l'état sim qui influence le futur). Deux états peuvent avoir le même hash et
diverger ensuite → le filet de sécurité déterminisme a des trous exactement là où
les bugs se cacheront. À étendre.
Lié : `World` ne sait que générer (seed) — pas de constructeur depuis snapshot.
Le save/load exige : (a) sérialisation de TOUT l'état (y compris RNG, tick,
burning, paths) en byte[] exposé par /Simulation (l'IO reste côté /Game),
(b) clé stable = noms JSON, jamais les ids numériques (si un id bouge dans
terrain.json, les saves cassent silencieusement).
Recommandé en CI : test golden-hash (N ticks, hash attendu commité) — attrape
les divergences Windows/Linux et les régressions de déterminisme par refactor.

## 🟠 Important, à traiter bientôt

4. **Constantes gameplay en dur dans World.cs** (SpreadChance, densités, faim,
   MoveSpeed…) — violation CLAUDE.md déjà notée au journal. Le vrai coût est
   apparu post-session 6 : chaque tuning recalcule les tick-counts des tests à
   la main. → `simulation.json` chargé comme les catalogues ; les tests lisent
   les mêmes valeurs.
5. **Rendu carte : 262 144 `SetPixel` interop par tick à 30 Hz.** Remplacer par
   un `byte[]`/buffer rempli côté C# + un seul `Image.SetData`/`Update` — c'est
   aussi l'occasion d'implémenter le `GetFramebuffer()` prévu dans la surface
   publique du CLAUDE.md (framebuffer indexé palette produit par la sim).
   Même logique pour AgentRenderer à ~2000 agents : `Multimesh.Buffer = float[]`
   en un appel au lieu de 2 appels interop par agent par frame.
6. **Famine : stampede + gel.** Un agent affamé sans buisson trouvable refait un
   BFS 33×33 complet à CHAQUE tick de pensée et reste immobile jusqu'à la mort.
   À 2000 agents en famine : ~16 M de visites de cases/s. → cooldown d'échec
   (prochain tick de recherche) + errance en attendant.
7. **WASM/AOT : `System.Text.Json` par réflexion ne survivra pas au trimming.**
   Passer aux source generators (`JsonSerializerContext`) — trivial maintenant,
   pénible avec 8 catalogues. Aussi : valider les ids dupliqués au Load (deux
   entrées même id = écrasement silencieux aujourd'hui).

## 🟡 À garder en tête (warnings, pas d'action immédiate)

8. **Discipline float** : la sim n'utilise que + − × ÷, Sqrt, Floor → toutes
   IEEE-exactes, reproductibles cross-platform. Règle à ajouter au CLAUDE.md :
   JAMAIS MathF.Sin/Cos/Pow/Exp dans /Simulation (implémentations différentes
   par plateforme → divergence WASM/x86/ARM).
9. **Repousse végétation** : scan scanline avec `return` quand le tableau est
   plein → biais spatial haut-gauche quand la capacité (5 %) sature. Et le
   monde converge vers la cendre : ash n'a aucune récupération → chaque feu
   réduit définitivement l'habitat. Prévoir ash→grass (lent, sur le tick lent).
10. **dt fixe** : `Tick(double delta)` n'est déterministe que si delta est
    constant. Rendre 1/30 une constante DE LA SIM que le renderer consomme.
11. **SpawnAgents** : rejection sampling sans borne → boucle infinie sur une
    carte quasi tout eau (seed dégénéré). Capper les essais.
12. **Couplage scène** : `GetNode("../WorldSprite")` + Seed/Size en dur dans
    WorldRenderer — acceptable en dev, à centraliser dans un nœud Game racine
    quand l'UI grossit.
13. **Xorshift bas bits** : `NextUInt64() & 3` (direction d'errance) utilise les
    bits faibles, les plus faibles du xorshift. Préférer `>> 62`.

## Aucun changement de code dans cette session
Le livrable est ce rapport. Si l'utilisateur veut enchaîner : la session 8
naturelle est le point 🔴 1+2+3 (IDs stables, flux RNG, hash complet + golden
test CI) AVANT d'écrire la reproduction.
