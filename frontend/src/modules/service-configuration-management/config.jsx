import { api } from '../../api'
import { titleField, descriptionField, ownerField } from '../../core/fields'

export const CI_TYPES = ['Application', 'Serveur', 'Base de données', 'Réseau', 'Stockage', 'Poste de travail', 'Logiciel', 'Autre']
export const RELATION_TYPES = ['Dépend de', 'Héberge', 'Fait partie de', 'Se connecte à']
/** Types qui portent une identification réseau et matérielle (§ 3.2, § 3.4 de la pratique). */
export const HARDWARE_TYPES = ['Serveur', 'Réseau', 'Stockage', 'Poste de travail']
/** CFG-44 : constructeur, numéro de série et emplacement exigés en service (un serveur peut être une VM). */
export const PHYSICAL_TYPES = ['Réseau', 'Stockage', 'Poste de travail']
export const CRITICALITIES = ['Vitale', 'Majeure', 'Standard', 'Mineure']
export const APP_LIFECYCLES = ['Étude', 'Développement', 'Homologation', 'En production', 'Dépréciée', 'Retirée']

const isApp = f => f.ciType === 'Application'
const isHardware = f => HARDWARE_TYPES.includes(f.ciType)
const IN_SERVICE = 'Requis pour un CI en service ou hors service.'

// Gestion de la configuration des services (ITIL) : une information exacte
// et fiable sur les services et les CI, quand et où elle est nécessaire.
export const ciConfig = {
  title: 'Configuration',
  sub: 'Éléments de configuration (CI) et leurs dépendances : une information fiable, disponible quand il le faut (ITIL).',
  api: api.configurationItems, basePath: '/configuration', linkType: 'ci',
  createLabel: '+ Nouveau CI', createTitle: 'Nouvel élément de configuration',
  empty: 'Aucun élément de configuration enregistré.', ownerLabel: 'Dont je suis propriétaire',
  titleLabel: 'Nom', statusAtCreation: 'En service (par défaut)',
  searchPlaceholder: 'Nom, référence, code applicatif, n° de série, IP…',
  filter: { key: 'type', label: 'Tous les types', options: CI_TYPES },
  statuses: [
    { value: 'Planifié', tone: 'new' }, { value: 'En service', tone: 'done' },
    { value: 'Hors service', tone: 'wait' }, { value: 'Retiré', tone: 'closed', manager: true },
  ],
  fields: [
    titleField('Nom', 'Ex. : srv-erp-01'),
    { key: 'ciType', label: 'Type', type: 'select', options: CI_TYPES, required: true, default: 'Serveur' },
    { key: 'environment', label: 'Environnement', type: 'select', options: ['Production', 'Recette', 'Développement', 'Autre'], required: true, default: 'Production' },
    { key: 'location', label: 'Emplacement', placeholder: 'Site > Bâtiment > Salle > Baie > U, ou cloud',
      hint: f => PHYSICAL_TYPES.includes(f.ciType) ? IN_SERVICE : null },
    ownerField('Propriétaire'),
    descriptionField('Description', undefined, 3),

    // Identification technique (§ 3.2) : critères de rapprochement de la découverte (CFG-41).
    { key: '_tech', type: 'heading', label: 'Identification technique', when: isHardware },
    { key: 'ipAddress', label: 'Adresse IP', placeholder: '10.0.0.12', when: isHardware },
    { key: 'macAddress', label: 'Adresse MAC', placeholder: '00:1A:2B:3C:4D:5E', when: isHardware },
    { key: 'operatingSystem', label: 'Système / micrologiciel', placeholder: 'Windows Server 2022, FortiOS 7.4…', when: isHardware },

    // Portefeuille applicatif (§ 3.3, CFG-43, CFG-48).
    { key: '_app', type: 'heading', label: 'Portefeuille applicatif', hint: 'Gouvernance de l’application (CFG-43).', when: isApp },
    { key: 'appCode', label: 'Code applicatif', placeholder: 'Ex. : FIN ou APP-FIN-001', hint: IN_SERVICE, when: isApp },
    { key: 'businessUnit', label: 'Direction utilisatrice', placeholder: 'Ex. : Direction financière', hint: IN_SERVICE, when: isApp },
    { key: 'businessOwnerId', nameKey: 'businessOwnerDisplayName', typeKey: 'businessOwnerType', label: 'Propriétaire métier',
      type: 'person', hint: IN_SERVICE, when: isApp },
    { key: 'technicalOwnerId', nameKey: 'technicalOwnerDisplayName', typeKey: 'technicalOwnerType', label: 'Responsable DSI',
      type: 'person', hint: IN_SERVICE, when: isApp },
    { key: 'criticality', label: 'Criticité', type: 'select', options: CRITICALITIES, hint: IN_SERVICE, when: isApp },
    { key: 'appLifecycle', label: 'Cycle applicatif', type: 'select', options: APP_LIFECYCLES,
      hint: 'Planifié : étude à homologation ; en service : en production ou dépréciée. Vide : déduit du statut.', when: isApp },
    { key: 'rtoHours', label: 'RTO (heures)', type: 'number', when: isApp },
    { key: 'rpoHours', label: 'RPO (heures)', type: 'number', when: isApp },
    { key: 'gdprSensitive', label: 'RGPD', type: 'checkbox', checkboxLabel: 'Données personnelles ou sensibles', when: isApp },
    { key: 'nis2Scope', label: 'NIS 2', type: 'checkbox', checkboxLabel: 'Dans le périmètre NIS 2', when: isApp },

    // Actifs matériels (§ 3.4, CFG-44, CFG-47).
    { key: '_hw', type: 'heading', label: 'Actif matériel', hint: 'Identification et cycle de vie (CFG-44, CFG-47).', when: isHardware },
    { key: 'vendor', label: 'Constructeur', placeholder: 'Cisco, Dell, HPE…', when: isHardware,
      hint: f => PHYSICAL_TYPES.includes(f.ciType) ? IN_SERVICE : null },
    { key: 'model', label: 'Modèle', when: isHardware },
    { key: 'serialNumber', label: 'Numéro de série', when: isHardware,
      hint: f => PHYSICAL_TYPES.includes(f.ciType) ? IN_SERVICE : 'Facultatif pour un serveur virtuel.' },
    { key: 'purchaseDate', label: 'Date d’acquisition', type: 'date', when: isHardware },
    { key: 'warrantyExpiry', label: 'Fin de garantie', type: 'date', when: isHardware },
    { key: 'endOfSupport', label: 'Fin de support constructeur', type: 'date', when: isHardware },
  ],
  columns: [
    { h: 'Type', v: r => r.ciType },
    { h: 'Environnement', v: r => r.environment },
    { h: 'Propriétaire', v: r => r.ownerDisplayName || '—' },
  ],
  help: 'Un CI en service a un propriétaire. Deux CI non retirés ne portent pas le même nom dans un même environnement, ni le même code applicatif ou numéro de série.',
  linkHint: 'Services rendus, incidents et changements qui touchent ce CI…',
}

