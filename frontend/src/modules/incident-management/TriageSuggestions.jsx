import React, { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api'
import { useFeatures } from '../../core/features'

const LABELS = { category: 'Catégorie', serviceId: 'Service', owner: 'Groupe responsable', impact: 'Impact' }

/**
 * TRI-02 : suggestions du tri assisté à la saisie d'un incident, sous les cas similaires : dès 4 caractères de
 * titre, après une courte pause de frappe (comme RAG-11). Une ligne par champ, avec son motif ; « Appliquer »
 * remplit le champ du formulaire (TRI-09), rien ne s'applique seul. Les incidents ouverts proches s'affichent
 * s'il y en a. Le conteneur arrêté ou lent, l'encadré ne s'affiche pas et la saisie continue (OPT-16).
 */
export default function TriageSuggestions({ form, merge, delay = 400 }) {
  const enabled = useFeatures().has('assisted-ticket-triage')
  const [answer, setAnswer] = useState({ suggestions: [], related: [] })
  const title = (form.title ?? '').trim()
  const draft = { title, description: form.description, serviceId: form.serviceId ? Number(form.serviceId) : null,
    category: form.category || null, impact: form.impact || null, ownerType: form.ownerType || null, ownerId: form.ownerId || null }
  const key = JSON.stringify(draft)
  useEffect(() => {
    if (!enabled || title.length < 4) { setAnswer({ suggestions: [], related: [] }); return }
    const timer = setTimeout(() => {
      Promise.resolve().then(() => api.triage.suggest(draft))
        .then(a => setAnswer({ suggestions: a?.suggestions ?? [], related: a?.related ?? [] }))
        .catch(() => setAnswer({ suggestions: [], related: [] }))
    }, delay)
    return () => clearTimeout(timer)
  }, [enabled, key, delay])

  // Une suggestion déjà portée par le formulaire n'est plus proposée.
  const current = s => (s.field === 'owner' ? form.ownerType === 'Group' && form.ownerId === s.value
    : String(form[s.field] ?? '') === String(s.value))
  const suggestions = answer.suggestions.filter(s => !current(s))
  const apply = s => merge(s.field === 'owner'
    ? { ownerId: s.value, ownerType: 'Group', ownerDisplayName: s.label ?? s.value }
    : { [s.field]: s.value })

  if (!enabled || (suggestions.length === 0 && answer.related.length === 0)) return null
  return (
    <div className="card suggestions" style={{ margin: '0 0 14px' }} aria-label="Suggestions du tri assisté">
      {suggestions.length > 0 && <>
        <p className="small" style={{ marginBottom: 6 }}><b>Suggestions</b> du tri assisté, à appliquer ou non :</p>
        <ul className="small">{suggestions.map(s => (
          <li key={s.field} style={{ marginBottom: 4 }}>
            <b>{LABELS[s.field] ?? s.field} : {s.label ?? s.value}</b> — {s.reasons}{' '}
            <button type="button" className="btn ghost small" onClick={() => apply(s)}>Appliquer</button>
          </li>))}
        </ul>
      </>}
      {answer.related.length > 0 && <>
        <p className="small" style={{ margin: '6px 0' }}><b>Incidents ouverts proches</b> : la même panne, peut-être.</p>
        <ul className="small">{answer.related.map(r => (
          <li key={r.id}><Link to={`/incidents/${r.id}`}><b>{r.reference}</b></Link> {r.title} <span className="badge">{r.status}</span></li>))}
        </ul>
      </>}
    </div>
  )
}
