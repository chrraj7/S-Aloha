import React, { useEffect, useState } from 'react'
import { api } from '../../../api'
import { dateTimeFr } from '../../fields'
import { refreshFeatures } from '../../features'
import TriageSettings from './TriageSettings'
import LanguageModelSettings from './LanguageModelSettings'
import AccessTokens from '../AccessTokens'

const TONE = { Activée: 't-done', Coupée: 't-wait', Indisponible: 't-bad' }

const WHAT = {
  'assisted-ticket-triage': 'Suggère la catégorie, le service, le groupe et l’impact d’un incident à la saisie, et signale les incidents ouverts qui semblent liés.',
  'assisted-cmdb': 'Propose de renommer, normaliser, reclasser, fusionner et relier des CI ; chaque proposition est décidée par un gestionnaire.',
  'mcp-server': 'Ouvre S-Aloha en lecture aux assistants des utilisateurs, avec les droits de chacun.',
}

function FeatureCard({ f, onSwitched }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [warnings, setWarnings] = useState([])
  const on = f.state === 'Activée'
  const c = f.containerState
  const toggle = () => {
    const verb = on ? 'Couper' : 'Activer'
    if (!window.confirm(`${verb} « ${f.name} » ?${on ? ' Ses écrans et ses routes disparaissent ; ce qu’elle a produit est gardé.' : ''}`)) return
    setBusy(true); setError(''); setWarnings([])
    api.admin.switchFeature(f.key, !on)
      .then(r => { setWarnings(r.warnings ?? []); onSwitched(r.feature); refreshFeatures(true) })
      .catch(e => setError(e.message))
      .finally(() => setBusy(false))
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <h2 className="card-title">{f.name} <span className={'badge ' + TONE[f.state]}>{f.state}</span></h2>
      <p className="muted small">{WHAT[f.key]}</p>
      {error && <div className="error" role="alert">{error}</div>}
      {warnings.map(w => <div key={w} className="notice" role="status">{w}</div>)}
      <p className="small"><b>Conteneur « {f.container} »</b> : {c.message}
        {c.reachable && c.tokenAccepted && <> Propose : {c.features.length ? c.features.join(', ') : 'aucune fonctionnalité'}.</>}
        <span className="muted"> Contrôlé le {dateTimeFr(c.checkedAt)}.</span></p>
      {/* TRI-06 : index des incidents ouverts du tri, tenu dans la mémoire du conteneur. */}
      {f.key === 'assisted-ticket-triage' && c.status?.[f.key] && <p className="small"><b>Index des incidents ouverts</b> : {c.status[f.key].indexBuilt
        ? `${c.status[f.key].openIncidents} incident(s), reconstruit le ${dateTimeFr(c.status[f.key].rebuiltAt)}`
        : 'pas encore construit (au premier enregistrement d’un incident)'}.</p>}
      {f.dependencies.length > 0 && (
        <ul className="small">
          {f.dependencies.map(d => (
            <li key={d.key}><b>{d.name}</b> ({d.required ? 'obligatoire' : 'facultatif'}) : {d.ok ? 'prêt. ' : ''}{d.message}</li>
          ))}
        </ul>
      )}
      {f.updatedAt && <p className="muted small">{on ? 'Activée' : 'Coupée'} le {dateTimeFr(f.updatedAt)} par {f.updatedBy}.</p>}
      <button className={on ? 'btn ghost' : 'btn'} onClick={toggle} disabled={busy || (!on && !f.available)}>
        {busy ? 'Vérification…' : on ? 'Couper' : 'Activer'}
      </button>
      {!on && !f.available && <p className="muted small">Indisponible tant que son conteneur ne la propose pas (exploitation).</p>}
      {/* TRI-10, TRI-13 : réglages et mesure du tri, la fonctionnalité activée seulement (OPT-04). */}
      {on && f.key === 'assisted-ticket-triage' && <TriageSettings />}
      {/* MCP-03, MCP-09 : jetons actifs de tous les comptes, révocables. */}
      {on && f.key === 'mcp-server' && <AccessTokens admin />}
    </div>
  )
}

/** Administration → Fonctionnalités optionnelles (OPT-01 à OPT-03, OPT-07, ADR-0023). */
export default function OptionalFeatures() {
  const [list, setList] = useState(null)
  const [error, setError] = useState('')
  useEffect(() => { api.admin.features().then(setList).catch(e => setError(e.message)) }, [])
  if (!list) return error ? <div className="error">{error}</div> : <p>Vérification des conteneurs…</p>
  const replace = v => setList(l => l.map(x => (x.key === v.key ? v : x)))
  return (
    <>
      <h1 className="page-title">Fonctionnalités optionnelles</h1>
      <p className="page-sub">Elles tournent hors de l’API, dans un conteneur à part : une panne de l’une d’elles n’atteint jamais le reste.
        Elles proposent ; une personne décide, et le journal d’audit garde l’origine de chaque modification.</p>
      {list.map(f => <FeatureCard key={f.key} f={f} onSwitched={replace} />)}
      {/* OPT-20 à OPT-27 : réglable et essayé avant d’activer la CMDB assistée, qui en dépend (OPT-03). */}
      <LanguageModelSettings />
    </>
  )
}
