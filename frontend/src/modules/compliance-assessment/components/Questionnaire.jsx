import React, { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../../api'
import { hrefOf } from '../../../core/itemTypes'
import { dateTimeFr } from '../../../core/fields'
import { SCORE_LABELS } from '../config'

const NA = 'na'
const valueOf = (r) => (r.notApplicable ? NA : r.score ?? '')

/**
 * NIS-08 : preuves d'une réponse — enregistrements désignés par leur référence
 * (changement, article, CI…) ; pièces jointes à venir avec SOC-25.
 */
function Evidence({ assessmentId, req, readOnly }) {
  const [items, setItems] = useState(req.evidence ?? [])
  const [reference, setReference] = useState('')
  const [error, setError] = useState('')
  const add = () => api.assessments.addEvidence(assessmentId, req.id, reference.trim())
    .then(x => { setItems(l => [...l, x]); setReference(''); setError('') }).catch(e => setError(e.message))
  const remove = x => api.assessments.removeEvidence(assessmentId, x.id)
    .then(() => setItems(l => l.filter(y => y.id !== x.id))).catch(e => setError(e.message))
  if (readOnly && items.length === 0) return null
  return (
    <div style={{ marginTop: 6 }}>
      {items.map(x => x.hidden
        // PER-08 : preuve d'un autre périmètre, comptée mais ni décrite, ni liée, ni retirable.
        ? <span className="chip muted" key={x.id} style={{ fontStyle: 'italic' }}>{x.title}</span>
        : (
        <span className="chip" key={x.id} title={x.title}>
          <Link to={hrefOf(x.targetType, x.targetId)}>{x.reference}</Link>
          {!readOnly && <button onClick={() => remove(x)} title="Retirer" aria-label={`Retirer la preuve ${x.reference}`}>×</button>}
        </span>))}
      {!readOnly && (
        <div style={{ display: 'flex', gap: 6, marginTop: 4 }}>
          <input value={reference} onChange={e => setReference(e.target.value)} placeholder="Preuve : CHG-…, KB-…, CI-…"
            aria-label={`Preuve ${req.code}`} onKeyDown={e => e.key === 'Enter' && reference.trim() && add()} />
          <button className="btn ghost small" onClick={add} disabled={!reference.trim()}>Ajouter</button>
        </div>)}
      {error && <div className="error small" style={{ margin: '4px 0 0' }}>{error}</div>}
    </div>
  )
}

/** Une exigence : identifiant, mesures ISO, texte importé, réponse, justification et preuves. */
function RequirementRow({ assessmentId, req, readOnly, onSaved }) {
  const [value, setValue] = useState(valueOf(req))
  const [justification, setJustification] = useState(req.justification)
  const [state, setState] = useState({ error: '', saved: req.updatedBy ? `${req.updatedBy}, ${dateTimeFr(req.updatedAt)}` : '' })

  const save = (v = value, j = justification) => {
    if (v === '' && !j) return
    api.assessments.answer(assessmentId, req.id, {
      score: v === NA || v === '' ? null : Number(v), notApplicable: v === NA, justification: j
    }).then(res => { setState({ error: '', saved: `${res.updatedBy}, ${dateTimeFr(res.updatedAt)}` }); onSaved(res) })
      .catch(e => setState(s => ({ ...s, error: e.message })))
  }

  return (
    <tr>
      <td style={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>
        <b>{req.code}</b>
        {req.isoControls && <div className="muted small" title="Mesures ISO 27002:2022 correspondantes, selon la table de correspondance de l'ANSSI">
          ISO {req.isoControls.split(' ').map(c => c.replace('27002:2022-', '')).join(', ')}</div>}
      </td>
      <td style={{ verticalAlign: 'top' }}>
        {req.text
          ? <p style={{ whiteSpace: 'pre-line', margin: 0 }}>{req.text}</p>
          : <p className="muted small" style={{ margin: 0 }}>Texte de l'exigence non importé : un administrateur l'importe depuis Paramétrage → Référentiel NIS 2.</p>}
        {state.error && <div className="error" style={{ marginTop: 6, marginBottom: 0 }}>{state.error}</div>}
      </td>
      <td style={{ verticalAlign: 'top', width: 250 }}>
        <select value={value} disabled={readOnly} aria-label={`Réponse ${req.code}`}
          onChange={e => { setValue(e.target.value); save(e.target.value) }}>
          <option value="">— Sans réponse —</option>
          {SCORE_LABELS.map((l, i) => <option key={i} value={i}>{l}</option>)}
          <option value={NA}>Non applicable</option>
        </select>
        <textarea rows={2} style={{ marginTop: 6 }} value={justification} disabled={readOnly}
          placeholder={value === NA ? 'Justification obligatoire' : 'Constat, preuve…'}
          aria-label={`Justification ${req.code}`}
          onChange={e => setJustification(e.target.value)} onBlur={() => justification !== req.justification && save()} />
        {state.saved && <div className="muted small">{state.saved}</div>}
        <Evidence assessmentId={assessmentId} req={req} readOnly={readOnly} />
      </td>
    </tr>
  )
}

/**
 * Questionnaire d'une évaluation, par objectif de sécurité puis thématique
 * (NIS-01, NIS-04). Chaque réponse est enregistrée aussitôt saisie.
 */
export default function Questionnaire({ assessment, onChanged }) {
  const [q, setQ] = useState(null)
  const [error, setError] = useState('')
  const [open, setOpen] = useState(null)
  const readOnly = assessment.status === 'Validée'
  const load = () => api.assessments.questionnaire(assessment.id).then(setQ).catch(e => setError(e.message))
  useEffect(() => { load() }, [assessment.id])
  if (error) return <div className="error">{error}</div>
  if (!q) return <p>Chargement…</p>

  const all = q.objectives.flatMap(o => o.themes.flatMap(t => t.requirements))
  const answered = (rs) => rs.filter(r => r.notApplicable || r.score !== null).length
  const onSaved = (res) => {
    // Compteurs à jour sans recharger le questionnaire (saisie en cours ailleurs).
    setQ(prev => ({ ...prev, objectives: prev.objectives.map(o => ({ ...o, themes: o.themes.map(t => ({
      ...t, requirements: t.requirements.map(r => r.id === res.requirementId ? { ...r, ...res } : r) })) })) }))
    if (res.assessmentStatus !== assessment.status) onChanged()
  }

  return (
    <div className="card">
      <p className="muted small" style={{ marginBottom: 12 }}>
        {answered(all)} réponse(s) sur {all.length} exigences applicables à une {assessment.entityCategory.toLowerCase()} · {q.referentialVersion}
        {readOnly && ' · Évaluation validée : lecture seule.'}
      </p>
      {q.objectives.map(o => {
        const rs = o.themes.flatMap(t => t.requirements)
        const isOpen = open === o.id
        return (
          <div key={o.id} style={{ borderTop: '1px solid var(--line)' }}>
            <button className="obj-toggle" aria-expanded={isOpen} onClick={() => setOpen(isOpen ? null : o.id)}>
              <span><b>{o.id}. {o.title}</b> <span className="muted small">· {o.pillar}</span></span>
              <span className="muted small">{answered(rs)}/{rs.length}</span>
            </button>
            {isOpen && o.themes.map(t => (
              <div key={t.theme} style={{ marginBottom: 14 }}>
                <h4 style={{ margin: '6px 0' }}>{t.theme}</h4>
                <table>
                  <thead><tr><th>Exigence</th><th>Énoncé</th><th>Réponse</th></tr></thead>
                  <tbody>{t.requirements.map(r =>
                    <RequirementRow key={r.id} assessmentId={assessment.id} req={r} readOnly={readOnly} onSaved={onSaved} />)}</tbody>
                </table>
              </div>
            ))}
          </div>
        )
      })}
    </div>
  )
}
