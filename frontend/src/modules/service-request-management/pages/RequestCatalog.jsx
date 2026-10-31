import React, { useEffect, useState } from 'react'
import { api } from '../../../api'
import useManagesPerimeter from '../../../core/useManagesPerimeter'
import useServices from '../../../core/useServices'
import ServiceSelect from '../../../core/components/ServiceSelect'

const EMPTY = { name: '', description: '', serviceId: '', requiresApproval: true, targetHours: '', fulfillmentGroup: '', active: true, tasks: [] }

/** REQ-24 : tâches prédéfinies du modèle, recopiées sur la demande à l'approbation. */
export function TaskTemplates({ tasks, onChange }) {
  const set = (i, patch) => onChange(tasks.map((t, j) => (j === i ? { ...t, ...patch } : t)))
  return (
    <div className="field full"><label>Tâches d’exécution prédéfinies</label>
      {tasks.map((t, i) => (
        <div className="toolbar" key={i} style={{ marginBottom: 6 }}>
          <input value={t.title} onChange={e => set(i, { title: e.target.value })} placeholder={`Tâche ${i + 1}, ex. : préparer le poste`}
            aria-label={`Titre de la tâche ${i + 1}`} style={{ flex: 2 }} />
          <input value={t.ownerId ?? ''} onChange={e => set(i, { ownerId: e.target.value, ownerType: 'User', ownerDisplayName: e.target.value })}
            placeholder="Responsable (compte AD), sinon celui de la demande" aria-label={`Responsable de la tâche ${i + 1}`} style={{ flex: 1 }} />
          <button type="button" className="btn ghost small" onClick={() => onChange(tasks.filter((_, j) => j !== i))}>Retirer</button>
        </div>))}
      <button type="button" className="btn ghost small" onClick={() => onChange([...tasks, { title: '', ownerId: '' }])}>+ Ajouter une tâche</button>
      <small className="field-hint">Créées sur la demande à son approbation ; la demande n’est satisfaite que ses tâches terminées ou annulées.</small>
    </div>
  )
}

/**
 * REQ-20 : catalogue de demandes. Chaque modèle donne l'objet demandé, le service, l'approbation
 * requise (REQ-21), le délai cible en heures de service (REQ-22) et le groupe d'exécution
 * (REQ-23). Tenu par les gestionnaires ; un modèle se désactive plutôt que de se supprimer.
 */
export default function RequestCatalog() {
  const [items, setItems] = useState(null)
  const [f, setF] = useState(null)   // modèle en édition (id absent : nouveau)
  const [error, setError] = useState('')
  const services = useServices() ?? []
  const manager = useManagesPerimeter()
  const load = () => api.requestCatalog.list(true).then(setItems).catch(e => setError(e.message))
  useEffect(() => { load() }, [])

  const set = k => e => setF({ ...f, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const save = () => {
    const dto = { ...f, serviceId: f.serviceId ? Number(f.serviceId) : null, targetHours: f.targetHours ? Number(f.targetHours) : null }
    return (f.id ? api.requestCatalog.update(f.id, dto) : api.requestCatalog.create(dto))
      .then(() => { setF(null); setError(''); load() }).catch(e => setError(e.message))
  }
  const serviceName = id => services.find(s => s.id === id)?.title ?? '—'

  return (
    <>
      <h1 className="page-title">Catalogue de demandes</h1>
      <p className="page-sub">Modèles de demande proposés aux utilisateurs : objet, service, approbation, délai cible et groupe qui les traite.</p>
      {error && <div className="error">{error}</div>}
      {manager && !f && <button className="btn" style={{ marginBottom: 16 }} onClick={() => setF(EMPTY)}>+ Nouveau modèle</button>}
      {f && (
        <div className="card" style={{ marginBottom: 16 }}>
          <div className="row2">
            <div className="field"><label htmlFor="cat-name">Objet demandé *</label>
              <input id="cat-name" value={f.name} onChange={set('name')} placeholder="Accès VPN, poste portable…" /></div>
            <div className="field"><label>Service</label>
              <ServiceSelect services={services.filter(s => s.status !== 'Retiré')} value={f.serviceId ?? ''} onChange={set('serviceId')} /></div>
          </div>
          <div className="field"><label htmlFor="cat-desc">Description</label>
            <textarea id="cat-desc" rows={2} value={f.description} onChange={set('description')} /></div>
          <div className="row3">
            <div className="field"><label htmlFor="cat-hours">Délai cible (heures de service)</label>
              <input id="cat-hours" type="number" min="1" value={f.targetHours ?? ''} onChange={set('targetHours')} /></div>
            <div className="field"><label htmlFor="cat-group">Groupe d'exécution (AD)</label>
              <input id="cat-group" value={f.fulfillmentGroup ?? ''} onChange={set('fulfillmentGroup')} placeholder="GRP-SUPPORT…" /></div>
            <div className="field">
              <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                <input type="checkbox" style={{ width: 'auto' }} checked={f.requiresApproval} onChange={set('requiresApproval')} /> Approbation requise</label>
              <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                <input type="checkbox" style={{ width: 'auto' }} checked={f.active} onChange={set('active')} /> Proposé (actif)</label>
            </div>
          </div>
          <TaskTemplates tasks={f.tasks ?? []} onChange={tasks => setF({ ...f, tasks })} />
          <button className="btn" onClick={save} disabled={!f.name.trim() || (f.tasks ?? []).some(t => !t.title.trim())}>Enregistrer</button>{' '}
          <button className="btn ghost" onClick={() => setF(null)}>Annuler</button>
        </div>
      )}
      {!items ? <p>Chargement…</p> : (
        <table>
          <thead><tr><th>Objet demandé</th><th>Service</th><th>Approbation</th><th>Délai cible</th><th>Groupe d'exécution</th><th>Statut</th>{manager && <th />}</tr></thead>
          <tbody>
            {items.map(i => (
              <tr key={i.id} style={i.active ? {} : { color: 'var(--muted)' }}>
                <td><b>{i.name}</b>{i.description && <div className="muted small">{i.description}</div>}</td>
                <td>{serviceName(i.serviceId)}</td>
                <td>{i.requiresApproval ? 'Requise' : 'Pré-approuvé'}</td>
                <td>{i.targetHours ? `${i.targetHours} h` : '—'}</td>
                <td>{i.fulfillmentGroup || '—'}</td>
                <td>{i.active ? 'Proposé' : 'Désactivé'}</td>
                {manager && <td><button className="btn ghost small" onClick={() => setF({ ...EMPTY, ...i, serviceId: i.serviceId ?? '', targetHours: i.targetHours ?? '', fulfillmentGroup: i.fulfillmentGroup ?? '', tasks: i.tasks ?? [] })}>Modifier</button></td>}
              </tr>))}
            {items.length === 0 && <tr><td colSpan={7} className="muted">Aucun modèle : les demandes restent libres.</td></tr>}
          </tbody>
        </table>)}
    </>
  )
}
