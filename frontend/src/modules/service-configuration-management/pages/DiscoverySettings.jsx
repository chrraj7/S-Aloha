import React, { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, getUser } from '../../../api'
import { dateFr, dateTimeFr } from '../../../core/fields'
import PerimeterSelect from '../../../core/components/PerimeterSelect'
import usePerimeters, { perimeterName } from '../../../core/usePerimeters'
import useManagesPerimeter from '../../../core/useManagesPerimeter'
import { JOB_TONES, SOURCE_LABELS } from '../config'

const SSH_HELP = 'Inventaire en lecture seule, sans sudo : système, constructeur, logiciels installés, services actifs, ports en écoute, connexions établies (flux proposés). '
  + 'La clé d’hôte est retenue au premier contact ; si elle change, l’hôte est refusé jusqu’à acceptation par l’Admin.'
/** Types de scan : cible attendue et types d'identifiant du coffre utilisables. */
const SCAN_TYPES = {
  IpSweep: { label: 'Balayage IP', target: 'Réseau CIDR (/24 au plus) ou adresse, ex. 192.168.1.0/24', credentials: [],
    help: 'Ping et sondage TCP des ports témoins, nom par DNS inverse, bannières SSH et HTTP des ports ouverts. Sans identifiant.' },
  Snmp: { label: 'SNMP v2c ou v3', target: 'Réseau CIDR (/24 au plus) ou adresse', credentials: ['SnmpV3', 'SnmpV2c'],
    help: 'Lecture seule (sysDescr, sysName, sysLocation, n° de série, interfaces) avec une communauté v2c ou un utilisateur v3 du coffre.' },
  ActiveDirectory: { label: 'Active Directory', target: 'BaseDN facultatif (sinon celle de l’annuaire configuré)', credentials: ['WindowsDomain'],
    help: 'Comptes d’ordinateur actifs. Compte de domaine du coffre facultatif (sinon le compte de service de l’API).' },
  Ssh: { label: 'Inventaire SSH (Linux, Unix)', target: 'Réseau CIDR (/24 au plus) ou adresse', credentials: ['Ssh'], help: SSH_HELP },
  Proxmox: { label: 'Proxmox VE', target: 'Adresse de l’API, ex. https://pve.corp.local:8006', credentials: ['ProxmoxToken'],
    help: 'Nœuds, machines virtuelles et conteneurs, avec un jeton d’API en lecture seule (rôle PVEAuditor). Relation « Héberge » proposée.' },
  WinRm: { label: 'Inventaire WinRM (Windows)', target: 'Réseau CIDR (/24 au plus) ou adresse', credentials: ['WindowsDomain'],
    help: 'WS-Management sur HTTPS (5986), compte de domaine ou local membre de « Remote Management Users » (NTLM) : système, '
      + 'constructeur, logiciels, services actifs, ports en écoute, connexions (flux proposés). À valider sur un Windows réel.' },
  VCenter: { label: 'VMware vCenter', target: 'Adresse de vCenter, ex. https://vcenter.corp.local', credentials: ['VCenter'],
    help: 'Hôtes ESXi et machines virtuelles (vCenter 7 et suivants), avec un compte en lecture seule. Relation « Héberge » proposée.' },
}
const CREDENTIAL_TYPES = {
  SnmpV3: { label: 'Utilisateur SNMP v3', secret: 'Phrase d’authentification' },
  SnmpV2c: { label: 'Communauté SNMP v2c', secret: 'Communauté' },
  WindowsDomain: { label: 'Compte de domaine Windows', secret: 'Mot de passe' },
  Ssh: { label: 'Compte SSH', secret: 'Mot de passe' },
  ProxmoxToken: { label: 'Jeton d’API Proxmox VE', secret: 'Secret du jeton' },
  VCenter: { label: 'Compte vCenter', secret: 'Mot de passe' },
}
// SNMP v3 (CFG-51) : MD5 et DES, cassés, ne sont pas proposés.
const V3_AUTH = [{ value: 'SHA256', label: 'SHA-256 (recommandé)' }, { value: 'SHA384', label: 'SHA-384' },
  { value: 'SHA512', label: 'SHA-512' }, { value: 'SHA1', label: 'SHA-1 (équipements anciens)' }]
