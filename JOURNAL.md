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

## Session 8 — Fondations d'identité et de déterminisme
- Fait : Agent.Id (uint, compteur monotone _nextAgentId) — identité
  stable et permanente, distincte de la position dans le tableau.
  MotherId/FatherId passent de int(-1) à uint(Agent.UnknownParent =
  uint.MaxValue), toujours inutilisés avant la reproduction (session
  10). Mise à jour étalée corrigée : (i & 3) → (agent.Id & 3), un agent
  ne change plus de groupe de pensée quand la compaction déplace un
  autre agent. Rng unique remplacé par 4 flux dérivés du seed principal
  (_rngWorldGen/_rngFire/_rngAgents/_rngVegetation, via le même mélange
  FNV que Hash()) — ajouter un tirage dans un système ne dérange plus
  la trajectoire des autres. Tirage de direction d'errance corrigé :
  NextUInt64() & 3 (bits faibles du xorshift) → NextUInt64() >> 62
  (bits forts). Hash() étendu pour couvrir tout ce qui manquait :
  _burning, _activeCurrent, _tickCounter, _nextAgentId, l'état des 4
  Rng (nouvelle propriété Rng.State), _agentPaths, et les champs
  d'agent qui n'étaient pas encore couverts (Id, MotherId, FatherId,
  Tracked, Species, Facing). Les buffers de travail de la recherche BFS
  restent exclus de Hash() (documenté en commentaire + CLAUDE.md :
  entièrement écrasés à chaque appel, n'influencent jamais le futur).
  Golden-hash test ajouté (seed+taille fixes, feu+agents+végétation sur
  5000 ticks, hash committé en dur). CLAUDE.md : interdiction de
  MathF.Sin/Cos/Pow/Exp dans /Simulation (non garantis cross-plateforme,
  seuls +,-,*,/,Sqrt,Floor autorisés — déjà respecté, vérifié par grep) ;
  règle Id-jamais-index formalisée. 21 tests verts (19 précédents + Id
  qui survit à plusieurs morts/compactions + golden-hash).
  Sur les "morts groupées" (hypothèse notée en session 6/7, à vérifier
  cette session) : le fix de l'étalement par Id NE change PAS le
  comportement du test d'extinction (toujours vert avec les mêmes
  comptes de ticks) — confirme l'analyse du plan : dans ce scénario
  aucun agent ne meurt avant l'instant synchronisé final, donc aucune
  compaction ne se produit avant pour perturber les groupes de pensée.
  La cause réelle reste l'incrément de faim fixe sans aucune variance.
  Le bug d'étalement par index était réel et valait la peine d'être
  corrigé (il aurait causé une dérive dans un scénario à morts
  échelonnées), mais ce n'était pas la cause de ce phénomène précis.
- Cassé : rien de connu, build + tests verts partout (Simulation, Tests,
  WorldSim). Aucun script Godot ne lit MotherId/FatherId/Id, donc aucun
  changement côté /scripts.
- Prochaine fois : reproduction régulée par la capacité de charge
  (session 10 selon le découpage donné), castes/traits, SimReport. Point
  d'attention pour la suite : je ne peux pas exécuter la CI Linux
  moi-même — si le golden-hash test passe en local (Windows) et échoue
  en CI après un push, ce sera une vraie divergence flottante
  cross-plateforme à investiguer, pas un test à neutraliser.

