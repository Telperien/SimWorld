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
