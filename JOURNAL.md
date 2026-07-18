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

## Session 14 — Reproduction, boom-bust, gradient de nourriture terrain-aware
- Fait : reproduction complète (species.json, âge/sexe/gestation sur
  Agent, grille grossière d'agents réutilisée pour recherche de
  partenaire + frein progressif par capacité de charge locale,
  naissances avec repli tuile sûre/tableau plein). Diagnostic
  boom-bust : le crash de population est une mortalité de faim
  massive (pas un écho de cohorte, ageD<30/intervalle partout vs
  faimD~3000 aux crashs) causée par une famine LOCALE — les
  naissances s'agglutinent, rasent leur voisinage plus vite que le
  délai de repousse, et créent des déserts hors de portée du BFS
  (±16) alors que 66% de la carte reste couverte. Décidé : l'oscillation
  boom-bust est le comportement voulu (Lotka-Volterra), pas un plateau
  à chercher — seule la CÉCITÉ (mourir sans avoir perçu une ressource
  abondante à 40 tuiles) est un bug. Fix : champ de gradient de
  nourriture diffusé sur la grille grossière (même patron double-buffer
  que le feu), suivi par un agent dont le BFS local échoue. Trouvaille
  en cours de route : la première version diffusait uniformément à
  travers tout terrain walkable, y compris le sable (jamais porteur de
  buisson) — un agent pouvait être attiré à travers un désert létal
  vers un amas lointain plutôt qu'une source proche (morts sur sable
  18,7%→61%). Corrigé en pondérant la diffusion par la conductivité de
  chaque cellule (fraction d'herbe, plancher 0,05) : le sable atténue
  le signal au lieu de le laisser passer intact.
  Résultat final (2M ticks) : morts de faim "vraiment aveugles" (aucun
  signal BFS ni gradient au dernier cycle de décision) à 9,5% (seed 42)
  et 6,1% (seed 7), contre 58%/54% avant le gradient. Oscillation
  bornée confirmée sur 2M ticks complets (aucune naissance refusée sur
  les deux seeds une fois AgentCapacityMultiplier relevé 15→40 — la
  régulation par capacité de charge locale, pas un plafond de tableau,
  gouverne bien la population).
  Mesure demandée (question 2b, pas un fix) : corrélation
  agents/végétation par quadrant mitigée selon le seed — nette
  corrélation inverse sur seed 42 (BG : le plus d'agents, le moins de
  végétation), mais sur seed 7 le quadrant HG a presque aucun agent
  ET la plus faible végétation — le broutage n'explique pas tout,
  un biais spatial pré-existant (s12/s13) semble aussi jouer.
  `Vegetation_SpatialDistribution_IsBalanced` passe maintenant sur les
  deux seeds sans y avoir touché (la dynamique a changé assez pour que
  le test, inchangé, passe).
- Cassé : rien de comportemental non voulu. 8 tests longs (2M ticks)
  + suite complète verts, golden-hash recalculé et signalé à chaque
  changement de comportement (reproduction, puis gradient, puis
  conductivité terrain).
- Prochaine fois : la corrélation agents/végétation mitigée sur seed 7
  mérite un vrai suivi si la clusterisation redevient un sujet actif.
  Foyers (prochaine session) vont délibérément agglutiner les agents —
  toute calibration fine de l'équilibre nourriture/population faite
  maintenant serait invalidée, donc pas touché (bushDensity,
  MaxFoodSearchRadius, formule de reproduction).

## Session 15 — Échelles de temps de la végétation
- Fait : la végétation partageait la bande temporelle de la faim
  (repousse 30s ≈ faim→cherche 20s ≈ faim→mort 34s) et
  `bushDensity=0.3` (rustine s13, 66% de l'herbe) tuait toute
  compétition spatiale et la sonde feu (1,7% détruite). Changement
  couplé par conservation explicite (production = repousse × valeur
  nutritive doit rester constante) : délai de repousse ×10 (900→9000
  ticks), maturation buisson ×10 (5→50 stades = 50s), foodValue ×10
  (160→1600, compense exactement) — `HarvestAmountPerTick` inchangé,
  un buisson tient maintenant plusieurs repas au lieu d'être une
  bouchée. Densité balayée empiriquement (0,05/0,10/0,15/0,20 sur 2M
  ticks + feu) : 0,05 et 0,10 → extinction totale de la population
  (effondrement de capacité de charge, pas un problème de cécité) ;
  0,15 survit de justesse (creux à 1 agent) ; **0,20 retenue** (creux
  jamais sous ~1950 sur le balayage initial, clusterisation ~20-30
  contre 39 à 0,05). Arbres : durée de vie ×10 (600000±200000 ticks,
  5h33±1h51) + taux de diffusion/spontané séparés des buissons pour la
  première fois (`TreeSpreadChance`/`TreeSpontaneousChance`) —
  premier calibrage à 10x plus bas que les buissons est resté saturé
  au plafond (la diffusion locale croît multiplicativement avec la
  population existante, un ratio proportionnel au taux buisson ne
  suffit pas) ; recalibré en visant l'équilibre population désiré
  plutôt qu'un ratio fixe → `0.00001`/`0.000002`, confirmé non saturé
  (fluctuation réelle observée 2968→3875 sur un run). Bug découvert en
  implémentant : un monde neuf démarrait sans aucun buisson mûr, et à
  50s de maturation, TOUTE la population initiale mourait de faim (34s)
  avant qu'un seul buisson n'ait eu le temps de pousser. Fix :
  `SeedInitialVegetation()`, appelée en fin de constructeur, plante
  directement à maturité jusqu'à la capacité de chaque tableau (même
  balayage tournant déterministe que la germination spontanée) — un
  monde généré démarre maintenant "déjà établi", comme la génération de
  terrain elle-même, pas une graine. Conséquence directe : la
  colonisation initiale (~83s avant cette session) est maintenant
  **instantanée** (le monde est mûr dès le tick 0) — plus un
  transitoire de démarrage à observer, un état de départ assumé.
  `AgentCapacityMultiplier` relevé 40→80 après avoir observé un pic à
  7959 dangereusement proche de l'ancien plafond 7960 pendant le
  balayage de densité.
  Effet de bord de `SeedInitialVegetation` sur les tests : plusieurs
  tests construisaient un petit monde puis plaçaient de la végétation
  au cas par cas (`ForceSpawnVegetation`) ou supposaient une carte
  vierge/sans nourriture — désormais fausse dès la construction. Deux
  fixes : `ForceSpawnVegetation` libère maintenant un slot arbitraire
  du même type si le tableau (capacité fixe, zéro marge) est déjà
  plein au lieu de planter hors limites ; nouveau `ClearAllVegetation()`
  (seam de test) appelé dans les scénarios qui exigent une carte
  réellement vierge (`MakeFoodless`, couples de reproduction isolés,
  désert contrôlé pour le test du gradient).
  Mesures finales (2M ticks, seeds 42/7, feu interval=20000 radius=6,
  densité 0,20) : creux jamais sous 756 (seed 7) / 1239 (seed 42) —
  bien au-dessus du plancher de sécurité (50) ; morts aveugles
  (`BlindWander`) à 6,4% (seed 42) / 7,2% (seed 7), contre un plafond
  resserré 20%→15% ; feu détruit 4,4%/5,4% du pic de végétation contre
  1,7% avant cette session ; clusterisation 23,6/20,7 (en hausse vs
  s14d, attendu — paysage en patches, plus un tapis) ; morts en transit
  vers une source connue (`FollowingGradient`) 40,0%/41,2% — budget de
  faim toujours dominé par les trouvailles directes (BFS) et le
  gradient, pas par l'errance aveugle.
- Cassé : golden-hash (nouvelles constantes + nouveaux champs config
  arbre + `SeedInitialVegetation`), recalculé et signalé.
  `Vegetation_SpatialDistribution_IsBalanced` (pré-existante, pas dans
  le périmètre des tests s15) : tolérance élargie 0,5x-1,5x → 0,15x-3x
  de la moyenne — la clusterisation accrue est un objectif assumé de
  cette session (paysage lisible), pas une régression à corriger.
  `Trees_StabilizeOverLongRun` : borne resserrée 0,8x-1,25x entre 1M et
  2M ticks retirée (les arbres fluctuent réellement maintenant) au
  profit d'un plafond de saturation (<90% capacité) qui fusionne avec
  le rôle de `Trees_ArrayIsNotSaturated` du plan, évitant un doublon de
  run 2M ticks. 49 tests verts (suite complète, y compris les runs
  longs).
- Prochaine fois : `Vegetation_SpatialDistribution_IsBalanced` reste
  un test à tolérance large plutôt qu'une vraie mesure de qualité de
  paysage — si la lisibilité du paysage redevient un sujet actif
  (rendu réel), le remplacer par une mesure plus directe (ex. taille
  des patches) plutôt que d'élargir encore la tolérance. Foyers
  (prochaine session) : `SeedInitialVegetation` change la donne pour la
  distribution spatiale de départ — à revérifier une fois les foyers
  en place, ils vont interagir avec un monde déjà mûr dès le tick 0,
  pas un monde qui se peuple progressivement.

## Session 17b — Diagnostic terrain + lisibilité visuelle

### Partie 1 — Diagnostic (mesure, zéro fix)
- **Connectivité de l'herbe confirmée** : `AnalyzeGrassConnectivity()`
  (flood-fill) trouve **12 poches** sur seed 42 (tailles 158 à 75063,
  médiane 2037) et **8 poches** sur seed 7 (158 à 115921, médiane
  1258) — l'hypothèse de départ tient : chaque lac ceinturé de sable
  isole bien l'herbe en îlots. Nombre de poches **stable sur les 2M
  ticks complets** (12/12/12/12 et 8/8/8/8) : la topologie ne change
  jamais, seul le contenu (buisson ou non) fluctue.
- **Pas de cliquet confirmé** : poches sans aucun buisson 6→4→3→6
  (seed 42) et 5→3→4→4 (seed 7) sur les 4 points de contrôle — ça
  fluctue, ça ne dérive PAS vers un maximum croissant. La règle
  CLAUDE.md ("aucune ressource ne doit disparaître localement sans
  pouvoir revenir localement") tient, contrairement à la crainte de
  départ.
- **Corrélation quadrant** : mitigée, comme déjà observé en s14d.
  Seed 42 : HD a le moins de végétation (7424) ET le plus haut ratio
  de poches sans buisson (3/5). Seed 7 : BD a le moins de végétation
  (7055) mais 0/1 poche sans buisson — son déficit vient de la TAILLE
  des poches locales, pas de leur état, donc pas une corrélation
  propre et généralisable.
- **Feu** : ~62-64 événements terminés sur 2M ticks (100 tentatives
  d'allumage), taille moyenne **~1120-1266 tuiles**, max ~4200-4260 —
  le feu brûle bel et bien significativement QUAND il prend, mais
  jamais au-delà de sa poche (îlot). Sur toutes les tentatives de
  propagation ratées : **~49% bloquées par du terrain non-inflammable
  (coupe-feu), ~51% par un tirage de probabilité raté** — le coupe-feu
  naturel est un contributeur réel et quasi aussi important que le
  hasard pur.
- **Couplage arbre/buisson** : confirmé mais modeste — les arbres
  n'occupent que **~2,9% de l'herbe totale** (3396-3548 tuiles sur
  118611-122823) une fois stabilisés (s15). Le couplage capacité
  buisson/arbre existe (même tuile, un seul occupant) mais n'explique
  qu'une fraction marginale de la disponibilité de buisson.
- **Cendre→herbe trop rapide** : confirmé et quantifié — guérison en
  **~1500 ticks (50s)**, soit ~6× plus rapide que le délai de repousse
  du buisson (9000 ticks/300s, s15) et du même ordre que la
  maturation de l'arbre (900 ticks/30s). Les cicatrices de brûlure
  guérissent bien trop vite pour rester visibles à l'échelle de temps
  du reste de l'écosystème post-s15.
- Golden-hash identique à la fin de s15 sur les deux seeds
  (`0xD0D00C1F590D2E9E` / `0x6AF6E2CBDBD9C246`) : confirme que toute
  l'instrumentation de cette session est bien un pur diagnostic, zéro
  changement de comportement.

### Partie 2 — Lignes droites : identifiées, pas devinées
- Nouvel outil `Tools/RenderDump` (reproduit exactement les pixels de
  `WorldRenderer.Redraw()`, écrit un vrai PNG). Le PNG révèle une
  **bande horizontale sombre bien réelle** (visible aussi au F5) —
  **root-causée** : `SeedInitialVegetation` (s15) sème en un seul
  balayage raster (ordre `y*Size+x`) tous les buissons jusqu'à
  capacité, PUIS continue le MÊME balayage en ne plantant plus que des
  arbres jusqu'à LEUR capacité. Comme la capacité arbre (5242) est
  bien plus petite que la capacité buisson (52428), la portion
  "arbres seulement" du balayage est une bande étroite et contiguë —
  confirmé par mesure directe : les lignes y=410-439 (seed 42) sont
  quasi 100% arbres (0-163 buissons contre 1588-1867 arbres par
  bande de 10 lignes, alors que le reste de la carte est
  majoritairement buisson). C'est aussi la cause directe de "la forêt
  pousse toujours aux mêmes endroits" : tous les arbres naissent dans
  cette bande unique, et les taux de diffusion/spontané (s15,
  volontairement très bas pour éviter la saturation) ne les font
  quasiment jamais apparaître ailleurs ensuite.
- **Ligne verticale à ~3/4 largeur : NON expliquée.** Le RLE des ids
  de terrain bruts à x=384 est propre (transitions organiques
  normales), et le balayage en densité bush/tree PAR COLONNE ne montre
  aucune bande "arbres seulement" analogue à la ligne horizontale — la
  cause n'est pas la même que ci-dessus. Piste éliminée par la revue
  de code initiale : aucun découpage par bloc/quadrant nulle part en
  C# (Simulation ou Game). Cause réelle non identifiée cette session —
  signalée pour investigation ultérieure (potentiellement côté
  pipeline Godot, non testable sans lancer le jeu).
- **Quadrant bas-gauche plus clair** : hypothèse plausible mais non
  confirmée — pourrait s'expliquer par une plus faible proportion
  d'arbres (plus sombres que l'herbe/le buisson) dans ce quadrant,
  mais pas vérifié précisément. À confirmer visuellement au F5.

### Partie 3 — Sprites procéduraux (livrable)
- Nouveau `Simulation/SpriteGenerator.cs` (pur C#, aucun `using
  Godot`) : silhouette humanoïde 6x8 (facing dérivé par miroir exact
  du buffer canonique, bras asymétrique pour que le miroir se voie
  réellement), buisson en disque irrégulier 4x4 (jeune, pâle) / 6x6
  (mûr, plus sombre + 1-2 pixels de baie) — **distinction visuelle
  immédiate jeune/mûr, forme ET couleur**, arbre tronc+couronne
  8x8-14x14 dont la taille suit `Stage/MatureStage` en continu. Toutes
  les formes utilisent un test de distance (`Sqrt` autorisé) avec une
  bande de bord bruitée par RNG — zéro trigonométrie. Chaque sprite
  dérive son seed d'une position de tuile ou d'un `Agent.Id` stable
  (jamais des flux RNG de la simulation).
- `data/vegetation.json` : buisson gagne `matureColor` (jeune `color`
  plus pâle qu'avant, mûr nettement plus sombre). Nouveau
  `data/palette.json` + `Simulation/PaletteCatalog.cs` (préparation
  multi-race/clan, donnée seule, aucune logique de sélection cette
  session).
- Côté rendu (`scripts/`) : `AgentRenderer` utilise la nouvelle
  silhouette (masque blanc, la teinte d'état FSM existante reste
  inchangée). Nouveau `scripts/VegetationRenderer.cs` :
  `MultiMeshInstance2D` par "bucket" (jeune/mûr buisson, 3 paliers de
  croissance arbre) au lieu de peindre un pixel par plante dans
  l'image plein-écran à chaque tick (risque de performance réel vu le
  nombre d'entités désormais en sprites multi-pixels) — même stratégie
  GPU-instanciée que les agents. `WorldRenderer.Redraw()` ne peint
  plus que le terrain.
- Choix SVG vs pixels : pixels procéduraux directs pour les trois
  catégories — à 4-14px, le SVG rasterisé n'apporte rien et ajoute une
  dépendance pour zéro gain visuel.
- 52 tests verts (49 existants + 3 nouveaux `SpriteGeneratorTests`).
  Golden-hash inchangé (confirmé, cf. Partie 1).

### Hors scope (respecté)
Aucune correction du terrain, du feu, de la propagation de végétation,
ni du couplage arbre/buisson cette session — uniquement diagnostic
(Parties 1-2) et rendu (Partie 3). Les fixes (notamment
`SeedInitialVegetation` pour la bande d'arbres) se décident ensemble
la prochaine fois.

### Prochaine fois
- Fixer `SeedInitialVegetation` pour répartir les arbres sur plusieurs
  bandes/zones au lieu d'un seul balayage continu (cause root-causée
  de la bande horizontale ET de "la forêt pousse toujours aux mêmes
  endroits").
- Investiguer la ligne verticale à ~3/4 largeur (non expliquée cette
  session).
- Décider s'il faut ralentir `AshToGrassChance` pour que les
  cicatrices de brûlure restent visibles plus longtemps (actuellement
  ~6× plus rapide que la repousse buisson).
- Lancement manuel du jeu (F5) pour confirmer visuellement
  l'amélioration de lisibilité (sprites) et la ligne verticale.

## Sessions 18/19/19b/19c — Clans, calibrage post-pool, deadlock Eating/Harvest

### Session 18 — Clans, le split récolte/manger
Le CLAN devient l'unité politique (ressources, reproduction) : `Clan.FoodPool`
partagé, `Agent.ClanId` hérité de la mère. Récolter (buisson → pool du
clan) et manger (pool → Hunger, sans déplacement) deviennent deux
actions distinctes — avant, un seul état `Eating` confondait les deux.
Spawn groupé par clan (grappe géographique) pour éviter un effet Allee
sur la recherche de partenaire inter-clan. Reproduction inter-clans
interdite. Golden-hash cassé (ClanId + bloc Clan dans `Hash()`), jamais
recalculé cette session (suite calibrage non aboutie).

### Session 19 (perf-diagnostic puis calibrage densité)
Diagnostic de perf : le run 2M ticks passé de 6s à 30min n'était PAS
une régression algorithmique (mesuré linéaire par agent via un nouveau
mode `SimReport --bench`) — c'était la population qui saturait le
tableau `Agent[]` (`AgentCapacityMultiplier` trop bas). `bushDensity`
descendue de 0.2 vers ~0.01-0.04 (10-25× l'ancienne production, jamais
recalibrée depuis avant le pool de clan). Bug de spawn trouvé et
corrigé : `SeedMinimumBushPerPatch` (nouvelle méthode, `World.cs`)
garantit un buisson mûr par poche d'herbe connectée dès la
construction — avant ce fix, une poche visitée tardivement par le
remplissage rotatif n'obtenait jamais aucun buisson, condamnant
d'office tout clan qui y naissait, indépendamment de la densité
globale.

### Session 19b — World law "pas de mort de faim"
Nouveau flag `SimulationConfig.AllowStarvationDeath` (défaut `false`) :
la faim ne tue plus par défaut, elle bloque seulement la reproduction
(déjà gaté par `Hunger < HungerSeekThreshold`). Décision actée : le
pool de clan PARTAGÉ transformait la famine en falaise synchronisée
(tout le clan meurt d'un coup) — sans mort de faim, plus de falaise,
seule la vieillesse régule la population.

**Découverte critique** : en retirant la mort de faim, un DEADLOCK
préexistant (introduit en session 18, jamais visible avant car masqué
par la mort de faim) est apparu au grand jour. Un agent en état
`Eating` n'était plus jamais réévalué par `ThinkAgent` et ne pouvait
en sortir que si `Hunger` retombait à 0 — impossible si le pool du
clan restait vide. Si toute la population d'un clan franchissait le
seuil de faim en même temps (quasi certain pendant une croissance),
plus personne n'était éligible à `TryStartHarvesting` : le pool ne
remontait plus jamais. Preuve empirique : run 2,5M ticks seed 42,
naissances tombées à 0 dès le tick 750 000 (2066 agents vivants),
extinction totale par vieillesse pure aux alentours du tick 2M, sans
aucune récupération sur les 500k ticks restants.

### Session 19c — Le vrai fix : manger devient un effet passif
Cause racine actée : "manger" avait été modélisé comme un ÉTAT FSM
EXCLUSIF (comme se déplacer ou récolter), alors que c'est un effet
PASSIF sans condition spatiale. Un état FSM qui ne peut se terminer
que si une ressource externe redevient disponible est un verrou en
puissance dès que la population peut collectivement l'atteindre.

**Fix** : `AgentState.Eating` SUPPRIMÉ de l'enum. Nouvelle méthode
`ApplyPassiveEating` (renommée depuis `EatFromPoolTick`), appelée
INCONDITIONNELLEMENT à chaque tick réel pour TOUT agent vivant, quel
que soit son état — y compris `Harvesting` (un cueilleur affamé mange
désormais sans jamais quitter sa récolte). `ThinkAgent` ne bloque plus
la réévaluation que pour `Seeking`/`Harvesting` (occupations réelles).
Un agent affamé Idle reste donc éligible à `TryStartHarvesting` — le
deadlock devient structurellement impossible. Nouveaux tests directs :
`No_Eating_State_Exists`, `Agent_EatsPassively_WhileHarvesting`,
`No_Starvation_Deadlock` (scénario exact du deadlock reproduit,
prouve qu'un agent finit toujours par redevenir cueilleur et que le
pool remonte).

En creusant la suite de tests pour valider tout ça, découverte que
**la suite complète n'avait jamais été vérifiée verte depuis la
session 18** (le fameux "S19: build/tests verts" restait en tâche
pending) : 6 tests cassés trouvés et corrigés, aucun lié au deadlock
lui-même —
- `Population_Extinguishes_OnFoodlessMap`, `Agents_DieOfHunger_InScarcityScenario`,
  `Agent_Dies_WithoutFood_AfterThreshold`, `Agent_Id_RemainsValid_AfterMultipleDeathsAndCompactions` :
  s'appuyaient sur la mort de faim par défaut (désormais `false`) —
  `AllowStarvationDeath=true` explicite ajouté, ou mort par âge
  substituée à la mort par faim pour les tests de compaction.
- `Newborn_HasCorrectStableParentIds`, `Starving_CannotReproduce`,
  `Newborn_InheritsMotherClan` : le helper `MakeFertileCouple` zérotait
  le pool de TOUS les clans (y compris celui du couple testé) pour
  "neutraliser" la population ambiante — bloquait donc AUSSI la
  reproduction du couple lui-même via `clanPoolRatio`. Fix : isoler le
  couple dans un clan qui n'appartient qu'à eux (réassignation de la
  population ambiante vers un autre clan via `SetAgentClanId`), puis
  financer UNIQUEMENT ce clan isolé.
- `Bushes_RecolonizeDepletedZone_Locally` : ne clearait pas la
  végétation à la construction — `SeedMinimumBushPerPatch` (session
  19) y laissait des buissons résiduels qui faussaient la mesure.
- Un vrai bug trouvé au passage : `MealsEaten` (int) débordait
  (`Repas cumules: -1419745631` observé) car il incrémente désormais à
  CHAQUE bouchée effective (tick réel), plus une fois par "session de
  repas" — passé en `long`, même raisonnement que
  `_clanFoodHarvestedCumulative` (session 18).

Golden-hash recalculé : `16475275109242875677` → `5609630853180351789`.

### Balayage de densité post-fix (bushDensity ∈ {0.01, 0.015, 0.02, 0.03}, seeds 42/7, 2M ticks, AgentCapacityMultiplier=250)

| densité | slots | pop finale seed42 | pop finale seed7 | clusterisation (42/7) |
|---|---|---|---|---|
| 0.01 | 2621 | 43 (déclin) | 685 (croissance) | 198 / 81 |
| 0.015 | 3932 | 1 (quasi-éteint) | 12291 (1 clan, encore en accélération) | 244 / 16 |
| 0.02 | 5242 | 168 (déclin) | 18832 (1 clan, encore en croissance) | 162 / 14 |
| 0.03 | 7864 | **16049, LES 3 CLANS VIVANTS** | 27956 (1 clan domine) | 12 / 9 |

**Sur les 8 runs (4 densités × 2 seeds) : `Faim=0` et `naissances refusées=0` partout, sans exception** —
les deux invariants de cette session tiennent structurellement, quelle
que soit la densité. Le deadlock est bien éliminé.

**Non résolu** : la variance seed-à-seed reste massive (un clan
écrase souvent les deux autres, ou toute la population décline vers
quasi-zéro) et aucune densité testée ne s'approche de la cible
CLAUDE.md (1500-2000) — soit très en dessous, soit largement au-dessus
sans signe de plafonnement dans la fenêtre de 2M ticks. `bushDensity=0.03`
seed42 est le premier run de toute cette série de sessions où les 3
clans survivent simultanément — signal encourageant mais pas une
calibration aboutie. Le calibrage fin de densité/taux reste une tâche
ouverte pour une session dédiée.

### Hors scope (respecté)
Arbres (toujours saturés au plafond du tableau, `Trees_StabilizeOverLongRun`
reste rouge — tâche déjà connue, non traitée), gradient, split
récolte/manger (le mécanisme lui-même, pas l'état FSM), territoires.

### Prochaine fois
- Calibrage fin de densité (la variance seed-à-seed n'est toujours pas
  comprise — géographie de spawn ? compétition inter-clan ?).
- Recalibrage des arbres sur un critère de lisibilité de paysage
  (toujours pending, tâche s19 jamais reprise).
- Étagement des tests xUnit (Fast/Slow) + budget de perf CI — différé
  depuis la session de diagnostic de perf, toujours pas fait.
- Mesure de la période d'oscillation une fois une densité stable
  trouvée.
