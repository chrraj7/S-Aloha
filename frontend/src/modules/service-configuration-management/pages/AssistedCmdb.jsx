import React, { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../../api'
import { dateTimeFr } from '../../../core/fields'
import usePerimeters from '../../../core/usePerimeters'
import { toForm, toPayload } from '../../../core/components/RecordForm'
import { ciConfig } from '../config'

/** CMA-02 : tâches que le conteneur exécute aujourd'hui (étapes 5a et 5b) ; Relier et Criticité suivront. */
export const TASKS = { rename: 'Renommer', normalize: 'Normaliser', reclassify: 'Reclasser', merge: 'Fusionner', relate: 'Relier', criticality: 'Criticité' }
const OFFERED = ['rename', 'normalize', 'reclassify', 'merge']
const FIELD = { title: 'Nom', ciType: 'Type', operatingSystem: 'Système', vendor: 'Constructeur', model: 'Modèle', criticality: 'Criticité', merge: 'Fusion' }
const TONE = { 'En file': 't-wait', 'En cours': 't-wait', Terminée: 't-done', 'En échec': 't-bad' }

/** CMA-01 : lancer une analyse sur un périmètre géré, avec ses tâches. */
function Launch({ onLaunched }) {
  const managed = usePerimeters().filter(p => ['Admin', 'Manager'].includes(p.role))
  const [perimeter, setPerimeter] = useState('')
  const [tasks, setTasks] = useState(OFFERED)
  const [error, setError] = useState('')
  const toggle = t => setTasks(ts => (ts.includes(t) ? ts.filter(x => x !== t) : [...ts, t]))
  const launch = () => {
    setError('')
    api.assistedCmdb.launch({ perimeterId: perimeter ? Number(perimeter) : undefined, tasks })
      .then(onLaunched).catch(e => setError(e.message))
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <h2 className="card-title">Lancer une analyse</h2>
      <p className="muted small">Tout le périmètre, hors CI retirés (500 CI au plus). Une analyse à la fois par périmètre ; chaque
        proposition se décide ensuite, ici ou sur la fiche du CI.</p>
      {error && <div className="error" role="alert">{error}</div>}
      {managed.length > 1 && (
        <div className="field"><label htmlFor="cma-perimeter">Périmètre</label>
          <select id="cma-perimeter" value={perimeter} onChange={e => setPerimeter(e.target.value)}>
            <option value="">—</option>
            {managed.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select></div>
      )}
      <div className="toolbar">
        {OFFERED.map(t => (
          <label key={t} style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
            <input type="checkbox" style={{ width: 'auto' }} checked={tasks.includes(t)} onChange={() => toggle(t)} /> {TASKS[t]}</label>
        ))}
      </div>
      <button className="btn" onClick={launch} disabled={tasks.length === 0 || (managed.length > 1 && !perimeter)}>Lancer l’analyse</button>
    </div>
  )
}

/** CMA-01 : les analyses des périmètres gérés. */
function Analyses({ list }) {
  if (list.length === 0) return <p className="muted small">Aucune analyse pour l’instant.</p>
  return (
    <table className="table small">
      <thead><tr><th>N°</th><th>Périmètre</th><th>Tâches</th><th>CI</th><th>Statut</th><th>Propositions</th><th>Écartées</th><th>Lancée</th></tr></thead>
      <tbody>{list.map(a => (
        <tr key={a.id}><td>{a.id}</td><td>{a.perimeterName}</td><td>{a.tasks.map(t => TASKS[t] ?? t).join(', ')}</td>
          <td>{a.wholePerimeter ? `${a.itemCount} (tout le périmètre)` : a.itemCount}</td>
          <td><span className={'badge ' + (TONE[a.status] ?? '')}>{a.status}</span>{a.reason && <div className="small">{a.reason}</div>}</td>
          <td>{a.proposalCount}</td><td>{a.rejectedCount}</td><td>{dateTimeFr(a.requestedAt)} par {a.requestedBy}</td></tr>
      ))}</tbody>
    </table>
  )
}

/**
 * CMA-03 : accepter passe par la modification du CI, toutes ses règles comprises (OPT-15) : le CI est relu, la valeur
 * proposée remplace la sienne, et l'en-tête de la proposition l'accompagne.
 */
export async function applyProposal(p) {
  const ci = await api.configurationItems.get(p.ciId)
  const form = { ...toForm(ciConfig.fields, ci), status: ci.status, [p.field]: p.after }
  return api.configurationItems.applyProposal(p.ciId, p.id, { ...(ciConfig.keep?.(ci) ?? {}), ...toPayload(ciConfig.fields, form), status: ci.status })
}

/**
 * CMA-05 : une fusion se décide par sa route, en gardant l'un des deux CI ; sans choix, celui que la proposition suggère.
 * L'autre CI, invisible, ne se fusionne pas : la proposition ne peut qu'être rejetée.
 */
const decideOne = (p, how, keep) => {
  if (how === 'reject') return api.configurationItems.rejectProposal(p.ciId, p.id)
  if (p.field === 'merge') return api.assistedCmdb.merge(p.id, keep ?? p.other.id)
  return applyProposal(p)
}

/** CMA-03 : file des propositions, filtrée par tâche ; décision une à une ou par sélection. */
function Queue({ version, onDecided }) {
  const [task, setTask] = useState('')
  const [list, setList] = useState(null)
  const [selected, setSelected] = useState([])
  const [errors, setErrors] = useState([])
  const load = () => Promise.resolve().then(() => api.assistedCmdb.proposals(task ? { task } : {}))
    .then(l => { setList(l); setSelected([]) }).catch(e => setErrors([e.message]))
  useEffect(() => { load() }, [task, version])
  const decide = async (items, how, keep) => {
    const failed = []
    for (const p of items) {
      try {
        await decideOne(p, how, keep)
      } catch (e) {
        failed.push(`${p.ciReference} — ${FIELD[p.field] ?? p.field} : ${e.message}`)
      }
    }
    setErrors(failed)
    onDecided(); load()
  }
  if (!list) return <p>Chargement…</p>
  const open = list.filter(p => !p.stale && (p.field !== 'merge' || p.other))
  const chosen = open.filter(p => selected.includes(p.id))
  return (
    <div className="card">
      <h2 className="card-title">Propositions à décider</h2>
      <div className="toolbar">
        <label htmlFor="cma-task" className="small">Tâche</label>
        <select id="cma-task" value={task} onChange={e => setTask(e.target.value)}>
          <option value="">Toutes</option>
          {OFFERED.map(t => <option key={t} value={t}>{TASKS[t]}</option>)}
        </select>
        <button className="btn small" disabled={chosen.length === 0} onClick={() => decide(chosen, 'apply')}>Appliquer la sélection</button>
        <button className="btn ghost small" disabled={chosen.length === 0} onClick={() => decide(chosen, 'reject')}>Rejeter la sélection</button>
      </div>
      {errors.map(e => <div key={e} className="error" role="alert">{e}</div>)}
      {list.length === 0 ? <p className="muted small">Aucune proposition ouverte.</p> : (
        <table className="table small">
          <thead><tr><th><span className="sr-only">Choisir</span></th><th>CI</th><th>Tâche</th><th>Avant</th><th>Après</th><th>Motif</th><th /></tr></thead>
          <tbody>{list.map(p => (
            <tr key={p.id}>
              <td><input type="checkbox" style={{ width: 'auto' }} aria-label={`Choisir ${p.ciReference} ${FIELD[p.field] ?? p.field}`}
                disabled={p.stale || (p.field === 'merge' && !p.other)}
                checked={selected.includes(p.id)} onChange={() => setSelected(s => (s.includes(p.id) ? s.filter(x => x !== p.id) : [...s, p.id]))} /></td>
              <td><Link to={`/configuration/${p.ciId}`}>{p.ciReference}</Link> {p.ciTitle}</td>
              <td>{TASKS[p.task] ?? p.task} · {FIELD[p.field] ?? p.field}</td>
              {p.field === 'merge'
                ? <><td>{p.ciReference} retiré</td>
                  <td>{p.other ? <>fusionné dans <Link to={`/configuration/${p.other.id}`}>{p.other.reference}</Link> {p.other.title}</> : '—'}</td></>
                : <><td>{p.before ?? '—'}</td><td><b>{p.after}</b></td></>}
              <td>{p.reasons}</td>
              <td>{p.stale ? <span className="small">Périmée : le CI a changé, elle se recalcule.</span> : <div className="toolbar">
                {p.field !== 'merge' && <button className="btn small" onClick={() => decide([p], 'apply')}>Appliquer</button>}
                {p.field === 'merge' && p.other && <>
                  <button className="btn small" onClick={() => decide([p], 'apply', p.other.id)}>Garder {p.other.reference}</button>
                  <button className="btn ghost small" onClick={() => decide([p], 'apply', p.ciId)}>Garder {p.ciReference}</button></>}
                {p.field === 'merge' && !p.other && <span className="small">L’autre CI n’est pas visible.</span>}
                <button className="btn ghost small" onClick={() => decide([p], 'reject')}>Rejeter</button>
              </div>}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
    </div>
  )
}

/** Configuration › CMDB assistée (gestionnaires) : lancer, suivre, décider (CMA-01, CMA-03). */
export default function AssistedCmdb() {
  const [analyses, setAnalyses] = useState([])
  const [version, setVersion] = useState(0)
  const [error, setError] = useState('')
  const reload = () => Promise.resolve().then(() => api.assistedCmdb.analyses()).then(setAnalyses).catch(e => setError(e.message))
  useEffect(() => { reload() }, [version])
  // Une analyse active se suit d'elle-même : relue toutes les 10 secondes.
  const active = analyses.some(a => ['En file', 'En cours'].includes(a.status))
  useEffect(() => {
    if (!active) return undefined
    const t = setInterval(() => setVersion(v => v + 1), 10000)
    return () => clearInterval(t)
  }, [active])
  return (
    <>
      <h1 className="page-title">CMDB assistée</h1>
      <p className="page-sub">Le modèle de langage propose de renommer, normaliser, reclasser et fusionner des CI. Rien n’est appliqué sans
        vous : chaque proposition s’accepte, se rejette, ou se corrige depuis la fiche du CI ; une fusion garde le CI de votre choix et
        retire l’autre, ses liens, relations et actifs du sas reportés.</p>
      {error && <div className="error" role="alert">{error}</div>}
      <Launch onLaunched={() => setVersion(v => v + 1)} />
      <div className="card" style={{ marginBottom: 16 }}>
        <h2 className="card-title">Analyses</h2>
        <Analyses list={analyses} />
      </div>
      <Queue version={version} onDecided={() => setVersion(v => v + 1)} />
    </>
  )
}

/** Paramétrage › Convention de nommage (CMA-04) : une description et des exemples par type de CI. */
export function NamingConventions() {
  const [list, setList] = useState(null)
  const [edits, setEdits] = useState({})
  const [message, setMessage] = useState(null)
  const types = ciConfig.fields.find(f => f.key === 'ciType')?.options ?? ['Application', 'Serveur', 'Base de données', 'Réseau', 'Stockage',
    'Poste de travail', 'Logiciel', 'Autre']
  const load = () => Promise.resolve().then(() => api.assistedCmdb.naming()).then(l => {
    setList(l)
    setEdits(Object.fromEntries(types.map(t => {
      const n = l.find(x => x.ciType === t)
      return [t, { description: n?.description ?? '', examples: (n?.examples ?? []).join('\n') }]
    })))
  }).catch(e => setMessage({ ok: false, text: e.message }))
  useEffect(() => { load() }, [])
  if (!list) return message ? <div className="error" role="alert">{message.text}</div> : <p>Chargement…</p>
  const save = t => api.assistedCmdb.saveNaming(t, { description: edits[t].description, examples: edits[t].examples.split('\n') })
    .then(() => { setMessage({ ok: true, text: `Convention « ${t} » enregistrée.` }); load() })
    .catch(e => setMessage({ ok: false, text: e.message }))
  const set = (t, k) => e => setEdits({ ...edits, [t]: { ...edits[t], [k]: e.target.value } })
  return (
    <>
      <h1 className="page-title">Convention de nommage</h1>
      <p className="page-sub">La tâche Renommer de la CMDB assistée la transmet au modèle ; sans convention pour un type, elle ne s’y applique pas.
        Une description vide retire la convention.</p>
      {message && <div className={message.ok ? 'notice ok' : 'error'} role={message.ok ? 'status' : 'alert'}>{message.text}</div>}
      {types.map(t => {
        const n = list.find(x => x.ciType === t)
        return (
          <div className="card" key={t} style={{ marginBottom: 12 }}>
            <h2 className="card-title">{t}</h2>
            <div className="row2">
              <div className="field"><label htmlFor={`naming-${t}`}>Description ({t})</label>
                <textarea id={`naming-${t}`} rows={2} maxLength={500} value={edits[t]?.description ?? ''} onChange={set(t, 'description')} /></div>
              <div className="field"><label htmlFor={`naming-ex-${t}`}>Exemples ({t}), un par ligne</label>
                <textarea id={`naming-ex-${t}`} rows={2} value={edits[t]?.examples ?? ''} onChange={set(t, 'examples')} /></div>
            </div>
            <button className="btn small" onClick={() => save(t)}>Enregistrer</button>
            {n && <span className="muted small"> Modifiée le {dateTimeFr(n.updatedAt)} par {n.updatedBy}.</span>}
          </div>
        )
      })}
    </>
  )
}
