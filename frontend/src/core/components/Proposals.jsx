import React, { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { toForm, toPayload } from './RecordForm'
import { hrefOf } from '../itemTypes'

/** Champ de la proposition (nom de l'API) → champ du formulaire : le responsable est un champ « personne ». */
const FORM_KEY = { owner: 'ownerId' }

/** Valeur lisible : libellé d'une option (service du catalogue) plutôt que son identifiant. */
const display = (field, value) => {
  const option = (field?.options ?? []).find(o => String(o?.value ?? o) === String(value))
  return option?.label ?? option ?? value
}

/**
 * OPT-15 : propositions ouvertes d'une fonctionnalité optionnelle sur une fiche, une ligne par champ, avec leur
 * motif et leurs sources visibles (TRI-08). L'utilisateur accepte, corrige (autre valeur) ou rejette ; accepter
 * passe par la modification de la pratique, toutes ses règles comprises. Un groupe responsable accepté est une
 * escalade (INC-17), au motif prérempli (TRI-09). Une proposition périmée ne s'accepte plus : elle se recalcule.
 */
export default function Proposals({ config, record, reload, title = 'Suggestions', reasonPrefix = '' }) {
  const [proposals, setProposals] = useState([])
  const [editing, setEditing] = useState(null)   // { id, value, reason }
  const [error, setError] = useState('')
  const load = () => Promise.resolve().then(() => config.api.proposals(record.id)).then(setProposals).catch(() => setProposals([]))
  useEffect(() => { load() }, [record.id, record.updatedAt])

  const fieldOf = p => config.fields.find(f => f.key === (FORM_KEY[p.field] ?? p.field))
  const done = () => { setEditing(null); setError(''); reload(); load() }

  const apply = (p, value, reason) => {
    const form = { ...toForm(config.fields, record), status: record.status }
    if (p.field === 'owner') {
      Object.assign(form, { ownerId: value, ownerType: 'Group', ownerDisplayName: value })
      if (reason !== undefined) form.escalationReason = reason
    } else form[FORM_KEY[p.field] ?? p.field] = value
    return config.api.applyProposal(record.id, p.id, { ...(config.keep?.(record) ?? {}), ...toPayload(config.fields, form), status: record.status })
      .then(done).catch(e => setError(e.message))
  }
  const reject = p => config.api.rejectProposal(record.id, p.id).then(done).catch(e => setError(e.message))
  // TRI-09 : un groupe se confie par une escalade, dont le motif se préremplit et se modifie.
  const start = (p, value = p.after) => setEditing({ id: p.id, value, reason: p.field === 'owner' ? `${reasonPrefix}${p.reasons}` : undefined })

  if (proposals.length === 0) return null
  return (
    <section className="card suggestions" style={{ marginTop: 16 }} aria-label={title}>
      <h3 className="card-title">{title}</h3>
      {error && <div className="error" role="alert">{error}</div>}
      <ul className="proposals">
        {proposals.map(p => {
          const field = fieldOf(p)
          const label = field?.label ?? p.field
          const open = editing?.id === p.id
          return (
            <li key={p.id} style={{ marginBottom: 10 }}>
              {p.field === 'merge'
                ? <b>Fusion avec {p.cited?.[0] ? <Link to={hrefOf(p.cited[0].type, p.cited[0].id)}>{p.cited[0].reference}</Link> : 'un autre CI'}</b>
                : <><b>{label} : {display(field, p.after)}</b>{p.before ? <span className="small"> (actuellement {display(field, p.before)})</span> : null}</>}
              <div className="small">{p.reasons}{p.cited?.length > 0 && <> — {p.cited.map((c, i) => <React.Fragment key={c.type + c.id}>
                {i > 0 && ', '}<Link to={hrefOf(c.type, c.id)}>{c.reference}</Link></React.Fragment>)}</>}</div>
              {p.stale
                ? <div className="small" role="note">Périmée : la fiche a changé depuis ; elle se recalcule.</div>
                : p.field === 'merge'
                  // CMA-05 : une fusion se décide dans la file de la CMDB assistée, où l'on choisit le CI conservé.
                  ? <div className="toolbar"><Link className="btn small" to="/configuration/assisted">Décider la fusion</Link>
                      <button className="btn ghost small" onClick={() => reject(p)}>Rejeter</button></div>
                  : !open && <div className="toolbar">
                    <button className="btn small" onClick={() => (p.field === 'owner' ? start(p) : apply(p, p.after))}>Appliquer</button>
                    <button className="btn ghost small" onClick={() => start(p, '')}>Corriger</button>
                    <button className="btn ghost small" onClick={() => reject(p)}>Rejeter</button>
                  </div>}
              {open && (
                <div className="form-grid" style={{ marginTop: 6 }}>
                  <div className="field"><label htmlFor={`proposal-${p.id}`}>{label}</label>
                    {field?.options
                      ? <select id={`proposal-${p.id}`} value={editing.value} onChange={e => setEditing({ ...editing, value: e.target.value })}>
                          <option value="">—</option>
                          {field.options.map(o => <option key={String(o?.value ?? o)} value={o?.value ?? o}>{o?.label ?? o}</option>)}
                        </select>
                      : <input id={`proposal-${p.id}`} value={editing.value} onChange={e => setEditing({ ...editing, value: e.target.value })} />}
                  </div>
                  {editing.reason !== undefined && (
                    <div className="field"><label htmlFor={`proposal-reason-${p.id}`}>Motif d’escalade</label>
                      <textarea id={`proposal-reason-${p.id}`} rows={2} value={editing.reason} onChange={e => setEditing({ ...editing, reason: e.target.value })} /></div>)}
                  <div className="toolbar">
                    <button className="btn small" onClick={() => apply(p, editing.value, editing.reason)} disabled={!String(editing.value).trim()}>Enregistrer</button>
                    <button className="btn ghost small" onClick={() => setEditing(null)}>Annuler</button>
                  </div>
                </div>)}
            </li>)
        })}
      </ul>
    </section>
  )
}
