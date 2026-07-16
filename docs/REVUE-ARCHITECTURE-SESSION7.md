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

---

# Addendum — cadrage pour la suite (process, design, frontières)

Complément au-delà du code actuel, destiné à l'IA qui codera les prochaines
sessions. À lire avec CLAUDE.md et JOURNAL.md.

## A. Trous concrets déjà présents dans le code (faciles à oublier)

- **Le feu est inoffensif pour les agents.** `TickFire` ne touche jamais
  `Agent[]`, une tuile en feu reste walkable, l'errance et le BFS traversent
  le feu sans réaction. C'est aujourd'hui un non-choix silencieux — à trancher
  explicitement (dégâts ? fuite ? tuile interdite ?) lors d'une session dédiée.
- **Un buisson (flammable=false) survit au feu et reste posé sur de la cendre**,
  alors que la repousse ne cible que l'herbe. Quirk cohérent mais à assumer
  ou corriger consciemment.
- Ces deux points illustrent la règle générale : **à chaque nouveau système,
  écrire dans le plan de session la matrice d'interaction avec CHAQUE système
  existant** (même pour dire « aucune »). Les combinaisons non écrites
  deviennent des bugs muets — et leur nombre croît au carré du nombre de
  systèmes (feu × bâtiments × agents × civs × pouvoirs…).

## B. Process de dev agentique (le vrai multiplicateur du projet)

- **Harnais headless.** Un runner console (dans /Tests ou un /Tools, jamais
  /Game) qui construit World, tick N milliers de fois et imprime un SimReport
  (pop, morts par cause, courbe de végétation, hash final). L'IA qui code doit
  pouvoir vérifier le COMPORTEMENT sans lancer Godot — aujourd'hui seule la
  boucle F5 humaine valide le gameplay réel, et le journal montre que c'est
  déjà le goulot (chaque session se termine par « non vérifié par moi »).
  C'est aussi l'outil de tuning : lancer 10 000 ticks et regarder la courbe
  de population vaut mieux que régler au ressenti.
- **ValidateInvariants() de debug**, appelée dans les tests : cohérence des
  tableaux miroirs (`_vegetationIndexAt` ↔ `_vegetation`, bientôt agents et
  bâtiments), compteurs, états légaux. Le pattern « tableau compacté + miroir
  tuile→slot » va se répliquer pour chaque type d'entité ; un miroir
  désynchronisé est une corruption silencieuse que le hash ne localise pas.
- **Budget de perf commité.** Un test qui mesure le coût d'un tick 512² peuplé
  et échoue au-delà d'un seuil (avec marge, côté /Tests — Stopwatch y est
  permis, pas dans /Simulation). Le dev agentique régresse la perf sans la
  voir ; un chiffre en CI la protège.
- **L'ordre des systèmes dans Tick() est un contrat** (déterminisme, saves,
  replays). Le documenter en un seul endroit et ne le changer que sciemment,
  jamais au fil d'un refactor.

## C. Design des prochains systèmes — pièges connus d'avance

- **Généalogie ≠ Agent[].** Les parents meurent et sont compactés : la lignée
  doit vivre dans un stockage séparé (record de naissance : Id, MotherId,
  FatherId, tick de naissance/mort) — rétention à vie pour les castes tracked,
  ring buffer borné pour les anonymes (déjà prévu dans CLAUDE.md, mais à ne
  PAS confondre avec le tableau des vivants au moment de coder la
  reproduction).
- **Reproduction régulée par capacité de charge : attendre des oscillations.**
  Une régulation à seuil produit naturellement des dents de scie démographiques
  (boom → famine → effondrement → boom). Prévoir une réponse progressive
  (probabilité de naissance qui décroît à l'approche de la capacité) plutôt
  que binaire, et une politique explicite quand Agent[] est plein : refuser
  les naissances, jamais agrandir le tableau.
- **Deuxième espèce = régime alimentaire en JSON.** La recherche de nourriture
  hardcode aujourd'hui « buisson mûr » pour tout le monde — correct avec une
  seule espèce, faux dès la deuxième. Le jour où Species ≠ 0 existe, le régime
  (types de végétation ciblés, seuils) passe dans un species.json. Pas avant.
- **Pouvoirs divins = des ICommand, rien d'autre.** L'identité de WorldBox,
  c'est les jouets destructifs que le joueur lâche sur une sim qui réagit.
  SpawnFire est le modèle exact : chaque pouvoir passe par les systèmes
  existants (feu, terrain, agents), jamais de logique spéciale dans World
  pour un pouvoir. Si un pouvoir a besoin d'un chemin spécial, c'est le
  système sous-jacent qui manque.
- **Vitesse de jeu (pause/×2/×3) : toujours N ticks par frame avec le même dt
  fixe, jamais un dt plus grand.** Et rendre une seule fois par frame quel que
  soit N — le rendu par tick devient le goulot dès ×3.

## D. Frontière sim/rendu — à verrouiller avant qu'elle ne s'érode

- **Toute mutation externe passe par Execute(ICommand).** `SetTerrainId`,
  `ForceSpawnVegetation`, `SetAgentHunger` sont des seams de test : les passer
  `internal` + `InternalsVisibleTo("Tests")`. Sinon l'UI ou le rendu finira
  par muter le monde hors commandes, et le replay (seed + log de commandes =
  reproduction de bug gratuite + format de save alternatif) sera mort avant
  d'exister.
- **Dirty list de présentation, pas d'events gameplay.** Quand les bâtiments
  seront peints dans la texture « au changement de tier » (CLAUDE.md), le
  rendu devra savoir quoi repeindre sans balayer la carte : une liste de
  tuiles sales produite par la sim, consommée et vidée par le rendu, purement
  présentation. À ne pas confondre avec les events gameplay (interdits par
  CLAUDE.md : les capacités se dérivent) — le gameplay dérive, le rendu peut
  consommer une dirty list.
- **Centraliser la conversion écran→tuile.** Le clic suppose aujourd'hui
  position monde = pixel = tuile (sprite à l'origine, échelle 1) ; ça cassera
  dès que la scène bouge ou qu'un HUD arrive. Une seule fonction, utilisée
  par tous les outils/brosses à venir (et les brosses voudront du
  cliquer-glisser, pas juste le clic simple actuel).
