import React, { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../../api'
import { dateTimeFr } from '../../fields'
import { ITEM_TYPES, hrefOf } from '../../itemTypes'

const ACTIONS = ['Création', 'Modification', 'Transition', 'Transition forcée', 'Suppression', 'Lien ajouté', 'Lien retiré']
// OPT-08 : origine d'une modification (saisie, ou proposition appliquée d'une fonctionnalité optionnelle).
const ORIGINS = ['Saisie', 'Tri assisté des tickets', 'CMDB assistée']
const iso = d => d.toISOString().slice(0, 10)

/** Administration → Journal d'audit (ADM-30, ADM-31) : toutes pratiques, filtres, export CSV. */
export default function AuditLog() {
  const today = new Date()
  const [f, setF] = useState({ author: '', type: '', action: '', reference: '', origin: '',
    from: iso(new Date(today.getTime() - 29 * 864e5)), to: iso(today) })
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const params = Object.fromEntries(Object.entries(f).filter(([, v]) => v))
  const load = (p = page) => api.admin.audit({ ...params, page: p }).then(d => { setData(d); setError('') }).catch(e => setError(e.message))
  useEffect(() => { load(page) }, [page])
  const search = () => { setPage(1); load(1) }
  const set = k => e => setF({ ...f, [k]: e.target.value })
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <>
      <h1 className="page-title">Journal d'audit</h1>
      <p className="page-sub">Toutes les créations, modifications, transitions, suppressions et liens, toutes pratiques confondues.</p>
      {error && <div className="error">{error}</div>}
      <div className="toolbar" role="search" aria-label="Filtres du journal">
        <input style={{ maxWidth: 200 }} placeholder="Auteur" value={f.author} onChange={set('author')} onKeyDown={e => e.key === 'Enter' && search()} aria-label="Auteur" />
        <input style={{ maxWidth: 170 }} placeholder="Référence" value={f.reference} onChange={set('reference')} onKeyDown={e => e.key === 'Enter' && search()} aria-label="Référence" />
        <select style={{ maxWidth: 180 }} value={f.type} onChange={set('type')} aria-label="Type">
          <option value="">Toutes les pratiques</option>
          {Object.entries(ITEM_TYPES).map(([k, t]) => <option key={k} value={k}>{t.label}</option>)}
        </select>
        <select style={{ maxWidth: 180 }} value={f.action} onChange={set('action')} aria-label="Action">
          <option value="">Toutes les actions</option>
          {ACTIONS.map(a => <option key={a}>{a}</option>)}
        </select>
        <select style={{ maxWidth: 200 }} value={f.origin} onChange={set('origin')} aria-label="Origine">
          <option value="">Toutes les origines</option>
          {ORIGINS.map(o => <option key={o}>{o}</option>)}
        </select>
        <label className="check">Du <input type="date" value={f.from} max={f.to} onChange={set('from')} /></label>
        <label className="check">au <input type="date" value={f.to} min={f.from} onChange={set('to')} /></label>
        <button className="btn" onClick={search}>Filtrer</button>
        <button className="btn ghost" style={{ marginLeft: 'auto' }} onClick={() => api.admin.auditExport(params).catch(e => setError(e.message))}
          disabled={!data?.total}>Exporter (CSV / Excel)</button>
      </div>
      {data && <p className="muted small" style={{ marginBottom: 8 }}>{data.total} ligne(s){data.total > 10000 && ' — l\'export en garde les 10 000 plus récentes'}.</p>}
      <div className="table-scroll">
        <table className="compact">
          <thead><tr><th>Date</th><th>Auteur</th><th>Enregistrement</th><th>Action</th><th>Détail</th></tr></thead>
          <tbody>
            {data?.items.map(a => (
              <tr key={a.id}>
                <td className="small mono">{dateTimeFr(a.at)}</td>
                <td>{a.authorDisplayName}</td>
                <td>{a.action === 'Suppression' || !ITEM_TYPES[a.entityType] ? <b>{a.reference}</b>
                  : <Link to={hrefOf(a.entityType, a.entityId)}><b>{a.reference}</b></Link>}
                  <div className="muted small">{ITEM_TYPES[a.entityType]?.label ?? a.entityType}</div></td>
                <td>{a.action}{a.origin && a.origin !== 'Saisie' && <div><span className="badge t-progress">{a.origin}</span></div>}</td>
                <td className="small">{a.field && <><b>{a.field}</b> : </>}{a.oldValue != null && <span className="muted">{a.oldValue} → </span>}{a.newValue}
                  {a.reason && <div className="muted">Motif : {a.reason}</div>}</td>
              </tr>))}
            {data?.items.length === 0 && <tr><td colSpan={5} className="muted">Aucune ligne pour ces filtres.</td></tr>}
          </tbody>
        </table>
      </div>
      {pages > 1 && (
        <div className="actions" style={{ marginTop: 12 }}>
          <button className="btn ghost small" onClick={() => setPage(p => p - 1)} disabled={page <= 1}>Précédente</button>
          <span className="small">Page {page} sur {pages}</span>
          <button className="btn ghost small" onClick={() => setPage(p => p + 1)} disabled={page >= pages}>Suivante</button>
        </div>
      )}
    </>
  )
}
