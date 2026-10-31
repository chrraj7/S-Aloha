import React, { useState } from 'react'
import { Link } from 'react-router-dom'
import { api, getUser } from '../../../api'
import RecordDetail from '../../../core/components/RecordDetail'
import useManagerOn from '../../../core/useManagerOn'
import { dateFr } from '../../../core/fields'
import { improvementConfig } from '../config'

/**
 * CSI-15 : à la date de contrôle, le porteur confirme que le résultat tient, ou décrit ce qui
 * ne tient pas : une nouvelle amélioration, reliée, reprend le sujet.
 */
export function FollowUp({ improvement, onDone }) {
  const [note, setNote] = useState('')
  const [msg, setMsg] = useState(null)
  const i = improvement
  // PER-06 : gestionnaire du périmètre de l'amélioration ; avant tout retour (règle des hooks).
  const isManager = useManagerOn(i.perimeterId)
  if (i.status !== 'Réalisée' || !i.followUpDate) return null
  if (i.followUpDoneAt) return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 className="card-title">Durabilité</h3>
      <p className="small">Contrôlée le {dateFr(i.followUpDoneAt)} par {i.followUpDoneBy} : {i.followUpHolds ? 'le résultat tient.' : `le résultat ne tient pas — ${i.followUpNote}`}</p>
    </div>)
  const user = getUser()
  const can = isManager || i.ownerType === 'Group' || (i.ownerId ?? '').toLowerCase() === (user?.username ?? '').toLowerCase()
  const due = new Date(i.followUpDate) <= new Date()
  const send = holds => api.improvements.followUp(i.id, { holds, note: note || null })
    .then(r => { setMsg({ ok: true, created: r.created }); onDone() }).catch(e => setMsg({ ok: false, text: e.message }))
  return (
    <div className="card" style={{ marginTop: 16 }} aria-label="Durabilité">
      <h3 className="card-title">Durabilité</h3>
      <p className="small">Contrôle prévu le {dateFr(i.followUpDate)} : le résultat constaté tient-il dans la durée ?</p>
      {msg && <div className={msg.ok ? 'notice ok' : 'error'} role="status">{msg.ok
        ? (msg.created ? <>Nouvelle amélioration ouverte : <Link to="/improvements">{msg.created}</Link>.</> : 'Contrôle enregistré.') : msg.text}</div>}
      {can && due && <>
        <div className="field"><label htmlFor="csi-note">Constat</label>
          <textarea id="csi-note" rows={2} value={note} onChange={e => setNote(e.target.value)} placeholder="Obligatoire si le résultat ne tient pas." /></div>
        <button className="btn" onClick={() => send(true)}>Le résultat tient</button>{' '}
        <button className="btn ghost" onClick={() => send(false)} disabled={!note.trim()}>Ne tient pas : ouvrir une amélioration</button>
      </>}
    </div>
  )
}

export default function ImprovementDetail() {
  return <RecordDetail config={improvementConfig}>{(r, reload) => <FollowUp improvement={r} onDone={reload} />}</RecordDetail>
}
