import React, { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, getUser } from '../../../api'
import { dateTimeFr } from '../../../core/fields'
import { nextDue } from '../../regulatory-notifications/config'
import RecordDetail from '../../../core/components/RecordDetail'
import SimilarCases from '../../../core/components/SimilarCases'
import { incidentConfig } from '../config'
import { useServiceConfig } from '../../../core/useServices'
import { useCategoryConfig } from '../../../core/useCategories'
import usePerimeters from '../../../core/usePerimeters'

/** Un incident récurrent ou de cause inconnue ouvre un problème, relié à l'incident. */
function OpenProblem({ incident }) {
  const [error, setError] = useState('')
  const nav = useNavigate()
  const open = async () => {
    try {
      const p = await api.problems.create({
        title: incident.title, description: incident.description, impact: incident.impact,
        urgency: incident.urgency, category: incident.category, affectedService: incident.affectedService,
        serviceId: incident.serviceId
      })
      await api.links.create({ fromType: 'incident', fromId: incident.id, toReference: p.reference })
      nav('/problems/' + p.id)
    } catch (e) { setError(e.message) }
  }
  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 style={{ color: 'var(--navy)', marginBottom: 4 }}>Cause sous-jacente</h3>
      <p style={{ color: 'var(--muted)', fontSize: 13, marginBottom: 10 }}>
        Incident récurrent ou cause inconnue : ouvrir un problème pour en rechercher la cause racine.</p>
      {error && <div className="error">{error}</div>}
      <button className="btn ghost" onClick={open}>Ouvrir un problème lié</button>
    </div>
  )
}

/**
 * INC-16 : incidents d'une même panne. Un enfant se rattache à un parent par sa référence ;
 * résoudre le parent résout ses enfants avec la même résolution.
 */
export function Family({ incident, reload }) {
  const [family, setFamily] = useState(null)
  const [reference, setReference] = useState('')
  const [error, setError] = useState('')
  useEffect(() => { api.incidents.family(incident.id).then(setFamily).catch(e => setError(e.message)) }, [incident.id, incident.parentId, incident.status])
  if (!family) return null
  const done = ['Résolu', 'Clos'].includes(incident.status)
  const attach = ref => api.incidents.setParent(incident.id, ref).then(() => { setReference(''); setError(''); reload() }).catch(e => setError(e.message))
  const row = i => <li key={i.id}><Link to={`/incidents/${i.id}`}><b>{i.reference}</b></Link> {i.title} <span className="badge">{i.status}</span></li>
  if (!family.parent && family.children.length === 0 && done) return null
  return (
    <section className="card" style={{ marginTop: 16 }} aria-label="Même panne">
      <h3 className="card-title">Même panne</h3>
      {error && <div className="error">{error}</div>}
      {family.parent && <p className="small">Rattaché à l’incident parent : <Link to={`/incidents/${family.parent.id}`}><b>{family.parent.reference}</b></Link> {family.parent.title}
        {!done && <> <button className="btn ghost small" onClick={() => attach('')}>Détacher</button></>}</p>}
      {family.children.length > 0 && <>
        <p className="small">Incidents enfants, résolus avec celui-ci :</p>
        <ul className="small">{family.children.map(row)}</ul>
      </>}
      {!family.parent && family.children.length === 0 && !done && (
        <div className="toolbar">
          <input aria-label="Référence de l’incident parent" value={reference} onChange={e => setReference(e.target.value)} placeholder="INC-2026-0042" />
          <button className="btn ghost" onClick={() => attach(reference.trim())} disabled={!reference.trim()}>Rattacher à cet incident parent</button>
        </div>)}
    </section>
  )
}

/**
 * INC-41, INC-42 : qualification réglementaire d'un incident de sécurité par un gestionnaire,
 * et ses déclarations (DRG-01). INC-44 : important au sens NIS 2 → proposé comme incident majeur.
 */
