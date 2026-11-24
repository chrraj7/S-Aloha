import React, { useEffect, useState } from 'react'
import { api } from '../../api'
import { useFeatures } from '../features'
import AccessTokens from './AccessTokens'

const MODES = [
  { value: 'Aucun', label: 'Aucun courriel', hint: 'Les notifications restent dans l’application (cloche).' },
  { value: 'Immédiat', label: 'Immédiat', hint: 'Un courriel au relevé suivant (quelques minutes) dès qu’une notification vous concerne.' },
  { value: 'Résumé quotidien', label: 'Résumé quotidien', hint: 'Un seul courriel par jour, avec les notifications des dernières 24 heures.' },
]

/** SOC-22 : mes préférences — les notifications par courriel (aucun, immédiat, résumé quotidien). */
export default function Preferences() {
  const [p, setP] = useState(null)
  const [msg, setMsg] = useState(null)
  const mcp = useFeatures().has('mcp-server')
  useEffect(() => { api.preferences.mail().then(setP).catch(e => setMsg({ ok: false, text: e.message })) }, [])
  if (!p) return msg ? <div className="error">{msg.text}</div> : <p>Chargement…</p>
  const save = () => api.preferences.saveMail({ mode: p.mode, address: p.address || null })
    .then(r => { setP(r); setMsg({ ok: true, text: 'Préférences enregistrées.' }) }).catch(e => setMsg({ ok: false, text: e.message }))
  return (
    <>
      <h1 className="page-title">Mes préférences</h1>
      <p className="page-sub">Recevoir aussi vos notifications par courriel : les mêmes que la cloche, jamais au-delà de ce que vous pouvez consulter.</p>
      {!p.mailEnabled && <div className="notice warn" role="status">L’envoi de courriels n’est pas encore activé par l’administrateur : votre choix sera appliqué dès son activation.</div>}
      {msg && <div className={msg.ok ? 'notice ok' : 'error'} role="status">{msg.text}</div>}
      <div className="card">
        <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
          <legend className="card-title">Notifications par courriel</legend>
          {MODES.map(m => (
            <label key={m.value} style={{ display: 'flex', gap: 8, alignItems: 'flex-start', marginBottom: 8 }}>
              <input type="radio" name="mail-mode" style={{ width: 'auto', marginTop: 3 }} checked={p.mode === m.value} onChange={() => setP({ ...p, mode: m.value })} />
              <span><b>{m.label}</b><br /><small className="muted">{m.hint}</small></span>
            </label>))}
        </fieldset>
        {p.mode !== 'Aucun' && <div className="field"><label htmlFor="mail-address">Adresse de réception</label>
          <input id="mail-address" type="email" autoComplete="email" value={p.address ?? ''} onChange={e => setP({ ...p, address: e.target.value })} placeholder="prenom.nom@example.org" /></div>}
        <button className="btn" onClick={save} disabled={p.mode !== 'Aucun' && !p.address?.trim()}>Enregistrer</button>
      </div>
      {/* MCP-03 : la fonctionnalité activée seulement (OPT-04). */}
      {mcp && <AccessTokens />}
    </>
  )
}
