# WorldSim

Simulateur de monde type WorldBox. Godot 4 (.NET) + C#. Windows/Linux.
Pixel art entièrement généré en code. Projet perso solo, dev 100 % agentique.

## Vision & non-objectifs

### Objectifs centraux (non négociables)
- Civs pleinement autonomes : exploration (via connaissance partielle par
  civ), expansion, guerre, décidées par utility AI — jamais scriptées à la
  main, jamais déclenchées par le joueur.
- Toute décision de civ doit être explicable (Breakdown des scores),
  visible par le joueur via un overlay/panneau. C'est ce qui doit dépasser
  WorldBox : pas plus de systèmes, une meilleure lisibilité de ceux qui
  existent.

### Confirmé pour plus tard (pas maintenant, pas oublié)
- Spawn manuel d'agents par le joueur (civilisés ou hostiles) : une
  ICommand de plus, une fois civs + hostilité posées.
- Sauvegarde/chargement de monde -> cartes pré-générées jouables. Dépend
  du travail de sérialisation d'état déjà identifié (hash complet, RNG
  par flux). Ne pas construire avant que Hash()/snapshot soit fiable.
- Éditeur de carte (dessiner son terrain à la main) : outil UI séparé de
  la simulation, écrit directement dans le tableau de terrain. Après les
  bâtiments et les civs.
- Vitesse de jeu variable (pause, x2, x3...) : N ticks par frame à dt
  fixe. Jamais un dt qui grandit. Rendu une seule fois par frame quel
  que soit N.

### Confirmé pour plus tard (inspiré de demandes communautaires WorldBox)
- Formes de gouvernement = profils de poids d'utility AI (personalities.json),
  pas un système séparé. Ex: "démocratique" = poids faible sur agressivité
  solo du dirigeant, poids fort sur consensus/stabilité.
- Historique affiché des civs éteintes (durée de vie, cause de chute) :
  dérivé des ticks de naissance/mort déjà prévus pour la généalogie.
- Traités de paix : une action de plus dans le scoring utility AI,
  symétrique à declareWar.
- Bâtiment capitale : simple valeur HP plus élevée dans buildings.json,
  aucun système nouveau.
- Capture/assimilation après conquête (alternative à l'élimination pure) :
  à trancher au moment de la session guerre, pas maintenant.

### Non-objectifs confirmés
- Contrôle direct d'une unité/meute par le joueur. Le joueur est un
  spectateur qui perturbe (pouvoirs), jamais un avatar.
- Bestiaire fantastique (dragons, zombies, démons, OVNIs) : pas notre
  identité pour l'instant. Les "mobs hostiles" à venir sont des menaces
  crédibles (prédateurs, raiders), pas du fantastique gratuit.

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

### Sauvegarde (prépare, ne construis pas encore)
- Tout nouvel état ajouté à World (civs, territoire, castes...) DOIT
  être ajouté à Hash() dans la même session qui l'introduit. Jamais
  après coup, jamais "je le ferai plus tard" — un hash incomplet est
  un bug de save latent invisible jusqu'au jour où on charge.
- La sauvegarde réelle (sérialisation complète, format de fichier)
  n'est construite qu'après la session 17 (civs stables), pas avant.

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