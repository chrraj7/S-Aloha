import React, { useEffect, useState } from 'react'
import { api, getUser } from '../../../api'
import RecordDetail from '../../../core/components/RecordDetail'
import useManagerOn from '../../../core/useManagerOn'
import Markdown from '../../../core/components/Markdown'
import { dateFr } from '../../../core/fields'
import { knowledgeConfig } from '../config'

/** KB-13 : « utile / pas utile », un vote par lecteur, modifiable, sur la version publiée. */
export function Feedback({ article }) {
  const [vote, setVote] = useState(null)
  const [counts, setCounts] = useState({ helpful: article.helpfulCount, notHelpful: article.notHelpfulCount })
  const [error, setError] = useState('')
  useEffect(() => { if (article.status === 'Publié') api.knowledge.myFeedback(article.id).then(r => setVote(r.myVote)).catch(() => {}) }, [article.id, article.version])
  if (article.status !== 'Publié') return null
  const send = helpful => api.knowledge.feedback(article.id, helpful)
    .then(r => { setVote(r.myVote); setCounts({ helpful: r.view.helpfulCount, notHelpful: r.view.notHelpfulCount }); setError('') })
    .catch(e => setError(e.message))
  return (
    <div className="toolbar" style={{ marginTop: 16 }} aria-label="Retour des lecteurs">
      <span className="small">Cet article vous a-t-il aidé ?</span>
      <button className={'btn small' + (vote === true ? '' : ' ghost')} aria-pressed={vote === true} onClick={() => send(true)}>Utile ({counts.helpful})</button>
      <button className={'btn small' + (vote === false ? '' : ' ghost')} aria-pressed={vote === false} onClick={() => send(false)}>Pas utile ({counts.notHelpful})</button>
      {error && <span className="error small">{error}</span>}
    </div>
  )
}

/** Onglet Lecture : l'article tel que le lecteur le voit, contenu rendu en Markdown (KB-10). */
function Reading({ article }) {
  return (
    <article className="card">
      {article.description && <p style={{ color: 'var(--text-secondary)', marginBottom: 16 }}>{article.description}</p>}
      <Markdown source={article.content}
        empty={<p style={{ color: 'var(--muted)' }}>Aucun contenu rédigé pour l'instant.</p>} />
      {article.version > 0 && <p className="muted small" style={{ marginTop: 12 }}>Version {article.version}{article.publishedAt && ` publiée le ${dateFr(article.publishedAt)} par ${article.publishedBy}`}.</p>}
      <Feedback article={article} />
    </article>
  )
}

/** KB-15 : versions publiées, de la plus récente à la plus ancienne ; chacune se relit. */
export function Versions({ article }) {
  const [versions, setVersions] = useState(null)
  const [open, setOpen] = useState(null)
  useEffect(() => { api.knowledge.versions(article.id).then(setVersions).catch(() => setVersions([])) }, [article.id, article.version])
  if (!versions?.length) return null
  return (
    <section className="card" style={{ marginTop: 16 }} aria-label="Versions publiées">
      <h3 className="card-title">Versions publiées</h3>
      <ul className="small">{versions.map(v => (
        <li key={v.version}>
          <button className="btn ghost small" onClick={() => setOpen(open === v.version ? null : v.version)} aria-expanded={open === v.version}>
            Version {v.version}</button> publiée le {dateFr(v.publishedAt)} par {v.publishedBy} · rédaction : {v.authors || '—'}
          {open === v.version && <div className="card" style={{ marginTop: 8 }}><b>{v.title}</b><Markdown source={v.content} /></div>}
        </li>))}</ul>
    </section>
  )
}

/**
 * Cycle de l'article : soumission à publication (KB-21), revue par le
 * responsable ou demande d'archivage (KB-17), utilisation (KB-14). L'API
 * reste juge des droits ; les boutons ne s'affichent qu'à qui peut agir.
 */
export function Lifecycle({ article, onSaved }) {
  const user = getUser()
  // PER-06 : gestionnaire du périmètre de l'article (`OwnsOrManages` de l'API).
  const isManager = useManagerOn(article.perimeterId)
  // Un groupe responsable : l'appartenance est vérifiée par l'API (SOC-10).
  const owns = isManager || article.ownerType === 'Group'
    || (article.ownerId ?? '').toLowerCase() === (user?.username ?? '').toLowerCase()
  const [usage, setUsage] = useState(null)
  const [next, setNext] = useState('')
  const [reason, setReason] = useState('')
  const [msg, setMsg] = useState(null)
  useEffect(() => { api.knowledge.usage(article.id).then(setUsage).catch(() => setUsage(null)) }, [article.id])

  const act = (call, ok) => call.then(() => { setMsg({ ok: true, text: ok }); onSaved() })
    .catch(e => setMsg({ ok: false, text: e.message }))
  const reviewDue = article.reviewDate && article.reviewDate.slice(0, 10) <= new Date().toISOString().slice(0, 10)

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <h3 style={{ color: 'var(--navy)', marginBottom: 8 }}>Cycle de l'article</h3>
      {msg && <div className="error" style={msg.ok ? { background: 'var(--blue-soft)', color: 'var(--navy)' } : undefined}>{msg.text}</div>}
      {usage && <p style={{ fontSize: 13, color: 'var(--muted)', marginBottom: 10 }}>
        Utilisation : {usage.incidents} incident(s) et {usage.requests} demande(s) reliés à cet article.</p>}

      {article.status === 'Brouillon' && (article.submittedAt
        ? <p style={{ fontSize: 13 }}>Soumis à publication par {article.submittedBy} le {dateFr(article.submittedAt)} : en attente d'un gestionnaire.</p>
        : !isManager && <button className="btn ghost" onClick={() => act(api.knowledge.submit(article.id),
            'Article soumis : il apparaît dans les décisions des gestionnaires.')}>Soumettre à publication</button>)}

      {article.status === 'Publié' && owns && <>
        <p style={{ fontSize: 13, marginBottom: 8 }}>
          Prochaine revue : {dateFr(article.reviewDate)}{reviewDue && <b> — échue</b>}.
          {article.archiveRequestedAt && <> Archivage demandé par {article.archiveRequestedBy} : « {article.archiveRequestReason} ».</>}
        </p>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div className="field"><label>Prochaine revue (défaut : dans un an)</label>
            <input type="date" value={next} onChange={e => setNext(e.target.value)} /></div>
          <button className="btn ghost" onClick={() => act(api.knowledge.review(article.id, next ? new Date(next).toISOString() : null),
            'Revue enregistrée : l\'article reste publié.')}>Confirmer l'article</button>
        </div>
        {!article.archiveRequestedAt && <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end', marginTop: 10 }}>
          <div className="field" style={{ flex: 1 }}><label>Motif d'archivage</label>
            <input value={reason} onChange={e => setReason(e.target.value)} placeholder="Ex. : procédure remplacée, outil retiré" /></div>
          <button className="btn ghost" disabled={!reason.trim()} onClick={() => act(api.knowledge.requestArchive(article.id, reason),
            'Archivage demandé : un gestionnaire décidera.')}>Demander l'archivage</button>
        </div>}
      </>}
    </div>
  )
}

export default function ArticleDetail() {
  return (
    <RecordDetail config={knowledgeConfig} tab={{ label: 'Lecture', render: r => <Reading article={r} /> }}>
      {(r, reload) => <><Lifecycle article={r} onSaved={reload} /><Versions article={r} /></>}
    </RecordDetail>
  )
}
