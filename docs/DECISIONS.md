# Décisions de design — journal daté

Une entrée par décision tranchée, avec sa date et son raisonnement
complet. La RÈGLE qui en découle vit dans CLAUDE.md (une ligne) ; ici
on conserve le pourquoi, pour ne jamais re-débattre une décision déjà
prise. Se lit à la demande, avant de rouvrir un débat de design.

## 2026-07-16 — RNG : flux par système, pas par entité

- Flux par SYSTÈME (_rngAgents, _rngFire, _rngVegetation, _rngWorldGen),
  pas un flux par entité individuelle.
- Conséquence ASSUMÉE, pas un bug : ajouter/retirer une entité en cours
  de partie décale tous les tirages futurs de CE flux, pour toutes les
  entités qui l'utilisent — pas seulement les voisines de l'entité
  modifiée. C'est un effet de bord numérique global, immédiat, mais
  souvent invisible (un tirage décalé ne franchit pas forcément un
  seuil de décision).

### Deux canaux de causalité distincts
1. RNG partagé (ci-dessus) : artefact d'implémentation, sans
   signification narrative. Divergence numérique immédiate et globale,
   mais souvent sans effet visible.
2. Compétition réelle pour des ressources localisées (nourriture,
   territoire, matériaux) : la VRAIE source d'émergence intéressante.
   Se propage de proche en proche via la grille spatiale (portée de
   recherche bornée), pas d'un coup partout. C'est ce canal qui doit
   produire l'essentiel de ce que le joueur perçoit comme "un monde
   vivant qui réagit" — le RNG partagé n'est que du bruit superposé.
- Toute nouvelle mécanique doit clairement passer par le canal 2
  (interactions spatiales réelles) pour produire de l'émergence
  perçue. Ne jamais compter sur le canal 1 pour ça.

## 2026-07-16 — Réservation de ressource : tranché

- État constaté dans le code (session 7) : aucun mécanisme de
  réservation, deux agents peuvent cibler le même buisson (le premier
  arrivé le vide, le second re-vérifie à l'arrivée et repasse Idle).
- Décision : la réservation de cible est le design retenu — règle dans
  CLAUDE.md, section "Récolte — réservation de cible". À implémenter
  lors d'une prochaine session touchant à Seeking/Eating. Pièges
  d'implémentation recensés dans docs/REVUE-ARCHITECTURE-SESSION7.md
  (addendum 2) : réserver par index de tuile, libération sur mort/
  abandon/destruction, Hash() dans la même session.

## 2026-07-24 — Rendu territoire : assombrir le neutre, pas teinter l'intérieur

- Deux tentatives précédentes (teinte franche, puis liseré fin
  renforcé) ont échoué à rendre la frontière de territoire lisible sur
  l'herbe moucheté — seulement visible sur l'eau unie. Un liseré fin
  ou un aplat translucide ne gagne jamais contre une texture bruitée.
- Décision : inverser la logique. Le terrain NEUTRE (hors de tout
  territoire) est assombri/désaturé vers un gris sombre fixe ; le
  territoire garde sa luminosité/texture normale avec une légère
  teinte de clan. Un écart de LUMINOSITÉ se lit sur n'importe quel
  fond, contrairement à une différence de teinte. Un liseré clair
  explicite reste nécessaire, mais seulement entre deux territoires de
  clans DIFFÉRENTS (la frontière territoire-neutre se lit déjà via le
  saut de luminosité). Implémenté dans `scripts/WorldRenderer.cs`
  (session territoire, suite).
- Toute future mécanique de rendu "zone possédée vs zone libre" doit
  suivre ce même principe (contraste de luminosité en fond, liseré
  explicite réservé aux frontières entre deux zones toutes deux
  "actives") plutôt que de re-tenter une teinte/bordure sur la zone
  possédée elle-même.
