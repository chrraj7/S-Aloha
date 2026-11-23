import React from 'react'
import { api } from '../../api'
import { titleField, descriptionField, ownerField, dateTimeFr } from '../../core/fields'
import TriageSuggestions from './TriageSuggestions'

// Gestion des incidents (ITIL 4) : minimiser l'impact négatif des incidents
// en rétablissant le service normal au plus vite.
/** INC-24 : tonalité de l'état SLA (dépassé, en risque, suspendu En attente…). */
export const SLA_TONE = { 'SLA dépassé': 't-bad', 'SLA en risque': 't-wait', 'SLA suspendu': 't-closed', 'Dans le SLA': 't-done',
  'SLA respecté': 't-done', 'SLA non respecté': 't-bad' }
export const SlaBadge = ({ state }) => (state && state !== 'Sans SLA' ? <span className={'badge ' + (SLA_TONE[state] ?? '')}>{state}</span> : null)

export const incidentConfig = {
  title: 'Incidents',
  sub: 'Rétablir le service normal au plus vite et limiter l\'impact des interruptions (ITIL).',
  api: api.incidents, basePath: '/incidents', linkType: 'incident',
  createLabel: '+ Déclarer un incident', createTitle: 'Déclarer un incident',
  empty: 'Aucun incident enregistré.', ownerLabel: 'Assignés à moi',
  statuses: [
    { value: 'Nouveau', tone: 'new' }, { value: 'En cours', tone: 'progress' },
    { value: 'En attente', tone: 'wait' }, { value: 'Résolu', tone: 'done' }, { value: 'Clos', tone: 'closed' },
  ],
  fields: [
    titleField('Titre', 'Ex. : Messagerie inaccessible depuis le site de Tana'),
    descriptionField('Description', 'Symptômes, utilisateurs touchés, depuis quand…'),
    { key: 'impact', label: 'Impact', type: 'select', options: ['Faible', 'Moyen', 'Élevé'], required: true, default: 'Moyen' },
    { key: 'urgency', label: 'Urgence', type: 'select', options: ['Faible', 'Moyenne', 'Élevée'], required: true, default: 'Moyenne' },
    { key: 'category', label: 'Catégorie', placeholder: 'Réseau, Poste de travail, Application…' },
    { key: 'affectedService', label: 'Service affecté', placeholder: 'Ex. : Messagerie' },
    // INC-04, INC-05 : canal de signalement et utilisateur affecté (déclarant métier).
    { key: 'channel', label: 'Canal', type: 'select', options: ['Portail', 'Téléphone', 'Courriel', 'Supervision', 'Sur place'] },
    { key: 'callerId', nameKey: 'callerDisplayName', label: 'Utilisateur affecté', type: 'person' },
    ownerField('Assigné à'),
    // INC-17 : changer de groupe responsable (escalade fonctionnelle) se motive.
    { key: 'escalationReason', label: 'Motif d’escalade', type: 'textarea', rows: 2, create: false,
      placeholder: 'Obligatoire pour confier l’incident à un autre groupe.' },
    { key: 'isMajor', label: 'Gravité', type: 'checkbox', checkboxLabel: 'Incident majeur', managerOnly: true },
    // INC-40 : tout rôle marque un incident de sécurité (cyber), qui peut appeler des déclarations réglementaires.
    { key: 'isSecurity', label: 'Sécurité', type: 'checkbox', checkboxLabel: 'Incident de sécurité (cyber)' },
    { key: 'resolution', label: 'Résolution', type: 'textarea', create: false, rows: 3,
      placeholder: 'Obligatoire pour passer à Résolu ou Clos.' },
    { key: 'resolutionCode', label: 'Code de résolution', type: 'select', create: false,
      options: ['Correctif appliqué', 'Contournement', 'Résolu sans action', 'Non reproductible', 'Doublon'] },
    // INC-07 : motif obligatoire pour passer En attente ; l'horloge SLA s'arrête pendant l'attente (INC-22).
    { key: 'onHoldReason', label: 'Motif d\'attente', type: 'select', create: false,
      options: ['Attente utilisateur', 'Attente fournisseur', 'Attente changement'] },
    { key: 'reopenReason', label: 'Motif de réouverture', type: 'textarea', create: false, rows: 2,
      placeholder: 'Obligatoire pour rouvrir un incident résolu.' },
  ],
  // INC-27 : début constaté, saisi par l'API (supervision) et absent du formulaire ; renvoyé
  // tel quel, sans quoi l'enregistrement de la fiche l'efface et l'incident sort du MTTD.
  keep: r => ({ occurredAt: r.occurredAt }),
  columns: [
    { h: 'Priorité', v: r => <span className={'badge ' + r.priority}>{r.priority}</span> },
    { h: 'Service', v: r => r.affectedService || '—' },
    { h: 'SLA', v: r => <SlaBadge state={r.slaState} /> },
    { h: 'Assigné à', v: r => r.ownerDisplayName || '—' },
  ],
  badges: r => <>
    <span className={'badge ' + r.priority}>{r.priority}</span>{' '}
    {r.isMajor && <span className="badge t-bad">Majeur</span>}{' '}
    {r.isSecurity && <span className="badge t-bad">Sécurité</span>}{' '}
    <SlaBadge state={r.slaState} />
  </>,
  // SLM-13, INC-21 : échéances tirées du SLA en vigueur du service.
  meta: r => [!r.firstResponseAt && r.responseDueAt && `Prise en charge attendue avant le ${dateTimeFr(r.responseDueAt)}`,
    r.firstResponseAt && `Pris en charge le ${dateTimeFr(r.firstResponseAt)}`,
    !r.resolvedAt && r.resolutionDueAt && `Résolution attendue avant le ${dateTimeFr(r.resolutionDueAt)}`,
    r.resolvedAt && `Résolu le ${dateTimeFr(r.resolvedAt)}`,
    r.reopenCount > 0 && `Rouvert ${r.reopenCount} fois`,
    r.status === 'En attente' && r.onHoldSince && `En attente depuis le ${dateTimeFr(r.onHoldSince)} (horloge SLA suspendue)`,
    r.holdFollowUp && 'Attente de plus de 5 jours ouvrés : relancer',
    r.heldMinutes > 0 && `Attente cumulée : ${Math.round(r.heldMinutes / 6) / 10} h de service`],
  help: 'Prise en charge (En cours) : un responsable. Attente : un motif ; l\'horloge SLA s\'arrête et reprend au retour En cours. Résolution : description et code ; un doublon est relié à l\'incident conservé (onglet Liens). Réouverture : un motif, dans les 10 jours ouvrés. Même panne : rattacher l\'incident à un parent ; résoudre le parent résout ses enfants. Une attente de plus de 5 jours ouvrés remonte au responsable. Incident majeur : déclaré par un gestionnaire ; il se résout relié à un problème. Confier l\'incident à un autre groupe se motive.',
  suggestOnTyping: true,
  // TRI-02 : suggestions du tri assisté à la saisie, s'il est activé.
  createExtras: ({ form, merge }) => <TriageSuggestions form={form} merge={merge} />,
  linkHint: 'Problème sous-jacent, CI touchés, article de connaissance appliqué…',
}
