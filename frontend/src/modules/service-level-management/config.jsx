import { api } from '../../api'
import { titleField, descriptionField, ownerField, dateFr } from '../../core/fields'

// Gestion des niveaux de service (ITIL) : fixer des cibles claires et
// orientées métier, et évaluer la prestation au regard de ces cibles.
export const serviceConfig = {
  title: 'Catalogue des services',
  sub: 'Services délivrés aux métiers, avec leur criticité et leurs heures de service (ITIL). Tenu par les gestionnaires.',
  api: api.services, basePath: '/services', linkType: 'service', managerOnly: true,
  createLabel: '+ Nouveau service', createTitle: 'Nouveau service', titleLabel: 'Service',
  empty: 'Aucun service au catalogue.', statusAtCreation: 'En conception (par défaut)',
  statuses: [{ value: 'En conception', tone: 'new' }, { value: 'En service', tone: 'done' }, { value: 'Retiré', tone: 'closed' }],
  fields: [
    titleField('Nom du service', 'Ex. : Messagerie'),
    { key: 'criticality', label: 'Criticité', type: 'select', options: ['Faible', 'Moyenne', 'Élevée'], required: true, default: 'Moyenne' },
    { key: 'serviceHours', label: 'Heures de service (libellé)', placeholder: 'Ex. : Lun–Ven 7h–19h' },
    // SLM-14 : heures de service structurées ; tout vide = 24 h/24, 7 j/7. Les délais SLA s'y comptent.
    { key: 'workingDays', label: 'Jours ouvrés', placeholder: '1,2,3,4,5 (1 = lundi … 7 = dimanche) ; vide : tous' },
    { key: 'openingTime', label: 'Ouverture', type: 'time' },
    { key: 'closingTime', label: 'Fermeture', type: 'time' },
    { key: 'timeZone', label: 'Fuseau horaire', placeholder: 'Europe/Paris, Indian/Antananarivo… (UTC par défaut)' },
    { key: 'holidays', label: 'Jours fériés', type: 'textarea', rows: 2, placeholder: 'AAAA-MM-JJ, un par ligne ou séparés par des espaces' },
    ownerField('Responsable du service'),
    descriptionField('Description', 'Ce que le service apporte aux utilisateurs', 3),
  ],
  columns: [
    { h: 'Criticité', v: r => r.criticality },
    { h: 'Heures de service', v: r => r.serviceHours || '—' },
    { h: 'Responsable', v: r => r.ownerDisplayName || '—' },
  ],
  linkHint: 'CI qui supportent le service, demandes qui le concernent…',
}

const hours = (p) => ({ key: 'resolutionHours' + p, label: `Résolution ${p} (heures)`, type: 'number' })
// SLM-13 : délais de prise en charge, facultatifs ; donnés, ils sont complets et ne dépassent pas la résolution.
const response = (p) => ({ key: 'responseHours' + p, label: `Prise en charge ${p} (heures)`, type: 'number' })

/** Configuration des SLA : la liste des services du catalogue alimente le choix du service. */
export const agreementConfig = (services) => ({
  title: 'Accords de niveau de service',
  sub: 'Cibles convenues avec les clients (disponibilité, délais de prise en charge et de résolution par priorité) et revues (ITIL).',
  api: api.agreements, basePath: '/agreements', linkType: 'agreement', managerOnly: true,
  createLabel: '+ Nouveau SLA', createTitle: 'Nouvel accord de niveau de service',
  empty: 'Aucun SLA enregistré.',
  statuses: [{ value: 'Brouillon', tone: 'new' }, { value: 'En vigueur', tone: 'done' }, { value: 'Expiré', tone: 'closed' }],
  fields: [
    titleField('Intitulé', 'Ex. : SLA Messagerie — Direction financière'),
    { key: 'serviceId', label: 'Service', type: 'select', required: true, numeric: true, default: services[0]?.id,
      options: services.map(s => ({ value: s.id, label: `${s.title} (${s.reference})` })) },
    { key: 'customer', label: 'Client', placeholder: 'Direction, entité, métier…' },
    { key: 'availabilityTarget', label: 'Disponibilité cible (%)', type: 'number' },
    // SLM-20, SLM-23 : sans cible, le respect est mesuré mais aucune alerte n'est émise.
    { key: 'complianceTarget', label: 'Cible de respect (% d\'incidents résolus dans le délai)', type: 'number' },
    hours('P1'), hours('P2'), hours('P3'), hours('P4'),
    response('P1'), response('P2'), response('P3'), response('P4'),
    { key: 'validFrom', label: 'Valide du', type: 'date' },
    { key: 'validTo', label: 'au', type: 'date' },
    { key: 'reviewDate', label: 'Prochaine revue', type: 'date' },
    ownerField('Responsable de l\'accord'),
    descriptionField('Engagements et exclusions', undefined, 3),
  ],
  columns: [
    { h: 'Service', v: r => r.service?.title ?? '—' },
    { h: 'Client', v: r => r.customer || '—' },
    { h: 'Disponibilité', v: r => (r.availabilityTarget != null ? r.availabilityTarget + ' %' : '—') },
    { h: 'Revue', v: r => dateFr(r.reviewDate) },
  ],
  linkHint: 'Services et CI couverts par l\'accord…',
})
