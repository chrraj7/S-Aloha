import { api } from '../../api'
import { titleField, descriptionField, ownerField, dateFr, dateTimeFr } from '../../core/fields'

// Gestion des demandes de service (ITIL 4) : traiter les demandes prédéfinies
// initiées par les utilisateurs, de façon efficace et conviviale.
export const requestConfig = {
  title: 'Demandes',
  sub: 'Traiter les demandes de service des utilisateurs (accès, matériel, information) selon la qualité convenue (ITIL).',
  api: api.requests, basePath: '/requests', linkType: 'request',
  createLabel: '+ Nouvelle demande', createTitle: 'Nouvelle demande de service',
  empty: 'Aucune demande enregistrée.', ownerLabel: 'Traitées par moi',
  statuses: [
    { value: 'Soumise', tone: 'new' }, { value: 'Approuvée', tone: 'progress', manager: true },
    { value: 'Rejetée', tone: 'bad', manager: true }, { value: 'Annulée', tone: 'closed' },
    { value: 'En cours', tone: 'progress' }, { value: 'Satisfaite', tone: 'done' }, { value: 'Close', tone: 'closed' },
  ],
  fields: [
    titleField('Titre', 'Ex. : Accès VPN pour un nouvel arrivant'),
    { key: 'requestedItem', label: 'Objet demandé', placeholder: 'Repris du modèle s\'il est vide ; sinon : accès VPN, poste portable…' },
    { key: 'requestedFor', nameKey: 'requestedForDisplayName', label: 'Bénéficiaire', type: 'person' },
    // REQ-22 : calculée depuis un modèle à délai cible à l'approbation ; ensuite, un gestionnaire seulement
    // la modifie. Sans délai cible, l'échéance souhaitée reste libre.
    { key: 'dueDate', label: 'Échéance', type: 'date' },
    ownerField('Traitée par'),
    descriptionField('Précisions'),
    { key: 'rejectionReason', label: 'Motif de rejet', type: 'textarea', rows: 2, create: false,
      placeholder: 'Obligatoire pour rejeter : communiqué au demandeur.' },
    // REQ-10 : contestation d'une demande satisfaite (demandeur, bénéficiaire, gestionnaire).
    { key: 'contestReason', label: 'Motif de contestation', type: 'textarea', rows: 2, create: false,
      when: f => ['Satisfaite', 'En cours'].includes(f.status),
      placeholder: 'Obligatoire pour renvoyer une demande satisfaite En cours : ce qui n’est pas conforme.' },
  ],
  columns: [
    { h: 'Objet demandé', v: r => r.requestedItem },
    { h: 'Bénéficiaire', v: r => r.requestedForDisplayName || '—' },
    // REQ-30 : en retard le lendemain de l'échéance.
    { h: 'Échéance', v: r => <span style={r.overdue ? { color: 'var(--danger)', fontWeight: 700 } : {}}>{dateFr(r.dueDate)}{r.overdue && ' ⚠'}</span> },
  ],
  meta: r => [r.approvedAt && `Approuvée par ${r.approvedBy} le ${dateTimeFr(r.approvedAt)}`,
    r.overdue && `En retard : échéance du ${dateFr(r.dueDate)} dépassée`,
    r.contestReason && `Contestée : ${r.contestReason}`, r.cancelReason && `Annulée — ${r.cancelReason}`,
    r.autoClosed && 'Close automatiquement, sans contestation dans les 5 jours ouvrés',
    r.satisfactionScore && `Satisfaction : ${r.satisfactionScore}/5${r.satisfactionComment ? ` — ${r.satisfactionComment}` : ''}`],
  help: 'Une demande se crée depuis un modèle du catalogue, qui en donne l\'objet, le service, le délai et le groupe qui la traite. Elle est approuvée (ou rejetée) par un gestionnaire avant d\'être traitée, sauf modèle pré-approuvé. Le bénéficiaire est par défaut le demandeur. Satisfaite, elle se conteste dans les 5 jours ouvrés, avec un motif. Un besoin non standard se requalifie en changement ou en incident. Les tâches du modèle sont créées à l\'approbation ; la demande n\'est satisfaite que ses tâches terminées ou annulées. Le demandeur confirme la clôture, sinon elle est automatique 5 jours ouvrés après la satisfaction.',
  suggestOnTyping: true,
  linkHint: 'Service du catalogue concerné, changement standard déclenché…',
}

/** REQ-20 : choix du modèle à la création (modèles actifs du catalogue). */
export const catalogField = items => ({
  key: 'catalogItemId', label: 'Modèle du catalogue', type: 'select', numeric: true, onlyCreate: true,
  options: items.map(i => ({ value: i.id, label: i.requiresApproval ? i.name : `${i.name} (pré-approuvé)` })),
})
