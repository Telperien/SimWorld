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
