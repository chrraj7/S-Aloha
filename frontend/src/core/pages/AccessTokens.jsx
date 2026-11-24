import React, { useEffect, useState } from 'react'
import { api } from '../../api'
import { dateTimeFr } from '../fields'

const DURATIONS = [7, 30, 90]

/**
 * MCP-03, MCP-09 : jetons d'accès personnels du serveur MCP. `admin` : tous les jetons actifs
 * (Administration › Fonctionnalités optionnelles) ; sinon, ceux de l'utilisateur (Mes préférences),
 * avec la création. Le jeton n'est montré qu'une fois, juste après sa création.
 */
export default function AccessTokens({ admin = false }) {
  const [list, setList] = useState(null)
  const [name, setName] = useState('')
  const [days, setDays] = useState(30)
  const [secret, setSecret] = useState(null)
  const [msg, setMsg] = useState(null)
  const load = () => (admin ? api.tokens.all() : api.tokens.mine()).then(setList).catch(e => setMsg({ ok: false, text: e.message }))
  useEffect(() => { load() }, [admin])   // eslint-disable-line react-hooks/exhaustive-deps
  const server = `${window.location.origin}/mcp`

  const create = () => api.tokens.create({ name: name.trim(), days })
    .then(r => { setSecret(r.secret); setName(''); setMsg(null); load() })
    .catch(e => setMsg({ ok: false, text: e.message }))
  const revoke = t => {
    if (!window.confirm(`Révoquer le jeton « ${t.name} » ? Le client qui s'en sert perdra l'accès.`)) return
    ;(admin ? api.tokens.revokeAny(t.id) : api.tokens.revoke(t.id))
      .then(() => { setMsg({ ok: true, text: `Jeton « ${t.name} » révoqué.` }); load() })
      .catch(e => setMsg({ ok: false, text: e.message }))
  }
  const copy = text => navigator.clipboard?.writeText(text)

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h2 className="card-title">{admin ? 'Jetons d’accès actifs' : 'Jetons d’accès'}</h2>
      {!admin && <p className="muted small">Pour un assistant compatible MCP : il lit S-Aloha avec vos droits, sans rien y écrire.
        Adresse du serveur : <code>{server}</code> <button className="btn ghost small" onClick={() => copy(server)}>Copier</button></p>}
      {msg && <div className={msg.ok ? 'notice ok' : 'error'} role="status">{msg.text}</div>}
      {secret && <div className="notice warn" role="alert">
        <b>Copiez ce jeton maintenant : il ne sera plus affiché.</b><br />
        <code data-testid="token-secret">{secret}</code> <button className="btn ghost small" onClick={() => copy(secret)}>Copier</button>
        <button className="btn ghost small" onClick={() => setSecret(null)}>J’ai copié le jeton</button>
      </div>}
      {!admin && <div style={{ display: 'flex', gap: 8, alignItems: 'flex-end', flexWrap: 'wrap', marginBottom: 12 }}>
        <div className="field"><label htmlFor="token-name">Nom du jeton</label>
          <input id="token-name" value={name} maxLength={100} onChange={e => setName(e.target.value)} placeholder="Ex. : assistant de mon éditeur" /></div>
        <div className="field"><label htmlFor="token-days">Durée</label>
          <select id="token-days" value={days} onChange={e => setDays(Number(e.target.value))}>
            {DURATIONS.map(d => <option key={d} value={d}>{d} jours</option>)}
          </select></div>
        <button className="btn" onClick={create} disabled={!name.trim()}>Créer un jeton</button>
      </div>}
      {list && (list.length === 0 ? <p className="muted small">Aucun jeton actif.</p> : (
        <table className="table">
          <thead><tr>{admin && <th>Compte</th>}<th>Nom</th><th>Créé le</th><th>Expire le</th><th>Dernière utilisation</th><th /></tr></thead>
          <tbody>{list.map(t => (
            <tr key={t.id}>
              {admin && <td>{t.displayName || t.username}</td>}
              <td>{t.name}</td><td>{dateTimeFr(t.createdAt)}</td><td>{dateTimeFr(t.expiresAt)}</td>
              <td>{t.lastUsedAt ? dateTimeFr(t.lastUsedAt) : 'jamais'}</td>
              <td><button className="btn ghost small" onClick={() => revoke(t)}>Révoquer</button></td>
            </tr>))}
          </tbody>
        </table>))}
    </div>
  )
}