/** Libellés des sources de découverte et des types de scan. */
export const SOURCE_LABELS = { IpSweep: 'Balayage IP', Snmp: 'SNMP', ActiveDirectory: 'Active Directory', Ssh: 'SSH', Proxmox: 'Proxmox VE', VCenter: 'vCenter', WinRm: 'WinRM' }
export const RULE_LABELS = { SerialNumber: 'Numéro de série', MacAddress: 'Adresse MAC', Hostname: 'Nom d’hôte', IpAddress: 'Adresse IP', None: 'Aucun' }
/** Ton des statuts du sas de staging (badges). */
export const STAGING_TONES = { 'À rapprocher': 't-progress', 'En conflit': 't-bad', 'Rapproché': 't-done', 'Ignoré': 't-closed' }
export const JOB_TONES = { 'Jamais lancée': 't-closed', 'En file': 't-new', 'En cours': 't-progress', 'Réussie': 't-done', 'En échec': 't-bad' }
export const SUPPORT_TONES = { 'Fin de support dépassée': 't-bad', 'Fin de support proche': 't-wait', 'Supporté': 't-done', 'Non renseigné': 't-closed' }
export const WARRANTY_TONES = { 'Expirée': 't-bad', 'Expire bientôt': 't-wait', 'Sous garantie': 't-done', 'Non renseigné': 't-closed' }
