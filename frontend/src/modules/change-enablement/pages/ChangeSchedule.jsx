import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../../../api'
import useManagesPerimeter from '../../../core/useManagesPerimeter'
import StatusBadge from '../../../core/components/StatusBadge'
import { dateTimeFr } from '../../../core/fields'
import { changeStatuses } from '../config'

/** Lundi de la semaine d'une date (clé de regroupement). */
const mondayOf = iso => {
  const d = new Date(iso)
  d.setHours(0, 0, 0, 0)
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7))
  return d
}

/**
 * CHG-25 : périodes de gel — aucun changement (sauf urgent) ne s'y planifie. Lues par tous,
 * tenues par les gestionnaires.
 */
export function Freezes() {
  const [items, setItems] = useState([])
  const [f, setF] = useState({ start: '', end: '', reason: '' })
  const [error, setError] = useState('')
  const manager = useManagesPerimeter()
  const load = () => Promise.resolve().then(() => api.changeFreezes.list()).then(setItems).catch(() => setItems([]))
  useEffect(() => { load() }, [])
  const add = () => api.changeFreezes.create({ start: new Date(f.start).toISOString(), end: new Date(f.end).toISOString(), reason: f.reason })
    .then(() => { setF({ start: '', end: '', reason: '' }); setError(''); load() }).catch(e => setError(e.message))
  const remove = id => api.changeFreezes.remove(id).then(load).catch(e => setError(e.message))
  if (!manager && items.length === 0) return null
  return (
    <div className="card" style={{ marginBottom: 14, borderLeft: '4px solid var(--danger)' }}>
      <h3 style={{ color: 'var(--navy)', marginBottom: 6 }}>Périodes de gel</h3>
      <p className="muted small" style={{ marginBottom: 8 }}>Aucun changement ne s'y planifie, sauf urgent.</p>
      {items.length === 0 && <p className="muted small">Aucune période de gel à venir.</p>}
      {items.map(x => (
        <p key={x.id} style={{ fontSize: 13, margin: '4px 0' }}>
          ❄ <b>{x.reason}</b> : du {dateTimeFr(x.start)} au {dateTimeFr(x.end)}
          {manager && <> <button className="btn ghost small" onClick={() => remove(x.id)}>Retirer</button></>}</p>))}
      {manager && (
        <div className="row3" style={{ marginTop: 8, alignItems: 'end' }}>
          <div className="field"><label htmlFor="freeze-start">Début</label>
            <input id="freeze-start" type="datetime-local" value={f.start} onChange={e => setF({ ...f, start: e.target.value })} /></div>
          <div className="field"><label htmlFor="freeze-end">Fin</label>
            <input id="freeze-end" type="datetime-local" value={f.end} onChange={e => setF({ ...f, end: e.target.value })} /></div>
          <div className="field"><label htmlFor="freeze-reason">Motif</label>
            <input id="freeze-reason" value={f.reason} onChange={e => setF({ ...f, reason: e.target.value })} placeholder="Clôture comptable, fêtes…" /></div>
          <button className="btn" onClick={add} disabled={!f.start || !f.end || !f.reason.trim()}>Ajouter la période de gel</button>
        </div>)}
      {error && <div className="error small">{error}</div>}
    </div>
  )
}

/** Calendrier des changements : changements planifiés, regroupés par semaine. */
export default function ChangeSchedule() {
  const [items, setItems] = useState(null)
  const [error, setError] = useState('')
  const nav = useNavigate()
  useEffect(() => { api.changes.schedule().then(setItems).catch(e => setError(e.message)) }, [])
  if (error) return <div className="error">{error}</div>
  if (!items) return <p>Chargement…</p>

  const weeks = []
  for (const c of items) {
    const key = mondayOf(c.plannedStart).getTime()
    const week = weeks.find(w => w.key === key)
    if (week) week.items.push(c); else weeks.push({ key, items: [c] })
  }

  return (
    <>
      <h1 className="page-title">Calendrier des changements</h1>
      <p className="page-sub">Changements planifiés (hors rejetés), de la semaine dernière à venir.</p>
      <Freezes />
      {weeks.length === 0 && <p style={{ color: 'var(--muted)' }}>Aucun changement planifié.</p>}
      {weeks.map(w => (
        <div className="card" key={w.key} style={{ marginBottom: 14 }}>
          <h3 style={{ color: 'var(--navy)', marginBottom: 10 }}>
            Semaine du {new Date(w.key).toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric' })}
          </h3>
          <table>
            <thead><tr><th>Début</th><th>Fin</th><th>Référence</th><th>Titre</th><th>Type</th><th>Risque</th><th>Responsable</th><th>Statut</th></tr></thead>
            <tbody>{w.items.map(c => (
              <tr key={c.id} className="clickable" onClick={() => nav('/changes/' + c.id)}
                style={c.conflicts.length ? { boxShadow: 'inset 4px 0 0 var(--accent-red)' } : undefined}>
                <td>{dateTimeFr(c.plannedStart)}</td><td>{dateTimeFr(c.plannedEnd)}</td>
                <td><b>{c.reference}</b></td>
                <td>{c.title}
                  {c.conflicts.map(x => (
                    <div key={x.changeId + '-' + x.ciId} style={{ color: 'var(--accent-red)', fontSize: 'var(--fs-xs)', fontWeight: 600 }}>
                      Conflit avec {x.changeReference} sur {x.ciReference} {x.ciTitle}
                    </div>))}
                </td><td>{c.changeType}</td><td>{c.risk}</td>
                <td>{c.ownerDisplayName || '—'}</td>
                <td><StatusBadge statuses={changeStatuses} status={c.status} /></td>
              </tr>))}
            </tbody>
          </table>
        </div>
      ))}
    </>
  )
}
