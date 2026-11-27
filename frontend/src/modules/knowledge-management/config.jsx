import { api } from '../../api'
import { titleField, descriptionField, ownerField, dateFr, dateTimeFr } from '../../core/fields'

export const ARTICLE_TYPES = ['Solution', 'Procédure', 'Erreur connue', 'FAQ']

// Gestion des connaissances (ITIL) : maintenir et améliorer l'usage
// efficace, efficient et pratique de l'information et des connaissances.
export const knowledgeConfig = {
  title: 'Connaissances',
  sub: 'Solutions, procédures, erreurs connues et FAQ : capitaliser pour résoudre plus vite et former (ITIL).',
  api: api.knowledge, basePath: '/knowledge', linkType: 'article',
  createLabel: '+ Rédiger un article', createTitle: 'Nouvel article',
  empty: 'Aucun article dans la base de connaissances.', ownerLabel: 'Dont je suis responsable',
  searchPlaceholder: 'Rechercher (titre, mots-clés, contenu)…',
  filter: { key: 'type', label: 'Tous les types', options: ARTICLE_TYPES },
  statuses: [
    { value: 'Brouillon', tone: 'new' }, { value: 'Publié', tone: 'done', manager: true }, { value: 'Archivé', tone: 'closed' },
  ],
  fields: [
    titleField('Titre', 'Ex. : Réinitialiser l\'accès VPN d\'un utilisateur'),
    { key: 'articleType', label: 'Type', type: 'select', options: ARTICLE_TYPES, required: true, default: 'Solution' },
    { key: 'keywords', label: 'Mots-clés', placeholder: 'vpn, accès, nomade' },
    { key: 'reviewDate', label: 'Prochaine revue', type: 'date' },
    ownerField('Responsable de l\'article'),
    descriptionField('Résumé', 'En une ou deux phrases : à quoi sert cet article', 2),
    { key: 'content', label: 'Contenu (Markdown)', type: 'textarea', rows: 12,
      placeholder: '## Contexte\n…\n\n## Étapes\n1. …\n2. …\n\nCode : `commande` ou bloc entre ```' },
  ],
  columns: [
    { h: 'Type', v: r => r.articleType },
    { h: 'Mots-clés', v: r => r.keywords || '—' },
    { h: 'Revue', v: r => dateFr(r.reviewDate) },
  ],
  meta: r => [r.publishedAt && `Publié par ${r.publishedBy} le ${dateTimeFr(r.publishedAt)}`,
    r.lastReviewedAt && `Revu par ${r.lastReviewedBy} le ${dateFr(r.lastReviewedAt)}`],
  badges: r => <>
    {r.submittedAt && <span className="badge t-progress">Soumis à publication</span>}
    {r.archiveRequestedAt && <span className="badge t-wait">Archivage demandé</span>}
  </>,
  help: 'Le rédacteur soumet son brouillon, un gestionnaire le publie, s\'il n\'en est pas le seul rédacteur (quatre yeux, sauf Admin) ; contenu obligatoire ; revue fixée à un an par défaut ; chaque publication garde sa version ; retouché par un autre utilisateur, il repasse en brouillon. À la revue, le responsable confirme l\'article ou demande son archivage ; trois « pas utile » de plus que d\'« utile » le signalent. Archiver et réactiver sont des décisions de gestionnaire.',
  linkHint: 'Problème (erreur connue), incidents résolus grâce à l\'article…',
}
