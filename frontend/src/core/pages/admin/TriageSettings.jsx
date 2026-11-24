import React, { useEffect, useState } from 'react'
import { api } from '../../../api'

const KEY = 'assisted-ticket-triage'

/** TRI-13 : réglages numériques, leurs bornes et leur pas. */
const NUMBERS = [
  { key: 'neighbours', label: 'Voisins qui votent', min: 5, max: 20 },
  { key: 'minVoters', label: 'Votants minimaux', min: 2, max: 10 },
  { key: 'minSharePercent', label: 'Part minimale (%)', min: 50, max: 100 },
  { key: 'correlationWindowMinutes', label: 'Fenêtre de corrélation (minutes)', min: 15, max: 240 },
  { key: 'outageThreshold', label: 'Seuil de panne probable (incidents)', min: 2, max: 20 },
  { key: 'correlationMinSimilarity', label: 'Seuil de similarité', min: 0.4, max: 0.95, step: 0.01 },
]
const SWITCHES = [
  { key: 'suggestCategory', label: 'Catégorie' }, { key: 'suggestService', label: 'Service' }, { key: 'suggestGroup', label: 'Groupe responsable' },
  { key: 'suggestImpact', label: 'Impact' }, { key: 'suggestOutage', label: 'Panne probable' },
]
/** Champ mesuré → réglage qui le coupe (TRI-10 : un champ surtout corrigé se coupe, TRI-13). */
const SWITCH_OF = { category: 'suggestCategory', serviceId: 'suggestService', owner: 'suggestGroup', impact: 'suggestImpact' }

/**
 * Administration › Fonctionnalités optionnelles › Tri assisté : réglages (TRI-13) et mesure (TRI-10). Visible le tri
 * activé seulement (OPT-04). Un champ dont les suggestions sont surtout corrigées est signalé : le couper se décide ici.
 */
export default function TriageSettings() {
  const [settings, setSettings] = useState(null)
  const [metrics, setMetrics] = useState(null)
  const [days, setDays] = useState(30)
  const [msg, setMsg] = useState(null)
  useEffect(() => {
    // Une erreur, même à l'appel, se lit comme un refus : la page des fonctionnalités reste affichée.
    Promise.resolve().then(() => api.admin.featureSettings(KEY)).then(setSettings).catch(e => setMsg({ ok: false, text: e.message }))
    Promise.resolve().then(() => api.admin.triageMetrics()).then(setMetrics).catch(() => setMetrics(null))
  }, [])
  if (!settings) return msg ? <div className="error" role="alert">{msg.text}</div> : null
  const set = patch => setSettings(s => ({ ...s, ...patch }))
  const save = () => api.admin.saveFeatureSettings(KEY, settings)
    .then(saved => { setSettings(saved); setMsg({ ok: true, text: 'Réglages enregistrés.' }) })
    .catch(e => setMsg({ ok: false, text: e.message }))
  const shown = metrics?.windows?.find(w => w.days === days)
  return (
    <section style={{ marginTop: 14 }} aria-label="Tri assisté : réglages et mesure">
      <h3 className="card-title">Réglages</h3>
      {msg && <div className={msg.ok ? 'notice ok' : 'error'} role={msg.ok ? 'status' : 'alert'}>{msg.text}</div>}
      <div className="form-grid">
        {NUMBERS.map(n => (
          <div className="field" key={n.key}><label htmlFor={`triage-${n.key}`}>{n.label}</label>
            <input id={`triage-${n.key}`} type="number" min={n.min} max={n.max} step={n.step ?? 1} value={settings[n.key]}
              onChange={e => set({ [n.key]: e.target.value === '' ? '' : Number(e.target.value) })} />
            <small className="field-hint">De {String(n.min).replace('.', ',')} à {String(n.max).replace('.', ',')}.</small></div>))}
      </div>
      <fieldset className="field"><legend>Suggestions proposées</legend>
        {SWITCHES.map(s => (
          <label key={s.key} style={{ marginRight: 14 }}>
            <input type="checkbox" checked={!!settings[s.key]} onChange={e => set({ [s.key]: e.target.checked })} /> {s.label}
          </label>))}
      </fieldset>
      <button className="btn" onClick={save}>Enregistrer les réglages</button>

      <h3 className="card-title" style={{ marginTop: 18 }}>Mesure</h3>
      {!metrics ? <p className="muted small">Mesure indisponible.</p> : <>
        <div className="toolbar" role="tablist">
          {[30, 90].map(d => <button key={d} role="tab" aria-selected={days === d} className={days === d ? 'btn small' : 'btn ghost small'}
            onClick={() => setDays(d)}>{d} jours</button>)}
        </div>
        <div className="table-scroll">
          <table className="compact">
            <thead><tr><th>Champ</th><th>Décidées</th><th>Acceptées</th><th>Corrigées</th><th>Rejetées</th><th>Laissées</th><th>En attente</th></tr></thead>
            <tbody>{shown.fields.map(f => {
              const mostlyCorrected = f.decided > 0 && f.correctedShare >= 50
              return (
                <tr key={f.field}>
                  <td>{f.label}{mostlyCorrected && settings[SWITCH_OF[f.field]] &&
                    <div className="small" role="note">Surtout corrigée : envisager de couper cette suggestion.</div>}</td>
                  <td>{f.decided}</td><td>{f.acceptedShare} %</td><td>{f.correctedShare} %</td><td>{f.rejectedShare} %</td><td>{f.leftShare} %</td>
                  <td>{f.pending}</td>
                </tr>)
            })}</tbody>
          </table>
        </div>
        <p className="muted small">Laissée : sans décision quand l’incident a quitté « Nouveau », ou expirée. En attente : incident encore Nouveau.</p>
      </>}
    </section>
  )
}
