# ADR-0013 — Recherche hybride : embeddings dans un service séparé, plein texte PostgreSQL, vecteurs sans pgvector

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-10-08
- **Statut** : accepté — point 3 (« vecteurs sans pgvector ») remplacé par [ADR-0022](adr-0022-pgvector-similarite-dans-postgresql.md) le 2026-11-09 ; l'alternative écartée « service hébergé chez un tiers » est revue par l'[ADR-0023](adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md) le 2026-11-23 pour le seul modèle de langage, au choix de l'Admin (le service d'embeddings reste local)
- **Décideur** : Leipa

## Contexte

La recherche de S-Aloha compare des chaînes (titre, référence, mots-clés) : elle ne trouve
pas un article qui décrit le même symptôme avec d'autres mots, ni l'incident déjà résolu
qui ressemble à celui qu'on traite. Les pratiques ITIL en ont besoin : chercher dans la
connaissance avant d'ouvrir un ticket, reconnaître un incident déjà vu, relier un incident
à un problème connu ([KB-04](../reference/processus/gestion-des-connaissances.md),
[INC-19](../reference/processus/gestion-des-incidents.md)).

Une recherche **hybride** combine deux signaux : les **mots** (plein texte) et le **sens**
(similarité entre vecteurs produits par un modèle d'embeddings). Décisions du décideur :
le modèle tourne dans un **service séparé** ; la première phase n'indexe que les **données
S-Aloha** (pas de dépôt de fichiers) ; l'application doit fonctionner **sans** ce service.

## Décision

1. **Modèle d'embeddings dans un service séparé** : `bge-m3` (multilingue, 1024
   dimensions) servi par Ollama, appelé en HTTP par l'API (`Embeddings:Url`,
   `Embeddings:Model`). Sans service configuré, injoignable ou trop lent, la recherche
   reste **lexicale**, sans erreur ([RAG-02](../reference/recherche.md)).
2. **Plein texte PostgreSQL** : colonne `tsvector` (configuration « french », titre et
   passage), index GIN, classement `ts_rank_cd`.
3. **Vecteurs sans pgvector** : stockés en `real[]` en base, chargés **en mémoire** dans
   l'API (1024 × 4 octets par passage), similarité cosinus calculée dans l'API.
4. **Fusion** des deux classements par **Reciprocal Rank Fusion** (k = 60), au niveau de
   l'enregistrement (son meilleur passage).
5. **Index dérivé** : passages de 800 caractères au plus, mis à jour en tâche de fond à
   chaque enregistrement d'un contenu validé, reconstructible à tout moment.

Spécification complète : [recherche](../reference/recherche.md).

## Conséquences

- Aucune nouvelle dépendance pour PostgreSQL : l'image standard suffit.
- L'API ne charge aucun modèle : sa mémoire reste celle d'une application web. Le service
  d'embeddings demande environ 1,2 Go de poids téléchargés au premier démarrage et une
  limite mémoire de 3 Go.
- La mémoire de l'API croît avec l'index : environ 4 Ko par passage, soit 40 Mo pour
  10 000 passages. **Seuil de bascule à surveiller** : au-delà de quelques dizaines de
  milliers de passages, ou si le temps de recherche vectorielle dépasse une centaine de
  millisecondes, passer à pgvector (nouvel ADR).
- La recherche reste utilisable dans tous les cas : un déploiement sans le service
  d'embeddings garde une recherche plein texte meilleure que la comparaison de chaînes.
- Le plancher de similarité (0,5 par défaut) **n'est pas mesuré** : il doit être calibré
  sur un jeu de requêtes réel avec `bge-m3` ([RAG-07](../reference/recherche.md)).
- Changer de modèle d'embeddings, ou restaurer une base, impose une reconstruction de
  l'index (Administration › Index de recherche).

## Alternatives envisagées

- **Modèle ONNX dans le processus de l'API** — écarté : environ 2 Go de poids et de mémoire
  dans le processus web, risque d'arrêt pour manque de mémoire, démarrage ralenti.
- **pgvector** — écarté à ce stade : impose de changer l'image PostgreSQL, pour un volume
  (quelques milliers de passages) que le calcul en mémoire traite sans difficulté. À
  reconsidérer au seuil indiqué ci-dessus.
- **Moteur de recherche externe** (Elasticsearch, OpenSearch, Meilisearch) — écarté : un
  service lourd de plus à exploiter, alors que l'index plein texte de PostgreSQL suffit.
- **Service d'embeddings hébergé chez un tiers** — écarté : les données des incidents et
  problèmes quitteraient le SI de l'organisation, contraire au principe d'hébergement
  chez soi.
- **Recherche sémantique seule** — écarté : elle rate les correspondances exactes
  (références, noms de serveurs, codes d'erreur) que le plein texte trouve à coup sûr.
