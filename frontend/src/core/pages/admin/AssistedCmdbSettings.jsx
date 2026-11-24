import React, { useEffect, useState } from 'react'
import { api } from '../../../api'

const KEY = 'assisted-cmdb'

/** CMA-02 : les tâches, dans l'ordre de la liste. */
const TASKS = [
  { key: 'rename', label: 'Renommer' }, { key: 'normalize', label: 'Normaliser' }, { key: 'reclassify', label: 'Reclasser' },
  { key: 'merge', label: 'Fusionner' }, { key: 'relate', label: 'Relier' }, { key: 'criticality', label: 'Criticité' },
]
/** CMA-12 : réglages numériques et leurs bornes. */
const NUMBERS = [
  { key: 'maxItems', label: 'CI au plus par analyse', min: 20, max: 500 },
  { key: 'pairsPerRequest', label: 'Paires au plus par requête (Fusionner)', min: 10, max: 50 },
]
const seconds = s => (s >= 60 ? `${Math.floor(s / 60)} min ${String(s % 60).padStart(2, '0')} s` : `${s} s`)

/**
 * Administration › Fonctionnalités optionnelles › CMDB assistée : réglages (CMA-12) et mesure (CMA-10). Visible la CMDB
 * assistée activée seulement (OPT-04). Une tâche dont les propositions sont surtout rejetées est signalée : la retirer
 * se décide ici.
 */
export default function AssistedCmdbSettings() {
  const [settings, setSettings] = useState(null)
  const [metrics, setMetrics] = useState(null)
  const [days, setDays] = useState(30)
  const [msg, setMsg] = useState(null)
  useEffect(() => {
    // Une erreur, même à l'appel, se lit comme un refus : la page des fonctionnalités reste affichée.
    Promise.resolve().then(() => api.admin.featureSettings(KEY)).then(setSettings).catch(e => setMsg({ ok: false, text: e.message }))
    Promise.resolve().then(() => api.admin.assistedCmdbMetrics()).then(setMetrics).catch(() => setMetrics(null))
  }, [])
  if (!settings) return msg ? <div className="error" role="alert">{msg.text}</div> : null
  const set = patch => setSettings(s => ({ ...s, ...patch }))
  const toggle = t => set({ tasks: settings.tasks.includes(t) ? settings.tasks.filter(x => x !== t) : [...settings.tasks, t] })
  const save = () => api.admin.saveFeatureSettings(KEY, settings)
    .then(saved => { setSettings(saved); setMsg({ ok: true, text: 'Réglages enregistrés.' }) })
    .catch(e => setMsg({ ok: false, text: e.message }))
  const shown = metrics?.windows?.find(w => w.days === days)
  return (
    <section style={{ marginTop: 14 }} aria-label="CMDB assistée : réglages et mesure">
      <h3 className="card-title">Réglages</h3>
      {msg && <div className={msg.ok ? 'notice ok' : 'error'} role={msg.ok ? 'status' : 'alert'}>{msg.text}</div>}
      <fieldset className="field"><legend>Tâches proposées aux gestionnaires</legend>
        {TASKS.map(t => (
          <label key={t.key} style={{ marginRight: 14 }}>
            <input type="checkbox" checked={settings.tasks.includes(t.key)} onChange={() => toggle(t.key)} /> {t.label}
          </label>))}
      </fieldset>
      <div className="form-grid">
        {NUMBERS.map(n => (
          <div className="field" key={n.key}><label htmlFor={`cmdb-${n.key}`}>{n.label}</label>
            <input id={`cmdb-${n.key}`} type="number" min={n.min} max={n.max} value={settings[n.key]}
              onChange={e => set({ [n.key]: e.target.value === '' ? '' : Number(e.target.value) })} />
            <small className="field-hint">De {n.min} à {n.max}.</small></div>))}
      </div>
      <div className="field">
        <label className="check"><input type="checkbox" checked={!!settings.stagingHelp} onChange={e => set({ stagingHelp: e.target.checked })} /> Aide
          au sas : proposer un CI candidat ou un type pour les actifs laissés au sas par une découverte</label>
      </div>
      <button className="btn" onClick={save}>Enregistrer les réglages</button>

      <h3 className="card-title" style={{ marginTop: 18 }}>Mesure</h3>
      {!shown ? <p className="muted small">Mesure indisponible.</p> : <>
        <div className="toolbar" role="tablist">
          {[30, 90].map(d => <button key={d} role="tab" aria-selected={days === d} className={days === d ? 'btn small' : 'btn ghost small'}
            onClick={() => setDays(d)}>{d} jours</button>)}
        </div>
        <div className="table-scroll">
          <table className="compact">
            <thead><tr><th>Tâche</th><th>Décidées</th><th>Acceptées</th><th>Corrigées</th><th>Rejetées</th><th>Expirées</th><th>En attente</th>
              <th>Réponses non conformes</th><th>Appel moyen</th></tr></thead>
            <tbody>{shown.tasks.map(t => {
              const offered = t.task === 'staging' ? settings.stagingHelp : settings.tasks.includes(t.task)
              const mostlyRejected = t.decided > 0 && t.rejectedShare >= 50 && offered
              return (
                <tr key={t.task}>
                  <td>{t.label}{mostlyRejected && <div className="small" role="note">Surtout rejetée : envisager de retirer cette tâche.</div>}</td>
                  <td>{t.decided}</td><td>{t.acceptedShare} %</td><td>{t.correctedShare} %</td><td>{t.rejectedShare} %</td><td>{t.expiredShare} %</td>
                  <td>{t.pending}</td>
                  <td>{t.responses === 0 ? '—' : `${t.nonConformingShare} % (${t.nonConforming}/${t.responses})`}
                    {t.failedCalls > 0 && <div className="small muted">{t.failedCalls} appel(s) en échec</div>}</td>
                  <td>{t.calls === 0 ? '—' : `${(t.averageCallMs / 1000).toFixed(1).replace('.', ',')} s`}</td>
                </tr>)
            })}</tbody>
          </table>
        </div>
        <p className="small">Analyses terminées : <b>{shown.analyses.done}</b>
          {shown.analyses.done > 0 && <> — durée moyenne {seconds(shown.analyses.averageSeconds)}, la plus longue {seconds(shown.analyses.longestSeconds)}</>}
          {shown.analyses.failed > 0 && <> ; en échec : {shown.analyses.failed}</>}.</p>
        <p className="muted small">Expirée : laissée sans décision 30 jours. En attente : encore ouverte. Réponses non conformes : écartées par la
          validation du schéma (OPT-25), réessayées une fois.</p>
      </>}
    </section>
  )
}
