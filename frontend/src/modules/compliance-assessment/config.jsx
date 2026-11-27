import { api } from '../../api'
import { titleField, descriptionField, ownerField, dateFr, dateTimeFr } from '../../core/fields'

export const CATEGORIES = ['Entité importante', 'Entité essentielle']

// Conformité NIS 2 (ITIL : gestion de la sécurité de l'information) :
// évaluation d'un périmètre au regard du Référentiel Cyber France (ReCyF) de
// l'ANSSI. Règles NIS-xx : docs/reference/processus/conformite-nis2.md.
export const assessmentConfig = {
  title: 'Conformité NIS 2',
  sub: 'Évaluations de conformité au Référentiel Cyber France (ReCyF) de l\'ANSSI : une réponse notée de 0 à 3 par exigence applicable, score et niveau de maturité.',
  api: api.assessments, basePath: '/assessments', linkType: 'assessment',
  createLabel: '+ Nouvelle évaluation', createTitle: 'Nouvelle évaluation NIS 2',
  empty: 'Aucune évaluation.', ownerLabel: 'Que je pilote',
  managerOnly: true,
  statuses: [
    { value: 'Brouillon', tone: 'new' }, { value: 'En cours', tone: 'progress' },
    { value: 'Validée', tone: 'done', manager: true },
  ],
  fields: [
    titleField('Intitulé', 'Ex. : Évaluation NIS 2 — SI de production, 2026'),
    { key: 'entityCategory', label: 'Catégorie d\'entité', type: 'select', options: CATEGORIES, required: true,
      default: CATEGORIES[0] },
    ownerField('Pilote'),
    // NIS-16 : 12 mois après la validation si vide ; reste modifiable une fois l'évaluation validée.
    { key: 'nextAssessmentDate', label: 'Prochaine évaluation', type: 'date', create: false },
    descriptionField('Périmètre', 'Systèmes d\'information, sites et activités couverts…', 3),
  ],
  columns: [
    { h: 'Catégorie', v: r => r.entityCategory },
    { h: 'Pilote', v: r => r.ownerDisplayName || '—' },
    { h: 'Prochaine évaluation', v: r => dateFr(r.nextAssessmentDate) },
  ],
  meta: r => [r.validatedAt && `Validée par ${r.validatedBy} le ${dateTimeFr(r.validatedAt)}`,
    r.nextAssessmentDate && `Prochaine évaluation le ${dateFr(r.nextAssessmentDate)}`],
  help: 'Une entité importante est évaluée sur les exigences « EI/EE », une entité essentielle sur toutes. Créée et validée par un gestionnaire ; tout le monde peut répondre au questionnaire. Une évaluation validée est figée : la rouvrir (statut « En cours ») pour la modifier ; seule la date de la prochaine évaluation (12 mois après la validation par défaut) reste modifiable, le pilote est relancé à l\'échéance.',
  linkHint: 'Actions d\'amélioration issues des écarts, CI et services du périmètre…',
}

export const SCORE_LABELS = ['0 — Absent', '1 — Partiel', '2 — Largement en place', '3 — Maîtrisé']
