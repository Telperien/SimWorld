# WorldSim

Simulateur de monde type WorldBox. Godot 4 (.NET) + C#. Windows/Linux.
Pixel art entièrement généré en code. Projet perso solo, dev 100 % agentique.

## Identité (résumé — détail dans docs/VISION.md)
- Civs pleinement autonomes (utility AI, connaissance partielle par civ) :
  jamais scriptées à la main, jamais déclenchées par le joueur.
- Toute décision de civ est explicable et visible (Breakdown des scores).
  C'est ça qui doit dépasser WorldBox — pas plus de systèmes, une
  meilleure lisibilité de ceux qui existent.
- Le joueur est un spectateur qui perturbe (pouvoirs), jamais un avatar.

## Documents de référence (à lire à la demande, ne pas dupliquer ici)
- docs/VISION.md : backlog confirmé, non-objectifs, pistes futures.
  À lire AVANT de proposer ou planifier une nouvelle feature.
- docs/DECISIONS.md : décisions de design tranchées, datées, avec leur
  raisonnement. À lire AVANT de rouvrir un débat de design.
- docs/REVUE-ARCHITECTURE-SESSION7.md : audit d'architecture + pièges
  d'implémentation connus. À lire avant les sessions IDs stables,
  RNG par flux, hash/save, reproduction, récolte/réservation.

## Architecture — non négociable
- /Simulation : C# pur. AUCUN `using Godot`. AUCUN `using System.IO`.
- /Game (racine Godot) : référence /Simulation. Jamais l'inverse.
- /Tests : xUnit, référence /Simulation uniquement.
- Le jeu, c'est /Simulation. Godot n'est qu'un afficheur remplaçable.
- Toute référence persistante à une entité utilise un Id stable, JAMAIS
  un index de tableau : tous les tableaux d'entités sont compactés par
  swap-with-last. Vaut pour les agents, et pour toute entité future
  (végétation, bâtiments, civs).

## Boucle de simulation
- ZÉRO allocation dans le tick. Pas de LINQ, pas de new, pas de lambda capturante.
- Entités = structs dans des tableaux préalloués. JAMAIS de nœuds Godot.
- Tableaux plats `y * Size + x`. Pas de tableaux 2D.
- Automates cellulaires en double buffer (lire _current, écrire _next, swap).
- Liste active : on ne simule que ce qui bouge. Jamais de full sweep.
- Deux vitesses : tick tuiles 30 Hz, tick civs 1 Hz.
- Mise à jour étalée des agents : chacun pense 1 tick sur 4.
- Aucune accumulation à sens unique. Toute ressource/état qui s'accumule
  doit avoir un chemin de sortie (mort, décroissance, récupération).
  Vérifier ce point pour CHAQUE nouvelle entité — c'est la classe de
  bug de la cendre irréversible et des arbres immortels.
- Corollaire : aucune ressource ne doit pouvoir disparaître LOCALEMENT
  sans pouvoir revenir LOCALEMENT. Une régénération globale sur une
  déplétion locale crée des déserts permanents (bug des buissons, s10-13).
- Séparation des échelles de temps. Chaque couche doit être au moins
  un ordre de grandeur plus lente que celle du dessous : mouvement
  (secondes) < cycle de vie (minutes) < paysage (dizaines de minutes)
  < civilisations (heures). Deux mécaniques dans la même bande
  temporelle se perçoivent comme du bruit : le joueur ne lit plus la
  causalité.

## Déterminisme (critique)
- RNG maison seedé (Xorshift). JAMAIS Random.Shared, DateTime.Now, Stopwatch.
- Un flux RNG par SYSTÈME (_rngAgents, _rngFire, _rngVegetation,
  _rngWorldGen), jamais par entité. Le décalage des tirages quand une
  entité apparaît/disparaît est assumé (raisonnement : docs/DECISIONS.md).
- L'émergence perçue doit venir des interactions spatiales réelles
  (compétition localisée pour les ressources), jamais du bruit RNG.
- Le delta est passé en paramètre.
- JAMAIS d'itération sur Dictionary/HashSet dans le tick : ordre non garanti
  en .NET → divergence silencieuse. Tableaux ou ordre trié uniquement.
- Requêtes spatiales (voisinage, choix de partenaire) : parcours en ordre
  stable (index croissant), jamais dépendant de l'ordre d'insertion.
- JAMAIS MathF.Sin/Cos/Pow/Exp dans /Simulation : implémentations non
  garanties identiques cross-plateforme (x86/ARM/WASM). Uniquement
  +,-,*,/,Sqrt,Floor ou des approximations maison.

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

## Ressources — pool commun, zéro logistique
- Aucun inventaire individuel, aucun transport, aucun rôle "porteur".
  Une ressource récoltée est ajoutée DIRECTEMENT au pool de la civ au
  fil de la récolte (mesuré sur plusieurs ticks, jamais un transfert
  instantané en un seul tick). Une ressource consommée (construction,
  nourriture une fois les civs formalisées) est déduite DIRECTEMENT
  du même pool.
- Un seul pool par civ. Jamais de stock localisé par village/bâtiment,
  jamais de caravane, jamais de route commerciale (cohérent avec la
  règle "pas de commerce" déjà posée).
- S'applique dès l'implémentation des matériaux (bois/pierre) et,
  plus tard, à la nourriture une fois que les civs existent et que la
  capacité de charge est calculée au niveau civ plutôt qu'individuel.
- Avant l'existence des civs : le comportement actuel (un agent
  mange directement un buisson, Hunger individuel) reste inchangé —
  il n'y a pas encore de pool à qui appartenir.

## Récolte — réservation de cible
- Un agent qui cible un buisson/gisement pour récolte le RÉSERVE :
  un autre agent en recherche ne doit pas converger vers la même
  cible pendant qu'elle est déjà visée. Évite la compétition
  absurde sur une même ressource et la disparition surprise d'une
  cible sous le nez d'un agent qui s'en approchait.

## IA
- Agents = FSM (byte) + A* court. JAMAIS de behavior tree, jamais de GOAP.
- Civs = utility AI à 1 Hz, poids en JSON, scores toujours dumpables.
- Capacités DÉRIVÉES à chaque tick civ, jamais stockées. Bâtiment détruit →
  capacité perdue automatiquement. Aucun event de destruction.
- Contrefactuel = ComputeWithout(). Un seul pas de profondeur. Jamais de planner.
- Chaque civ a son byte[] _known ; elle ne score QUE ce qu'elle connaît.
  Aucune omniscience.
- Pas de ML, pas de réseau de neurones.
- L'errance de secours doit être DIRIGÉE (marche corrélée), jamais une
  marche aléatoire pure : le déplacement d'une marche aléatoire croît
  en √N, donc elle ne peut pas échapper à une zone stérile plus large
  que quelques tuiles.
- Un agent ne doit jamais mourir faute d'avoir PERÇU une ressource
  abondante. La perception locale (BFS ±16) ne suffit pas dès que les
  agents s'agglutinent : ils carvent des déserts plus larges que leur
  perception. Toute pénurie locale doit être échappable par un
  gradient, jamais par une marche aléatoire (déplacement en √N).

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
- Décision de design tranchée en session → entrée datée dans
  docs/DECISIONS.md ; seule la règle qui en découle vit ici.
- Nouvelle idée ou feature future → docs/VISION.md, JAMAIS ici.
  CLAUDE.md ne contient que des règles actives.
