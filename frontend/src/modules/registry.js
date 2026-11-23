// Registre des pratiques ITIL et des modules de conformité couverts par
// S-Aloha, et des sections de paramétrage et d'administration — source unique
// de la navigation (barre latérale : Pilotage, Pratiques, Conformité &
// réglementation, Paramétrage, Administration). Un module passe de
// `soon` à `active` le jour où son interface existe : rien d'autre à toucher
// côté navigation. Cartographie et état : docs/reference/produit.md.

/**
 * Groupes de modules, dans l'ordre de la barre latérale. Les pratiques ITIL
 * d'un côté ; de l'autre, les obligations réglementaires (NIS 2, RGPD…) qui
 * ne relèvent pas d'une pratique ITIL : les mêler surchargerait la liste des
 * pratiques et brouillerait le cycle de vie du service. `tone` : repère de
 * couleur du titre de section.
 */
export const GROUPS = [
  { id: 'practices', label: 'Pratiques' },
  { id: 'projects', label: 'Projets', tone: 'projects' },
  { id: 'compliance', label: 'Conformité & réglementation', tone: 'compliance' },
]

/**
 * Modules, dans l'ordre du cycle de vie du service pour les pratiques.
 * - `group`  : groupe de la barre latérale (GROUPS) ; absent, « practices » ;
 * - `href`   : point d'entrée du module ;
 * - `routes` : préfixes d'URL qui appartiennent au module (état actif) ;
 * - `pages`  : sous-entrées affichées quand le module est ouvert ; un `href`
 *              peut porter un filtre (`/problems?status=…`) ;
 * - `roles`  : (module ou sous-entrée) rôles qui voient l'entrée — absent, tous.
 *              Réservé au paramétrage d'une pratique (Manager, Admin) : l'entrée
 *              est masquée pour les autres rôles, que l'API refuse de toute façon.
 *
 * Une entrée n'apparaît que lorsque sa règle est implémentée : pas de page
 * vide (docs/reference/frontend.md § Navigation cible).
 */
export const MODULES = [
  {
    id: 'incident', label: 'Incidents', description: 'Rétablir le service au plus vite', status: 'active',
    href: '/incidents', routes: ['/incidents'],
  },
  {
    id: 'request', label: 'Demandes', description: 'Demandes de service utilisateurs', status: 'active',
    href: '/requests', routes: ['/requests'],
    pages: [
      { href: '/requests', label: 'Registre des demandes' },
      { href: '/requests/catalog', label: 'Catalogue de demandes' },
    ],
  },
  {
    id: 'problem', label: 'Problèmes', description: 'Causes racines et erreurs connues', status: 'active',
    href: '/problems', routes: ['/problems', '/actions'],
    pages: [
      { href: '/problems', label: 'Registre des problèmes' },
      { href: '/problems?' + new URLSearchParams({ status: 'Erreur connue' }), label: 'Erreurs connues' },
      { href: '/actions', label: 'Actions correctives' },
    ],
  },
  {
    id: 'change', label: 'Changements', description: 'Changements et mises en production', status: 'active',
    href: '/changes', routes: ['/changes'],
    pages: [
      { href: '/changes', label: 'Registre des changements' },
      { href: '/changes/schedule', label: 'Calendrier des changements' },
      { href: '/changes/models', label: 'Modèles standard' },
    ],
  },
  {
    id: 'configuration', label: 'Configuration', description: 'Actifs et dépendances (CMDB)', status: 'active',
    href: '/configuration', routes: ['/configuration'],
    pages: [
      { href: '/configuration', label: 'Registre des CI' },
      { href: '/configuration/inventory', label: 'Inventaire' },
      // CFG-40 : file de travail des gestionnaires, pas un réglage (docs/reference/frontend.md).
      { href: '/configuration/staging', label: 'Sas de staging', roles: ['Manager', 'Admin'] },
    ],
  },
  {
    id: 'service-level', label: 'Niveaux de service', description: 'Catalogue, SLA et engagements', status: 'active',
    href: '/services', routes: ['/services', '/agreements'],
    pages: [
      { href: '/services', label: 'Catalogue des services' },
      { href: '/agreements', label: 'Accords (SLA)' },
    ],
  },
  {
    id: 'knowledge', label: 'Connaissances', description: 'Capitalisation et formation', status: 'active',
    href: '/knowledge', routes: ['/knowledge'],
  },
  {
    id: 'csi', label: 'Amélioration (CSI)', description: 'Amélioration continue des services', status: 'active',
    href: '/improvements', routes: ['/improvements'],
  },
  // --- Projets ---
  {
    // Gestion de projet : la méthode choisie (Scrum, Kanban, cycle en V,
    // PRINCE2, hybride) active les composants et les onglets du projet.
    id: 'project', group: 'projects', label: 'Projets', description: 'Agile, cycle en V, PRINCE2', status: 'active',
    href: '/projects', routes: ['/projects'],
  },
  // --- Conformité & réglementation ---
  {
    // Évaluation de conformité NIS 2 au Référentiel Cyber France de l'ANSSI.
    id: 'compliance', group: 'compliance', label: 'NIS 2', description: 'Évaluations ReCyF (ANSSI)', status: 'active',
    href: '/assessments', routes: ['/assessments'],
  },
  {
    // Déclarations réglementaires des incidents de sécurité (DRG) : NIS 2, RGPD.
    id: 'regulatory-notifications', group: 'compliance', label: 'Déclarations', description: 'Incidents de sécurité : ANSSI, CNIL',
    status: 'active', href: '/regulatory-notifications', routes: ['/regulatory-notifications'],
  },
]

