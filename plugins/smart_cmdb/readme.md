smart_cmdb — Plugin d'organisation intelligente de la CMDB par LLM pour S-Aloha

smart_cmdb est un plugin officiel/communautaire pour la plateforme ITSM ITIL 4 & conformité NIS 2 / ReCyF S-Aloha (https://github.com/chrraj7/S-Aloha). Il permet de connecter le modèle de langage (LLM) de votre choix — hébergé localement (On-Premise / souverain) ou via API cloud — afin de structurer, dédupliquer, enrichir et cartographier automatiquement votre base de données de gestion des configurations (CMDB).

Fonctionnalités principales

Agnostique au fournisseur LLM (Bring Your Own LLM) :

Local / Souverain (On-Prem) : Ollama, vLLM, LocalAI, LM Studio, llama.cpp (compatible API OpenAI /v1/chat/completions).

Cloud / API managées : Mistral AI, OpenAI, Anthropic Claude, Azure OpenAI, Google Gemini.

Classification & normalisation automatique des CI (Configuration Items) :

Catégorisation ITIL (Serveur, Réseau, Poste AD, Application, Base de données, Stockage, Service métier).

Harmonisation des conventions de nommage, des environnements (Production, Préproduction, Recette, On-Prem) et des niveaux de criticité.

Détection et fusion des doublons :

Identification des CI redondants issus de sources hétérogènes (imports CSV, découverte réseau, Active Directory, saisie manuelle) avec score de confiance et proposition de fusion assistée.

Cartographie des dépendances et relations ITIL :

Inférence des liens (héberge, dépend de, alimente, sauvegardé par) entre infrastructures, applications et services métiers pour faciliter l'analyse d'impact (incidents & changements).

Alignement NIS 2 / ReCyF :

Suggestion automatique des actifs essentiels/importants et rattachement aux périmètres de conformité réglementaire.

Mode Human-in-the-Loop (validation humaine) :

Aucune modification n'est appliquée silencieusement : chaque restructuration proposée par le LLM passe par une file d'approbation avec aperçu avant / après et journalisation d'audit complète.

Architecture & confidentialité

+-------------------+       HTTPS / Localhost       +---------------------------+
|     S-Aloha       |  -------------------------->  |     Plugin smart_cmdb     |
|  (CMDB & NIS 2)   |  <--------------------------  |  - Anonymisation / Masque |
+-------------------+     Propositions validées     |  - Prompting structuré    |
                                                    |  - File d'approbation     |
                                                    +-------------+-------------+
                                                                  |
                                      API Standard (OpenAI / Ollama / Mistral / Anthropic)
                                                                  v
                                                    +---------------------------+
                                                    |   LLM choisi par l'admin  |
                                                    | (On-Premise ou Cloud API) |
                                                    +---------------------------+


Mode Air-Gapped / On-Premise : conçu pour fonctionner intégralement sur votre réseau interne avec un serveur Ollama ou vLLM, garantissant qu'aucune donnée d'infrastructure ne quitte votre périmètre.

Masquage des données sensibles (PII / Secrets Redaction) : option intégrée permettant de masquer automatiquement les adresses IP publiques, identifiants et chaînes de connexion avant l'envoi au modèle.

Prérequis

Instance S-Aloha (v1.x ou supérieure) avec droits d'administration des plugins et accès à l'API CMDB.

Un point de terminaison LLM accessible depuis le serveur S-Aloha :

Exemple local : http://localhost:11434/v1 (Ollama) ou http://10.20.10.30:8000/v1 (vLLM).

Exemple cloud : https://api.mistral.ai/v1, https://api.openai.com/v1, etc.

Installation

Cloner ou copier le plugin dans le répertoire plugins/ de votre instance S-Aloha :

cd /opt/s-aloha/plugins
git clone https://github.com/chrraj7/smart_cmdb.git


Installer les dépendances du plugin (si applicable selon votre environnement) :

cd smart_cmdb
npm install --production


Activer le plugin dans l'interface d'administration de S-Aloha :

Rendez-vous dans Administration → Plugins → smart_cmdb → Activer.

Configuration

Configurez le fournisseur LLM via les variables d'environnement de S-Aloha ou directement dans Administration → Plugins → smart_cmdb → Paramètres.

Variables d'environnement (.env)

# Activation du plugin
SMART_CMDB_ENABLED=true

# Fournisseur LLM : openai_compatible | ollama | mistral | anthropic | azure
SMART_CMDB_LLM_PROVIDER=ollama

# URL de base de l'API LLM
SMART_CMDB_LLM_BASE_URL=http://localhost:11434/v1

# Nom du modèle à utiliser
SMART_CMDB_LLM_MODEL=mistral-nemo:latest

# Clé API (laisser vide pour une instance locale sans authentification)
SMART_CMDB_LLM_API_KEY=

# Paramètres d'exécution et sécurité
SMART_CMDB_TEMPERATURE=0.1
SMART_CMDB_MAX_TOKENS=4096
SMART_CMDB_ANONYMIZE_IPS=true
SMART_CMDB_REQUIRE_APPROVAL=true


Exemples de configuration par fournisseur

Fournisseur

SMART_CMDB_LLM_PROVIDER

SMART_CMDB_LLM_BASE_URL

Exemple de SMART_CMDB_LLM_MODEL

Ollama (Local)

ollama

http://localhost:11434/v1

qwen2.5:14b / mistral-nemo

vLLM (On-Prem)

openai_compatible

http://vllm.local:8000/v1

meta-llama/Llama-3.3-70B-Instruct

Mistral AI

mistral

https://api.mistral.ai/v1

mistral-large-latest

OpenAI

openai_compatible

https://api.openai.com/v1

gpt-4.1

Anthropic

anthropic

https://api.anthropic.com/v1

claude-sonnet-4-5

Utilisation

1. Depuis l'interface web S-Aloha

Ouvrez le module CMDB → Organisation intelligente (smart_cmdb).

Sélectionnez un périmètre (ou l'ensemble de la CMDB) et choisissez l'action souhaitée :

Normaliser & catégoriser les CI

Détecter les doublons

Générer la carte des dépendances

Auditer la criticité NIS 2 / ReCyF

Examinez les suggestions générées dans l'onglet Propositions en attente, puis cliquez sur Appliquer ou Rejeter.

2. Via l'API REST du plugin

Vous pouvez déclencher une analyse automatisée (par exemple après un import nocturne d'inventaire) :

curl -X POST "https://saloha.local/api/plugins/smart_cmdb/analyze" \
  -H "Authorization: Bearer <VOTRE_TOKEN_API>" \
  -H "Content-Type: application/json" \
  -d '{
    "perimeterId": "PER-IDF-01",
    "tasks": ["normalize", "deduplicate", "infer_dependencies"],
    "dryRun": true
  }'


Sécurité, gouvernance et auditabilité

Traçabilité complète : chaque requête envoyée au LLM, le modèle utilisé, l'horodatage et l'utilisateur ayant approuvé les changements sont consignés dans le journal d'audit S-Aloha.

Faible température par défaut (0.1) : garantit des réponses déterministes et structurées au format JSON strict.

Validation de schéma : toutes les sorties du LLM sont validées contre le schéma de données CMDB de S-Aloha avant toute écriture en base.

Contribution & Licence

Les contributions (connecteurs de découverte, modèles de prompts métiers, règles de validation NIS 2) sont les bienvenues via Pull Request sur le dépôt du projet.

Distribué sous la même licence open-source que S-Aloha.
