import React, { useEffect, useState } from 'react'
import { useParams, Link, useNavigate } from 'react-router-dom'
import { api, getUser } from '../../../api'
import FiveWhys from '../components/FiveWhys'
import Ishikawa from '../components/Ishikawa'
import FtaTree from '../components/FtaTree'
import RaciEditor from '../../../core/components/RaciEditor'
import LinkedItems from '../../../core/components/LinkedItems'
import SimilarCases from '../../../core/components/SimilarCases'
import useTransitions from '../../../core/useTransitions'
import StatusField, { forceOptions } from '../../../core/components/StatusField'
import History from '../../../core/components/History'
import Comments from '../../../core/components/Comments'
import DeleteDialog from '../../../core/components/DeleteDialog'
import RecordForm from '../../../core/components/RecordForm'
import { ownerField } from '../../../core/fields'
import usePerimeters, { perimeterName } from '../../../core/usePerimeters'
import useServices from '../../../core/useServices'
import ServiceSelect from '../../../core/components/ServiceSelect'
import CategoryInput from '../../../core/components/CategoryInput'
import useCategories from '../../../core/useCategories'

const STATUSES = ['Nouveau', 'En analyse', 'Erreur connue', 'Résolu', 'Clos']
/** Codes de clôture (PRB-13) selon le statut d'origine ; une transition forcée les admet tous. */
const CLOSURE_CODES = ['Corrigé', 'Erreur connue acceptée', 'Doublon', 'Non retenu']
const CLOSURE_FROM = { 'Résolu': ['Corrigé'], 'Erreur connue': ['Erreur connue acceptée'], Nouveau: ['Doublon', 'Non retenu'] }
/** Libellés des champs au journal d'audit (SOC-20). */
const LABELS = {
  title: 'Titre', description: 'Description', impact: 'Impact', urgency: 'Urgence', priority: 'Priorité',
  category: 'Catégorie', affectedService: 'Service affecté', knownErrorWorkaround: 'Contournement',
  rootCause: 'Cause racine', closureCode: 'Code de clôture', ownerDisplayName: 'Gestionnaire du problème',
  reopenReason: 'Motif de réouverture'
}
const OWNER = ownerField('Gestionnaire du problème')
const METHODS = { FIVE_WHYS: '5 Pourquoi', ISHIKAWA: 'Ishikawa (6M)', FTA: 'Arbre des défaillances (FTA)' }
const badge = s => s.replace(/[ é]/g, m => (m === ' ' ? '' : 'e'))

/**
 * PRB-30, KB-12 : une erreur connue se documente dans la base de connaissances.
 * L'article (type Erreur connue, brouillon) reprend le titre, les symptômes et le
 * contournement, et se relie au problème ; s'il existe déjà, la fiche y renvoie.
 */
export function KnownErrorArticle({ p }) {
  const [article, setArticle] = useState(undefined)
  const [error, setError] = useState('')
  const nav = useNavigate()
  useEffect(() => {
    api.links.list('problem', p.id)
      .then(l => setArticle(l.find(x => x.type === 'article') ?? null)).catch(() => setArticle(null))
  }, [p.id])
  if (article === undefined || !(p.status === 'Erreur connue' || article)) return null

  const create = async () => {
    try {
      const a = await api.knowledge.create({
        title: p.title, articleType: 'Erreur connue',
        description: `Erreur connue documentée depuis le problème ${p.reference}.`,
        content: `## Symptômes\n\n${p.description || '—'}\n\n## Contournement\n\n${p.knownErrorWorkaround || '—'}\n\n## Problème\n\n${p.reference}`,
        keywords: [p.category, p.affectedService].filter(Boolean).join(', ')
      })
      await api.links.create({ fromType: 'problem', fromId: p.id, toReference: a.reference })
      nav('/knowledge/' + a.id)
    } catch (e) { setError(e.message) }
  }
  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 style={{ color: 'var(--navy)', marginBottom: 4 }}>Article d'erreur connue</h3>
      {error && <div className="error">{error}</div>}
      {article
        ? <p style={{ fontSize: 13 }}>Documentée dans <Link to={'/knowledge/' + article.id}><b>{article.reference}</b></Link> — {article.title} ({article.status}).</p>
        : <>
          <p style={{ color: 'var(--muted)', fontSize: 13, marginBottom: 10 }}>
            Publier l'erreur connue dans la base de connaissances : le support applique le contournement sans attendre le correctif.</p>
          <button className="btn ghost" onClick={create}>Créer l'article d'erreur connue</button>
        </>}
    </div>
  )
}