const V3_PRIV = [{ value: 'AES', label: 'AES-128 (recommandé)' }, { value: 'AES192', label: 'AES-192' },
  { value: 'AES256', label: 'AES-256' }, { value: 'None', label: 'Aucun (authentification seule)' }]
const BUSY = ['En file', 'En cours']
const blankJob = { name: '', scanType: 'IpSweep', targetRange: '', portList: '', credentialId: '', frequencyHours: 24,
  windowStart: '20:00', windowEnd: '06:00', isActive: true }

function JobForm({ job, credentials, onSaved, onCancel }) {
  const perimeters = usePerimeters().filter(p => ['Admin', 'Manager'].includes(p.role))
  const [f, setF] = useState(() => job ? { ...blankJob, ...job, credentialId: job.credentialId ?? '', portList: job.portList ?? '',
    windowStart: job.windowStart ?? '', windowEnd: job.windowEnd ?? '' } : blankJob)
  const [perimeterId, setPerimeterId] = useState(job?.perimeterId ?? null)
  const [error, setError] = useState('')
  // Un seul périmètre géré (parmi d'autres consultés) : pas de choix à faire, il est retenu d'office.
  useEffect(() => { if (!job && perimeterId === null && perimeters.length === 1) setPerimeterId(perimeters[0].id) }, [perimeters.length])
  const set = patch => setF(prev => ({ ...prev, ...patch }))
  const type = SCAN_TYPES[f.scanType]
  const usable = credentials.filter(c => type.credentials.includes(c.credentialType) && (!perimeterId || c.perimeterId === perimeterId))
  const save = () => {
    const dto = { ...f, credentialId: f.credentialId ? Number(f.credentialId) : null, frequencyHours: Number(f.frequencyHours) || 0,
      windowStart: f.windowStart || null, windowEnd: f.windowEnd || null, perimeterId }
    ;(job ? api.discovery.jobs.update(job.id, dto) : api.discovery.jobs.create(dto)).then(onSaved).catch(e => setError(e.message))
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <h3 className="card-title">{job ? `Modifier « ${job.name} »` : 'Nouvelle tâche de scan'}</h3>
      {error && <div className="error" role="alert">{error}</div>}
      {!job && <PerimeterSelect perimeters={perimeters} value={perimeterId} onChange={setPerimeterId} required />}
      <div className="form-grid">
        <div className="field"><label>Nom *</label>
          <input value={f.name} onChange={e => set({ name: e.target.value })} placeholder="Ex. : Réseau du siège" /></div>
        <div className="field"><label>Type de scan *</label>
          <select value={f.scanType} onChange={e => set({ scanType: e.target.value, credentialId: '' })}>
            {Object.entries(SCAN_TYPES).map(([k, t]) => <option key={k} value={k}>{t.label}</option>)}
          </select>
          <small className="field-hint">{type.help}</small></div>
        <div className="field full"><label>Cible{f.scanType !== 'ActiveDirectory' && ' *'}</label>
          <input value={f.targetRange} onChange={e => set({ targetRange: e.target.value })} placeholder={type.target} /></div>
        {f.scanType === 'IpSweep' && <div className="field full"><label>Ports TCP sondés</label>
          <input value={f.portList} onChange={e => set({ portList: e.target.value })} placeholder="Par défaut : 22,80,443,135,445,3389,5985,9100" />
          <small className="field-hint">Séparés par des virgules, 50 au plus.</small></div>}
        {['Ssh', 'WinRm'].includes(f.scanType) && <div className="field"><label>{f.scanType === 'Ssh' ? 'Port SSH' : 'Port WinRM (HTTPS)'}</label>
          <input value={f.portList} onChange={e => set({ portList: e.target.value })} placeholder={f.scanType === 'Ssh' ? '22' : '5986'} inputMode="numeric" /></div>}
        {type.credentials.length > 0 && <div className="field"><label>Identifiant du coffre{f.scanType !== 'ActiveDirectory' && ' *'}</label>
          <select value={f.credentialId} onChange={e => set({ credentialId: e.target.value })}>
            <option value="">{f.scanType === 'ActiveDirectory' ? 'Compte de service de l’API' : '— Choisir —'}</option>
            {usable.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
          {usable.length === 0 && <small className="field-hint">Aucun identifiant « {type.credentials.map(t => CREDENTIAL_TYPES[t].label).join(' » ou « ')} » : l’Admin l’ajoute dans l’onglet Identifiants.</small>}
        </div>}
        <div className="field"><label>Fréquence (heures)</label>
          <input type="number" min="0" value={f.frequencyHours} onChange={e => set({ frequencyHours: e.target.value })} />
          <small className="field-hint">0 : lancement manuel seulement.</small></div>
        <div className="field"><label>Fenêtre de scan</label>
          <span style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <input type="time" value={f.windowStart} onChange={e => set({ windowStart: e.target.value })} aria-label="Début de la fenêtre" />
            à <input type="time" value={f.windowEnd} onChange={e => set({ windowEnd: e.target.value })} aria-label="Fin de la fenêtre" />
          </span>
          <small className="field-hint">Heure locale ; hors heures ouvrées de préférence. Vide : à toute heure.</small></div>
        <div className="field"><label>État</label>
          <label className="check"><input type="checkbox" checked={f.isActive} onChange={e => set({ isActive: e.target.checked })} /> Active (lancements planifiés)</label></div>
      </div>
      <div className="actions">
        <button className="btn" onClick={save} disabled={!f.name.trim()}>Enregistrer</button>
        <button className="btn ghost" onClick={onCancel}>Annuler</button>
      </div>
    </div>
  )
}

/** Tâches de scan (CFG-46) : planification, lancement immédiat, suivi du dernier passage. */
function Jobs({ isAdmin }) {
  const [jobs, setJobs] = useState(null)
  const [credentials, setCredentials] = useState([])
  const [editing, setEditing] = useState(null)       // null, 'new' ou une tâche
  const [error, setError] = useState('')
  const perimeters = usePerimeters()
  const timer = useRef(null)
  const load = () => api.discovery.jobs.list().then(setJobs).catch(e => setError(e.message))
  useEffect(() => {
    load()
    api.discovery.credentials.list().then(r => setCredentials(r.items)).catch(() => setCredentials([]))
  }, [])
  // Un scan en file ou en cours : l'écran se rafraîchit jusqu'à la fin.
  useEffect(() => {
    clearTimeout(timer.current)
    if (jobs?.some(j => BUSY.includes(j.lastStatus))) timer.current = setTimeout(load, 3000)
    return () => clearTimeout(timer.current)
  }, [jobs])

  const run = j => api.discovery.jobs.run(j.id).then(load).catch(e => setError(e.message))
  const remove = j => window.confirm(`Supprimer la tâche « ${j.name} » ? Les actifs déjà découverts restent dans le sas.`)
    && api.discovery.jobs.remove(j.id).then(load).catch(e => setError(e.message))
  const saved = () => { setEditing(null); load() }

  return (
    <>
      {error && <div className="error" role="alert">{error}</div>}
      {editing ? <JobForm job={editing === 'new' ? null : editing} credentials={credentials} onSaved={saved} onCancel={() => setEditing(null)} />
        : <div className="toolbar"><button className="btn" onClick={() => setEditing('new')}>+ Nouvelle tâche de scan</button>
            <Link className="btn ghost" style={{ marginLeft: 'auto' }} to="/configuration/staging">Ouvrir le sas de staging</Link></div>}
      {jobs?.length === 0 && !editing && (
        <div className="card empty-state">
          <h3>Aucune tâche de scan</h3>
          <p className="muted">Commencez par un balayage IP d’un réseau de /24 : les équipements trouvés arrivent dans le sas de staging, sans rien modifier dans la CMDB.</p>
          <button className="btn" onClick={() => setEditing('new')}>Créer une première tâche</button>
        </div>
      )}
      {jobs?.length > 0 && (
        <div className="table-scroll">
          <table>
            <thead><tr><th>Tâche</th><th>Cible</th><th>Planification</th><th>Dernier passage</th><th></th></tr></thead>
            <tbody>{jobs.map(j => (
              <tr key={j.id}>
                <td><b>{j.name}</b><div className="muted small">{SCAN_TYPES[j.scanType]?.label ?? j.scanType}
                  {j.credential && ` · ${j.credential.name}`}{perimeters.length > 1 && ` · ${perimeterName(perimeters, j.perimeterId)}`}</div></td>
                <td className="mono small">{j.targetRange || 'Annuaire configuré'}</td>
                <td className="small">{!j.isActive ? 'Désactivée' : j.frequencyHours === 0 ? 'Manuelle' : `Toutes les ${j.frequencyHours} h`}
                  {j.isActive && j.frequencyHours > 0 && j.windowStart && <div className="muted">entre {j.windowStart} et {j.windowEnd}</div>}</td>
                <td><span className={'badge ' + (JOB_TONES[j.lastStatus] ?? 't-closed')}>{j.lastStatus}</span>
                  {j.lastRunAt && <div className="muted small">{dateTimeFr(j.lastRunAt)}{j.lastFoundCount != null && ` · ${j.lastFoundCount} équipement(s)`}</div>}
                  {j.lastError && <div className="small" style={{ color: 'var(--accent-red)' }}>{j.lastError}</div>}</td>
                <td className="row-actions">
                  <button className="btn small" onClick={() => run(j)} disabled={BUSY.includes(j.lastStatus)}>
                    {BUSY.includes(j.lastStatus) ? j.lastStatus + '…' : 'Lancer'}</button>
                  <button className="btn ghost small" onClick={() => setEditing(j)}>Modifier</button>
                  {isAdmin && <button className="btn ghost small" onClick={() => remove(j)} aria-label={`Supprimer ${j.name}`}>Supprimer</button>}
                </td>
              </tr>))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}

function CredentialForm({ credential, onSaved, onCancel }) {
  const perimeters = usePerimeters()
  const [f, setF] = useState({ name: credential?.name ?? '', credentialType: credential?.credentialType ?? 'SnmpV2c',
    username: credential?.username ?? '', domain: credential?.domain ?? '', secret: '',
    authProtocol: credential?.authProtocol ?? 'SHA256', privProtocol: credential?.privProtocol ?? 'AES', privacySecret: '',
    sshMethod: 'password', privateKey: '', keyPassphrase: '' })
  const [perimeterId, setPerimeterId] = useState(null)
  const [error, setError] = useState('')
  const set = patch => setF(prev => ({ ...prev, ...patch }))
  const kind = CREDENTIAL_TYPES[f.credentialType]
  // SSH : seul le moyen choisi (mot de passe ou clé) part vers l'API.
  const payload = () => {
    const { sshMethod, ...dto } = f
    if (f.credentialType !== 'Ssh') return { ...dto, privateKey: null, keyPassphrase: null }
    return sshMethod === 'key' ? { ...dto, secret: null } : { ...dto, privateKey: null, keyPassphrase: null }
  }
  const save = () => (credential ? api.discovery.credentials.update(credential.id, payload())
    : api.discovery.credentials.create({ ...payload(), perimeterId })).then(onSaved).catch(e => setError(e.message))
  const sshKey = f.credentialType === 'Ssh' && f.sshMethod === 'key'
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <h3 className="card-title">{credential ? `Modifier « ${credential.name} »` : 'Nouvel identifiant'}</h3>
      {error && <div className="error" role="alert">{error}</div>}
      {!credential && <PerimeterSelect perimeters={perimeters} value={perimeterId} onChange={setPerimeterId} required />}
      <div className="form-grid">
        <div className="field"><label>Nom *</label>
          <input value={f.name} onChange={e => set({ name: e.target.value })} placeholder="Ex. : Communauté SNMP du siège" /></div>
        <div className="field"><label>Type *</label>
          <select value={f.credentialType} onChange={e => set({ credentialType: e.target.value })} disabled={credential?.usedBy > 0}>
            {Object.entries(CREDENTIAL_TYPES).map(([k, t]) => <option key={k} value={k}>{t.label}</option>)}
          </select></div>
        {f.credentialType === 'WindowsDomain' && <>
          <div className="field"><label>Compte *</label>
            <input value={f.username} onChange={e => set({ username: e.target.value })} placeholder="svc-decouverte" autoComplete="off" /></div>
          <div className="field"><label>Domaine</label>
            <input value={f.domain} onChange={e => set({ domain: e.target.value })} placeholder="MONDOMAINE" autoComplete="off" /></div>
        </>}
        {f.credentialType === 'ProxmoxToken' && <div className="field"><label>Identifiant du jeton *</label>
          <input value={f.username} onChange={e => set({ username: e.target.value })} placeholder="inventaire@pve!saloha" autoComplete="off" />
          <small className="field-hint">utilisateur@royaume!nom du jeton ; rôle PVEAuditor suffisant.</small></div>}
        {f.credentialType === 'VCenter' && <div className="field"><label>Compte *</label>
          <input value={f.username} onChange={e => set({ username: e.target.value })} placeholder="inventaire@vsphere.local" autoComplete="off" />
          <small className="field-hint">Rôle Lecture seule suffisant.</small></div>}
        {f.credentialType === 'Ssh' && <>
          <div className="field"><label>Compte *</label>
            <input value={f.username} onChange={e => set({ username: e.target.value })} placeholder="svc-inventaire" autoComplete="off" />
            <small className="field-hint">Compte sans droits d’administration : l’inventaire n’utilise pas sudo.</small></div>
          <div className="field"><label>Authentification</label>
            <select value={f.sshMethod} onChange={e => set({ sshMethod: e.target.value })} aria-label="Authentification SSH">
              <option value="password">Mot de passe</option>
              <option value="key">Clé privée</option>
            </select></div>
        </>}
        {sshKey && <>
          <div className="field full"><label>Clé privée{!credential && ' *'}</label>
            <textarea rows={5} value={f.privateKey} onChange={e => set({ privateKey: e.target.value })} spellCheck={false}
              placeholder={credential ? 'Laisser vide pour conserver la clé actuelle' : '-----BEGIN OPENSSH PRIVATE KEY-----'} aria-label="Clé privée" />
            <small className="field-hint">OpenSSH ou PEM ; chiffrée dès l’enregistrement, jamais réaffichée.</small></div>
          <div className="field"><label>Phrase de la clé</label>
            <input type="password" value={f.keyPassphrase} onChange={e => set({ keyPassphrase: e.target.value })} autoComplete="new-password"
              placeholder="Si la clé est protégée" aria-label="Phrase de la clé" /></div>
        </>}
        {f.credentialType === 'SnmpV3' && <>
          <div className="field"><label>Utilisateur (securityName) *</label>
            <input value={f.username} onChange={e => set({ username: e.target.value })} placeholder="svc-snmp" autoComplete="off" /></div>
          <div className="field"><label>Authentification</label>
            <select value={f.authProtocol} onChange={e => set({ authProtocol: e.target.value })}>
              {V3_AUTH.map(p => <option key={p.value} value={p.value}>{p.label}</option>)}
            </select></div>
          <div className="field"><label>Chiffrement</label>
            <select value={f.privProtocol} onChange={e => set({ privProtocol: e.target.value })}>
              {V3_PRIV.map(p => <option key={p.value} value={p.value}>{p.label}</option>)}
            </select></div>
        </>}
        {!sshKey && <div className="field"><label>{kind.secret}{!credential && ' *'}</label>
          <input type="password" value={f.secret} onChange={e => set({ secret: e.target.value })} autoComplete="new-password"
            placeholder={credential ? 'Laisser vide pour conserver l’actuel' : ''} />
          <small className="field-hint">Chiffré dès l’enregistrement ; jamais réaffiché.{f.credentialType === 'SnmpV3' && ' 8 caractères au moins.'}</small></div>}
        {f.credentialType === 'SnmpV3' && f.privProtocol !== 'None' && (
          <div className="field"><label>Phrase de chiffrement{!credential && ' *'}</label>
            <input type="password" value={f.privacySecret} onChange={e => set({ privacySecret: e.target.value })} autoComplete="new-password"
              placeholder={credential ? 'Laisser vide pour conserver l’actuelle' : ''} />
            <small className="field-hint">8 caractères au moins.</small></div>
        )}
      </div>
      <div className="actions">
        <button className="btn" onClick={save} disabled={!f.name.trim() || (!credential && !(sshKey ? f.privateKey.trim() : f.secret))}>Enregistrer</button>
        <button className="btn ghost" onClick={onCancel}>Annuler</button>
      </div>
    </div>
  )
}

/** Coffre d'identifiants (CFG-45), Admin : communautés SNMP v2c, utilisateurs SNMP v3, comptes de domaine. */
function Credentials() {
  const [data, setData] = useState(null)
  const [editing, setEditing] = useState(null)
  const [error, setError] = useState('')
  const load = () => api.discovery.credentials.list().then(setData).catch(e => setError(e.message))
  useEffect(() => { load() }, [])
  const remove = c => window.confirm(`Supprimer « ${c.name} » du coffre ?`)
    && api.discovery.credentials.remove(c.id).then(load).catch(e => setError(e.message))
  if (!data) return error ? <div className="error">{error}</div> : <p>Chargement…</p>
  return (
    <>
      {error && <div className="error" role="alert">{error}</div>}
      {!data.vaultConfigured && <div className="notice warn">
        Le coffre n’est pas configuré : définir <code>Discovery:VaultKey</code> (32 caractères au moins) dans la configuration de l’API
        avant d’enregistrer un identifiant. Ne plus la changer ensuite : les secrets déjà chiffrés deviendraient illisibles.
      </div>}
      {editing ? <CredentialForm credential={editing === 'new' ? null : editing} onSaved={() => { setEditing(null); load() }} onCancel={() => setEditing(null)} />
        : <div className="toolbar"><button className="btn" onClick={() => setEditing('new')} disabled={!data.vaultConfigured}>+ Nouvel identifiant</button></div>}
      <div className="table-scroll">
        <table>
          <thead><tr><th>Identifiant</th><th>Type</th><th>Compte</th><th>Secret</th><th>Utilisé par</th><th></th></tr></thead>
          <tbody>
            {data.items.map(c => (
              <tr key={c.id}>
                <td><b>{c.name}</b></td>
                <td>{CREDENTIAL_TYPES[c.credentialType]?.label ?? c.credentialType}{c.credentialType === 'SnmpV3' && <div className="muted small">{c.authProtocol} · {c.privProtocol === 'None' ? 'sans chiffrement' : c.privProtocol}</div>}</td>
                <td>{c.username ? `${c.domain ? c.domain + '\\' : ''}${c.username}` : '—'}</td>
                <td className="small">{c.hasSecret ? <>Enregistré, chiffré{c.secretChangedAt && <div className="muted">changé le {dateFr(c.secretChangedAt)}</div>}</> : 'Absent'}</td>
                <td>{c.usedBy ? `${c.usedBy} tâche(s)` : '—'}</td>
                <td className="row-actions">
                  <button className="btn ghost small" onClick={() => setEditing(c)}>Modifier ou remplacer le secret</button>
                  <button className="btn ghost small" onClick={() => remove(c)} disabled={c.usedBy > 0}
                    title={c.usedBy > 0 ? 'Utilisé par des tâches de scan' : undefined}>Supprimer</button>
                </td>
              </tr>))}
            {data.items.length === 0 && <tr><td colSpan={6} className="muted">Aucun identifiant. Un scan SNMP exige une communauté v2c ou un utilisateur v3 ; un scan de l’annuaire peut utiliser un compte de domaine dédié.</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  )
}

/** Fins de support (CFG-47) : produits et date de fin de support, rapprochés du système des CI. */
function EndOfLife({ canEdit }) {
  const [rules, setRules] = useState(null)
  const [f, setF] = useState({ product: '', endOfSupport: '', notes: '' })
  const [error, setError] = useState('')
  const load = () => api.discovery.endOfLife.list().then(setRules).catch(e => setError(e.message))
  useEffect(() => { load() }, [])
  const add = () => api.discovery.endOfLife.create({ ...f, endOfSupport: f.endOfSupport || null })
    .then(() => { setF({ product: '', endOfSupport: '', notes: '' }); setError(''); load() }).catch(e => setError(e.message))
  const remove = r => window.confirm(`Retirer la fin de support de « ${r.product} » ?`)
    && api.discovery.endOfLife.remove(r.id).then(load).catch(e => setError(e.message))
  const today = new Date().toISOString().slice(0, 10)
  return (
    <>
      <p className="muted small" style={{ marginBottom: 12 }}>
        Un CI dont le système, le constructeur ou le modèle contient le produit est en fin de support à cette date ; il apparaît
        dans l’inventaire et le tableau de bord (obsolescence, NIS 2). Nommer le produit comme il figure dans le système des CI.
      </p>
      {error && <div className="error" role="alert">{error}</div>}
      {canEdit && (
        <div className="toolbar">
          <input style={{ maxWidth: 280 }} placeholder="Produit, ex. Windows Server 2012" value={f.product}
            onChange={e => setF({ ...f, product: e.target.value })} aria-label="Produit" />
          <input type="date" style={{ maxWidth: 170 }} value={f.endOfSupport} onChange={e => setF({ ...f, endOfSupport: e.target.value })} aria-label="Fin de support" />
          <input style={{ maxWidth: 260 }} placeholder="Note (facultatif)" value={f.notes} onChange={e => setF({ ...f, notes: e.target.value })} aria-label="Note" />
          <button className="btn" onClick={add} disabled={!f.product.trim() || !f.endOfSupport}>Ajouter</button>
        </div>
      )}
      <div className="table-scroll">
        <table className="compact">
          <thead><tr><th>Produit</th><th>Fin de support</th><th>Note</th><th></th></tr></thead>
          <tbody>
            {rules?.map(r => (
              <tr key={r.id}>
                <td><b>{r.product}</b></td>
                <td>{dateFr(r.endOfSupport)} {r.endOfSupport.slice(0, 10) < today && <span className="badge t-bad">Dépassée</span>}</td>
                <td className="small">{r.notes ?? '—'}</td>
                <td className="row-actions">{canEdit && <button className="btn ghost small" onClick={() => remove(r)}>Retirer</button>}</td>
              </tr>))}
            {rules?.length === 0 && <tr><td colSpan={4} className="muted">Aucune fin de support enregistrée.</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  )
}

/**
 * Clés d'hôte SSH (CFG-53) : retenues au premier contact. Une clé changée bloque l'inventaire de
 * l'hôte (usurpation possible) jusqu'à ce que l'Admin, après vérification, l'accepte.
 */
function SshHostKeys({ isAdmin }) {
  const [keys, setKeys] = useState(null)
  const [error, setError] = useState('')
  const load = () => api.discovery.sshHostKeys.list().then(setKeys).catch(e => setError(e.message))
  useEffect(() => { load() }, [])
  const accept = k => window.confirm(k.protocol === 'tls'
    ? `Accepter le nouveau certificat de ${k.host} ? Vérifier d’abord son empreinte SHA-256 sur l’hyperviseur.`
    : `Accepter la nouvelle clé de ${k.host} ? Vérifier d’abord son empreinte sur le serveur (ssh-keygen -lf).`)
    && api.discovery.sshHostKeys.accept(k.id).then(load).catch(e => setError(e.message))
  const forget = k => window.confirm(`Oublier ${k.host} ? Sa prochaine clé sera retenue sans contrôle.`)
    && api.discovery.sshHostKeys.forget(k.id).then(load).catch(e => setError(e.message))
  return (
    <>
      <p className="muted small" style={{ marginBottom: 12 }}>
        La clé de chaque serveur SSH, et le certificat d’un hyperviseur que l’autorité de certification ne garantit pas (autosigné), sont
        retenus au premier contact. S’ils changent (réinstallation, ou serveur usurpé), l’identifiant n’est pas envoyé et l’hôte n’est
        plus inventorié tant que l’Admin n’a pas accepté la nouvelle clé.
      </p>
      {error && <div className="error" role="alert">{error}</div>}
      <div className="table-scroll">
        <table className="compact">
          <thead><tr><th>Hôte</th><th>Type</th><th>Clé retenue</th><th>État</th><th>Vue le</th><th></th></tr></thead>
          <tbody>
            {keys?.map(k => (
              <tr key={k.id}>
                <td><b>{k.host}</b>{k.port !== 22 && <span className="muted">:{k.port}</span>}</td>
                <td className="small">{k.protocol === 'tls' ? 'Certificat TLS' : 'Clé SSH'}</td>
                <td className="small"><code>{k.algorithm} SHA256:{k.fingerprint}</code></td>
                <td>{k.changed
                  ? <><span className="badge t-bad">{k.protocol === 'tls' ? 'Certificat changé' : 'Clé changée'}</span><div className="small"><code>{k.pendingAlgorithm} SHA256:{k.pendingFingerprint}</code></div></>
                  : <span className="badge t-done">De confiance</span>}</td>
                <td className="small">{dateTimeFr(k.changed ? k.pendingSeenAt : k.lastSeenAt)}</td>
                <td className="row-actions">{isAdmin && <>
                  {k.changed && <button className="btn small" onClick={() => accept(k)}>{k.protocol === 'tls' ? 'Accepter le nouveau certificat' : 'Accepter la nouvelle clé'}</button>}
                  <button className="btn ghost small" onClick={() => forget(k)}>Oublier</button>
                </>}</td>
              </tr>))}
            {keys?.length === 0 && <tr><td colSpan={6} className="muted">Aucune clé ni aucun certificat retenu.</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  )
}

const IEEE_OUI_URL = 'https://standards-oui.ieee.org/oui/oui.csv'

/**
 * Constructeurs par préfixe MAC (CFG-50) : la base n'est pas fournie avec S-Aloha ; l'Admin
 * importe le registre public de l'IEEE, et l'importe de nouveau pour la tenir à jour.
 */
function MacVendors({ isAdmin }) {
  const [status, setStatus] = useState(null)
  const [report, setReport] = useState(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const load = () => api.discovery.macVendors.get().then(setStatus).catch(e => setError(e.message))
  useEffect(() => { load() }, [])
  const importFile = async file => {
    if (!file) return
    setBusy(true); setReport(null); setError('')
    try {
      setReport(await api.discovery.macVendors.import(await file.text()))
      await load()
    } catch (e) { setError(e.message) } finally { setBusy(false) }
  }
  return (
    <>
      <p className="muted small" style={{ marginBottom: 12 }}>
        Le constructeur d’un équipement découvert sans SNMP se déduit du préfixe de son adresse MAC (OUI). Les adresses
        aléatoires (téléphones, machines virtuelles) n’en ont pas. La base vient du registre public de
        l’<a href={IEEE_OUI_URL} target="_blank" rel="noreferrer">IEEE</a> (fichier <code>oui.csv</code>) ; un nouvel import la remplace.
      </p>
      {error && <div className="error" role="alert">{error}</div>}
      {status && (
        <p style={{ marginBottom: 12 }}>
          {status.count === 0
            ? 'Aucun préfixe importé : le constructeur ne vient que de SNMP et des bannières.'
            : <><b>{status.count.toLocaleString('fr-FR')}</b> préfixe(s) · dernier import le {dateTimeFr(status.lastImport)}</>}
        </p>
      )}
      {isAdmin
        ? <label className="btn ghost" style={{ display: 'inline-flex', cursor: busy ? 'wait' : 'pointer' }}>
            {busy ? 'Import en cours…' : 'Importer oui.csv'}
            <input type="file" accept=".csv,text/csv" hidden disabled={busy} aria-label="Fichier oui.csv"
              onChange={e => { importFile(e.target.files[0]); e.target.value = '' }} />
          </label>
        : <p className="muted small">L’import est réservé aux administrateurs.</p>}
      {report && (
        <div className="notice ok" role="status" style={{ marginTop: 12 }}>
          Import terminé : {report.imported.toLocaleString('fr-FR')} préfixe(s), {report.skipped} ligne(s) écartée(s) (autre registre ou mal formée).
        </div>
      )}
    </>
  )
}

/**
 * Paramétrage → Découverte du réseau (Manager, Admin) : tâches de scan, coffre
 * d'identifiants (Admin) et référentiel des fins de support.
 */
export default function DiscoverySettings() {
  const [params, setParams] = useSearchParams()
  const role = getUser()?.role
  const isAdmin = role === 'Admin'
  const canEditEol = useManagesPerimeter()
  const tabs = [
    { id: 'jobs', label: 'Tâches de scan' },
    isAdmin && { id: 'credentials', label: 'Identifiants (coffre)' },
    { id: 'eol', label: 'Fins de support' },
    { id: 'ssh', label: 'Clés et certificats' },
    { id: 'oui', label: 'Constructeurs (OUI)' },
  ].filter(Boolean)
  const tab = tabs.find(t => t.id === params.get('tab')) ?? tabs[0]
  return (
    <>
      <h1 className="page-title">Découverte du réseau</h1>
      <p className="page-sub">
        Ce que la découverte parcourt ({Object.values(SOURCE_LABELS).join(', ')}), avec quels identifiants et à quelle heure ;
        les équipements trouvés attendent dans le sas de staging.
      </p>
      <div className="tabs" role="tablist">
        {tabs.map(t => <button key={t.id} role="tab" aria-selected={t.id === tab.id} className={t.id === tab.id ? 'on' : ''}
          onClick={() => setParams(t.id === 'jobs' ? {} : { tab: t.id }, { replace: true })}>{t.label}</button>)}
      </div>
      {tab.id === 'jobs' && <Jobs isAdmin={isAdmin} />}
      {tab.id === 'credentials' && <Credentials />}
      {tab.id === 'eol' && <EndOfLife canEdit={canEditEol} />}
      {tab.id === 'ssh' && <SshHostKeys isAdmin={isAdmin} />}
      {tab.id === 'oui' && <MacVendors isAdmin={isAdmin} />}
    </>
  )
}
