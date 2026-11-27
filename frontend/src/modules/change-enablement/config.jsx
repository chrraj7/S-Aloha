import { api } from '../../api'
import { titleField, descriptionField, ownerField, dateTimeFr } from '../../core/fields'

// Habilitation des changements (ITIL) : maximiser le nombre de changements
// réussis en évaluant les risques, en autorisant et en gérant le calendrier.
export const changeStatuses = [
  { value: 'Demandé', tone: 'new' }, { value: 'Évalué', tone: 'wait' },
  { value: 'Autorisé', tone: 'progress', manager: true }, { value: 'Rejeté', tone: 'bad', manager: true },
  { value: 'Planifié', tone: 'progress' }, { value: 'Mis en œuvre', tone: 'done' }, { value: 'Clos', tone: 'closed' },
]

export const changeConfig = {
  title: 'Changements',
  sub: 'Évaluer les risques, autoriser et planifier les changements pour qu\'ils réussissent (ITIL).',
  api: api.changes, basePath: '/changes', linkType: 'change',
  createLabel: '+ Demander un changement', createTitle: 'Demande de changement',
  empty: 'Aucun changement enregistré.', ownerLabel: 'Sous ma responsabilité',
  statuses: changeStatuses,
  fields: [
    titleField('Titre', 'Ex. : Migration du serveur de fichiers vers le nouveau stockage'),
    { key: 'changeType', label: 'Type', type: 'select', options: ['Standard', 'Normal', 'Urgent'], required: true, default: 'Normal' },
    { key: 'risk', label: 'Risque', type: 'select', options: ['Faible', 'Moyen', 'Élevé'], required: true, default: 'Moyen' },
    { key: 'plannedStart', label: 'Début planifié', type: 'datetime' },
    { key: 'plannedEnd', label: 'Fin planifiée', type: 'datetime' },
    ownerField('Responsable du changement'),
    descriptionField('Justification et périmètre'),
    { key: 'implementationPlan', label: 'Plan de mise en œuvre', type: 'textarea', rows: 3 },
    { key: 'backoutPlan', label: 'Plan de retour arrière', type: 'textarea', rows: 3 },
    // CHG-14 : dates réelles (début exigé pour Mis en œuvre, fin pour Clos).
    { key: 'actualStart', label: 'Début réel', type: 'datetime', create: false },
    { key: 'actualEnd', label: 'Fin réelle', type: 'datetime', create: false },
    { key: 'outcome', label: 'Résultat', type: 'select', options: ['Réussi', 'Échoué'], create: false },
    // CHG-15 : exigée avant de clore un changement urgent ou échoué.
    { key: 'pirNotes', label: 'Revue post-implémentation', type: 'textarea', rows: 3, create: false,
      placeholder: 'Ce qui s\'est passé, causes, enseignements. Obligatoire pour clore un changement urgent ou échoué.' },
    // CHG-22 : exigé de l'autorité qui autorise un changement en conflit de calendrier.
    { key: 'conflictComment', label: 'Commentaire sur le conflit de calendrier', type: 'textarea', rows: 2, create: false, managerOnly: true,
      placeholder: 'Obligatoire pour autoriser un changement en conflit : pourquoi le conflit est acceptable, ou comment il sera géré.' },
    { key: 'rejectionReason', label: 'Motif de rejet', type: 'textarea', rows: 2, create: false,
      placeholder: 'Obligatoire pour rejeter : communiqué au demandeur.' },
  ],
  columns: [
    { h: 'Type', v: r => r.changeType },
    { h: 'Risque', v: r => r.risk },
    { h: 'Début planifié', v: r => <>{dateTimeFr(r.plannedStart)}{r.overrun && <span className="badge t-bad" style={{ marginLeft: 6 }}>Dépassement</span>}</>},
  ],
  meta: r => [r.authorizedAt && `Autorisé par ${r.authorizedBy} le ${dateTimeFr(r.authorizedAt)}`,
    r.overrun && `Dépassement : fin planifiée le ${dateTimeFr(r.plannedEnd)}${r.actualEnd ? `, fin réelle le ${dateTimeFr(r.actualEnd)}` : ''}`,
    // CHG-30, CHG-31 : relance en cours.
    r.followUp && `À relancer : ${r.followUp.toLowerCase()}`],
  help: 'Standard : pré-autorisé. Normal et urgent : plans de mise en œuvre et de retour arrière pour l\'évaluation, puis autorisation par un gestionnaire. Planifié : début et fin. Un rejet se motive. Modifier le type, le risque, les plans ou le créneau d\'un changement autorisé redemande une autorisation. Standard : créé depuis un modèle standard, dont il reprend les plans et le risque. Normal à risque élevé : autorisé par le CAB (deux gestionnaires). Planification refusée dans une période de gel, sauf urgent. Normal et urgent : au moins un CI relié (onglet Liens) pour l\'évaluation ; le demandeur n\'autorise pas son propre changement. Mis en œuvre : début réel ; Clos : fin réelle et résultat, revue post-implémentation pour un changement urgent ou échoué. Un changement en conflit de calendrier s\'autorise avec un commentaire. Relancé quand sa fin planifiée passe sans mise en œuvre, ou s\'il reste mis en œuvre plus de 2 jours ouvrés sans être clos.',
  linkHint: 'Problème ou demande à l\'origine, CI modifiés…',
}

/** CHG-26 : choix du modèle standard à la création (obligatoire pour un changement Standard). */
export const standardModelField = models => ({
  key: 'standardModelId', label: 'Modèle standard (changement Standard)', type: 'select', numeric: true, onlyCreate: true,
  options: models.map(m => ({ value: m.id, label: `${m.name} (risque ${m.risk.toLowerCase()})` })),
})
