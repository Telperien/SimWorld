# WorldSim

Simulateur de monde type WorldBox. Godot 4 (.NET) + C#. Windows/Linux.
Pixel art entièrement généré en code. Projet perso solo, dev 100 % agentique.

## Architecture — non négociable
- /Simulation : C# pur. AUCUN `using Godot`. AUCUN `using System.IO`.
- /Game (racine Godot) : référence /Simulation. Jamais l'inverse.
- /Tests : xUnit, référence /Simulation uniquement.
- Le jeu, c'est /Simulation. Godot n'est qu'un afficheur remplaçable.

## Boucle de simulation
- ZÉRO allocation dans le tick. Pas de LINQ, pas de new, pas de lambda capturante.
- Entités = structs dans des tableaux préalloués. JAMAIS de nœuds Godot.
- Tableaux plats `y * Size + x`. Pas de tableaux 2D.
- Automates cellulaires en double buffer (lire _current, écrire _next, swap).
- Liste active : on ne simule que ce qui bouge. Jamais de full sweep.
- Deux vitesses : tick tuiles 30 Hz, tick civs 1 Hz.
- Mise à jour étalée des agents : chacun pense 1 tick sur 4.

## Déterminisme (critique)
- RNG maison seedé (Xorshift). JAMAIS Random.Shared, DateTime.Now, Stopwatch.
- Le delta est passé en paramètre.
- JAMAIS d'itération sur Dictionary/HashSet dans le tick : ordre non garanti
  en .NET → divergence silencieuse. Tableaux ou ordre trié uniquement.
- Requêtes spatiales (voisinage, choix de partenaire) : parcours en ordre
  stable (index croissant), jamais dépendant de l'ordre d'insertion.

## Portabilité (cible WASM future)
- PAS de threads, pas de Parallel.For. Simulation mono-thread.
- Invariant culture only.
- Surface publique minimale : Tick(delta), GetFramebuffer(), Execute(ICommand).

## Données
- Tout le contenu = JSON chargé au boot dans des tableaux indexés par int :
  terrains, matériaux, bâtiments, traits, castes, techs, poids d'IA, palette.
- Aucune valeur de gameplay en dur. Aucun switch sur un type de contenu.
- buildings.json porte cost / material / provides dès sa création, même ignorés.
- Les comptes sont des densités × surface, jamais des nombres absolus.
- Taille du monde = paramètre de constructeur, puissance de 2, jamais une
  const. Aucune valeur dérivée de la taille en dur.

## Population & lignées
- Reproduction régulée par la capacité de charge (nourriture + logement).
  JAMAIS par un taux de natalité tuné à la main : frein structurel, pas paramètre.
- La struct Agent porte MotherId, FatherId et Tracked dès sa première version.
- Généalogie conservée à vie uniquement pour les castes tracked ;
  les anonymes partent dans un ring buffer borné.

## IA
- Agents = FSM (byte) + A* court. JAMAIS de behavior tree, jamais de GOAP.
- Civs = utility AI à 1 Hz, poids en JSON, scores toujours dumpables.
- Capacités DÉRIVÉES à chaque tick civ, jamais stockées. Bâtiment détruit →
  capacité perdue automatiquement. Aucun event de destruction.
- Contrefactuel = ComputeWithout(). Un seul pas de profondeur. Jamais de planner.
- Chaque civ a son byte[] _known ; elle ne score QUE ce qu'elle connaît.
  Aucune omniscience.
- Pas de ML, pas de réseau de neurones.

## Lisibilité
- Toute valeur affichée = Breakdown(final, Modifier[]). Jamais un float nu.
- Tout refus (construction, tech) est expliqué ligne par ligne.
- Encyclopédie/tooltips générés depuis le JSON. Jamais de texte en dur.

## Graphismes — tout en code
- AUCUN asset binaire, aucun PNG. Le rendu est calculé.
- Couleurs = palette JSON (hex). Sprites = string[] ASCII parsés au boot.
  Icônes/UI = SVG (texte) ou dessin en code.
- Agents rendus via MultiMeshInstance2D. Bâtiments statiques peints dans
  la texture de la carte (redessin uniquement au changement de tier).
- Texture filter = Nearest partout. Zoom par paliers entiers (1x/2x/4x/8x).
  Position caméra arrondie au pixel.
- Le LOD n'affecte QUE le rendu. La sim ignore totalement la caméra.

## Godot C# — pièges
- Nom de classe = nom de fichier.
- Call/CallDeferred/Connect/Get/Set attendent l'API snake_case :
  CallDeferred("add_child"), PAS "AddChild". Membres custom non concernés.
- Hot-reload ne restaure pas l'état (sauf [Export]).
- .NET 8+. Pas d'export web en C# : ne JAMAIS proposer de solution web.
- Le csproj Godot globe tous les .cs → conserver
  <Compile Remove="Simulation/**" /> et <Compile Remove="Tests/**" />.

## Scope — bornes dures
- 2 matériaux (bois, pierre). Pas de commerce.
- 8-12 traits max. 15 techs max. ~1500-2000 agents. Monde 512² en dev.
- Territoire des civs sur grille grossière (32² pour 512²), jamais par tuile.
- Ne jamais élargir le scope. Un système manquant se signale, ne se construit pas.

## Fin de session
- Tests verts → commit atomique → 5 lignes dans JOURNAL.md
  (fait / cassé / prochaine fois).