/**
 * CHG-23 : le correctif d'un problème se met en œuvre par un changement. Créé depuis la fiche,
 * il reprend le titre et la description, et se relie au problème ; il naît dans le périmètre
 * du problème, sans quoi l'API refuse pour un utilisateur de plusieurs périmètres (PER-04).
 */
export function ProblemChange({ p }) {
  const [error, setError] = useState('')
  const nav = useNavigate()
  const create = async () => {
    try {
      const c = await api.changes.create({
        title: p.title, changeType: 'Normal', risk: 'Moyen', perimeterId: p.perimeterId,
        description: `${p.description ? p.description + '\n\n' : ''}Correctif du problème ${p.reference}.`
      })
      await api.links.create({ fromType: 'problem', fromId: p.id, toReference: c.reference })
      nav('/changes/' + c.id)
    } catch (e) { setError(e.message) }
  }
  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 style={{ color: 'var(--navy)', marginBottom: 4 }}>Correctif</h3>
      <p style={{ color: 'var(--muted)', fontSize: 13, marginBottom: 10 }}>
        Mettre en œuvre le correctif par un changement, relié à ce problème.</p>
      {error && <div className="error">{error}</div>}
      <button className="btn ghost" onClick={create}>Créer un changement</button>
    </div>
  )
}

export default function ProblemDetail() {
  const { id } = useParams()
  const [p, setP] = useState(null)
  const [tab, setTab] = useState('infos')
  const [error, setError] = useState('')
  const perimeters = usePerimeters()

  const load = () => api.problems.get(id).then(setP).catch(e => setError(e.message))
  // Une autre fiche (lien suivi depuis l'onglet Liens) s'ouvre sur ses Informations.
  useEffect(() => { setTab('infos'); load() }, [id])
  if (error) return <div className="error">{error}</div>
  if (!p) return <p>Chargement…</p>
  // PER-06 : qualifier le problème revient au gestionnaire de son périmètre (le rôle AD tant que la liste manque).
  const here = perimeters.find(x => x.id === p.perimeterId)?.role
  const isManager = ['Admin', 'Manager'].includes(here ?? getUser()?.role)

  return (
    <>
      <p style={{ marginBottom: 8 }}><Link to="/problems">← Problèmes</Link></p>
      <h1 className="page-title">{p.reference} — {p.title}</h1>
      <p className="page-sub">
        <span className={'badge ' + badge(p.status)}>{p.status}</span>{' '}
        <span className={'badge ' + p.priority}>{p.priority}</span>{' '}
        Déclaré par {p.createdByDisplayName} le {new Date(p.createdAt).toLocaleDateString('fr-FR')}
        {p.resolvedAt && <> · Résolu le {new Date(p.resolvedAt).toLocaleDateString('fr-FR')}</>}
        {p.closureCode && <> · Clôture : {p.closureCode}</>}
        {p.ownerDisplayName && <> · Gestionnaire : {p.ownerDisplayName}</>}
        {perimeters.length > 1 && <> · Périmètre : {perimeterName(perimeters, p.perimeterId)}</>}
      </p>

      {/* Onglets communs des fiches (docs/reference/frontend.md § Navigation
          cible) : Informations → propres à la pratique → Liens. */}
      <div className="tabs" role="tablist">
        {[['infos', 'Informations'], ['rca', 'Analyse de cause racine'],
          ['actions', `Actions correctives (${p.actions.length})`], ['links', 'Liens'], ['comments', 'Commentaires'], ['history', 'Historique']]
          .map(([k, l]) => <button key={k} role="tab" aria-selected={tab === k}
            className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{l}</button>)}
      </div>

      {tab === 'infos' && <><Infos p={p} isManager={isManager} onSaved={load} /><KnownErrorArticle p={p} /><ProblemChange p={p} /><SimilarCases type="problem" id={p.id} /></>}
      {tab === 'rca' && <Rca p={p} isManager={isManager} onSaved={load} />}
      {tab === 'actions' && <ActionsTab p={p} isManager={isManager} onSaved={load} />}
      {tab === 'comments' && <Comments type="problem" id={p.id} />}
      {tab === 'history' && <History type="problem" id={p.id} labels={LABELS} refreshKey={p.updatedAt} />}
      {tab === 'links' && <LinkedItems type="problem" id={p.id}
        hint="Incidents à l'origine, changement qui corrige, article d'erreur connue…" />}
    </>
  )
}