## Session 9 — Outils de tuning : simulation.json + SimReport
- Fait : les 14 constantes de gameplay recensées dans le plan (chance de
  feu, densités, faim, vitesse, recherche de nourriture, seuils de
  génération de terrain) sortent de World.cs vers data/simulation.json
  (SimulationConfig.cs, désérialisation directe avec propriétés
  `required` — une valeur manquante fait échouer le chargement plutôt
  que de silencieusement valoir 0). MaxSearchRadius n'étant plus une
  constante de compilation, les buffers de recherche BFS déménagent des
  field initializers vers le corps du constructeur (alloués depuis
  config.MaxFoodSearchRadius). Les tests lisent désormais la même config
  que World au lieu de recalculer des tick-counts à la main : nouveau
  helper TicksUntilHungerThreshold(config, seuil) dans AgentTests.cs,
  qui remplace les litéraux 1000/1040/1080/700 — un futur tuning dans le
  JSON n'imposera plus de retoucher les tests. Validation des ids
  dupliqués ajoutée à TerrainCatalog/VegetationCatalog (throw explicite
  au lieu d'un écrasement silencieux). SpawnAgents borné (×10 tentatives
  max), nouveau flag AgentSpawnCapped (pas de log direct — /Simulation
  n'écrit jamais sur la console, c'est à l'appelant de le faire).
  World.TickIntervalSeconds (const public) remplace la constante locale
  dupliquée de WorldRenderer.cs — source unique du pas de temps.
  Nouveau projet /Tools/SimReport (console, référence /Simulation
  uniquement, PEUT utiliser System.IO/Console contrairement à
  /Simulation) : CLI --seed/--ticks/--size, échantillonne population +
  buissons/arbres + tuiles herbe/cendre (compteurs GrassTileCount/
  AshTileCount entretenus en O(1), jamais un balayage par tick) sur ~20
  points, imprime un tableau texte compact + morts par cause
  (DeathCause, une seule valeur Hunger pour l'instant — compteur de
  diagnostic, hors Hash() comme précisé) + hash final. Piège de glob
  Godot anticipé : <Compile Remove="Tools/**" /> ajouté avant même de
  lancer un premier build, vérifié par dotnet msbuild -getItem:Compile
  (seuls les 3 scripts existants, rien de Tools/Simulation/Tests). 21
  tests toujours verts, y compris le golden-hash — **hash inchangé**,
  confirme qu'aucun comportement n'a bougé pendant l'extraction.
  SimReport --seed 42 --ticks 50000 --size 512 : 0,14s, population
  stable à 199 (zéro mort — carte riche en 11537 buissons + 1570 arbres,
  la végétation sature sa capacité de 13107 dès le tick 2500 puis se
  fige, aucune régénération de tuile n'existe encore côté herbe/cendre).
  Comportement cohérent avec l'absence de reproduction : sans naissance
  ni mort significative, la population ne peut que rester plate ou
  descendre, jamais monter.
- Cassé : rien de connu, build + tests verts sur les 4 projets
  (Simulation, Tests, SimReport, WorldSim).
- Prochaine fois : reproduction régulée par la capacité de charge —
  c'est le système qui va enfin faire bouger cette courbe de
  population plate. Non vérifié par moi : rien de nouveau côté F5 cette
  session (aucun changement de gameplay), simulation.json chargé
  correctement par WorldRenderer à confirmer quand même à l'ouverture.

## Session 10 — Santé du monde
- Fait : nourriture par buisson enfin finie. Vegetation.FoodRemaining
  (stock partagé, initialisé à FoodValue), récolte étalée sur plusieurs
  ticks (HarvestAmountPerTick retire la même quantité au stock ET à la
  faim de l'agent — récolter et se nourrir sont le même geste), le
  buisson disparaît à 0 au lieu de redevenir jeune. Agent.EatingTimer
  (précalculé) supprimé, devenu inutile. Repousse à délai
  (_vegetationClearedTick, posé au même endroit pour épuisement ET feu
  — un point unique) + biais spatial haut-gauche corrigé (point de
  départ tournant tiré de _rngVegetation, balayage Size² à partir de ce
  point avec modulo, toujours borné). Cendre → herbe (TickAshRecovery,
  jet indépendant par tuile, pas de biais possible faute de capacité à
  saturer). Cooldown de famine (Agent.SeekCooldown) : ThinkAgent
  restructurée pour qu'un agent affamé en attente de cooldown tombe
  dans le bloc d'errance commun au lieu de rester figé — c'est ce
  correctif qui rend tout le reste observable. Matrice d'interaction
  écrite avant codage (cf. plan) : confirmé qu'un buisson en cours de
  récolte ne réagit pas au feu (flammable=false depuis la session 7,
  vérifié en relisant le code) et que le fallback "cible disparue
  pendant Seeking" (session 6) couvrait déjà la disparition par
  épuisement sans modification. SimulationConfig passe de class à
  record (support de l'expression `with`, utilisée par --scarcity et
  les tests de rareté). SimReport : buissons jeunes/mûrs séparés,
  répartition par quadrant, distribution des états d'agents, compteur
  cumulé de repas, flag --scarcity (agentDensity ×4, vegetationDensity
  ÷10 — vérifié empiriquement que ça tue vraiment, pas cosmétique).
  27 tests verts (21 précédents + 6 nouveaux : buisson qui disparaît,
  repousse non instantanée, répartition équilibrée par quadrant,
  cendre qui récupère, agent qui erre au lieu de geler, morts réelles
  en scénario de rareté).
  **Golden-hash recalculé** (comportement changé légitimement, comme
  attendu) : 1527739277831296971 → 8812310094165850180.
- Cassé : rien de connu, build + tests verts sur les 4 projets.
- Prochaine fois : reproduction régulée par la capacité de charge —
  la nourriture finie + la repousse à délai donnent enfin un vrai
  signal de pression à réguler dessus. SimReport (seed 42, 50k ticks) :
  scénario normal — population 199→186 (13 morts, contre 0 en session
  9), buissons mûrs déclinent lentement face aux arbres qui grignotent
  les emplacements libérés (aucune mort d'arbre sans feu), quadrants
  équilibrés (2989-3494, plus de biais systématique), 14184 repas
  cumulés. Scénario --scarcity — extinction totale (786→0, 786 morts
  de faim) : la rareté demandée n'est pas cosmétique. Non vérifié par
  moi : rendu réel (buissons qui apparaissent/disparaissent
  visiblement, cendre qui repousse en herbe, agents qui errent au lieu
  de geler) — à confirmer via F5.

## Session 11 — Équilibre du monde
- Fait : diagnostic obligatoire d'abord (SimReport 500k ticks, 3 seeds)
  — hypothèse du cliquet arbres confirmée sans ambiguïté avant tout
  code (arbres jamais plafonnés, jusqu'à 8859 à 500k ticks, population
  quasi éteinte sur les 3 seeds). Fix retenu : Option A seule (durée de
  vie des arbres), pas B (séparer les tableaux ne résout rien de plus
  une fois les arbres mortels — voir raisonnement dans le plan).
  Vegetation.DeathTick (tick absolu, -1 = immortel par l'âge — les
  buissons le restent, ils sortent déjà par la consommation).
  VegetationType.LifespanTicks/LifespanVarianceTicks dans
  vegetation.json (0 pour bush, 60000±20000 pour tree — valeur ajustée
  empiriquement via SimReport, pas devinée). TickVegetationAging
  (nouveau, même patron swap-scan que CleanupDeadAgents) retire les
  arbres arrivés à échéance dans le même bloc tick lent que croissance/
  repousse/cendre. Matrice d'interaction écrite avant code : confirmé
  qu'un arbre ne peut pas mourir de vieillesse pendant qu'il brûle
  (TickFire s'exécute avant TickVegetationAging dans le même Tick(),
  le feu a déjà retiré l'instance si applicable), qu'un arbre mort
  libère de l'herbe jamais de la cendre (la cendre est exclusivement un
  produit du feu), et que les agents en Seeking ne sont jamais impactés
  (TryFindNearestMatureBush ne cible que les buissons, confirmé).
  --scarcity recalibré APRÈS le fix (comme demandé) : AgentDensity/
  VegetationDensity ajustés à 0.0011/0.03 après plusieurs essais réels
  (pas estimés) jusqu'à obtenir un déclin qui ralentit nettement au
  lieu d'un massacre total. SimReport gagne --fire/--fire-interval/
  --fire-radius (positions tirées d'un Rng local au rapport, seedé sur
  --seed — stimulus externe comme un clic joueur, hors Hash()) et deux
  compteurs cumulés (TilesBurnedCumulative, VegetationLostToFire,
  diagnostics comme MealsEaten — hors Hash()). CLAUDE.md : règle
  "aucune accumulation à sens unique" ajoutée sous Boucle de simulation.
  31 tests verts (27 précédents + 4 nouveaux : arbre qui meurt et
  libère sa case en laissant de l'herbe, arbres qui ne s'accumulent
  plus indéfiniment sur 500k ticks, population qui survit sur 500k
  ticks sur 2 seeds, plus les tests existants revérifiés).
  **Golden-hash recalculé** (comportement changé légitimement) :
  8812310094165850180 → 1977737263434058813.
- Cassé : rien de connu, build + tests verts (8s en config par défaut,
  2s en Release, y compris les tests à 500k ticks).
- Prochaine fois : reproduction régulée par la capacité de charge —
  maintenant que la nourriture respire vraiment (arbres plafonnés,
  buissons qui remontent), c'est le bon moment. SimReport 500k ticks,
  seed 42 : AVANT (session 10, non plafonné) — arbres 0→8859 sans
  jamais redescendre, population 199→5. APRÈS (ce fix) — arbres
  montent puis REDESCENDENT (pic ~3030 vers 50k, 898 à 500k), buissons
  mûrs remontent en miroir (10024→12193), population 199→66 (bien
  mieux, mais toujours en déclin lent — normal sans reproduction :
  aucune naissance, seulement des sorties). --scarcity recalibré,
  seed 42 : 288→26 sur 500k ticks avec ralentissement net de la pente
  (contre 786→0 avant) — pas un plateau parfaitement stable (impossible
  sans naissances), mais un vrai signal de pression qui ralentit plutôt
  qu'un massacre. --fire --fire-interval 3000 --fire-radius 8 sur 50k
  ticks : 9240 tuiles brûlées cumulées, 183 végétation perdue au feu,
  cendre qui fluctue et repousse en herbe visible dans le rapport (le
  ash→grass de la session 10 tourne enfin en conditions réelles). Non
  vérifié par moi : rendu réel des arbres qui meurent/disparaissent
  visiblement — à confirmer via F5.

## Session 12 — Diagnostic : pourquoi les agents meurent de faim entourés de nourriture
- Fait : instrumentation pure (aucun fix). Autopsie de mort ajoutée
  (distance globale au buisson mûr le plus proche, terrain, échecs de
  recherche, répartition Idle/Moving/Seeking/Eating, faim au dernier
  repas commencé) — champs diagnostiques sur Agent, histogrammes/sommes
  sur World, tous exclus de Hash(). 2M ticks, seed 42 + seed 7 : 93-95%
  des morts de faim ont un buisson mûr <33 tuiles (portée BFS) au
  moment de la mort → budget énergétique, pas désert spatial. Cause
  arithmétique identifiée : l'incrément de faim (World.cs:528) tourne
  sans condition à chaque tick de pensée, y compris pendant le
  SeekCooldown — 8,1-8,2 échecs consécutifs en moyenne × 11 points
  (10 ticks de cooldown + 1 de recherche) ≈ 90 points brûlés, soit 86%
  de la marge (150→255) rien que dans la boucle échec-attente. Bonus
  (étape 3) : la dérive spatiale de la végétation (test qui passe à
  court terme) ne suit PAS la répartition de l'herbe sur les deux
  seeds — vraie dérive de repousse, direction différente par seed
  (dynamique auto-amplifiante, pas un biais directionnel fixe).
- Cassé : rien. 31/31 tests verts, golden-hash inchangé (confirmé, pas
  supposé) — l'instrumentation n'a aucune influence comportementale.
- Prochaine fois (session 13, décidé ensemble) : fix du cooldown de
  faim (geler l'accumulation pendant l'attente, ou réduire son coût)
  + séparation arbres/buissons en deux tableaux (option B session 11,
  le cliquet des arbres est inversé mais pas cassé — cause racine :
  tableau partagé à capacité fixe).

## Session 13 — Le fix : budget de faim, repousse locale, errance dirigée
- Fait : arbres/buissons séparés en deux tableaux à capacité indépendante
  (ne se disputent plus les slots) ; repousse locale par diffusion
  depuis la végétation existante (comme le feu, double-buffer implicite)
  + germination spontanée résiduelle à taux bas (piège symétrique :
  une région rasée peut toujours repartir) ; errance idle dirigée
  (WanderDirection/WanderTicksRemaining, incluse dans Hash()) au lieu
  d'une marche aléatoire pure — vérifié empiriquement (~800 agents,
  5 seeds) : déplacement net ≈1.5x celui d'une marche aléatoire pure ;
  cooldown de recherche 10→3 ticks de pensée. Trouvaille imprévue en
  cours de route : la diffusion locale seule, à la capacité historique
  (5%), crée une course au plafond global entre buissons — quelques
  gros amas gagnent presque toute la capacité, laissant de vrais
  déserts ailleurs (clusterisation mesurée à 35-42 tuiles, PIRE
  qu'avant). Diagnostiqué en élevant expérimentalement le plafond :
  au-delà de la course, l'équilibre naturel (récolte + délai de
  repousse vs diffusion) se stabilise seul, bien en dessous du
  plafond, si celui-ci est assez généreux. Retenu : bushDensity
  0.05→0.3 (équilibre naturel observé ~78 590, identique sur les deux
  seeds, donc pas un artefact de plafond) ; treeDensity laissé à 0.02
  (les arbres n'ont pas cet équilibre naturel sous-plafond — ils
  occupent tout plafond donné, mais restent un vrai plateau stable,
  ni ratchet vers 0 ni extinction, ce qui était le seul bug réel de
  s11). Résultat 2M ticks : seed 42 → 11 morts de faim (198 avant),
  clusterisation 1,15 (contre 35+ avec la capacité historique) ; seed
  7 → 1 mort de faim, clusterisation 0,70. Scarcity et feu revérifiés
  (feu : 2 morts/199, cendre repousse normalement ; scarcity : forte
  pression comme voulu, sans rapport avec le critère "conditions
  normales").
- Cassé : rien de comportemental non voulu. 38/38 tests verts,
  golden-hash recalculé et signalé (nouveaux champs Agent inclus,
  nouvelle logique de repousse — changement de comportement assumé).
- Prochaine fois : la densité de buissons ~66% de l'herbe (nécessaire
  pour éviter la course au plafond) est peut-être trop dense
  visuellement/thématiquement — à juger une fois le rendu réel en
  place. Si des morts de faim résiduelles apparaissent à plus grande
  échelle (2000 agents visés), reconsidérer MaxFoodSearchRadius
  (non touché cette session, comme convenu).
