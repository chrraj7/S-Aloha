import React, { useEffect, useState } from 'react'
import { api } from '../../../api'
import { dateTimeFr } from '../../fields'

/** Secret saisi : absent (inchangé), vide (effacé) ou nouvelle valeur. */
export function SecretField({ id, label, stored, value, onChange }) {
  return (
    <div className="field"><label htmlFor={id}>{label}</label>
      <input id={id} type="password" autoComplete="new-password" value={value ?? ''} onChange={e => onChange(e.target.value)}
        placeholder={stored ? 'Enregistré : laisser vide pour le garder' : 'Non enregistré'} />
      {stored && value == null && <button type="button" className="btn ghost small" onClick={() => onChange('')}>Effacer</button>}
      {stored && value === '' && <small className="field-hint">Sera effacé à l’enregistrement.</small>}
    </div>
  )
}

export const Result = ({ r }) => r && <div className={r.ok ? 'notice ok' : 'error'} role="status">{r.message}</div>

const Updated = ({ s }) => s.updatedAt ? <p className="muted small">Modifié le {dateTimeFr(s.updatedAt)} par {s.updatedBy}.</p> : null

/** ADM-60, ADM-62 : serveur SMTP des courriels. */
function MailCard() {
  const [f, setF] = useState(null)
  const [password, setPassword] = useState(null)
  const [to, setTo] = useState('')
  const [result, setResult] = useState(null)
  const [error, setError] = useState('')
  useEffect(() => { api.admin.mail().then(setF).catch(e => setError(e.message)) }, [])
  if (!f) return error ? <div className="error">{error}</div> : <p>Chargement…</p>
  const set = k => e => setF({ ...f, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const save = () => api.admin.saveMail({ ...f, port: Number(f.port), password: password === null ? undefined : password })
    .then(s => { setF(s); setPassword(null); setError(''); setResult({ ok: true, message: 'Réglages enregistrés.' }) })
    .catch(e => setError(e.message))
  const test = () => { setResult(null); api.admin.testMail(to).then(setResult).catch(e => setError(e.message)) }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <h2 className="card-title">Courriels (SMTP)</h2>
      <p className="muted small">Serveur d’envoi des courriels de la plateforme : notifications par courriel (SOC-22).</p>
      {error && <div className="error">{error}</div>}
      <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
        <input type="checkbox" style={{ width: 'auto' }} checked={f.enabled} onChange={set('enabled')} /> Envoi activé</label>
      <div className="row3">
        <div className="field"><label htmlFor="smtp-host">Serveur</label><input id="smtp-host" value={f.host} onChange={set('host')} placeholder="smtp.example.org" /></div>
        <div className="field"><label htmlFor="smtp-port">Port</label><input id="smtp-port" type="number" min="1" max="65535" value={f.port} onChange={set('port')} /></div>
        <div className="field"><label htmlFor="smtp-security">Chiffrement</label>
          <select id="smtp-security" value={f.security} onChange={set('security')}>
            <option value="STARTTLS">STARTTLS (port 587)</option><option value="TLS">TLS implicite (port 465)</option><option value="Aucun">Aucun (relais interne)</option>
          </select></div>
      </div>
      <div className="row2">
        <div className="field"><label htmlFor="smtp-user">Compte (facultatif)</label><input id="smtp-user" value={f.username ?? ''} onChange={set('username')} autoComplete="off" /></div>
        <SecretField id="smtp-password" label="Mot de passe" stored={f.hasPassword} value={password} onChange={setPassword} />
      </div>
      <div className="row2">
        <div className="field"><label htmlFor="smtp-from">Adresse d’expéditeur</label><input id="smtp-from" value={f.fromAddress} onChange={set('fromAddress')} placeholder="itsm@example.org" /></div>
        <div className="field"><label htmlFor="smtp-name">Nom d’expéditeur</label><input id="smtp-name" value={f.fromName} onChange={set('fromName')} /></div>
      </div>
      <div className="field"><label htmlFor="smtp-url">Adresse de l’interface (liens des courriels)</label>
        <input id="smtp-url" value={f.publicUrl ?? ''} onChange={set('publicUrl')} placeholder="https://itsm.example.org" /></div>
      <button className="btn" onClick={save}>Enregistrer</button>
      <Updated s={f} />
      <div className="toolbar" style={{ marginTop: 12 }}>
        <input aria-label="Destinataire du courriel d’essai" value={to} onChange={e => setTo(e.target.value)} placeholder="Destinataire de l’essai" />
        <button className="btn ghost" onClick={test} disabled={!to.trim()}>Envoyer un courriel d’essai</button>
      </div>
      <Result r={result} />
    </div>
  )
}

/** ADM-61, ADM-63 : stockage objet compatible S3 (Garage) des pièces jointes. */
function StorageCard() {
  const [f, setF] = useState(null)
  const [secret, setSecret] = useState(null)
  const [result, setResult] = useState(null)
  const [error, setError] = useState('')
  useEffect(() => { api.admin.storage().then(setF).catch(e => setError(e.message)) }, [])
  if (!f) return error ? <div className="error">{error}</div> : <p>Chargement…</p>
  const set = k => e => setF({ ...f, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const save = () => api.admin.saveStorage({ ...f, maxFileMb: Number(f.maxFileMb), secretKey: secret === null ? undefined : secret })
    .then(s => { setF(s); setSecret(null); setError(''); setResult({ ok: true, message: 'Réglages enregistrés.' }) })
    .catch(e => setError(e.message))
  const test = () => { setResult(null); api.admin.testStorage().then(setResult).catch(e => setError(e.message)) }
  return (
    <div className="card">
      <h2 className="card-title">Pièces jointes (stockage S3)</h2>
      <p className="muted small">Stockage objet compatible S3, Garage par exemple : les fichiers joints aux enregistrements y sont déposés, hors de la base (SOC-25).</p>
      {error && <div className="error">{error}</div>}
      <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
        <input type="checkbox" style={{ width: 'auto' }} checked={f.enabled} onChange={set('enabled')} /> Pièces jointes activées</label>
      <div className="row3">
        <div className="field"><label htmlFor="s3-endpoint">Adresse</label><input id="s3-endpoint" value={f.endpoint} onChange={set('endpoint')} placeholder="http://garage:3900" /></div>
        <div className="field"><label htmlFor="s3-region">Région</label><input id="s3-region" value={f.region} onChange={set('region')} /></div>
        <div className="field"><label htmlFor="s3-bucket">Bucket</label><input id="s3-bucket" value={f.bucket} onChange={set('bucket')} placeholder="s-aloha" /></div>
      </div>
      <div className="row2">
        <div className="field"><label htmlFor="s3-key">Clé d’accès</label><input id="s3-key" value={f.accessKey} onChange={set('accessKey')} autoComplete="off" /></div>
        <SecretField id="s3-secret" label="Clé secrète" stored={f.hasSecretKey} value={secret} onChange={setSecret} />
      </div>
      <div className="row2">
        <div className="field"><label htmlFor="s3-max">Taille maximale d’un fichier (Mo)</label>
          <input id="s3-max" type="number" min="1" max="100" value={f.maxFileMb} onChange={set('maxFileMb')} /></div>
        <div className="field"><label style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
          <input type="checkbox" style={{ width: 'auto' }} checked={f.pathStyle} onChange={set('pathStyle')} /> Adressage par chemin (Garage, MinIO)</label></div>
      </div>
      <button className="btn" onClick={save}>Enregistrer</button>{' '}
      <button className="btn ghost" onClick={test}>Tester le dépôt</button>
      <Updated s={f} />
      <Result r={result} />
    </div>
  )
}

/** Administration → Messagerie et stockage (ADM-60 à ADM-63, ADR-0020). */
export default function PlatformSettings() {
  return (
    <>
      <h1 className="page-title">Messagerie et stockage</h1>
      <p className="page-sub">Les secrets sont chiffrés par le coffre (clé <code>Discovery:VaultKey</code>) et ne sont jamais réaffichés ; chaque modification est tracée au journal d’audit. Les essais utilisent les réglages enregistrés.</p>
      <MailCard />
      <StorageCard />
    </>
  )
}
