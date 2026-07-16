# Vision & backlog — WorldSim

Ce fichier n'est PAS chargé automatiquement dans le contexte : il se lit
à la demande, avant de proposer ou planifier une nouvelle feature.
CLAUDE.md ne contient que les règles ; ici vivent le pourquoi et le
plus-tard.

## Objectifs centraux (non négociables)
- Civs pleinement autonomes : exploration (via connaissance partielle par
  civ), expansion, guerre, décidées par utility AI — jamais scriptées à la
  main, jamais déclenchées par le joueur.
- Toute décision de civ doit être explicable (Breakdown des scores),
  visible par le joueur via un overlay/panneau. C'est ce qui doit dépasser
  WorldBox : pas plus de systèmes, une meilleure lisibilité de ceux qui
  existent.

## Confirmé pour plus tard (pas maintenant, pas oublié)
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

## Confirmé pour plus tard (inspiré de demandes communautaires WorldBox)
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

## Non-objectifs confirmés
- Contrôle direct d'une unité/meute par le joueur. Le joueur est un
  spectateur qui perturbe (pouvoirs), jamais un avatar.
- Bestiaire fantastique (dragons, zombies, démons, OVNIs) : pas notre
  identité pour l'instant. Les "mobs hostiles" à venir sont des menaces
  crédibles (prédateurs, raiders), pas du fantastique gratuit.

## Pistes futures non urgentes
- Chaos volontaire — feature "comparaison de timelines" : lancer deux
  World au même seed, appliquer une commande sur l'un seul, comparer les
  hash tick par tick pour visualiser la vitesse de divergence. Amusant,
  cohérent avec l'esprit "petri dish", zéro coût d'architecture.
  Pas prioritaire.