export function Regulatory({ incident, reload }) {
  // PER-06 : gestionnaire du périmètre de l'incident, comme l'API (ADR-0014 D4 : le rôle par
  // périmètre prime sur le rôle AD) ; tant que la liste n'est pas chargée, le rôle AD.
  const here = usePerimeters().find(p => p.id === incident.perimeterId)?.role
  const isManager = ['Admin', 'Manager'].includes(here ?? getUser()?.role)
  const [declarations, setDeclarations] = useState(null)
  const [f, setF] = useState({ nis2Important: '', nis2Reason: '', personalDataBreach: '', personalDataBreachReason: '', awareAt: '' })
  const [msg, setMsg] = useState(null)
  const load = () => api.regulatoryNotifications.list({ incident: incident.id }).then(setDeclarations).catch(e => setMsg({ ok: false, text: e.message }))
  useEffect(() => { load() }, [incident.id, incident.qualifiedAt])
  const set = patch => setF(prev => ({ ...prev, ...patch }))
  const qualify = () => api.incidents.qualify(incident.id, {
    nis2Important: f.nis2Important === 'true', nis2Reason: f.nis2Reason || null,
    personalDataBreach: f.personalDataBreach === 'true', personalDataBreachReason: f.personalDataBreachReason || null,
    awareAt: f.awareAt ? new Date(f.awareAt).toISOString() : null,
  }).then(res => {
    setMsg({ ok: true, text: (res.created.length ? `Déclaration(s) créée(s) : ${res.created.join(', ')}.` : 'Qualification enregistrée, aucune déclaration requise.')
      + (res.proposeMajor ? ' Incident important au sens de NIS 2 : le déclarer incident majeur (case « Incident majeur »).' : '') })
    reload()
  }).catch(e => setMsg({ ok: false, text: e.message }))
  if (!incident.isSecurity) return null
  const ready = f.nis2Important !== '' && f.personalDataBreach !== ''
  return (
    <section className="card" style={{ marginTop: 16 }} aria-label="Déclarations réglementaires">
      <h3 className="card-title">Incident de sécurité : déclarations réglementaires</h3>
      {msg && <div className={msg.ok ? 'notice ok' : 'error'} role={msg.ok ? 'status' : 'alert'}>{msg.text}</div>}
      {incident.qualifiedAt
        ? <p className="small" style={{ marginBottom: 8 }}>
            Qualifié par {incident.qualifiedBy} le {dateTimeFr(incident.qualifiedAt)} — NIS 2 : {incident.nis2Important ? 'incident important' : `non important (${incident.nis2Reason})`} ;
            RGPD : {incident.personalDataBreach ? 'violation de données personnelles' : `pas de violation (${incident.personalDataBreachReason})`}.
          </p>
        : <p className="small" style={{ marginBottom: 8 }}>
            À qualifier par un gestionnaire, dans les 24 h suivant la prise de connaissance : chaque régime retenu crée sa déclaration, avec ses échéances légales.
          </p>}
      {isManager && !incident.qualifiedAt && (
        <div className="form-grid">
          <div className="field"><label>Incident important au sens de NIS 2 ?</label>
            <select value={f.nis2Important} onChange={e => set({ nis2Important: e.target.value })} aria-label="NIS 2">
              <option value="">—</option><option value="true">Oui : déclarer à l’ANSSI</option><option value="false">Non</option>
            </select></div>
          <div className="field"><label>Motif (NIS 2){f.nis2Important === 'false' && ' *'}</label>
            <input value={f.nis2Reason} onChange={e => set({ nis2Reason: e.target.value })} placeholder="Ex. : pas d’impact significatif sur le service" /></div>
          <div className="field"><label>Violation de données à caractère personnel ?</label>
            <select value={f.personalDataBreach} onChange={e => set({ personalDataBreach: e.target.value })} aria-label="RGPD">
              <option value="">—</option><option value="true">Oui : notifier la CNIL</option><option value="false">Non</option>
            </select></div>
          <div className="field"><label>Motif (RGPD){f.personalDataBreach === 'false' && ' *'}</label>
            <input value={f.personalDataBreachReason} onChange={e => set({ personalDataBreachReason: e.target.value })} placeholder="Ex. : aucune donnée personnelle concernée" /></div>
          <div className="field"><label>Prise de connaissance</label>
            <input type="datetime-local" value={f.awareAt} onChange={e => set({ awareAt: e.target.value })} aria-label="Prise de connaissance" />
            <small className="field-hint">Point de départ des délais ; par défaut, la déclaration de l’incident.</small></div>
        </div>)}
      {isManager && !incident.qualifiedAt && <button className="btn" onClick={qualify} disabled={!ready}>Qualifier</button>}
      {declarations?.length > 0 && (
        <div className="table-scroll" style={{ marginTop: 12 }}>
          <table className="compact">
            <thead><tr><th>Déclaration</th><th>Régime</th><th>Statut</th><th>Prochaine échéance</th></tr></thead>
            <tbody>{declarations.map(d => (
              <tr key={d.id}>
                <td><Link to={'/regulatory-notifications/' + d.id}><b>{d.reference}</b></Link></td>
                <td>{d.regime} · {d.authority}</td><td>{d.status}</td><td>{nextDue(d)}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  )
}

export default function IncidentDetail() {
  const config = useCategoryConfig(useServiceConfig(incidentConfig), 'incident')
  if (!config) return <p>Chargement…</p>
  return <RecordDetail config={config}>{(r, reload) => <><Regulatory incident={r} reload={reload} /><Family incident={r} reload={reload} /><OpenProblem incident={r} /><SimilarCases type="incident" id={r.id} /></>}</RecordDetail>
}
