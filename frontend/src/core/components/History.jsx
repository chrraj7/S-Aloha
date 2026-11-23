import React, { useEffect, useState } from 'react'
import { api } from '../../api'
import { dateTimeFr } from '../fields'

const SAISIE = 'Saisie'
const isDate = v => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(v)
const show = v => (v == null || v === '' ? '—' : isDate(v) ? dateTimeFr(v) : v)

/** Valeur abrégée dans le tableau, entière au survol. */
function Value({ v }) {
  const text = show(v)
  return <span title={text.length > 120 ? text : undefined}>{text.length > 120 ? text.slice(0, 119) + '…' : text}</span>
}

function Detail({ e, labels }) {
  const label = f => labels[f] ?? f
  switch (e.action) {
    case 'Création':
      return <>{e.field ? <b>{e.field}</b> : <>Statut initial : <b>{show(e.newValue)}</b></>}</>
    case 'Suppression':
      return <>{e.field ? <b>{e.field}</b> : <>Statut : {show(e.oldValue)}</>}</>
    case 'Lien ajouté':
      return <>{e.field && <>{e.field} </>}<b>{show(e.newValue)}</b></>
    case 'Lien retiré':
      return <>{e.field && <>{e.field} </>}<s>{show(e.oldValue)}</s></>
    default:
      return (
        <>
          {e.field === 'status' ? 'Statut' : label(e.field)} : <Value v={e.oldValue} /> → <b><Value v={e.newValue} /></b>
          {e.reason && <div style={{ color: 'var(--muted)', fontSize: 'var(--fs-sm)' }}>Motif : {e.reason}</div>}
        </>
      )
  }
}

/**
 * Historique d'un enregistrement (SOC-20) : journal d'audit, du plus récent au
 * plus ancien. `labels` traduit les noms de champs de l'API en libellés.
 */
export default function History({ type, id, labels = {}, refreshKey }) {
  const [entries, setEntries] = useState(null)
  const [error, setError] = useState('')
  const [origin, setOrigin] = useState('')
  useEffect(() => { api.audit.list(type, id).then(setEntries).catch(e => setError(e.message)) }, [type, id, refreshKey])
  // OPT-08 : une modification qui applique une proposition porte son origine ; le filtre
  // n'apparaît que si l'historique en compte plusieurs.
  const origins = [...new Set((entries ?? []).map(e => e.origin ?? SAISIE))]
  const shown = (entries ?? []).filter(e => !origin || (e.origin ?? SAISIE) === origin)
  return (
    <div className="card">
      <h3 style={{ color: 'var(--navy)', marginBottom: 10 }}>Historique</h3>
      {error && <div className="error">{error}</div>}
      {entries?.length === 0 && <p style={{ color: 'var(--muted)', fontSize: 'var(--fs-sm)' }}>Aucune entrée au journal.</p>}
      {origins.length > 1 && (
        <select style={{ maxWidth: 240, marginBottom: 8 }} value={origin} onChange={e => setOrigin(e.target.value)} aria-label="Origine">
          <option value="">Toutes les origines</option>
          {origins.map(o => <option key={o}>{o}</option>)}
        </select>
      )}
      {entries?.length > 0 && (
        <table>
          <thead><tr><th>Date</th><th>Auteur</th><th>Action</th><th>Détail</th></tr></thead>
          <tbody>{shown.map(e => (
            <tr key={e.id}>
              <td style={{ whiteSpace: 'nowrap' }}>{dateTimeFr(e.at)}</td>
              <td>{e.authorDisplayName}</td>
              <td>{e.action === 'Transition forcée' ? <span className="badge t-bad">{e.action}</span> : e.action}
                {e.origin && e.origin !== SAISIE && <div><span className="badge t-progress">{e.origin}</span></div>}</td>
              <td><Detail e={e} labels={labels} /></td>
            </tr>))}
          </tbody>
        </table>
      )}
    </div>
  )
}

/** Libellés des champs d'une configuration de module (`config.fields`). */
export const labelsOf = fields => Object.fromEntries(fields.filter(f => f.type !== 'heading').flatMap(f =>
  [[f.key, f.label], ...(f.nameKey ? [[f.nameKey, f.label]] : [])]))
