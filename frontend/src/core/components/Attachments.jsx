import React, { useEffect, useRef, useState } from 'react'
import { api, getUser } from '../../api'
import { dateTimeFr } from '../fields'
import useManagerOn from '../useManagerOn'

const size = n => n < 1024 ? `${n} o` : n < 1024 * 1024 ? `${(n / 1024).toFixed(0)} Ko` : `${(n / 1024 / 1024).toFixed(1)} Mo`

/**
 * SOC-25 : pièces jointes de l'enregistrement — liste, téléchargement, dépôt (types
 * bureautiques, texte et images, taille réglée par l'Admin), retrait par l'auteur ou un
 * gestionnaire du périmètre de l'enregistrement (`perimeterId`, PER-06 ; sans lui, le rôle AD).
 * Le stockage n'est pas activé : l'onglet le dit.
 */
export default function Attachments({ type, id, perimeterId }) {
  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const input = useRef(null)
  const user = getUser()
  const isManager = useManagerOn(perimeterId)
  const load = () => api.attachments.list(type, id).then(setData).catch(e => setError(e.message))
  useEffect(() => { load() }, [type, id])
  if (!data) return error ? <div className="error">{error}</div> : <p>Chargement…</p>
  const upload = e => {
    const file = e.target.files?.[0]
    if (!file) return
    if (file.size > data.maxFileMb * 1024 * 1024) { setError(`Fichier trop volumineux : ${data.maxFileMb} Mo au plus.`); e.target.value = ''; return }
    setBusy(true); setError('')
    api.attachments.upload(type, id, file).then(load).catch(err => setError(err.message))
      .finally(() => { setBusy(false); if (input.current) input.current.value = '' })
  }
  const remove = a => api.attachments.remove(a.id).then(load).catch(e => setError(e.message))
  const canRemove = a => isManager || a.createdBy.toLowerCase() === (user?.username ?? '').toLowerCase()
  return (
    <div className="card">
      {error && <div className="error" role="alert">{error}</div>}
      {!data.enabled && <div className="notice warn" role="status">Pièces jointes non activées : l’administrateur règle le stockage (Administration › Messagerie et stockage).</div>}
      {data.items.length === 0 ? <p className="muted small">Aucune pièce jointe.</p> : (
        <ul className="plain-list">
          {data.items.map(a => (
            <li key={a.id} style={{ display: 'flex', gap: 8, alignItems: 'baseline', flexWrap: 'wrap' }}>
              <a href={api.attachments.contentUrl(a.id)} download={a.fileName}><b>{a.fileName}</b></a>
              <span className="muted small">{size(a.size)} · {a.createdByDisplayName}, {dateTimeFr(a.createdAt)}</span>
              {canRemove(a) && <button className="btn ghost small" onClick={() => remove(a)} aria-label={`Retirer ${a.fileName}`}>Retirer</button>}
            </li>))}
        </ul>)}
      {data.enabled && <div style={{ marginTop: 12 }}>
        <label className="btn ghost" style={{ cursor: busy ? 'wait' : 'pointer' }}>
          {busy ? 'Envoi…' : '+ Joindre un fichier'}
          <input ref={input} type="file" hidden onChange={upload} disabled={busy} accept={data.extensions.join(',')} aria-label="Fichier à joindre" />
        </label>
        <small className="field-hint">{data.maxFileMb} Mo au plus · {data.extensions.join(', ')}</small>
      </div>}
    </div>
  )
}
