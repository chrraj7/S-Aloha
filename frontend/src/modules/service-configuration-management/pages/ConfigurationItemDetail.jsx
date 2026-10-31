import React, { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, getUser } from '../../../api'
import { dateFr, dateTimeFr } from '../../../core/fields'
import RecordDetail from '../../../core/components/RecordDetail'
import useManagerOn from '../../../core/useManagerOn'
import { ciConfig, RELATION_TYPES } from '../config'
import { Sources, StagingBadge } from '../components/Discovery'

/** Dépendances du CI : relations sortantes (ce CI → autre) et entrantes (autre → ce CI). */
function Relations({ ci, onChanged }) {
  const [type, setType] = useState(RELATION_TYPES[0])
  const [reference, setReference] = useState('')
  const [error, setError] = useState('')
  const add = () => api.configurationItems.addRelation(ci.id, { targetReference: reference, type })
    .then(() => { setReference(''); setError(''); onChanged() }).catch(e => setError(e.message))
  const remove = id => api.configurationItems.removeRelation(id).then(onChanged).catch(e => setError(e.message))

  const rows = [
    ...ci.outgoing.map(r => ({ id: r.id, text: `${ci.title} ${r.type.toLowerCase()}`, other: r.target })),
    ...ci.incoming.map(r => ({ id: r.id, text: `${r.type.toLowerCase()} ${ci.title}`, other: r.source, before: true })),
  ]
  return (
    <div className="card">
      <h3 style={{ color: 'var(--navy)', marginBottom: 10 }}>Relations ({rows.length})</h3>
      {error && <div className="error">{error}</div>}
      {rows.length > 0 && (
        <table style={{ marginBottom: 12 }}>
          <tbody>{rows.map(r => {
            const other = <Link to={'/configuration/' + r.other.id}><b>{r.other.reference}</b> {r.other.title}</Link>
            return (
              <tr key={r.id}>
                <td>{r.before ? <>{other} {r.text}</> : <>{r.text} {other}</>}</td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn ghost small" onClick={() => remove(r.id)} title="Retirer la relation">×</button>
                </td>
              </tr>)
          })}</tbody>
        </table>
      )}
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        <select style={{ maxWidth: 180 }} value={type} onChange={e => setType(e.target.value)}>
          {RELATION_TYPES.map(t => <option key={t}>{t}</option>)}
        </select>
        <input style={{ maxWidth: 220 }} value={reference} onChange={e => setReference(e.target.value)}
          placeholder="CI cible, ex. CI-2026-0002" />
        <button className="btn ghost" onClick={add} disabled={!reference}>Ajouter</button>
      </div>
    </div>
  )
}

/** Une colonne de la vue d'impact : CI atteints, en retrait selon leur distance. */
function ImpactList({ title, hint, nodes }) {
  return (
    <div>
      <h4 style={{ color: 'var(--navy)', marginBottom: 2 }}>{title} ({nodes.length})</h4>
      <p style={{ color: 'var(--muted)', fontSize: 'var(--fs-sm)', marginBottom: 8 }}>{hint}</p>
      {nodes.length === 0 && <p style={{ color: 'var(--muted)', fontSize: 'var(--fs-sm)' }}>Aucun.</p>}
      {nodes.map(n => (
        <div key={n.id} style={{ paddingLeft: (n.depth - 1) * 18, marginBottom: 6 }}>
          <Link to={'/configuration/' + n.id}><b>{n.reference}</b> {n.title}</Link>
          <span style={{ color: 'var(--muted)', fontSize: 'var(--fs-xs)' }}> — {n.ciType}, {n.status} · {n.relation.toLowerCase()} {n.via.reference}</span>
        </div>
      ))}
    </div>
  )
}

/** Vue d'impact transitive : ce qui tombe si ce CI tombe (aval), ce dont il dépend (amont). */
function Impact({ ci }) {
  const [impact, setImpact] = useState(null)
  const [error, setError] = useState('')
  useEffect(() => { api.configurationItems.impact(ci.id).then(setImpact).catch(e => setError(e.message)) }, [ci])
  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 style={{ color: 'var(--navy)', marginBottom: 10 }}>Impact</h3>
      {error && <div className="error">{error}</div>}
      {impact && (
        <>
          <div className="row2">
            <ImpactList title="En aval" hint="Touchés si ce CI tombe." nodes={impact.downstream} />
            <ImpactList title="En amont" hint="Ce dont ce CI dépend." nodes={impact.upstream} />
          </div>
          <ServicesTouched services={impact.services} />
        </>
      )}
    </div>
  )
}

/** CFG-21 : services du catalogue touchés (supportés par ce CI ou par un CI en aval). */
export function ServicesTouched({ services }) {
  return (
    <div style={{ marginTop: 12 }}>
      <h4 style={{ color: 'var(--navy)', marginBottom: 2 }}>Services touchés ({services.length})</h4>
      <p style={{ color: 'var(--muted)', fontSize: 'var(--fs-sm)', marginBottom: 8 }}>
        Services supportés par ce CI ou par un CI en aval (liens CI ↔ service, onglet Liens).</p>
      {services.length === 0 && <p style={{ color: 'var(--muted)', fontSize: 'var(--fs-sm)' }}>Aucun.</p>}
      {services.map(s => (
        <div key={s.id} style={{ marginBottom: 6 }}>
          <Link to={'/services/' + s.id}><b>{s.reference}</b> {s.title}</Link>
          <span style={{ color: 'var(--muted)', fontSize: 'var(--fs-xs)' }}> — {s.status}{s.ownerDisplayName && ` · ${s.ownerDisplayName}`}</span>
        </div>
      ))}
    </div>
  )
}

