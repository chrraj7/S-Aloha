import React, { useEffect, useState } from 'react'
import { api } from '../../../api'
import { dateTimeFr } from '../../fields'
import { Result, SecretField } from './PlatformSettings'

const IN_SI = 'Dans le SI'
const HOSTED = 'Hébergé hors du SI'
const FEATURE = { 'language-model': 'Essai', 'assisted-cmdb': 'CMDB assistée' }

/** OPT-27 : compteurs des appels des 30 derniers jours, tirés des traces. */
function Usage({ version }) {
  const [usage, setUsage] = useState(null)
  useEffect(() => {
    Promise.resolve().then(() => api.admin.languageModelUsage(30)).then(setUsage).catch(() => setUsage({ days: 30, rows: [] }))
  }, [version])
  if (!usage) return null
  if (usage.rows.length === 0) return <p className="muted small">Aucun appel au modèle ces {usage.days} derniers jours.</p>
  return (
    <table className="table small" style={{ marginTop: 8 }}>
      <caption className="muted small" style={{ textAlign: 'left' }}>Appels des {usage.days} derniers jours (traces gardées 90 jours)</caption>
      <thead><tr><th>Origine</th><th>Issue</th><th>Appels</th><th>Durée moyenne</th><th>Réponses écartées</th><th>Dernier</th></tr></thead>
      <tbody>{usage.rows.map(r => (
        <tr key={r.feature + r.outcome}><td>{FEATURE[r.feature] ?? r.feature}</td><td>{r.outcome}</td><td>{r.count}</td>
          <td>{(r.averageMs / 1000).toFixed(1)} s</td><td>{r.rejected}</td><td>{dateTimeFr(r.last)}</td></tr>
      ))}</tbody>
    </table>
  )
}

/**
 * Administration → Fonctionnalités optionnelles → Modèle de langage (OPT-20, OPT-22, OPT-23, OPT-26, OPT-27) : réglages,
 * avertissement permanent d'un modèle hébergé, essai avec les réglages enregistrés et compteurs des appels.
 */
export default function LanguageModelSettings() {
  const [f, setF] = useState(null)
  const [saved, setSaved] = useState(null)
  const [key, setKey] = useState(null)
  const [confirm, setConfirm] = useState(false)
  const [result, setResult] = useState(null)
  const [error, setError] = useState('')
  const [testing, setTesting] = useState(false)
  const [version, setVersion] = useState(0)
  // Promise.resolve : une erreur de l'appel lui-même reste dans la promesse, affichée, jamais levée au rendu.
  useEffect(() => {
    Promise.resolve().then(() => api.admin.languageModel()).then(s => { setF(s); setSaved(s) }).catch(e => setError(e.message))
  }, [])
  if (!f) return error ? <div className="error" role="alert">{error}</div> : <p>Chargement…</p>
  const set = k => e => setF({ ...f, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  // OPT-22 : passer à « hébergé » demande une confirmation explicite, à chaque passage.
  const needsConfirm = f.location === HOSTED && saved.location !== HOSTED
  const save = () => {
    setResult(null)
    api.admin.saveLanguageModel({
      enabled: f.enabled, location: f.location, url: f.url, model: f.model, key: key === null ? undefined : key,
      timeoutSeconds: Number(f.timeoutSeconds), maxResponseKb: Number(f.maxResponseKb), confirmHosted: needsConfirm && confirm,
    })
      .then(s => { setF(s); setSaved(s); setKey(null); setConfirm(false); setError(''); setResult({ ok: true, message: 'Réglages enregistrés.' }) })
      .catch(e => setError(e.message))
  }
  const test = () => {
    setResult(null); setTesting(true)
    api.admin.testLanguageModel().then(setResult).catch(e => setError(e.message))
      .finally(() => { setTesting(false); setVersion(v => v + 1) })
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <h2 className="card-title">Modèle de langage</h2>
      <p className="muted small">Le modèle dont se sert la CMDB assistée, au format de complétion de conversation (Ollama, vLLM, LocalAI,
        llama.cpp ou une offre hébergée). Avant chaque envoi, les adresses, noms d’hôte, numéros de série, comptes et courriels sont
        remplacés par des jetons ; aucun secret du coffre n’est envoyé.</p>
      {saved.dataLeavesSi && (
        <div className="notice" role="status"><b>Les données envoyées au modèle quittent le SI.</b> Inscrire ce traitement au registre des
          traitements.{saved.hostedConfirmedAt && <> Confirmé le {dateTimeFr(saved.hostedConfirmedAt)} par {saved.hostedConfirmedBy}.</>}</div>
      )}
      {error && <div className="error" role="alert">{error}</div>}
      <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
        <input type="checkbox" style={{ width: 'auto' }} checked={f.enabled} onChange={set('enabled')} /> Modèle activé</label>
      <div className="row3">
        <div className="field"><label htmlFor="lm-location">Emplacement</label>
          <select id="lm-location" value={f.location} onChange={set('location')}>
            <option value={IN_SI}>{IN_SI}</option><option value={HOSTED}>{HOSTED}</option>
          </select></div>
        <div className="field"><label htmlFor="lm-url">Adresse de l’API du modèle</label>
          <input id="lm-url" value={f.url} onChange={set('url')} placeholder="http://modele.interne:11434/v1" /></div>
        <div className="field"><label htmlFor="lm-model">Modèle</label>
          <input id="lm-model" value={f.model} onChange={set('model')} placeholder="qwen2.5:7b" /></div>
      </div>
      {needsConfirm && (
        <label style={{ display: 'flex', gap: 6, alignItems: 'center' }} className="small">
          <input type="checkbox" style={{ width: 'auto' }} checked={confirm} onChange={e => setConfirm(e.target.checked)} />
          Je confirme que les données envoyées au modèle quittent le SI, et que ce traitement sera inscrit au registre des traitements.</label>
      )}
      <div className="row3">
        <SecretField id="lm-key" label="Clé (facultative)" stored={f.hasKey} value={key} onChange={setKey} />
        <div className="field"><label htmlFor="lm-timeout">Délai d’une requête (s)</label>
          <input id="lm-timeout" type="number" min="10" max="300" value={f.timeoutSeconds} onChange={set('timeoutSeconds')} /></div>
        <div className="field"><label htmlFor="lm-max">Taille maximale d’une réponse (Ko)</label>
          <input id="lm-max" type="number" min="1" max="1024" value={f.maxResponseKb} onChange={set('maxResponseKb')} /></div>
      </div>
      <button className="btn" onClick={save} disabled={needsConfirm && !confirm}>Enregistrer</button>{' '}
      <button className="btn ghost" onClick={test} disabled={testing || !saved.url || !saved.model}>
        {testing ? 'Essai en cours…' : 'Essayer le modèle'}</button>
      {saved.updatedAt && <p className="muted small">Modifié le {dateTimeFr(saved.updatedAt)} par {saved.updatedBy}. L’essai utilise les réglages enregistrés, même désactivés.</p>}
      <Result r={result} />
      <Usage version={version} />
    </div>
  )
}
