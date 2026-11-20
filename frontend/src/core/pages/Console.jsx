import React, { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import usePerimeters from '../usePerimeters'
import { api, getUser } from '../../api'
import { ITEM_TYPES, hrefOf } from '../itemTypes'

const badge = s => s.replace(/[ é]/g, m => (m === ' ' ? '' : 'e'))
const dateFr = d => new Date(d).toLocaleDateString('fr-FR', { day: 'numeric', month: 'short' })
const today = new Date().toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' })

const ROLE = {
  User: { label: 'Utilisateur', intro: 'Vos actions et les dossiers dont vous êtes responsable, du plus urgent au moins urgent.' },
  Manager: { label: 'Gestionnaire', intro: 'Ce qui est urgent, ce qui attend votre décision, puis ce qu’il faut relancer.' },
  Admin: { label: 'Administrateur', intro: 'Ce qui est urgent, ce qui attend votre décision, puis ce qu’il faut relancer.' },
}

const Chevron = () => (
  <svg className="row-chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden><path d="M9 6l6 6-6 6" /></svg>
)
const Check = () => (
  <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
    strokeLinecap="round" strokeLinejoin="round" aria-hidden><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>
)

/** Une liste du tableau de bord : titre, aide, lignes compactes cliquables.
 * Masquée si vide — la console ne montre que ce qui demande quelque chose. */
function List({ title, hint, tone, rows, render, onRow }) {
  if (rows.length === 0) return null
  return (
    <section className={'c-list tone-' + tone}>
      <header>
        <h3>{title} <span className="c-count">{rows.length}</span></h3>
        {hint && <p>{hint}</p>}
      </header>
      <ul>
        {rows.map(r => (
          <li key={(r.type ?? '') + r.id}>
            <button type="button" onClick={() => onRow(r)}>
              {render(r)}
              <Chevron />
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

/** Contenu d'une ligne : référence, titre, puis métadonnées alignées à droite. */
const Row = ({ reference, title, meta }) => (
  <>
    <span className="row-ref">{reference}</span>
    <span className="row-title">{title}</span>
    <span className="row-meta">{meta}</span>
  </>
)

const Roles = ({ roles }) => roles.map(x => <span key={x} className={'badge ' + x}>{x}</span>)

/** Groupe de la console (Urgent, Décisions…) : n'apparaît que s'il a du contenu. */
function Group({ id, title, total, children }) {
  if (total === 0) return null
  return (
    <section className="c-group" id={id} aria-labelledby={id + '-t'}>
      <h2 id={id + '-t'}>{title}</h2>
      {children}
    </section>
  )
}

/** Console orientée action : ce qui requiert une intervention d'abord, le suivi ensuite. */
export default function Console() {
  const [c, setC] = useState(null)
  const [error, setError] = useState('')
  const [perimeter, setPerimeter] = useState('')
  const perimeters = usePerimeters()
  const nav = useNavigate()
  // PER-09 : la console couvre mes périmètres ; avec plusieurs, on peut n'en regarder qu'un.
  useEffect(() => { api.console.get(perimeter).then(setC).catch(e => setError(e.message)) }, [perimeter])
  if (error) return <div className="error">{error}</div>
  if (!c) return <p className="muted">Chargement…</p>

  const goP = r => nav('/problems/' + (r.problem ? r.problem.id : r.id))
  const goItem = r => nav(hrefOf(r.type, r.id))
  const m = c.management
  const role = ROLE[c.role] ?? ROLE.User
  const firstName = (getUser()?.displayName || '').split(' ')[0]

  const myOverdue = c.myActions.filter(a => a.overdue)
  const myTodo = c.myActions.filter(a => !a.overdue)
  const L = k => m?.[k]?.length ?? 0
  // CFG-40 : actifs découverts en attente de décision, regroupés par vue du sas.
  const st = m?.staging
  const staging = !st ? [] : [
    st.conflicts > 0 && { id: 'conflict', reference: 'Sas de staging', title: `${st.conflicts} actif(s) en conflit à arbitrer`, href: '/configuration/staging?view=conflict' },
    st.toReconcile > 0 && { id: 'open', reference: 'Sas de staging', title: `${st.toReconcile} actif(s) découvert(s) à rapprocher`,
      meta: st.unknown > 0 ? `dont ${st.unknown} inconnu(s)` : null, href: '/configuration/staging' },
  ].filter(Boolean)

  // Les quatre temps de la console, dans l'ordre où l'on doit les traiter.
  const urgent = myOverdue.length + L('majorIncidents') + (c.mySlaIncidents?.length ?? 0) + L('slaIncidents') + L('regulatory')
  const decide = L('decisions') + L('toQualify') + staging.length
  // NIS-23 : exigences du référentiel NIS 2 sans texte (Admin).
  const referential = m?.referentialWithoutText > 0
    ? [{ id: 'nis2', reference: 'Référentiel NIS 2', title: `${m.referentialWithoutText} exigence(s) encore sans texte : importer le texte de l’ANSSI`, href: '/settings/nis2' }] : []
  const chase = L('overdueActions') + L('knownErrorsNoAction') + L('reviewsDue') + L('overdueRequests') + L('projectAlerts') + L('changeFollowUps') + L('incidentFollowUps') + L('idleProblems') + L('unhelpfulArticles') + L('cisToVerify') + L('expiringAgreements') + referential.length
  const work = myTodo.length + c.myAssignments.length + (c.myProjectAlerts?.length ?? 0) + (c.myChangeFollowUps?.length ?? 0) + (c.myIncidentFollowUps?.length ?? 0) + (c.myUnhelpfulArticles?.length ?? 0) + (c.myFollowUpChecks?.length ?? 0) + (c.myCisToVerify?.length ?? 0)
  const follow = c.myProblems.length + L('inAnalysisNoRootCause')

  const tiles = [
    { id: 'urgent', label: 'Urgent', caption: 'Retards et incidents majeurs', n: urgent, tone: 'red' },
    m && { id: 'decisions', label: 'Décisions', caption: 'À autoriser, approuver, qualifier, rapprocher', n: decide, tone: 'blue' },
    m && { id: 'relances', label: 'Relances', caption: 'Retards d’équipe, revues échues', n: chase, tone: 'amber' },
    { id: 'travail', label: 'Mon travail', caption: 'Actions et dossiers en cours', n: work, tone: 'emerald' },
  ].filter(Boolean)

  const itemRow = r => <Row reference={r.reference}
    title={r.title}
    meta={<><span className="row-kind">{ITEM_TYPES[r.type]?.label ?? r.type}</span><span className="row-status">{r.status}</span></>} />

  return (
    <>
      <header className="c-head">
        <p className="c-eyebrow">{today} · Console {role.label.toLowerCase()}</p>
        <h1 className="page-title">{firstName ? `Bonjour, ${firstName}` : 'Ma console'}</h1>
        <p className="page-sub">{role.intro}</p>
        {perimeters.length > 1 && (
          <label className="c-perimeter">Périmètre{' '}
            <select value={perimeter} onChange={e => setPerimeter(e.target.value)}>
              <option value="">Tous mes périmètres</option>
              {perimeters.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </label>)}
      </header>

      <nav className="c-tiles" aria-label="Synthèse">
        {tiles.map(t => (
          <a key={t.id} href={'#' + t.id} className={'c-tile tone-' + t.tone + (t.n === 0 ? ' is-zero' : '')}
            onClick={e => { e.preventDefault(); document.getElementById(t.id)?.scrollIntoView({ behavior: 'smooth' }) }}
            aria-disabled={t.n === 0}>
            <span className="c-tile-n">{t.n}</span>
            <span className="c-tile-l">{t.label}</span>
            <span className="c-tile-c">{t.caption}</span>
          </a>
        ))}
      </nav>

      {urgent + decide + chase + work === 0 && (
        <div className="c-clear">
          <span className="c-clear-icon"><Check /></span>
          <div>
            <b>Tout est à jour</b>
            <p>Aucune intervention ne vous est demandée pour le moment.</p>
          </div>
        </div>
      )}

      <Group id="urgent" title="Urgent" total={urgent}>
        <List title="Mes actions en retard" tone="red" rows={myOverdue} onRow={goP}
          hint="Échéance dépassée : à traiter ou à replanifier."
          render={r => <Row reference={r.problem.reference} title={r.title}
            meta={<><Roles roles={r.myRoles} /><span className="row-date is-late">{dateFr(r.dueDate)}</span></>} />} />
        {/* INC-24 : SLA en risque (moins de 25 % du délai) ou dépassé. */}
        <List title="Mes incidents : SLA en risque ou dépassé" tone="red" rows={c.mySlaIncidents ?? []} onRow={goItem}
          hint="Échéance de résolution proche ou passée : à traiter en priorité." render={itemRow} />
        {m && <List title="Incidents : SLA en risque ou dépassé" tone="red" rows={m.slaIncidents ?? []} onRow={goItem}
          hint="Périmètres que vous gérez : relancer le responsable, escalader." render={itemRow} />}
        {m && <List title="Incidents majeurs en cours" tone="red" rows={m.majorIncidents} onRow={goItem}
          hint="Coordonner le rétablissement et la communication." render={itemRow} />}
        {/* INC-41, DRG-11 : incidents de sécurité à qualifier, échéances légales proches ou dépassées. */}
        {m && <List title="Déclarations réglementaires" tone="red" rows={m.regulatory ?? []} onRow={goItem}
          hint="Qualifier les incidents de sécurité ; envoyer l’alerte ou la notification avant l’échéance légale." render={itemRow} />}
      </Group>

      {m && <Group id="decisions" title="Décisions" total={decide}>
        <List title="En attente de votre décision" tone="blue" rows={m.decisions} onRow={goItem}
          hint="Changements à autoriser, demandes à approuver, améliorations à valider." render={itemRow} />
        <List title="Découverte : actifs à traiter" tone="blue" rows={staging} onRow={r => nav(r.href)}
          hint="Rapprocher d’un CI, créer le CI ou ignorer : rien n’entre dans la CMDB sans votre décision."
          render={r => <Row reference={r.reference} title={r.title} meta={r.meta && <span className="row-status">{r.meta}</span>} />} />
        <List title="Problèmes à qualifier" tone="blue" rows={m.toQualify} onRow={goP}
          hint="Nouveaux problèmes : impact, urgence et catégorie à confirmer."
          render={r => <Row reference={r.reference} title={r.title}
            meta={<><span className={'badge ' + r.priority}>{r.priority}</span><span className="row-who">{r.createdByDisplayName}</span><span className="row-date">{dateFr(r.createdAt)}</span></>} />} />
      </Group>}

      {m && <Group id="relances" title="Relances" total={chase}>
        <List title="Actions en retard, toutes équipes" tone="amber" rows={m.overdueActions} onRow={goP}
          hint="À relancer auprès des responsables (R)."
          render={r => <Row reference={r.problem.reference} title={r.title}
            meta={<><span className="row-who">{r.responsibles.join(', ') || '—'}</span><span className="row-date is-late">{dateFr(r.dueDate)}</span></>} />} />
        <List title="Erreurs connues sans action corrective" tone="amber" rows={m.knownErrorsNoAction} onRow={goP}
          hint="Une erreur connue doit porter au moins une action planifiée."
          render={r => <Row reference={r.reference} title={r.title}
            meta={<span className={'badge ' + r.priority}>{r.priority}</span>} />} />
        <List title="Revues échues" tone="amber" rows={m.reviewsDue} onRow={goItem}
          hint="Articles publiés et SLA en vigueur dont la date de revue est passée, évaluations NIS 2 à renouveler." render={itemRow} />
        {/* SLM-24 */}
        <List title="SLA expirant" tone="amber" rows={m.expiringAgreements ?? []} onRow={goItem}
          hint="SLA en vigueur dont la fin de validité tombe dans les 30 jours : le renouveler ou le laisser expirer." render={itemRow} />
        {/* REQ-30 */}
        <List title="Demandes en retard" tone="amber" rows={m.overdueRequests ?? []} onRow={goItem}
          hint="Échéance dépassée : relancer le responsable." render={itemRow} />
        {/* PRJ-34 */}
        <List title="Projets à relancer" tone="amber" rows={m.projectAlerts ?? []} onRow={goItem}
          hint="Hors tolérance (délai, coût, fin prévue) ou jalons en retard." render={itemRow} />
        {/* NIS-23 */}
        <List title="Référentiel NIS 2 incomplet" tone="amber" rows={referential} onRow={r => nav(r.href)}
          hint="Les évaluations portent sur des exigences dont le texte n’a pas été importé."
          render={r => <Row reference={r.reference} title={r.title} />} />
        {/* CHG-30, CHG-31 */}
        <List title="Changements à relancer" tone="amber" rows={m.changeFollowUps ?? []} onRow={goItem}
          hint="Fin planifiée passée sans mise en œuvre, ou mis en œuvre depuis plus de 2 jours ouvrés sans clôture." render={itemRow} />
        {/* KB-13 */}
        <List title="Articles jugés peu utiles" tone="amber" rows={m.unhelpfulArticles ?? []} onRow={goItem}
          hint="Trois « pas utile » de plus que d’« utile » : faire revoir l’article par son responsable." render={itemRow} />
        {/* PRB-32 */}
        <List title="Problèmes sans activité" tone="amber" rows={m.idleProblems ?? []} onRow={goItem}
          hint="Ni analyse, ni action, ni modification depuis 30 jours : relancer le gestionnaire du problème, ou le clore." render={itemRow} />
        {/* INC-26 */}
        <List title="Incidents en attente prolongée" tone="amber" rows={m.incidentFollowUps ?? []} onRow={goItem}
          hint="En attente depuis plus de 5 jours ouvrés : relancer l’utilisateur, le fournisseur ou le changement attendu." render={itemRow} />
        {/* CFG-15 */}
        <List title="CI à vérifier" tone="amber" rows={m.cisToVerify ?? []} onRow={goItem}
          hint="CI en service dont la fiche n’a pas été vérifiée depuis 12 mois : relancer le propriétaire, ou la vérifier." render={itemRow} />
      </Group>}

      <Group id="travail" title="Mon travail" total={work}>
        <List title="Mes actions en cours" tone="emerald" rows={myTodo} onRow={goP}
          render={r => <Row reference={r.problem.reference} title={r.title}
            meta={<><Roles roles={r.myRoles} /><span className="row-status">{r.status}</span>{r.dueDate && <span className="row-date">{dateFr(r.dueDate)}</span>}</>} />} />
        <List title="Mes dossiers ouverts" tone="emerald" rows={c.myAssignments} onRow={goItem}
          hint="Incidents, demandes, changements, améliorations et projets dont vous êtes responsable." render={itemRow} />
        {/* PRJ-34 : relances du chef de projet. */}
        <List title="Mes projets à relancer" tone="amber" rows={c.myProjectAlerts ?? []} onRow={goItem}
          hint="Vos projets hors tolérance ou à jalons en retard." render={itemRow} />
        {/* CHG-30, CHG-31 : relances du responsable d'un changement. */}
        <List title="Mes changements à relancer" tone="amber" rows={c.myChangeFollowUps ?? []} onRow={goItem}
          hint="Mettre en œuvre ou replanifier ; renseigner le résultat et clore." render={itemRow} />
        {/* INC-26 */}
        <List title="Mes incidents en attente prolongée" tone="amber" rows={c.myIncidentFollowUps ?? []} onRow={goItem}
          hint="En attente depuis plus de 5 jours ouvrés : relancer, ou reprendre l’incident." render={itemRow} />
        {/* KB-13 */}
        <List title="Mes articles jugés peu utiles" tone="amber" rows={c.myUnhelpfulArticles ?? []} onRow={goItem}
          hint="Les lecteurs ne s’y retrouvent pas : revoir le contenu." render={itemRow} />
        {/* CSI-15 */}
        <List title="Contrôles de durabilité" tone="amber" rows={c.myFollowUpChecks ?? []} onRow={goItem}
          hint="Le résultat de l’amélioration tient-il ? Confirmer, ou ouvrir une nouvelle amélioration." render={itemRow} />
        {/* CFG-15 */}
        <List title="Mes CI à vérifier" tone="amber" rows={c.myCisToVerify ?? []} onRow={goItem}
          hint="Fiche non vérifiée depuis 12 mois : la relire, la corriger si besoin, puis attester qu’elle est exacte." render={itemRow} />
      </Group>

      <Group id="suivi" title="À suivre" total={follow}>
        <div className="c-follow">
          <List title="Mes problèmes déclarés" tone="neutral" rows={c.myProblems} onRow={goP}
            render={r => <Row reference={r.reference} title={r.title}
              meta={<span className={'badge ' + badge(r.status)}>{r.status}</span>} />} />
          {m && <List title="Analyses sans cause racine" tone="neutral" rows={m.inAnalysisNoRootCause} onRow={goP}
            render={r => <Row reference={r.reference} title={r.title}
              meta={<span className="row-who">{r.analysesCount} analyse{r.analysesCount > 1 ? 's' : ''}</span>} />} />}
        </div>
      </Group>

      <p className="c-foot">Indicateurs et volumétrie : <Link to="/reports">Reporting</Link></p>
    </>
  )
}