/**
 * CFG-16 : attributs propres au type du CI (CPU, mémoire, version, éditeur…). L'enregistrement
 * renvoie la fiche entière avec ses attributs ; l'API vérifie les clés, les nombres et les adresses.
 */
export function Attributes({ ci, onSaved }) {
  const [types, setTypes] = useState(null)
  const [f, setF] = useState(null)
  const [error, setError] = useState('')
  useEffect(() => { api.configurationItems.attributeTypes().then(setTypes).catch(() => setTypes({})) }, [])
  const defined = types?.[ci.ciType] ?? []
  if (!types || defined.length === 0) return null
  const save = () => api.configurationItems.update(ci.id, { ...ci, attributes: f })
    .then(() => { setF(null); setError(''); onSaved() }).catch(e => setError(e.message))
  return (
    <div className="card" style={{ marginTop: 16 }} aria-label="Attributs">
      <h3 className="card-title">Attributs ({ci.ciType})</h3>
      {error && <div className="error">{error}</div>}
      {f ? <>
        <div className="row3">{defined.map(a => (
          <div className="field" key={a.key}><label htmlFor={'attr-' + a.key}>{a.label}</label>
            <input id={'attr-' + a.key} value={f[a.key] ?? ''} inputMode={a.kind === 'nombre' ? 'decimal' : undefined}
              type={a.kind === 'url' ? 'url' : 'text'} onChange={e => setF({ ...f, [a.key]: e.target.value })} /></div>))}
        </div>
        <button className="btn" onClick={save}>Enregistrer</button>{' '}
        <button className="btn ghost" onClick={() => setF(null)}>Annuler</button>
      </> : <>
        <dl className="small">{defined.filter(a => ci.attributes?.[a.key]).map(a => (
          <div key={a.key}><dt className="muted" style={{ display: 'inline' }}>{a.label} : </dt><dd style={{ display: 'inline', margin: 0 }}>{ci.attributes[a.key]}</dd></div>))}
        </dl>
        {!defined.some(a => ci.attributes?.[a.key]) && <p className="muted small">Aucun attribut renseigné.</p>}
        <button className="btn ghost small" onClick={() => setF({ ...(ci.attributes ?? {}) })}>Modifier les attributs</button>
      </>}
    </div>
  )
}

/** CFG-15 : le propriétaire (ou un gestionnaire) atteste que la fiche est exacte. */
export function Verification({ ci, onSaved }) {
  const [msg, setMsg] = useState(null)
  const user = getUser()
  // PER-06 : gestionnaire du périmètre du CI, comme `IsManager` de l'API.
  const isManager = useManagerOn(ci.perimeterId)
  const can = isManager || ci.ownerType === 'Group'
    || (ci.ownerId ?? '').toLowerCase() === (user?.username ?? '').toLowerCase()
  const verify = () => api.configurationItems.verify(ci.id)
    .then(() => { setMsg({ ok: true, text: 'Fiche attestée exacte.' }); onSaved() }).catch(e => setMsg({ ok: false, text: e.message }))
  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 style={{ color: 'var(--navy)', marginBottom: 6 }}>Vérification</h3>
      {msg && <div className="error" style={msg.ok ? { background: 'var(--blue-soft)', color: 'var(--navy)' } : undefined}>{msg.text}</div>}
      <p style={{ fontSize: 13, marginBottom: 10 }}>
        {ci.lastVerifiedAt ? `Vérifiée le ${dateFr(ci.lastVerifiedAt)} par ${ci.lastVerifiedBy}.` : 'Jamais vérifiée.'}
        {' '}Un CI en service non vérifié depuis 12 mois est signalé à son propriétaire.</p>
      {can && <button className="btn ghost" onClick={verify}>Attester que la fiche est exacte</button>}
    </div>
  )
}

/**
 * CFG-40 : actifs de la découverte rapprochés de ce CI (gestionnaires) — d'où vient son
 * identification technique, et quand il a été vu pour la dernière fois.
 */
function DiscoveryTrace({ ci }) {
  const [assets, setAssets] = useState([])
  const isManager = ['Admin', 'Manager'].includes(getUser()?.role)
  useEffect(() => {
    if (isManager) api.discovery.staging.list({ ci: ci.id }).then(setAssets).catch(() => setAssets([]))
  }, [ci.id])
  if (assets.length === 0) return null
  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 className="card-title">Découverte</h3>
      <ul className="plain-list">
        {assets.map(a => (
          <li key={a.id}>
            <Link to={'/configuration/staging/' + a.id}><b>{a.hostname ?? a.ipAddress}</b></Link>{' '}
            <StagingBadge status={a.status} />
            <span className="muted"> — <Sources value={a.sources} />, vu le {dateTimeFr(a.lastSeenAt)}</span>
          </li>))}
      </ul>
    </div>
  )
}

export default function ConfigurationItemDetail() {
  return (
    <RecordDetail config={ciConfig} tab={{
      label: 'Relations et impact',
      render: (r, reload) => <><Relations ci={r} onChanged={reload} /><Impact ci={r} /></>
    }}>
      {(r, reload) => <><Attributes ci={r} onSaved={reload} /><Verification ci={r} onSaved={reload} /><DiscoveryTrace ci={r} /></>}
    </RecordDetail>
  )
}
