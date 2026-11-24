### Sécurité

- **Mot de passe du compte de service de l'annuaire hors de `docker-compose.yml`** (S1) : il
  se fournit par l'environnement (`LDAP_BIND_PASSWORD`), comme les autres secrets ; hors
  développement, l'API refuse de démarrer s'il est vide, un bind sans mot de passe étant
  anonyme. Une installation lancée avec le seul `docker-compose.yml` doit désormais fournir
  cette variable ; la démonstration ne change pas.