/** Groupe d'un module (GROUPS) : « practices » par défaut. */
export const groupOf = (module) => module.group ?? 'practices'

/**
 * Sections réservées, après les pratiques. Une section n'apparaît que si elle
 * contient au moins une entrée visible pour le rôle : vides aujourd'hui, elles
 * restent invisibles jusqu'à leur première page (pas de section vide).
 * - Paramétrage : le fonctionnement des pratiques (catalogue de demandes,
 *   modèles de changement, périodes de gel, référentiels) — délégable aux
 *   gestionnaires ;
 * - Administration : la plateforme elle-même (utilisateurs et rôles, santé,
 *   journal d'audit) — Admin seul.
 * Une entrée : { href, label, description, icon (clé de SECTION_ICONS, propre à
 * l'entrée), roles?, feature? }. `feature` : clé d'une fonctionnalité optionnelle ;
 * l'entrée n'apparaît que si l'Admin l'a activée (OPT-04).
 */
export const SECTIONS = [
  {
    id: 'settings', label: 'Paramétrage', roles: ['Manager', 'Admin'], entries: [
      { href: '/settings/categories', label: 'Catégories', description: 'Incidents et problèmes', icon: 'categories' },
      { href: '/settings/discovery', label: 'Découverte du réseau', description: 'Tâches de scan, coffre, fins de support',
        icon: 'discovery' },
      { href: '/settings/nis2', label: 'Référentiel NIS 2', description: 'Structure et texte des exigences',
        icon: 'nis2-framework', roles: ['Admin'] },
    ]
  },
  {
    id: 'administration', label: 'Administration', roles: ['Admin'], entries: [
      { href: '/admin/users', label: 'Utilisateurs et rôles', description: 'Annuaire, rôles, connexions', icon: 'users' },
      { href: '/admin/perimeters', label: 'Périmètres', description: 'Directions, sites, affectations', icon: 'perimeters' },
      { href: '/admin/search', label: 'Index de recherche', description: 'État et reconstruction', icon: 'search-index' },
      { href: '/admin/platform', label: 'Messagerie et stockage', description: 'SMTP des courriels, stockage S3 des pièces jointes', icon: 'platform' },
      { href: '/admin/features', label: 'Fonctionnalités optionnelles', description: 'Tri assisté, CMDB assistée, serveur MCP', icon: 'features' },
      { href: '/admin/health', label: 'Santé', description: 'Base, annuaire, services, version', icon: 'health' },
      { href: '/admin/audit', label: "Journal d'audit", description: 'Toutes pratiques, export', icon: 'audit' },
      { href: '/admin/duplicates', label: 'Nettoyage des doublons', description: 'Détection et fusion', icon: 'duplicates' },
    ]
  },
]

/**
 * L'entrée (module, sous-entrée, section) est-elle visible pour ce rôle ? Une entrée
 * liée à une fonctionnalité optionnelle (`feature`) exige aussi qu'elle soit activée
 * (`features` : ensemble des clés activées, core/features.js).
 */
export const visibleFor = (entry, role, features) =>
  (!entry.roles || entry.roles.includes(role)) && (!entry.feature || !!features?.has(entry.feature))