function Infos({ p, isManager, onSaved }) {
  const graph = useTransitions(api.problems.transitions)
  const services = useServices() ?? []
  const categories = useCategories('problem')
  const [reason, setReason] = useState('')
  const [f, setF] = useState({
    title: p.title, description: p.description, status: p.status, impact: p.impact,
    urgency: p.urgency, category: p.category, affectedService: p.affectedService, serviceId: p.serviceId ?? '',
    knownErrorWorkaround: p.knownErrorWorkaround || '', rootCause: p.rootCause || '',
    closureCode: p.closureCode || '', reopenReason: p.reopenReason || '', source: p.source || 'Réactif',
    ownerType: p.ownerType || '', ownerId: p.ownerId || '', ownerDisplayName: p.ownerDisplayName || ''
  })
  const [msg, setMsg] = useState('')
  const [deleting, setDeleting] = useState(null)   // SOC-14 : aperçu de la suppression (Admin)
  const nav = useNavigate()
  const set = k => e => setF({ ...f, [k]: e.target.value })
  const force = forceOptions(graph, p.status, f.status, reason)
  // PRB-21 : le gestionnaire du problème est le responsable du socle ; vide, l'API
  // désigne le gestionnaire qui enregistre.
  const save = () => api.problems.update(p.id, { ...f, serviceId: f.serviceId ? Number(f.serviceId) : null, ownerType: f.ownerType || null, ownerId: f.ownerId || null,
    ownerDisplayName: f.ownerDisplayName || null }, force)
    .then(() => { setMsg('Modifications enregistrées.'); onSaved() })
    .catch(e => setMsg(e.message))

  return (
    <div className="card">
      {msg && <div className="error" style={{ background: 'var(--blue-soft)', color: 'var(--navy)' }}>{msg}</div>}
      <div className="field"><label>Titre</label>
        <input value={f.title} onChange={set('title')} disabled={!isManager} /></div>
      <div className="field"><label>Description</label>
        <textarea rows={4} value={f.description} onChange={set('description')} disabled={!isManager} /></div>
      <div className="row3">
        <div><StatusField statuses={STATUSES.map(value => ({ value }))} current={p.status} value={f.status}
          onChange={status => setF({ ...f, status })} graph={graph} isManager={isManager}
          isAdmin={getUser()?.role === 'Admin'} disabled={!isManager} reason={reason} onReason={setReason} /></div>
        <div className="field"><label>Impact</label>
          <select value={f.impact} onChange={set('impact')} disabled={!isManager}>
            <option>Faible</option><option>Moyen</option><option>Élevé</option></select></div>
        <div className="field"><label>Urgence</label>
          <select value={f.urgency} onChange={set('urgency')} disabled={!isManager}>
            <option>Faible</option><option>Moyenne</option><option>Élevée</option></select></div>
      </div>
      <div className="row2">
        <div className="field"><label>Catégorie</label>
          <CategoryInput categories={categories} value={f.category} onChange={set('category')} disabled={!isManager} /></div>
        <div className="field"><label>Source</label>
          <select value={f.source} onChange={set('source')} disabled={!isManager} aria-label="Source">
            <option value="Réactif">Réactif — issu d’incidents</option>
            <option value="Proactif">Proactif — tendance, analyse de risque</option>
          </select></div>
        <div className="field"><label>Service affecté</label>
          <ServiceSelect services={services} value={f.serviceId} onChange={set('serviceId')} disabled={!isManager}
            legacy={!p.serviceId && p.affectedService} /></div>
      </div>
      <RecordForm fields={[OWNER]} form={f} onChange={patch => setF(prev => ({ ...prev, ...patch }))} disabled={!isManager} />
      <div className="field"><label>Contournement (erreur connue)</label>
        <textarea rows={2} value={f.knownErrorWorkaround} onChange={set('knownErrorWorkaround')} disabled={!isManager}
          placeholder="Solution de contournement documentée si le problème devient une erreur connue." /></div>
      <div className="field"><label>Cause racine validée</label>
        <textarea rows={2} value={f.rootCause} onChange={set('rootCause')} disabled={!isManager}
          placeholder="Renseignée à l'issue de l'analyse. Obligatoire pour résoudre." /></div>
      {/* PRB-10 : rouvrir un problème résolu ou clos exige un motif ; il reste affiché jusqu'à la résolution suivante. */}
      {((['Résolu', 'Clos'].includes(p.status) && f.status === 'En analyse') || p.reopenReason) && (
        <div className="field"><label>Motif de réouverture{p.reopenReason ? '' : ' *'}</label>
          <textarea rows={2} value={f.reopenReason} onChange={set('reopenReason')} disabled={!isManager}
            placeholder="Récidive constatée, correctif inefficace…" /></div>
      )}
      {f.status === 'Clos' && (
        <div className="field" style={{ maxWidth: 300 }}><label>Code de clôture *</label>
          <select value={f.closureCode} onChange={set('closureCode')} disabled={!isManager}>
            <option value="">—</option>
            {(p.status === 'Clos' ? CLOSURE_CODES : CLOSURE_FROM[p.status] ?? CLOSURE_CODES)
              .map(c => <option key={c}>{c}</option>)}
          </select></div>
      )}
      <div className="actions">
        {isManager
          ? <button className="btn" onClick={save} disabled={force && !reason.trim()}>Enregistrer</button>
          : <p style={{ color: 'var(--muted)', fontSize: 13 }}>Seuls les gestionnaires de problèmes peuvent modifier ces informations.</p>}
        {getUser()?.role === 'Admin' && <button className="btn danger" style={{ marginLeft: 'auto' }}
          onClick={() => api.problems.deletion(p.id).then(setDeleting).catch(e => setMsg(e.message))}>Supprimer</button>}
      </div>
      {deleting && <DeleteDialog reference={deleting.reference} title={deleting.title} impacts={deleting.impacts}
        onConfirm={() => api.problems.remove(p.id).then(() => nav('/problems'))} onClose={() => setDeleting(null)} />}
    </div>
  )
}

