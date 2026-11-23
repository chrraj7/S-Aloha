# 2026-11-23 — Mode démonstration du tri assisté et calibrage (F14 étape 3e, TRI-12)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** :
  - **Mode démonstration** : `docker-compose.demo.yml` démarre le conteneur `features` sans
    son profil (`profiles: !reset []`, Docker Compose 2.24 ou plus) et donne son adresse à
    l'API. Le chargeur des données d'exemple charge le jeu d'essai du tri
    (`demo/donnees/tri-assiste.json`) : 32 incidents résolus, avec catégorie, service et
    groupe. Il active ensuite le tri, dès que le conteneur répond, puis attend que l'index de
    recherche ait ces voisins, vecteurs compris. Il crée enfin une panne probable de quatre
    incidents ouverts sur l'accès distant. Sans conteneur, le tri reste coupé.
  - **Calibrage** (TRI-12) : `demo/donnees/calibrer-tri.py` mesure le vote sur 30 essais
    formulés autrement, pour 5, 10 et 15 voisins, et la similarité bge-m3 des incidents d'une
    même panne et d'incidents sans rapport. Résultat : 10 voisins (95 à 96 % de suggestions
    justes, 73 à 77 % de couverture) et un seuil de 0,60 maintenu (4 paires de la panne sur 6,
    aucune fausse liaison entre ouverts, 5 sur 384 entre résolus de thèmes différents). La
    mesure est consignée dans TRI-12.
- **Pourquoi** : sous-partie 3e de l'étape 3 du chantier F14 (#365), dernière règle du tri
  (TRI-12) et décision du 2026-11-23 sur la démonstration.
- **Preuve** : essai réel de la démonstration construite depuis cette branche, avec bge-m3.
  Le tri est activé. La panne compte 5 incidents (les 4 du jeu, plus un incident VPN ouvert
  des données d'exemple, relié par le service), avec une seule notification et l'impact
  Élevé proposé. À la saisie de « Je ne reçois plus aucun mail », le tri suggère Messagerie
  (8 voisins sur 8) et GRP-MESSAGERIE (7 sur 8), avec un incident ouvert proche.

  Un premier chargement, qui n'attendait pas l'index, avait privé un incident de sa
  proposition de groupe. Le chargeur attend désormais l'index, et une modification de
  l'incident a fait apparaître la proposition (GRP-RESEAU, 7 sur 10).
- **Fichiers** : `docker-compose.demo.yml`, `demo/donnees/charger.py`,
  `demo/donnees/tri-assiste.json`, `demo/donnees/calibrer-tri.py`
- **Documentation** : tri assisté (TRI-12 ✅, mesure consignée ; TRI-06 calibré), exploitation
  (§ Mode démonstration), fonctionnalités optionnelles (§ 7), plan d'action, synthèse des
  pratiques (382 ✅), note `changelog.d/365-3e-demonstration.md`
- **Commit** : #365
