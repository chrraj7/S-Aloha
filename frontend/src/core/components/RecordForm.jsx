import React from 'react'
import DirectoryPicker from './DirectoryPicker'
import { getUser } from '../../api'

/*
 * Formulaire décrit par une liste de champs (configuration des modules) :
 *   { key, label, type, options, required, default, full, create, readOnly, managerOnly, placeholder, rows }
 * type : text (défaut), textarea, select, date, datetime, number, checkbox,
 *        person (utilisateur/groupe AD : clés `key` et `nameKey`, `typeKey` facultatif).
 * `create: false` masque le champ dans le formulaire de création, `onlyCreate` hors de
 * la création (donnée de proposition, comme l'enregistrement d'origine) ; `boolean` : liste
 * Oui / Non / non renseigné, envoyée en vrai, faux ou null ; `numeric`
 * envoie la valeur d'une liste comme nombre (identifiant, étape…) ; `managerOnly`
 * fige le champ pour qui n'est ni Manager ni Admin (incident majeur, INC-13).
 * `when(form)` n'affiche le champ que si la condition tient (champs du portefeuille pour une
 * application, CFG-43) ; `hint` : aide sous le champ ; `type: 'heading'` : intertitre d'un
 * bloc de champs (sans valeur), soumis au même `when`. `hint` peut dépendre des valeurs (fonction).
 */

/** Champs porteurs d'une valeur (les intertitres n'en ont pas). */
const valued = fields => fields.filter(fd => fd.type !== 'heading')

/** Le champ s'applique-t-il aux valeurs saisies ? */
export const applies = (fd, form) => !fd.when || fd.when(form)

/** Aide du champ : texte, ou fonction des valeurs saisies. */
const hintOf = (fd, form) => (typeof fd.hint === 'function' ? fd.hint(form) : fd.hint)

const pad = n => String(n).padStart(2, '0')
/** ISO UTC → valeur d'un <input type="datetime-local"> (heure locale). */
const toLocalInput = iso => {
  const d = new Date(iso)
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Valeurs de formulaire à partir d'un enregistrement de l'API (ou de valeurs par défaut). */
export function toForm(fields, record = {}) {
  const f = {}
  for (const fd of valued(fields)) {
    const v = record[fd.key] ?? fd.default
    if (fd.type === 'date') f[fd.key] = v ? String(v).slice(0, 10) : ''
    else if (fd.type === 'datetime') f[fd.key] = v ? toLocalInput(v) : ''
    else if (fd.type === 'checkbox') f[fd.key] = !!v
    else if (fd.boolean) f[fd.key] = v === true ? 'true' : v === false ? 'false' : ''
    else f[fd.key] = v ?? ''
    if (fd.type === 'person') {
      f[fd.nameKey] = record[fd.nameKey] ?? ''
      if (fd.typeKey) f[fd.typeKey] = record[fd.typeKey] ?? ''
    }
  }
  return f
}

/** Corps de requête à partir des valeurs de formulaire. */
export function toPayload(fields, form) {
  const p = {}
  for (const fd of valued(fields)) {
    const v = form[fd.key]
    if (fd.type === 'date' || fd.type === 'datetime') p[fd.key] = v ? new Date(v).toISOString() : null
    else if (fd.type === 'number') p[fd.key] = v === '' || v === null ? null : Number(v)
    else if (fd.type === 'select') p[fd.key] = v === '' ? null : fd.numeric ? Number(v) : fd.boolean ? v === 'true' : v
    else p[fd.key] = v
    if (fd.type === 'person') {
      p[fd.nameKey] = form[fd.nameKey] || null
      if (fd.typeKey) p[fd.typeKey] = form[fd.typeKey] || null
      p[fd.key] = v || null
    }
  }
  return p
}

function PersonInput({ fd, form, onChange, disabled }) {
  const id = form[fd.key]
  const set = (entry) => onChange({
    [fd.key]: entry?.id ?? '', [fd.nameKey]: entry?.displayName ?? '',
    ...(fd.typeKey ? { [fd.typeKey]: entry?.type ?? '' } : {})
  })
  return (
    <>
      {id && (
        <span className="chip">
          {form[fd.typeKey] === 'Group' ? '👥' : '👤'} {form[fd.nameKey] || id}
          {!disabled && <button type="button" onClick={() => set(null)} title="Retirer">×</button>}
        </span>
      )}
      {!disabled && <DirectoryPicker onPick={set} placeholder={id ? 'Remplacer…' : 'Rechercher dans l\'annuaire…'} />}
    </>
  )
}

function Field({ fd, form, onChange, disabled }) {
  const v = form[fd.key]
  const set = e => onChange({ [fd.key]: fd.type === 'checkbox' ? e.target.checked : e.target.value })
  let input
  switch (fd.type) {
    case 'textarea':
      input = <textarea rows={fd.rows ?? 4} value={v} onChange={set} disabled={disabled} placeholder={fd.placeholder} />
      break
    case 'select':
      input = (
        <select value={v} onChange={set} disabled={disabled}>
          {!fd.required && <option value="">—</option>}
          {fd.keepUnknown && v !== '' && !fd.options.some(o => (typeof o === 'object' ? o.value : o) === v)
            && <option value={v}>{fd.keepUnknown(v)}</option>}
          {fd.options.map(o => typeof o === 'object'
            ? <option key={o.value} value={o.value}>{o.label}</option>
            : <option key={o}>{o}</option>)}
        </select>)
      break
    case 'date': input = <input type="date" value={v} onChange={set} disabled={disabled} />; break
    case 'time': input = <input type="time" value={v} onChange={set} disabled={disabled} />; break
    case 'datetime': input = <input type="datetime-local" value={v} onChange={set} disabled={disabled} />; break
    case 'number': input = <input type="number" step="any" value={v} onChange={set} disabled={disabled} />; break
    case 'checkbox':
      input = (
        <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontWeight: 400 }}>
          <input type="checkbox" checked={v} onChange={set} disabled={disabled} style={{ width: 'auto' }} /> {fd.checkboxLabel}
        </label>)
      break
    case 'person': input = <PersonInput fd={fd} form={form} onChange={onChange} disabled={disabled} />; break
    default: input = <input value={v} onChange={set} disabled={disabled} placeholder={fd.placeholder} />
  }
  return (
    <div className={'field' + (fd.full || fd.type === 'textarea' ? ' full' : '')}>
      <label>{fd.label}{fd.required && ' *'}</label>
      {input}
      {hintOf(fd, form) && <small className="field-hint">{hintOf(fd, form)}</small>}
    </div>
  )
}

/**
 * Grille de champs ; `onChange` reçoit un objet partiel à fusionner dans `form`.
 * `isManager` : rôle de gestionnaire sur le périmètre de l'enregistrement (PER-06), calculé
 * par la fiche ; sans lui, le rôle AD.
 */
export default function RecordForm({ fields, form, onChange, disabled, creating, isManager: managerHere }) {
  const isManager = managerHere ?? ['Admin', 'Manager'].includes(getUser()?.role)
  return (
    <div className="form-grid">
      {fields.filter(fd => !(creating && fd.create === false) && !(!creating && fd.onlyCreate) && applies(fd, form)).map(fd =>
        fd.type === 'heading'
          ? <h4 key={fd.key} className="form-heading">{fd.label}{fd.hint && <small>{fd.hint}</small>}</h4>
          : <Field key={fd.key} fd={fd} form={form} onChange={onChange} disabled={disabled || fd.readOnly || (fd.managerOnly && !isManager)} />)}
    </div>
  )
}
