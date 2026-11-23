import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, setUser } from '../../api'
import { refreshFeatures } from '../features'
import { BrandMark, IconConsole, IconProblem, IconChange, IconReport } from '../components/icons'
import { SOURCE_URL, LICENSE_LABEL } from '../about'

// Ce que l'outil permet de faire, pas la liste de ses menus. Chaque bloc
// renvoie à une fonctionnalité existante (docs/reference/regles-metier.md).
const FEATURES = [
  { Icon: IconConsole, title: 'Savoir quoi traiter d\'abord',
    text: 'Une console par rôle, classée par criticité : retards, incidents majeurs, décisions en attente.' },
  { Icon: IconProblem, title: 'Remonter à la cause racine',
    text: '5 Pourquoi, Ishikawa ou arbre des défaillances, puis actions correctives en RACI, confiées à des comptes ou groupes AD.' },
  { Icon: IconChange, title: 'Encadrer les changements',
    text: 'Risque, autorisation, plan de retour arrière et calendrier des mises en production.' },
  { Icon: IconReport, title: 'Mesurer le service rendu',
    text: 'Cibles SLA, MTTR des incidents et des problèmes, taux de changements réussis.' },
]

export default function Login() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const nav = useNavigate()

  const submit = async (e) => {
    e.preventDefault()
    setError(''); setBusy(true)
    try {
      setUser(await api.login(username, password))
      refreshFeatures(true)   // OPT-04 : fonctionnalités activées, lues à la connexion
      nav('/')
    } catch (e) { setError(e.message) }
    finally { setBusy(false) }
  }

  return (
    <div className="login">
      <aside className="login-hero">
        <div className="login-glow a" aria-hidden />
        <div className="login-glow b" aria-hidden />

        <div className="brand">
          <div className="brand-mark large"><BrandMark size={40} /></div>
          <span className="brand-name large">S-Aloha</span>
        </div>

        <div className="login-hero-body">
          <div className="login-message">
            <p className="login-kicker">Plateforme ITIL de la DSI</p>
            <h1>Piloter le service IT, de l'incident à l'amélioration.</h1>
            <p className="login-motto">
              <span lang="mg">« Ny fahaiza-manao ho amin'ny tolotra tsara kokoa. »</span>
              <span className="login-motto-fr">Le savoir-faire au service d'une meilleure offre.</span>
            </p>
            <p className="login-pun">
              <i lang="mg">Aloha</i> signifie « d'abord » en malgache :
              {' '}<i lang="mg">Service alohan'ny zavatra rehetra</i>, le service avant toute chose.
            </p>
          </div>

          <div>
            <p className="login-kicker">Ce que vous y faites</p>
            <ul className="login-features">
              {FEATURES.map(({ Icon, title, text }) => (
                <li key={title}>
                  <b><Icon size={17} />{title}</b>
                  <p>{text}</p>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </aside>

      <div className="login-side bg-canvas">
        <form className="login-card" onSubmit={submit}>
          <div className="login-compact-brand">
            <div className="brand-mark large"><BrandMark size={40} /></div>
            <div>
              <p className="brand-name large">S-Aloha</p>
              <p className="muted small">Plateforme ITIL</p>
            </div>
          </div>
          <div>
            <h2>Connexion</h2>
            <p className="muted small">Avec votre compte Active Directory.</p>
          </div>
          {error && <div className="error">{error}</div>}
          <div className="field">
            <label htmlFor="username">Identifiant</label>
            <input id="username" value={username} onChange={e => setUsername(e.target.value)}
              placeholder="prenom.nom" autoFocus autoComplete="username" required />
          </div>
          <div className="field">
            <label htmlFor="password">Mot de passe</label>
            <input id="password" type="password" value={password} onChange={e => setPassword(e.target.value)}
              autoComplete="current-password" required />
          </div>
          <button className="btn block" type="submit" disabled={busy}>
            {busy ? 'Connexion…' : 'Se connecter'}
          </button>
          <a className="source-link muted" href={SOURCE_URL} target="_blank" rel="noreferrer">Code source · {LICENSE_LABEL}</a>
        </form>
      </div>
    </div>
  )
}
