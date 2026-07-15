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