function Rca({ p, isManager, onSaved }) {
  const [current, setCurrent] = useState(p.analyses[p.analyses.length - 1] || null)
  // Initialisation paresseuse : passée en valeur, l'expression était
  // réévaluée à CHAQUE rendu, et une analyse qu'on démarre (sans dataJson)
  // faisait planter l'onglet — aucune analyse ne pouvait être créée (B1).
  const [data, setData] = useState(() => (current ? JSON.parse(current.dataJson) : {}))
  const [msg, setMsg] = useState('')
  const [deleting, setDeleting] = useState(false)

  const start = (method) => { setCurrent({ method, id: null }); setData({}) }
  // SOC-14 : un gestionnaire supprime une analyse (doublon, méthode abandonnée).
  const remove = () => api.analyses.remove(p.id, current.id).then(() => { setDeleting(false); setCurrent(null); setMsg(''); onSaved() })
  const open = (a) => { setCurrent(a); setData(JSON.parse(a.dataJson)) }
  // PRB-07 : un gestionnaire retient la conclusion de l'analyse comme cause racine du problème.
  const validate = () => api.analyses.validate(p.id, current.id)
    .then(a => { setCurrent(a); setMsg('Conclusion retenue comme cause racine du problème.'); onSaved() }).catch(e => setMsg(e.message))

  const save = async () => {
    const dto = { method: current.method, dataJson: JSON.stringify(data), conclusion: data.rootCause || null }
    try {
      const saved = current.id
        ? await api.analyses.update(p.id, current.id, dto)
        : await api.analyses.create(p.id, dto)
      setCurrent(saved); setMsg('Analyse enregistrée.'); onSaved()
    } catch (e) { setMsg(e.message) }
  }

  return (
    <div className="card">
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
        {p.analyses.map(a => (
          <button key={a.id} className={'btn small ' + (current?.id === a.id ? '' : 'ghost')} onClick={() => open(a)}>
            {METHODS[a.method]} — {new Date(a.createdAt).toLocaleDateString('fr-FR')}
          </button>
        ))}
        <span style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
          {Object.entries(METHODS).map(([k, l]) =>
            <button key={k} className="btn ghost small" onClick={() => start(k)}>+ {l}</button>)}
        </span>
      </div>

      {!current && <p style={{ color: 'var(--muted)' }}>
        Choisissez une méthodologie de recherche de cause racine pour démarrer une analyse.</p>}

      {current && (
        <>
          <h3 style={{ color: 'var(--navy)', marginBottom: 14 }}>{METHODS[current.method]}</h3>
          {msg && <div className="error" style={{ background: 'var(--blue-soft)', color: 'var(--navy)' }}>{msg}</div>}
          {current.method === 'FIVE_WHYS' && <FiveWhys data={data} onChange={setData} />}
          {current.method === 'ISHIKAWA' && <Ishikawa data={data} onChange={setData} />}
          {current.method === 'FTA' && <FtaTree data={data} onChange={setData} />}
          <div className="actions" style={{ marginTop: 16 }}>
            <button className="btn" onClick={save}>Enregistrer l'analyse</button>
            {isManager && current.id && current.conclusion && !['Résolu', 'Clos'].includes(p.status) && (
              current.validatedAt
                ? <span className="badge t-done" title={`Validée par ${current.validatedBy}`}>Cause racine retenue</span>
                : <button className="btn ghost" onClick={validate}>Valider comme cause racine</button>)}
            {isManager && current.id && <button className="btn ghost" style={{ marginLeft: 'auto' }} onClick={() => setDeleting(true)}>Supprimer l'analyse</button>}
          </div>
          {deleting && <DeleteDialog reference={`${METHODS[current.method]} du ${new Date(current.createdAt).toLocaleDateString('fr-FR')}`}
            note="La cause racine validée du problème n'est pas modifiée." onConfirm={remove} onClose={() => setDeleting(false)} />}
        </>
      )}
    </div>
  )
}

function ActionsTab({ p, isManager, onSaved }) {
  const nav = useNavigate()
  const [deleting, setDeleting] = useState(null)
  const [showForm, setShowForm] = useState(false)
  const [f, setF] = useState({ title: '', description: '', dueDate: '', raci: [] })
  const [msg, setMsg] = useState('')

  const submit = () => api.actions.create(p.id, {
    ...f, dueDate: f.dueDate ? new Date(f.dueDate).toISOString() : null
  }).then(() => { setShowForm(false); setF({ title: '', description: '', dueDate: '', raci: [] }); onSaved() })
    .catch(e => setMsg(e.message))

  const setStatus = (a, status) =>
    api.actions.update(a.id, { title: a.title, description: a.description, status, dueDate: a.dueDate, raci: a.raci })
      .then(onSaved).catch(e => setMsg(e.message))
  // PRB-25 : le R demande à l'A d'approuver l'action ; seul l'A (ou un gestionnaire) la termine.
  const askApproval = a => api.actions.requestApproval(a.id).then(onSaved).catch(e => setMsg(e.message))
  // PRB-27 : le changement reprend le titre et la description de l'action, puis lui est rattaché (et relié au problème).
  const createChange = async a => {
    try {
      const c = await api.changes.create({
        title: a.title, changeType: 'Normal', risk: 'Moyen',
        description: `${a.description ? a.description + '\n\n' : ''}Correctif de l'action « ${a.title} » du problème ${p.reference}.`
      })
      await api.actions.setChange(a.id, c.reference)
      nav('/changes/' + c.id)
    } catch (e) { setMsg(e.message) }
  }

  return (
    <>
      {msg && <div className="error">{msg}</div>}
      <button className="btn" style={{ marginBottom: 16 }} onClick={() => setShowForm(v => !v)}>
        {showForm ? 'Fermer' : '+ Nouvelle action corrective'}
      </button>

      {showForm && (
        <div className="card" style={{ marginBottom: 20 }}>
          <div className="field"><label>Titre</label>
            <input value={f.title} onChange={e => setF({ ...f, title: e.target.value })} /></div>
          <div className="field"><label>Description</label>
            <textarea rows={3} value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></div>
          <div className="field" style={{ maxWidth: 220 }}><label>Échéance</label>
            <input type="date" value={f.dueDate} onChange={e => setF({ ...f, dueDate: e.target.value })} /></div>
          <label>Matrice RACI</label>
          <RaciEditor value={f.raci} onChange={raci => setF({ ...f, raci })} />
          <button className="btn" style={{ marginTop: 14 }} onClick={submit} disabled={!f.title}>Créer l'action</button>
        </div>
      )}

      {p.actions.map(a => (
        <div className="card" key={a.id} style={{ marginBottom: 12 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <b style={{ flex: 1 }}>{a.title}</b>
            <select style={{ maxWidth: 150 }} value={a.status} onChange={e => setStatus(a, e.target.value)}>
              <option>À faire</option><option>En cours</option><option>Terminée</option><option>Annulée</option>
            </select>
          </div>
          <p style={{ fontSize: 13, color: 'var(--muted)', margin: '6px 0' }}>
            {a.description} {a.dueDate && <> — Échéance : {new Date(a.dueDate).toLocaleDateString('fr-FR')}</>}
          </p>
          {a.approvalRequestedAt && a.status === 'En cours' &&
            <p style={{ fontSize: 13, margin: '0 0 6px' }}>⏳ Approbation demandée le {new Date(a.approvalRequestedAt).toLocaleDateString('fr-FR')} : l'approbateur (A) la déclare terminée.</p>}
          <div>
            {a.raci.map(r => (
              <span className="chip" key={r.id}>
                <span className={'badge ' + r.role}>{r.role}</span>
                {r.assigneeType === 'Group' ? '👥' : '👤'} {r.assigneeDisplayName}
              </span>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 8, marginTop: 8, alignItems: 'center', flexWrap: 'wrap' }}>
            {a.status === 'En cours' && !a.approvalRequestedAt &&
              <button className="btn ghost small" onClick={() => askApproval(a)}>Demander l'approbation</button>}
            {a.changeReference
              ? <span style={{ fontSize: 13 }}>Correctif : <Link to={'/changes/' + a.changeId}><b>{a.changeReference}</b></Link></span>
              : !['Terminée', 'Annulée'].includes(a.status) &&
                <button className="btn ghost small" onClick={() => createChange(a)}>Créer un changement</button>}
            {isManager && <button className="btn ghost small" style={{ marginLeft: 'auto' }} onClick={() => setDeleting(a)}
              aria-label={`Supprimer l'action ${a.title}`}>Supprimer</button>}
          </div>
        </div>
      ))}
      {p.actions.length === 0 && !showForm &&
        <p style={{ color: 'var(--muted)' }}>Aucune action corrective pour l'instant.</p>}
      {deleting && <DeleteDialog reference={`Action « ${deleting.title} »`}
        impacts={deleting.raci.length ? [{ label: 'affectations RACI', count: deleting.raci.length }] : []}
        note={deleting.changeReference ? `Le changement ${deleting.changeReference} n'est pas supprimé.` : undefined}
        onConfirm={() => api.actions.remove(deleting.id).then(() => { setDeleting(null); onSaved() })} onClose={() => setDeleting(null)} />}
    </>
  )
}
