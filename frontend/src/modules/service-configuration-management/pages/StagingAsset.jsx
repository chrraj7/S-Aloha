import React, { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../../../api'
import { dateTimeFr } from '../../../core/fields'
import RecordForm, { toForm, toPayload } from '../../../core/components/RecordForm'
import { ciConfig, HARDWARE_TYPES, RULE_LABELS } from '../config'
import { KeyValues, ScoreMeter, Sources, StagingBadge } from '../components/Discovery'
import { useFeatures } from '../../../core/features'

const OPEN = ['À rapprocher', 'En conflit']
const COMPARED = [
  ['Adresse IP', 'ipAddress', 'ipAddress'], ['Adresse MAC', 'macAddress', 'macAddress'],
  ['Système', 'detectedOs', 'operatingSystem'], ['Constructeur', 'vendor', 'vendor'], ['Modèle', 'model', 'model'],
  ['Numéro de série', 'serialNumber', 'serialNumber'], ['Emplacement', 'location', 'location'],
]

/** Ce que la découverte a vu de l'équipement. */
function Seen({ asset }) {
  return (
    <section className="card">
      <h3 className="card-title">Ce que la découverte a vu</h3>
      <KeyValues rows={[
        ['Nom d’hôte', asset.hostname], ['Adresse IP', asset.ipAddress], ['Adresse MAC', asset.macAddress],
        ['Type déduit', asset.detectedType], ['Système', asset.detectedOs], ['Constructeur', asset.vendor],
        ['Modèle', asset.model], ['Numéro de série', asset.serialNumber], ['Emplacement', asset.location],
        ['Ports ouverts', asset.openPorts],
        ['Bannières', asset.banners && <pre className="banners">{asset.banners}</pre>],
        ['Interfaces', asset.interfaces && <pre className="banners">{asset.interfaces}</pre>],
        ['Ports en écoute', asset.listeningPorts],
        ['Services actifs', asset.services && <pre className="banners">{asset.services}</pre>],
        ['Sources', <Sources value={asset.sources} />],
        ['Annuaire', asset.inDirectory ? 'Compte d’ordinateur présent' : 'Absent de l’annuaire'],
        ['Première détection', dateTimeFr(asset.firstSeenAt)], ['Dernière détection', dateTimeFr(asset.lastSeenAt)],
      ]} />
    </section>
  )
}

/** CFG-52 : logiciels installés relevés par SSH, filtrables. */
function Software({ items }) {
  const [q, setQ] = useState('')
  if (!items?.length) return null
  const shown = items.filter(s => s.name.toLowerCase().includes(q.trim().toLowerCase()))
  return (
    <section className="card" style={{ marginTop: 16 }}>
      <h3 className="card-title">Logiciels installés ({items.length})</h3>
      <input value={q} onChange={e => setQ(e.target.value)} placeholder="Filtrer, ex. openssl" aria-label="Filtrer les logiciels" style={{ maxWidth: 280, marginBottom: 8 }} />
      <div className="table-scroll" style={{ maxHeight: 360 }}>
        <table className="compact">
          <thead><tr><th>Logiciel</th><th>Version</th></tr></thead>
          <tbody>{shown.slice(0, 500).map(s => <tr key={s.name}><td>{s.name}</td><td className="small">{s.version ?? '—'}</td></tr>)}</tbody>
        </table>
      </div>
      {shown.length > 500 && <p className="muted small">500 premiers affichés : affiner le filtre.</p>}
    </section>
  )
}

/** Rapprochement suggéré (CFG-41) : score, critère, et comparaison avec le CI de la CMDB. */
function Match({ data }) {
  const { asset, suggestion, targetCi } = data
  const score = suggestion?.score ?? asset.confidenceScore
  const rule = suggestion?.rule ?? asset.matchRule
  const conflict = suggestion?.conflict ?? asset.conflictReason
  return (
    <section className="card">
      <h3 className="card-title">Rapprochement</h3>
      <p style={{ marginBottom: 10 }}><ScoreMeter score={score} rule={rule} /></p>
      {conflict && <div className="notice warn small" role="alert">{conflict}</div>}
      {!targetCi ? (
        <p className="muted small">Aucun CI ne correspond (numéro de série, adresse MAC, nom d’hôte ou adresse IP) : créer le CI, ou le rapprocher d’un CI existant.</p>
      ) : (
        <>
          <p className="small" style={{ marginBottom: 8 }}>
            {asset.status === 'Rapproché' ? 'CI rapproché' : `CI proposé (${RULE_LABELS[rule] ?? rule})`} :{' '}
            <Link to={'/configuration/' + targetCi.id}><b>{targetCi.reference}</b> {targetCi.title}</Link>
            <span className="muted"> — {targetCi.ciType}, {targetCi.status}</span>
          </p>
          <table className="compact compare">
            <thead><tr><th></th><th>Découvert</th><th>En CMDB</th></tr></thead>
            <tbody>{COMPARED.map(([label, a, c]) => {
              const seen = asset[a] || null, known = targetCi[c] || null
              const differs = seen && known && String(seen).toLowerCase() !== String(known).toLowerCase()
              return (
                <tr key={label} className={differs ? 'diff' : undefined}>
                  <th scope="row">{label}</th><td>{seen ?? '—'}</td><td>{known ?? '—'}{differs && <span className="sr-only"> (différent)</span>}</td>
                </tr>)
            })}</tbody>
          </table>
          <p className="muted small" style={{ marginTop: 8 }}>
            Rapprocher met à jour l’IP, la MAC et le système du CI ; constructeur, modèle et numéro de série ne sont remplis que s’ils manquent.
          </p>
        </>
      )}
    </section>
  )
}

/** Création du CI depuis l'actif : formulaire du registre pré-rempli, mêmes règles (CFG-40). */
function CreateCi({ data, onDone, onCancel }) {
  const [f, setF] = useState(() => ({ ...toForm(ciConfig.fields, data.proposal), status: 'Planifié' }))
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const merge = patch => setF(prev => ({ ...prev, ...patch }))
  const submit = () => {
    setBusy(true)
    api.discovery.staging.reconcile(data.asset.id, { action: 'create', ci: { ...toPayload(ciConfig.fields, f), status: f.status } })
      .then(onDone).catch(e => { setError(e.message); setBusy(false) })
  }
  return (
    <div className="part-form">
      <h4 className="form-heading" style={{ marginTop: 0 }}>Nouveau CI</h4>
      {error && <div className="error" role="alert">{error}</div>}
      <div className="field" style={{ maxWidth: 240 }}>
        <label>Statut</label>
        <select value={f.status} onChange={e => merge({ status: e.target.value })}>
          {['Planifié', 'En service', 'Hors service'].map(s => <option key={s}>{s}</option>)}
        </select>
        <small className="field-hint">En service : propriétaire exigé, et pour un équipement, constructeur, n° de série et emplacement.</small>
      </div>
      <RecordForm fields={ciConfig.fields} form={f} onChange={merge} creating />
      <div className="actions">
        <button className="btn" onClick={submit} disabled={busy || !f.title}>Créer le CI et rapprocher</button>
        <button className="btn ghost" onClick={onCancel}>Annuler</button>
      </div>
    </div>
  )
}

/** Rapprochement vers un CI choisi : serveurs et équipements non retirés du même périmètre. */
function PickCi({ asset, onPick, onCancel }) {
  const [q, setQ] = useState(asset.hostname?.split('.')[0] ?? asset.ipAddress ?? '')
  const [found, setFound] = useState(null)
  const search = () => api.configurationItems.list({ q, perimeter: asset.perimeterId })
    .then(l => setFound(l.filter(c => HARDWARE_TYPES.includes(c.ciType) && c.status !== 'Retiré'))).catch(() => setFound([]))
  useEffect(() => { if (q) search() }, [])
  return (
    <div className="part-form">
      <h4 className="form-heading" style={{ marginTop: 0 }}>Rapprocher d’un autre CI</h4>
      <div className="toolbar">
        <input value={q} onChange={e => setQ(e.target.value)} onKeyDown={e => e.key === 'Enter' && search()}
          placeholder="Nom, référence, n° de série, IP…" aria-label="Rechercher un CI" style={{ maxWidth: 320 }} />
        <button className="btn ghost" onClick={search}>Rechercher</button>
        <button className="btn ghost" onClick={onCancel}>Annuler</button>
      </div>
      {found?.length === 0 && <p className="muted small">Aucun serveur ni équipement ne correspond dans ce périmètre.</p>}
      <ul className="pick-list">
        {found?.map(c => (
          <li key={c.id}>
            <span><b>{c.reference}</b> {c.title} <span className="muted small">— {c.ciType}, {c.status}{c.ipAddress && `, ${c.ipAddress}`}</span></span>
            <button className="btn small" onClick={() => onPick(c)}>Rapprocher</button>
          </li>))}
      </ul>
    </div>
  )
}

/**
 * CMA-08 : propositions de la CMDB assistée sur l'actif, un CI candidat ou un type pour la création, avec leur motif. La
 * décision reste celle du rapprochement (CFG-40) : la suivre, en décider autrement, ou la rejeter.
 */
function AssistedHelp({ asset, onUpdate, onCreate, onError }) {
  const enabled = useFeatures().has('assisted-cmdb')
  const [list, setList] = useState([])
  const load = () => Promise.resolve().then(() => api.discovery.staging.proposals(asset.id)).then(setList).catch(() => setList([]))
  useEffect(() => { if (enabled) load() }, [enabled, asset.id, asset.lastSeenAt])
  if (!enabled || list.length === 0) return null
  const reject = p => api.discovery.staging.rejectProposal(asset.id, p.id).then(load).catch(e => onError(e.message))
  return (
    <div className="suggestions" aria-label="CMDB assistée" style={{ marginBottom: 12 }}>
      <h4 className="form-heading" style={{ marginTop: 0 }}>Proposé par la CMDB assistée</h4>
      {list.map(p => (
        <div key={p.id} style={{ marginBottom: 8 }}>
          {p.field === 'matchedCiId'
            ? <b>Rapprocher de {p.candidate ? <Link to={'/configuration/' + p.candidate.id}>{p.candidate.reference} {p.candidate.title}</Link> : 'un CI'}</b>
            : <b>Créer un CI de type {p.after}</b>}
          <div className="small">{p.reasons}</div>
          <div className="toolbar">
            {p.field === 'matchedCiId' && p.candidate && <button className="btn small" onClick={() => onUpdate(p.candidate)}>Rapprocher de {p.candidate.reference}</button>}
            {p.field === 'ciType' && <button className="btn small" onClick={() => onCreate(p.after)}>Créer en {p.after}</button>}
            <button className="btn ghost small" onClick={() => reject(p)}>Rejeter</button>
          </div>
        </div>))}
    </div>
  )
}

/** Décision du gestionnaire : l'action principale suit la suggestion ; les autres restent à portée. */
function Decision({ data, onChanged, onError }) {
  const [mode, setMode] = useState(null)
  const [type, setType] = useState(null)
  const { asset, suggestion, targetCi } = data
  const nav = useNavigate()
  const act = (p, then) => p.then(then ?? onChanged).catch(e => onError(e.message))
  const update = ci => window.confirm(`Rapprocher cet actif de ${ci.reference} — ${ci.title} ?`)
    && act(api.discovery.staging.reconcile(asset.id, { action: 'update', targetCiId: ci.id }))
  const proposeUpdate = targetCi && suggestion?.action !== 'CreateNew'

  if (asset.status === 'Ignoré') return (
    <section className="card decision">
      <h3 className="card-title">Actif ignoré</h3>
      <p className="muted small">Il reste en mémoire pour ne pas revenir à chaque scan. Le rétablir le remet à rapprocher.</p>
      <div className="actions">
        <button className="btn" onClick={() => act(api.discovery.staging.restore(asset.id))}>Rétablir</button>
      </div>
    </section>
  )
  if (!OPEN.includes(asset.status)) return null
  return (
    <section className="card decision" aria-label="Décision">
      <h3 className="card-title">Votre décision</h3>
      {!mode && <AssistedHelp asset={asset} onUpdate={update} onError={onError} onCreate={t => { setType(t); setMode('create') }} />}
      {!mode && (
        <div className="actions">
          {proposeUpdate && <button className="btn" onClick={() => update(targetCi)}>Rapprocher de {targetCi.reference}</button>}
          <button className={proposeUpdate ? 'btn ghost' : 'btn'} onClick={() => setMode('create')}>Créer le CI</button>
          <button className="btn ghost" onClick={() => setMode('pick')}>Rapprocher d’un autre CI</button>
          <button className="btn ghost" style={{ marginLeft: 'auto' }}
            onClick={() => window.confirm('Ignorer cet actif ? Il ne sera plus proposé à la réconciliation.')
              && act(api.discovery.staging.ignore(asset.id))}>Ignorer</button>
        </div>
      )}
      {mode === 'create' && <CreateCi data={type ? { ...data, proposal: { ...data.proposal, ciType: type } } : data}
        onCancel={() => { setMode(null); setType(null) }}
        onDone={r => nav('/configuration/' + r.configurationItem.id)} />}
      {mode === 'pick' && <PickCi asset={asset} onCancel={() => setMode(null)} onPick={update} />}
    </section>
  )
}

export default function StagingAsset() {
  const { id } = useParams()
  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const nav = useNavigate()
  const load = () => api.discovery.staging.get(id).then(d => { setData(d); setError('') }).catch(e => setError(e.message))
  useEffect(() => { load() }, [id])
  if (!data) return error ? <div className="error">{error}</div> : <p>Chargement…</p>
  const { asset, targetCi } = data
  const remove = () => window.confirm('Supprimer cet actif du sas ? Il reviendra au prochain scan s’il est toujours présent.')
    && api.discovery.staging.remove(asset.id).then(() => nav('/configuration/staging')).catch(e => setError(e.message))

  return (
    <>
      <p style={{ marginBottom: 8 }}><Link to="/configuration/staging">← Sas de staging</Link></p>
      <h1 className="page-title">{asset.hostname ?? asset.ipAddress}</h1>
      <p className="page-sub">
        <StagingBadge status={asset.status} />{' '}
        {asset.unknown && <span className="badge t-wait">Actif inconnu (Shadow IT)</span>}{' '}
        {asset.detectedType}{asset.ipAddress && asset.hostname && ` · ${asset.ipAddress}`}
      </p>
      {error && <div className="error" role="alert">{error}</div>}
      {asset.status === 'Rapproché' && targetCi && (
        <div className="notice ok">
          Rapproché de <Link to={'/configuration/' + targetCi.id}><b>{targetCi.reference}</b> {targetCi.title}</Link>
          {asset.reconciledBy && <> par {asset.reconciledBy}</>} le {dateTimeFr(asset.reconciledAt)}.
          Les prochains scans tiendront son identification technique à jour.
        </div>
      )}

      <Decision data={data} onChanged={load} onError={setError} />
      <div className="row2 stack-mobile" style={{ marginTop: 16 }}>
        <Seen asset={asset} />
        <Match data={data} />
      </div>
      <Software items={data.software} />
      <p style={{ marginTop: 16 }}><button className="btn danger small" onClick={remove}>Supprimer du sas</button></p>
    </>
  )
}
