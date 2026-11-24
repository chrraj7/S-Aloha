# 🔎 Recherche

> Recherche hybride (mots et sens) sur les contenus validés de S-Aloha : chercher dans la
> connaissance, retrouver les cas similaires. Capacité transverse du socle, pas une
> pratique ITIL. Préfixe des règles : **RAG**. Conventions (✅ / 🔜, lots) :
> [règles métier](regles-metier.md#3-conventions-de-ce-référentiel). Décisions :
> [ADR-0013](../decisions/adr-0013-recherche-hybride-service-embeddings-separe.md),
> [ADR-0022](../decisions/adr-0022-pgvector-similarite-dans-postgresql.md) (pgvector).

<sub>[← Documentation](../readme.md) · [Produit](produit.md) · [Règles métier](regles-metier.md) · [Architecture](architecture.md) · [Base de données](base-de-donnees.md) · [Frontend](frontend.md) · [Exploitation](exploitation.md) · [Sécurité](securite.md) · [Glossaire](glossaire.md)</sub>

## 1. Objectif

Trouver ce que l'organisation sait déjà, même formulé autrement :

- **chercher d'abord** dans la connaissance avant d'ouvrir un incident ou une demande
  ([KB-04](processus/gestion-des-connaissances.md)) ;
- reconnaître, depuis un incident ou un problème, les **cas similaires** déjà traités
  ([INC-19](processus/gestion-des-incidents.md)).

La recherche est **hybride** : un signal **lexical** (les mots, plein texte PostgreSQL) et
un signal **sémantique** (le sens, vecteurs produits par le modèle `bge-m3`), fusionnés.
Sans service d'embeddings, elle reste lexicale.

## 2. Contenu indexé

| Source | Indexée quand | Texte indexé | Statut |
|---|---|---|---|
| Article de connaissance | Statut **Publié** | Titre, résumé, contenu | ✅ |
| Problème | Statut **Erreur connue**, **Résolu** ou **Clos** | Titre, description, « Cause racine : … », « Contournement : … » | ✅ |
| Incident | Statut **Résolu** ou **Clos**, avec résolution | Titre, description, « Résolution : … » | ✅ |
| Pièce jointe ([SOC-25](regles-metier.md#9-traçabilité-commentaires-administration)) d'une des sources ci-dessus | Quand son enregistrement l'est | « Pièce jointe : <nom> », puis le texte extrait (RAG-12) | ✅ |

Pas de brouillon, pas d'incident en cours, ni leurs pièces jointes : seul le contenu
**validé** est proposé comme réponse. Un résultat n'est visible que de qui voit
l'enregistrement (RAG-13) ; une pièce jointe suit le sien.

## 3. Règles

| ID | Règle | Statut | Lot |
|---|---|---|---|
| RAG-01 | Seul le **contenu validé** est indexé (§ 2). Un enregistrement qui **quitte** ces statuts (article archivé ou repassé en brouillon, problème rouvert, incident rouvert) ou qui est **supprimé** sort de l'index : toute création, modification ou suppression d'un article, problème ou incident est mise en file, et la réindexation retire les anciens passages avant de ne recréer que ceux d'un enregistrement encore éligible. Retrait asynchrone : quelques instants | ✅ | |
| RAG-02 | **Sans service d'embeddings** (`Embeddings:Url` vide, panne, délai dépassé : 5 s pour une recherche, 120 s pour une indexation), la recherche reste **lexicale, sans erreur** ; les passages sont créés sans vecteur et vectorisés au retour du service (au démarrage, s'il en manque) | ✅ | |
| RAG-03 | L'index est **dérivé** : mis à jour en tâche de fond à chaque enregistrement d'un article, problème ou incident (intercepteur EF → file → indexeur, un enregistrement à la fois) ; construit au démarrage s'il est vide ; un échec d'indexation ne bloque **jamais** l'enregistrement | ✅ | |
| RAG-04 | **Passages** de 800 caractères au plus, découpés par paragraphes ; un paragraphe trop long est découpé avec 100 caractères de recouvrement ; le titre accompagne chaque passage dans le vecteur | ✅ | |
| RAG-05 | **Recherche libre** : lexical = `tsvector` « french » sur titre et passage (index GIN), termes en **OU**, classement `ts_rank_cd` ; sémantique = cosinus `bge-m3` calculé dans PostgreSQL par pgvector (index HNSW sur `EmbeddingVector vector(1024)` ; les 20 plus proches, puis le plancher RAG-07) ; fusion **RRF** (k = 60) au niveau de l'enregistrement (son meilleur passage) ; chaque résultat indique s'il a été trouvé **par les mots, par le sens ou les deux** | ✅ | |
| RAG-06 | **Cas similaires** d'un incident ou d'un problème : sens sur titre et description (2 000 caractères au plus), mots sur le titre seul ; articles, problèmes et incidents indexés, l'enregistrement lui-même exclu ; 5 résultats. Un incident ouvert ne voit donc que des incidents résolus | ✅ | |
| RAG-07 | **Plancher de similarité** cosinus `Search:MinSimilarity` = 0,5 par défaut. **Non mesuré** : à calibrer sur un jeu de requêtes réel avec `bge-m3` (RAG-10) | ✅ (valeur provisoire) | |
| RAG-08 | **Administration › Index de recherche** (Admin) : état du service (configuré, joignable, modèle, vecteurs en mémoire), et par source : enregistrements, passages, passages avec vecteur, dernière indexation ; **reconstruction** de l'index en tâche de fond | ✅ | |
| RAG-13 | **Périmètres** ([PER-09](perimetres.md)) : la recherche et les cas similaires ne renvoient que les enregistrements visibles de l'utilisateur (ses périmètres, articles publiés de tous) ; filtrés dans les requêtes lexicale et vectorielle, avant le classement, sur l'enregistrement vivant (son périmètre actuel, [PER-07](perimetres.md)), sans copie du périmètre dans l'index ; les cas similaires d'un enregistrement invisible répondent 404 | ✅ | |
| RAG-09 | Depuis les cas similaires d'un incident, **relier** l'article ou le problème retenu à l'incident en un clic (achève INC-19) : bouton « Relier » sur les articles et les problèmes des cas similaires d'un incident | ✅ | 2 |
| RAG-10 | **Calibrage** : jeu d'au moins 30 requêtes avec leurs réponses attendues ; plancher et nombre de résultats fixés d'après la précision mesurée, consignés ici. **Mesure du 2026-11-09** (environnement d'essai, 36 enregistrements de test ; 36 requêtes formulées autrement que les titres, dont 4 sans réponse attendue ; jeu validé par le mainteneur) : bonne réponse parmi les 5 premiers résultats hybrides pour 81 % des requêtes à 0,50, **88 % à 0,47**, 91 % à 0,45 ; aucune requête sans réponse ne dépasse 0,449, d'où 0,47 (marge de 0,02). Nombre de résultats inchangé : 10 pour la recherche libre, 5 pour les cas similaires. Données synthétiques : à remesurer sur des données réelles après la première version stable | ✅ | 1 |
| RAG-11 | **Suggestions à la saisie** : à la création d'un incident ou d'une demande, les cas similaires s'affichent dès que le titre est saisi (4 caractères au moins, après une courte pause de frappe ; 5 résultats) | ✅ | 2 |
| RAG-12 | **Pièces jointes** indexées (texte extrait des documents déposés, SOC-25 ; décisions du 2026-11-09 et du 2026-11-24, #280) : texte brut (txt, log, csv, json, eml), PDF à couche texte (PdfPig, Apache-2.0), Office Open XML (docx, xlsx, pptx) et OpenDocument (odt, ods, odp) lus comme archives zip de XML, sans bibliothèque ; **100 000 caractères** au plus par fichier ; ni OCR (images, PDF numérisés), ni doc, xls, ppt, msg ou zip. Les passages, préfixés « Pièce jointe : <nom> », sont **rattachés à l'enregistrement** : mêmes statuts (RAG-01), mêmes droits (RAG-13). Réindexation au dépôt et au retrait ; stockage coupé ou fichier illisible : la pièce est passée et journalisée, l'enregistrement reste indexé (RAG-03). Le XML se lit en flux, sans DTD ni entité externe, 20 millions de caractères au plus par document | ✅ (`AttachmentText`, `SearchIndexer`) | 3 |

## 4. Interface

| Endroit | Contenu | Statut |
|---|---|---|
| Connaissances | Bloc « **Chercher d'abord** » sous le titre du registre (`RecordList` accepte `intro`) : recherche libre, résultats marqués mots / sens | ✅ |
| Fiche incident, fiche problème | Encadré « **Cas similaires** » dans l'onglet Informations | ✅ |
| Administration › Index de recherche | État et reconstruction (RAG-08) ; avis ambre si le service d'embeddings est injoignable | ✅ |

## 5. API

| Route | Policy | Rôle |
|---|---|---|
| `GET /api/search?q=&types=&limit=` | User | Recherche libre (RAG-05) ; `types` : `article`, `problem`, `incident`, séparés par des virgules |
| `GET /api/search/similar?type=&id=` | User | Cas similaires (RAG-06) |
| `GET /api/search/status` | Admin | État de l'index (RAG-08) |
| `POST /api/search/reindex` | Admin | Reconstruction en tâche de fond (202) |

## 6. Exploitation

Service `embeddings` du compose, plafond mémoire et téléchargement du modèle, marche sans
le service, reconstruction : [exploitation § Recherche](exploitation.md#recherche).

## 7. Scénarios d'acceptation

- **RAG-01** — *Étant donné* un article en Brouillon contenant « passerelle VPN », *quand* on cherche « passerelle », *alors* il n'apparaît pas ; publié, il apparaît ; archivé, il disparaît de nouveau (de même pour un problème rouvert ou un incident supprimé).
- **RAG-12** — *Étant donné* un incident résolu dont la procédure est dans un fichier `.odt` joint, *quand* on cherche un mot de la procédure, *alors* l'incident est trouvé ; la pièce jointe d'un incident en cours ne l'est pas ; une fois la pièce retirée, le mot ne trouve plus l'incident.
- **RAG-02** — *Étant donné* `Embeddings:Url` vide, *quand* on cherche « coupure VPN », *alors* les résultats viennent des mots seuls, sans erreur.
- **RAG-03** — *Étant donné* un service d'embeddings en panne, *quand* on publie un article, *alors* la publication réussit ; l'article est trouvé par les mots, puis par le sens au retour du service.
- **RAG-05** — *Étant donné* un article « Rétablir une session VPN qui coupe », *quand* on cherche « connexion à distance qui tombe », *alors* il est trouvé **par le sens** même sans mot commun.
- **RAG-06** — *Étant donné* l'incident en cours « VPN inaccessible », *alors* ses cas similaires listent au plus 5 éléments, sans lui-même, dont des incidents résolus et l'erreur connue liée.
