# 2026-11-24 — S1 : le mot de passe du compte de service de l'annuaire vient de l'environnement

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `docker-compose.yml` lit `Ldap__BindPassword` dans `LDAP_BIND_PASSWORD`, vide à
  défaut, comme `POSTGRES_PASSWORD`, `JWT_KEY` et `DISCOVERY_VAULT_KEY` depuis #289 : le
  fichier ne porte plus aucun secret d'exemple. S7 refuse désormais aussi un `BindPassword`
  vide hors développement : un bind simple sans mot de passe est un bind anonyme
  (RFC 4513 § 5.1.2), que certains annuaires acceptent, et le contrôle de santé de l'annuaire
  l'aurait pris pour le compte de service. La démonstration garde son compte de service
  (`Demo-Service-2026`, annuaire de démonstration) ; la connexion d'un utilisateur refusait déjà
  un mot de passe vide (400).
- **Pourquoi** : reste de la part *code* de S1 (plan d'action § 5), noté dans #289 ; S1 fait
  partie du périmètre de la version stable v1.0.0.
- **Preuve** : `ComposeSecretsTests.S1_le_mot_de_passe_du_compte_de_service_vient_de_l_environnement_et_vide_il_est_refuse`,
  rouge sur `main` (valeur d'exemple dans le compose), vert avec le changement ; tests
  `ComposeSecretsTests` et S7 de `SecurityStartupTests` : 7 verts. Aucun démarrage de l'API
  hors démonstration dans la CI (Recette sur le compose de démonstration).
- **Fichiers** : `docker-compose.yml`, `backend/Core/Auth/SampleSecrets.cs`,
  `tests/SAloha.Api.Tests/ComposeSecretsTests.cs`
- **Documentation** : `docs/reference/exploitation.md` (§ Secrets, configuration),
  `docs/reference/securite.md` (secrets), `README.md` (intégration à l'entreprise),
  `docs/plan-action.md` (S1 : part code faite) ; note `changelog.d/s1-mot-de-passe-ldap.md`
- **Soldé du plan d'action** : part code de S1 ; reste l'action de l'exploitant.
- **Commit** : S1
