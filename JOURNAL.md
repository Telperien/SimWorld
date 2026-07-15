# Journal

## Session 1 — Squelette du projet
- Fait : /Simulation (classlib net8.0, sans Godot) et /Tests (xUnit) créés,
  ajoutés à la solution, référencés depuis WorldSim.csproj avec exclusion du
  glob (Compile Remove Simulation/** et Tests/**). `dotnet build` et
  `dotnet test` passent depuis la racine.
- Cassé : rien de connu.
- Prochaine fois : v0 — terrain généré + rendu (Image 512²) + caméra
  zoom/pan. Vérifier à l'ouverture que Godot charge bien le projet sans
  erreur (non vérifié dans cette session, pas de binaire Godot disponible
  en CLI).

## Session 2 — Le monde en mémoire, headless
- Fait : Rng (Xorshift64, classe pour éviter la copie silencieuse d'un
  struct mutable), PerlinNoise écrit à la main, TerrainCatalog data-driven
  (data/terrain.json, résolution par nom jamais par id en dur), World(seed,
  size, catalog) avec rejet des tailles non puissance de 2 et Hash()
  FNV-1a. 4 tests verts (même seed → même hash, seed différent → hash
  différent, taille invalide rejetée, les 4 terrains sont chargés).
  Trouvé et corrigé en cours de route : `ImplicitUsings=enable` injecte
  silencieusement `global using System.IO` dans Simulation.csproj —
  contournait la règle CLAUDE.md sans qu'aucune ligne de code ne le
  montre ; retiré via `<Using Remove="System.IO" />`.
- Cassé : rien de connu.
- Prochaine fois : v0.1 — couche ICommand + feu (sonde pour interroger la
  simu) + clic. Le côté Godot n'a toujours pas de loader qui lit
  data/terrain.json ni de rendu ; à faire quand le rendu (Image 512²)
  arrivera.

## Session 3 — Premiers pixels
- Fait : Main.tscn (Sprite2D + Camera2D) devient la main_scene ; F5 lance
  directement le terrain généré par World(seed 42, size 512). WorldRenderer
  (scripts/) lit World/TerrainCatalog une fois en _Ready et peint une Image
  pixel par pixel. CameraController gère le pan aux flèches et le zoom par
  paliers {1,2,4,8} à la molette, position caméra arrondie au pixel à
  chaque frame. Filtre de texture Nearest et stretch aspect "keep" réglés
  dans project.godot. Confirmé par `dotnet msbuild -getItem:Compile` que
  seuls les deux scripts de /scripts sont compilés côté WorldSim (pas de
  fuite de /Simulation ou /Tests). Tests session 2 toujours verts.
- Cassé : rien de connu côté build/tests.
- Prochaine fois : v0.1 — couche ICommand + feu. Non vérifié par moi :
  le rendu réel à l'écran, le zoom/pan en main, l'absence de
  scintillement pixel art — pas de binaire Godot en CLI sur cette
  machine pour un test headless, à confirmer via F5.

## Session 4 — Commandes + feu
- Fait : ICommand + SpawnFire(x,y,radius) dans /Simulation. World gère
  désormais le feu en double buffer (_activeCurrent/_activeNext, des
  List<int> pour un ordre d'itération reproductible), propagation aux 4
  voisines avec chance 0.5 tirée du même Rng seedé que la génération de
  terrain (déterminisme bout en bout), une tuile en feu devient cendre
  après exactement un tick. Ajout du terrain "ash" dans terrain.json
  (walkable, non flammable). Nouveaux accesseurs World.SetTerrainId
  (seam de test indépendant du bruit de Perlin) et World.IsBurning.
  WorldRenderer tick la simulation à 30 Hz via un accumulateur à pas
  fixe, redessine toute l'image à chaque tick (pas de dirty-tracking,
  reporté comme prévu), réutilise la même Image/ImageTexture via
  Update() plutôt que d'en recréer à chaque frame. Clic gauche →
  GetGlobalMousePosition() → SpawnFire(rayon 3), sans calcul de caméra
  à la main. 8 tests verts (4 précédents + 4 nouveaux sur la
  propagation, l'arrêt à l'eau, la transformation en cendre, le
  déterminisme multi-instances).
- Cassé : rien de connu côté build/tests. Point de vigilance réel :
  262 144 SetPixel par tick à 30 Hz en continu (feu actif ou non) —
  si c'est perceptiblement lent en jeu, la solution est le
  dirty-tracking déjà identifié et reporté.
- Prochaine fois : v0.2 — agents (spawn, errance, faim, mort,
  reproduction régulée par capacité de charge). Non vérifié par moi :
  le rendu du feu à l'écran (couleur orange, disparition en cendre),
  le ressenti de fluidité à 30 Hz, la conversion clic→tuile en jeu réel
  avec zoom/pan — à confirmer via F5.

## Session 5 — Les agents existent
- Fait : struct Agent (position continue X/Y float pour un mouvement
  fluide, TargetX/TargetY pour la tuile visée, MotherId/FatherId/Tracked
  prévus mais inutilisés, State Idle/Moving, Species toujours 0). World
  alloue Agent[] une fois (densité × surface, ~200 agents à 512², jamais
  un nombre absolu en dur) et spawne uniquement sur tuiles walkable via
  le même Rng seedé que la génération de terrain. Mise à jour étalée :
  la décision Idle→Moving n'a lieu que pour 1/4 des agents par tick
  (index % 4), l'interpolation de mouvement avance à chaque tick pour
  rester fluide à 30 Hz (Tick(delta) utilise enfin son paramètre delta).
  Hash() étendu aux positions/états des agents (déterminisme vérifié
  bout en bout). Rendu via un seul MultiMeshInstance2D (AgentRenderer.cs,
  QuadMesh 3x3) qui lit World en lecture seule, sans influence sur la
  simulation. 12 tests verts (8 précédents + 4 nouveaux : spawn sur
  walkable uniquement, densité dans la fourchette attendue, déterminisme
  multi-instances, et Tick_StillAllocatesNothing — zéro octet alloué sur
  50 ticks avec agents actifs, testé volontairement sans feu actif pour
  isoler ce qui est vérifié).
- Cassé : rien de connu côté build/tests.
- Prochaine fois : v0.2 suite — faim, mort, reproduction régulée par
  capacité de charge, SimReport (rapport texte pop/civs/anomalies). Non
  vérifié par moi : le rendu réel des agents à l'écran (points visibles,
  mouvement perceptible, un seul draw call sans ralentissement avec
  terrain 512² + feu + ~200 agents) — à confirmer via F5.

## Session 6 — Faim, nourriture, mort, FSM complète
- Fait : Agent gagne Hunger (byte, incrément fixe par tick de réflexion —
  délai de mort exact et déterministe, pas d'accumulation flottante) et
  EatingTimer. FSM étendue à Idle/Moving/Seeking/Eating/Dead. Nourriture
  data-driven (terrain.json : foodCapacity=100 sur grass, 0 ailleurs),
  stockée comme une simple date de dernier repas par tuile et **dérivée**
  à la demande (min(capacité, ticksÉcoulés × régénération)) — jamais de
  balayage complet pour la régénération, cohérent avec la règle "capacités
  dérivées, jamais stockées" déjà dans CLAUDE.md. Recherche de nourriture :
  BFS 4-directionnelle bornée à un rayon fixe (16, boîte locale 33x33)
  directement sur la grille de terrain — pas de grille spatiale séparée,
  justifié dans le plan (la ressource est déjà une propriété de tuile,
  donc déjà indexée en O(1) ; le vrai A* avec heuristique n'aurait rien
  vers quoi pointer tant qu'il n'y a pas de cible connue à l'avance).
  Chemin stocké par agent dans une List<int> réutilisée (jamais de
  Dictionary/HashSet). Nettoyage en fin de tick : swap-with-last O(1) par
  mort, avec échange en miroir des listes de chemin pour rester associées
  au bon agent après le swap. Rendu : couleur par état (rouge/orange/vert),
  VisibleInstanceCount suit AliveCount (les morts disparaissent sans
  reconstruire le MultiMesh). 15 tests verts (12 précédents + 3 nouveaux :
  mort après délai exact ~512 ticks calculé à la main, recherche de
  nourriture observée en Seeking, extinction totale sur carte sans
  nourriture) ; le test de déterminisme allongé à 550 ticks pour traverser
  tout le FSM ; Tick_StillAllocatesNothing inchangé (sa fenêtre de mesure
  est plus courte que le délai d'apparition de la faim, donc jamais
  affectée par le nouveau chemin BFS).
- Cassé : rien de connu côté build/tests. À noter (pas un bug) : sur une
  carte sans nourriture, tous les agents meurent au même tick — incrément
  de faim fixe et étalement parfaitement synchronisé entre les 4 groupes,
  donc "délai fixe" produit une mort groupée plutôt qu'étalée.
- Prochaine fois : reproduction régulée par la capacité de charge, castes/
  traits, SimReport. Non vérifié par moi : rendu réel des couleurs par
  état, disparition visuelle des agents morts, fluidité générale avec
  faim/recherche/repas actifs — à confirmer via F5.

## Ajustement post-session 6 — vitesse et rythme de la faim
- Fait (retour F5) : MoveSpeed doublée (2 → 4 tuiles/s). HungerIncreasePerThink
  divisé par 2 (2 → 1, la faim monte ~2x plus lentement). La faim ne
  retombe plus instantanément à 0 en arrivant sur la nourriture : elle
  descend progressivement pendant tout Eating (HungerDecreasePerEatTick=8
  sur EatingDuration=20 ticks), pour que "se nourrir" se voie comme un
  processus plutôt qu'un reset. Tick counts des tests de faim/mort/
  extinction recalculés en conséquence (délai de mort désormais ~1020
  ticks). Ces constantes restent en dur dans World.cs, pas encore dans le
  JSON — à garder en tête si d'autres ajustements de ressenti suivent.
- Cassé : rien de connu, 15 tests toujours verts.

## Session 7 — Végétation et agents en sprite 3x3
- Fait : le mécanisme de "nourriture dérivée par tuile" (session 6) est
  remplacé par de vraies entités Vegetation (buisson/arbre), stockées en
  tableau préalloué compacté par swap-with-last (même pattern que
  Agent[]/AliveCount), plus un tableau miroir _vegetationIndexAt (tuile →
  slot, -1 = rien) pour un lookup O(1) sans Dictionary. Buissons : jeune
  → mûr sur un tick séparé, beaucoup plus lent (~1 Hz, toutes les 30
  tuiles-tick) ; mangés, ils redeviennent jeunes plutôt que disparaître
  (ressource renouvelable au même endroit, cohérent avec la capacité de
  charge à venir). Repousse spontanée : balayage complet des tuiles
  d'herbe vides, mais explicitement hors du chemin chaud 30 Hz (règle
  CLAUDE.md — un tick séparé peut se permettre ce que le tick tuiles ne
  peut pas). Arbres : 0 nourriture (réservés au futur système de bois),
  poussent plus rarement/lentement, flammable=true dans vegetation.json
  et réellement branchés sur TickFire — un arbre est détruit quand sa
  tuile finit de brûler, un buisson (flammable=false) survit. La
  recherche de nourriture des agents (BFS bornée, session 6) cible
  maintenant les buissons mûrs au lieu des tuiles-nourriture ; manger
  remet le buisson à Stage=0 et calcule EatingTimer depuis le FoodValue
  du JSON (foodValue / HungerDecreasePerEatTick), sans nouveau champ sur
  Agent. Rendu : agents en sprite ASCII 3x3 asymétrique (".#.","##.",".#.")
  parsé une fois en texture blanche/transparente, partagée par toutes les
  instances MultiMesh — la teinte par état (session 6) continue de
  fonctionner par multiplication. Variante gauche/droite = flip du
  Transform2D (échelle X) selon le nouveau champ Agent.Facing, pas deux
  textures. Végétation peinte dans la texture de la carte (statique,
  pas MultiMesh), une passe proportionnelle à VegetationCount après le
  terrain+feu. 19 tests verts (15 précédents adaptés à la nouvelle
  signature World(seed,size,catalog,vegetationCatalog) + 4 nouveaux :
  croissance jeune→mûr, repousse spontanée, arbre détruit par le feu,
  agent qui mange un buisson voit sa faim baisser).
- Cassé : rien de connu côté build/tests. Point technique découvert en
  testant : à très petite taille de monde (32² avec la densité
  d'agents actuelle), la capacité peut tomber à 0 agent — corrigé en
  session en utilisant 64² pour le test qui a besoin d'au moins un agent.
- Prochaine fois : reproduction régulée par la capacité de charge
  (maintenant que la nourriture est une vraie ressource spatiale, la
  capacité de charge peut se brancher dessus), castes/traits, SimReport.
  Non vérifié par moi : le sprite 3x3 et son flip à l'écran, les
  couleurs de végétation sur la carte, l'apparition/repousse visible des
  buissons, la disparition des arbres en feu — à confirmer via F5.
