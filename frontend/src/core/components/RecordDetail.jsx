import React, { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { getUser } from '../../api'
import RecordForm, { toForm, toPayload } from './RecordForm'
import StatusBadge from './StatusBadge'
import LinkedItems from './LinkedItems'
import { dateFr } from '../fields'
import useTransitions from '../useTransitions'
import StatusField, { forceOptions } from './StatusField'
import History, { labelsOf } from './History'
import Comments from './Comments'
import Attachments from './Attachments'
import DeleteDialog from './DeleteDialog'
import PerimeterSelect from './PerimeterSelect'
import usePerimeters, { perimeterName } from '../usePerimeters'

/** Édition : statut + champs ; recréé (clé) à chaque rechargement de l'enregistrement. */
/** SOC-15 : statuts de clôture des pratiques (avec ceux sans transition sortante). */
const CLOSED = ['Clos', 'Close', 'Rejeté', 'Rejetée', 'Annulée', 'Réalisée', 'Abandonnée', 'Retiré']

function Editor({ config, record, onSaved }) {
  const role = getUser()?.role
  const perimeters = usePerimeters()
  // PER-06 : gestionnaire du périmètre de l'enregistrement (ADR-0014 D4 : le rôle par
  // périmètre prime sur le rôle AD) ; tant que la liste n'est pas chargée, le rôle AD.
  const here = perimeters.find(p => p.id === record.perimeterId)?.role
  const isManager = ['Admin', 'Manager'].includes(here ?? role)
  const [perimeterId, setPerimeterId] = useState(record.perimeterId)
  // PER-07 : déplacer vers un périmètre dont on est aussi gestionnaire.
  const targets = perimeters.filter(p => ['Admin', 'Manager'].includes(p.role))
  const readOnly = config.managerOnly && !isManager
  const [f, setF] = useState(() => ({ ...toForm(config.fields, record), status: record.status }))
  const [msg, setMsg] = useState(null)
  const nav = useNavigate()
  const graph = useTransitions(config.api.transitions)
  const [reason, setReason] = useState('')
  const [deleting, setDeleting] = useState(null)   // aperçu SOC-14 de la suppression
  const merge = patch => setF(prev => ({ ...prev, ...patch }))
  const force = forceOptions(graph, record.status, f.status, reason)
  // SOC-15 : un enregistrement terminé se modifie par un gestionnaire ; un User peut seulement
  // le rouvrir (statut), si la pratique le permet.
  const finished = !!graph && (CLOSED.includes(record.status) || (graph[record.status] ?? []).length === 0)
  const locked = finished && !isManager

  // `config.keep(record)` : champs que le formulaire ne montre pas mais que la
  // modification doit renvoyer tels quels (business case d'un projet, édité dans
  // son onglet) — sans quoi l'API les effacerait.
  const save = () => config.api.update(record.id, { ...(config.keep?.(record) ?? {}), ...toPayload(config.fields, f), status: f.status,
    perimeterId: perimeterId !== record.perimeterId ? perimeterId : null }, force)
    .then(saved => {
      // L'API peut ramener le statut en arrière (article retouché, changement modifié).
      setMsg({ ok: true, text: saved.status === f.status ? 'Modifications enregistrées.'
        : `Modifications enregistrées. Statut repassé à « ${saved.status} » : une nouvelle validation d'un gestionnaire est requise.` })
      onSaved()
    })
    .catch(e => setMsg({ ok: false, text: e.message }))
  // SOC-14 : la confirmation rappelle la référence et ce qui part avec la suppression.
  const askRemove = () => config.api.deletion(record.id).then(setDeleting).catch(e => setMsg({ ok: false, text: e.message }))
  const remove = () => config.api.remove(record.id).then(() => nav(config.basePath))

  return (
    <div className="card">
      {msg && <div className="error" style={msg.ok ? { background: 'var(--blue-soft)', color: 'var(--navy)' } : undefined}>{msg.text}</div>}
      {locked && <div className="notice" role="note">
        Enregistrement {record.status.toLowerCase()} : seul un gestionnaire le modifie.
        {(graph[record.status] ?? []).length > 0 ? ' Pour le reprendre, le rouvrir (statut).' : ''}
      </div>}
      <StatusField statuses={config.statuses} current={record.status} value={f.status}
        onChange={status => merge({ status })} graph={graph} isManager={isManager} isAdmin={role === 'Admin'}
        disabled={readOnly} reason={reason} onReason={setReason} />
      {isManager && targets.some(p => p.id === record.perimeterId) &&
        <PerimeterSelect perimeters={targets} value={perimeterId} onChange={setPerimeterId} disabled={readOnly} />}
      {/* Un champ porteur de show(record) ne s'affiche que s'il s'applique ; sa valeur repart telle quelle. */}
      <RecordForm fields={config.fields.filter(x => !x.show || x.show(record))} form={f} onChange={merge} disabled={readOnly || (locked && f.status === record.status)} />
      <div style={{ display: 'flex', gap: 10 }}>
        {!readOnly && <button className="btn" onClick={save} disabled={!f.title || (force && !reason.trim()) || (locked && f.status === record.status)}>Enregistrer</button>}
        {role === 'Admin' && <button className="btn danger" style={{ marginLeft: 'auto' }} onClick={askRemove}>Supprimer</button>}
      </div>
      {deleting && <DeleteDialog reference={deleting.reference} title={deleting.title} impacts={deleting.impacts}
        onConfirm={remove} onClose={() => setDeleting(null)} />}
    </div>
  )
}

/**
 * Fiche d'un enregistrement : en-tête (référence, statut, badges du module),
 * puis les onglets communs, dans l'ordre de docs/reference/frontend.md
 * § Navigation cible : Informations → onglet propre à la pratique → Liens.
 *
 * - `children(record, reload)` : complément de l'onglet Informations (action
 *   liée à l'enregistrement, ex. « Ouvrir un problème lié ») ;
 * - `tab = { label, render(record, reload) }` : l'onglet propre à la pratique
 *   (relations et impact d'un CI, accords d'un service, conflits d'un changement) ;
 *   une liste `[{ label, render }, …]` quand la pratique en a plusieurs (évaluation
 *   NIS 2 : Questionnaire, Synthèse), dans l'ordre donné.
 */
export default function RecordDetail({ config, children, tab }) {
  const { id } = useParams()
  const [r, setR] = useState(null)
  const [error, setError] = useState('')
  const [version, setVersion] = useState(0)
  const [current, setCurrent] = useState('infos')
  const perimeters = usePerimeters()
  const load = () => config.api.get(id).then(x => { setR(x); setVersion(v => v + 1) }).catch(e => setError(e.message))
  // Une autre fiche (lien suivi depuis l'onglet Liens) s'ouvre sur ses Informations.
  useEffect(() => { setCurrent('infos'); load() }, [id])
  if (error) return <div className="error">{error}</div>
  if (!r) return <p>Chargement…</p>

  const meta = [perimeters.length > 1 && `Périmètre : ${perimeterName(perimeters, r.perimeterId)}`,
    ...(config.meta?.(r) ?? [])].filter(Boolean)
  // `tab` : objet, liste, ou fonction de l'enregistrement (onglets d'un projet selon ses composants).
  const practice = tab ? [].concat(typeof tab === 'function' ? tab(r) : tab) : []
  const tabs = [['infos', 'Informations'], ...practice.map((t, i) => ['practice-' + i, t.label]),
    config.linkType && ['links', 'Liens'], config.linkType && ['comments', 'Commentaires'], config.linkType && ['attachments', 'Pièces jointes'],
    config.linkType && ['history', 'Historique']].filter(Boolean)
  return (
    <>
      <p style={{ marginBottom: 8 }}><Link to={config.basePath}>← {config.title}</Link></p>
      <h1 className="page-title">{r.reference} — {r.title}</h1>
      <p className="page-sub">
        <StatusBadge statuses={config.statuses} status={r.status} />{' '}
        {config.badges?.(r)}{' '}
        Créé par {r.createdByDisplayName} le {dateFr(r.createdAt)}
        {meta.map(m => <React.Fragment key={m}> · {m}</React.Fragment>)}
      </p>
      {config.help && <p style={{ color: 'var(--muted)', fontSize: 13, marginTop: -16, marginBottom: 20 }}>{config.help}</p>}

      <div className="tabs" role="tablist">
        {tabs.map(([k, l]) => (
          <button key={k} role="tab" aria-selected={current === k}
            className={current === k ? 'on' : ''} onClick={() => setCurrent(k)}>{l}</button>))}
      </div>

      {current === 'infos' && <>
        <Editor key={version} config={config} record={r} onSaved={load} />
        {children?.(r, load)}
      </>}
      {practice.map((t, i) => current === 'practice-' + i && <React.Fragment key={i}>{t.render(r, load)}</React.Fragment>)}
      {current === 'links' && <LinkedItems type={config.linkType} id={r.id} hint={config.linkHint} refreshKey={version} />}
      {current === 'comments' && <Comments type={config.linkType} id={r.id} />}
      {current === 'attachments' && <Attachments type={config.linkType} id={r.id} perimeterId={r.perimeterId} />}
      {current === 'history' && <History type={config.linkType} id={r.id} labels={labelsOf(config.fields)} refreshKey={version} />}
    </>
  )
}
