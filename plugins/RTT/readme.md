# RTT — Real-Time Ticket Triage

**RTT** (_Real-Time Ticket Triage_) est un futur plugin S-Aloha dédié à la priorisation assistée et au routage en temps réel des tickets d'assistance.

> **Objectif :** aider les équipes de support à identifier rapidement les demandes les plus critiques, les regrouper et les affecter au bon service, sans se substituer à la décision humaine.

[![Plugin S-Aloha](https://img.shields.io/badge/S--Aloha-plugin-blue)](https://github.com/chrraj7/S-Aloha)
[![Licence](https://img.shields.io/badge/licence-AGPL--3.0-blue)](../../LICENSE)

---

## Sommaire

- [Vision](#vision)
- [Fonctionnalités prévues](#fonctionnalités-prévues)
- [Architecture](#architecture)
- [Flux de traitement](#flux-de-traitement)
- [Modèle de données](#modèle-de-données)
- [Règles de priorité](#règles-de-priorité)
- [Installation](#installation)
- [Configuration](#configuration)
- [Roadmap](#roadmap)
- [Sécurité et conformité](#sécurité-et-conformité)
- [Limitations](#limitations)

## Vision

Dans un centre de services, les tickets arrivent à cadence variable et avec des niveaux de détail très différents. RTT vise à réduire le temps nécessaire pour comprendre, prioriser et aiguiller chaque ticket.

```mermaid
flowchart LR
    T[Ticket entrant] --> C[Collecte temps réel]
    C --> A[Analyse et enrichissement]
    A --> P[Calcul de priorité]
    P --> R[Routage vers une équipe]
    R --> H[Validation humaine]
    H -->|Approuvé| G[Gestionnaire ITSM]
    H -->|Corrigé| A
-----
Fonctionnalités prévues
Fonctionnalité	Description	Valeur métier
Classification rapide	Détection de la catégorie, du service et du type de demande	Réduit les retours manuels
Analyse de criticité	Évaluation de l'impact et de l'urgence	Met les incidents majeurs en évidence
Suggestion de priorité	Proposition conforme aux règles ITIL	Standardise le traitement
Routage intelligent	Affectation vers le groupe ou le rôle le plus pertinent	Réduit les réaffectations
Détection de corrélation	Repérage de tickets liés ou répétitifs	Facilite la résolution collective
Explication des décisions	Présentation des motifs ayant influencé la recommandation	Améliore la confiance des analystes
Revue humaine	Contrôle avant toute action sensible	Conserve l'autorité de l'opérateur
Architecture

RTT devra rester découplé du moteur principal de S-Aloha afin de faciliter son activation, sa désactivation et ses mises à jour indépendantes.

flowchart TB
    subgraph S-Aloha
        API[API et événements]
        ITSM[Gestion des tickets]
        USER[Interface utilisateur]
        DB[(Base de données S-Aloha)]
    end

    subgraph Plugin RTT
        ING[Connecteur d'entrée]
        RULE[Règles métier]
        ENG[Moteur d'évaluation]
        OUT[Connecteur de sortie]
    end

    API -->|Événement ticket| ING
    ING --> ENG
    RULE --> ENG
    ENG --> OUT
    OUT -->|Suggestion| USER
    OUT -->|Affectation avec validation| ITSM
    ITSM <--> DB

Composants

    Connecteur d'entrée : reçoit les événements de création ou de modification des tickets.
    Règles métier : adapte les politiques de triage aux besoins de l'organisation.
    Moteur d'évaluation : calcule une recommandation à partir des données disponibles.
    Connecteur de sortie : expose les suggestions dans S-Aloha et applique uniquement les actions autorisées.
    Journal d'audit : conserve les données utiles à la traçabilité des décisions.

Flux de traitement
sequenceDiagram
    actor Utilisateur
    participant S as S-Aloha
    participant R as Plugin RTT
    participant A as Analyste

    Utilisateur->>S: Crée ou met à jour un ticket
    S-->>R: Publie l'événement
    R->>R: Normalise les données
    R->>R: Évalue les règles et calcule la priorité
    R-->>S: Retourne classification et score
    S-->>A: Affiche les recommandations
    A->>S: Accepte, corrige ou rejette
    S-->>R: Enregistre la décision finale

Exemple de séquence fonctionnelle

    Un utilisateur ouvre un incident lié à la messagerie.
    RTT analyse le titre, la description, le service concerné et les informations disponibles.
    Le plugin propose une catégorie, une priorité et une équipe d'affectation.
    L'analyste valide ou corrige la proposition.
    S-Aloha applique la décision et conserve son historique.

Modèle de données

Les structures suivantes sont indicatives et pourront évoluer avec l'implémentation du plugin.
classDiagram
    class TicketContext {
        +string TicketId
        +string Title
        +string Description
        +string Service
        +string RequestType
        +string RequesterId
    }

    class TriageRecommendation {
        +string Category
        +string Priority
        +string Team
        +number Confidence
        +list Reasons
    }

    class HumanDecision {
        +string Decision
        +string FinalPriority
        +string FinalTeam
        +string OperatorId
        +datetime DecidedAt
    }

    TicketContext --> TriageRecommendation : évalué par
    TriageRecommendation --> HumanDecision : validé ou corrigé par

Règles de priorité

RTT pourra combiner les critères suivants :
Critère	Exemples
Impact	Utilisateur unique, service limité, interruption générale
Urgence	Incident isolé, incident répétitif, panne totale
Disponibilité	Comptes, postes, réseau, applications, téléphonie
Sécurité	Compte compromis, fuite ou suspicion d'intrusion
SLA	Risque d'échéance, délai déjà dépassé
Corrélation	Nombre de tickets liés, même service ou même symptômes

Les règles devront être visibles, paramétrables et auditées. Toute priorité automatique restera soumise à validation conformément aux politiques internes.
Installation

    Cette section décrit le comportement cible. Les commandes exactes dépendront de l'architecture finale du plugin.

    Compiler ou télécharger le paquet du plugin RTT.
    Le placer dans le répertoire des plugins configuré par S-Aloha.
    Redémarrer S-Aloha ou déclencher le chargement des plugins selon le mode d'exploitation.
    Vérifier l'entrée dans les journaux d'application.
    Activer RTT pour les profils ou catégories autorisés.

Configuration

Exemple conceptuel de configuration :
rtt:
  enabled: true

  processing:
    realtime: true
    requireHumanApproval: true

  routing:
    defaultTeam: "support-niveau-1"
    allowedTeams:
      - "support-niveau-1"
      - "support-niveau-2"
      - "exploitation"

  audit:
    retainDecisions: true

  privacy:
    minimizePersonalData: true

Les options et noms exacts devront être alignés avec le format de configuration retenu par S-Aloha.
Roadmap
timeline
    title Roadmap RTT
    Phase 1 : Définir le contrat d'interface
           : Modèle de recommandation
           : Règles de priorité
    Phase 2 : Développer le connecteur
           : Journalisation
           : Vue de validation humaine
    Phase 3 : Ajouter l'analyse avancée
           : Détection de corrélation
           : Explication des décisions
    Phase 4 : Industrialiser
           : Paramètres par organisation
           : Mesures de performance
           : Tests de charge

Jalons

    Valider les cas d’usage avec les équipes support
    Définir le contrat d'entrée/sortie
    Implémenter les règles prioritaires minimales
    Ajouter la traçabilité complète
    Fournir une interface de validation
    Mesurer les faux positifs et les corrections humaines
    Documenter le déploiement et la configuration

Sécurité et conformité

RTT doit être conçu selon les principes suivants :

    Moindre privilège : aucun droit d'écriture inutile.
    Donnée minimale : ne transmettre que les champs nécessaires.
    Réversible : permettre l'annulation ou la correction d'une décision.
    Traçable : conserver l'origine des règles ayant produit une recommandation.
    Contrôlable : permettre aux administrateurs de désactiver ou restreindre le plugin.
    Conforme RGPD : tenir compte de la finalité, de la durée de conservation et des droits des personnes.
    On-premise : privilégier un traitement local compatible avec les besoins de souveraineté de S-Aloha.

Limitations

    Une recommandation ne remplace pas l'analyse d'un technicien.
    Les règles simples peuvent être insuffisantes pour des incidents complexes.
    Une qualité de données faible dégrade directement les suggestions.
    Toute intégration avec une intelligence artificielle devra être évaluée séparément pour ses performances, ses biais et sa conformité.
    Les performances et volumes pris en charge restent à définir lors de l'implémentation.

Contribuer

Les contributions sont les bienvenues : documentation, modèles de règles, tests fonctionnels ou retours d'expérience.

Avant toute proposition :

    Décrire le cas d’usage métier.
    Identifier les données réellement nécessaires.
    Documenter les risques et règles de sécurité.
    Ajouter des tests pour les décisions attendues.

Licence

Ce plugin fait partie de S-Aloha, distribué sous licence GNU Affero General Public License v3.0.
