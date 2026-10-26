import React, { useEffect, useState } from 'react'

// Graphiques du tableau de bord (guide de visualisation) : traits de 2 px,
// grille fine, légende dès deux séries, étiquettes directes en bout de courbe,
// réticule au survol et vue tableau. Couleurs : --chart-1 à --chart-3,
// palette catégorielle validée en clair et en sombre (styles.css).

const day = (iso) => new Date(iso).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' })

/** Hauteur fixe des graphiques : le tracé suit la largeur de sa carte, jamais le texte. */
const CHART_H = 220

/**
 * Largeur réelle du conteneur, pour dessiner le SVG à l'échelle 1 : un viewBox
 * étiré agrandirait aussi les graduations (18 px sur une carte pleine largeur).
 * Le nœud passe par un callback ref : le conteneur n'existe pas encore quand
 * le graphique est monté sans données, et il est recréé après la vue tableau.
 * Sans ResizeObserver (tests), largeur de repli.
 */
function useWidth(fallback = 640) {
  const [node, setNode] = useState(null)
  const [width, setWidth] = useState(fallback)
  useEffect(() => {
    if (!node || typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(([e]) => { const w = Math.round(e.contentRect.width); if (w > 0) setWidth(w) })
    ro.observe(node)
    return () => ro.disconnect()
  }, [node])
  return [setNode, width]
}

/**
 * Regroupe des comptes journaliers par semaine au-delà de 45 jours : 90 ou
 * 365 barres d'un pixel ne se lisent pas. Une semaine porte la date de son
 * premier jour.
 */
export function bucketDays(days, keys) {
  if (days.length <= 45) return { rows: days, unit: 'jour' }
  const rows = []
  days.forEach((d, i) => {
    if (i % 7 === 0) rows.push({ date: d.date, ...Object.fromEntries(keys.map(k => [k, 0])) })
    const w = rows[rows.length - 1]
    keys.forEach(k => { w[k] += d[k] ?? 0 })
  })
  return { rows, unit: 'semaine' }
}

/** Graduations rondes (0, 2, 5, 10…), prolongées jusqu'à la graduation qui couvre le maximum. */
export function ticks(max) {
  const step = max <= 10 ? 2 : max <= 25 ? 5 : max <= 50 ? 10 : Math.ceil(max / 5 / 10) * 10
  const n = Math.ceil(Math.max(max, 1) / step)
  return Array.from({ length: n + 1 }, (_, i) => i * step)
}

/**
 * Courbes journalières. `series` : [{ key, label, slot }] — `slot` 1 à 3 choisit
 * la couleur ; chaque série porte aussi son étiquette en bout de courbe : la
 * couleur n'est jamais le seul repère.
 */
export function LineChart({ title, days, series, unit = '' }) {
  const [hover, setHover] = useState(null)
  const [table, setTable] = useState(false)
  const [ref, W] = useWidth()
  const H = CHART_H, M = { top: 12, right: 72, bottom: 26, left: 32 }
  if (!days?.length) return <ChartCard title={title}><p className="muted small">Aucune donnée sur la période.</p></ChartCard>

  const yTicks = ticks(Math.max(...days.flatMap(d => series.map(s => d[s.key]))))
  const top = yTicks[yTicks.length - 1]
  const x = (i) => M.left + (days.length === 1 ? 0 : (i * (W - M.left - M.right)) / (days.length - 1))
  const y = (v) => M.top + (1 - v / top) * (H - M.top - M.bottom)
  const path = (key) => days.map((d, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(d[key]).toFixed(1)}`).join('')
  const labelEvery = Math.ceil(days.length / Math.max(2, Math.floor((W - M.left - M.right) / 64)))
  const last = days.length - 1
  // Étiquettes de fin écartées d'au moins 13 px pour rester lisibles quand les courbes se rejoignent.
  const ends = series.map(s => ({ s, py: y(days[last][s.key]) })).sort((a, b) => a.py - b.py)
  ends.forEach((e, i) => { if (i && e.py - ends[i - 1].py < 13) e.py = ends[i - 1].py + 13 })
  // Puis remontées en bloc si la dernière sort du cadre (séries toutes à zéro).
  const overflow = ends.length ? ends[ends.length - 1].py - (H - M.bottom - 2) : 0
  if (overflow > 0) ends.forEach(e => { e.py -= overflow })

  const onMove = (e) => {
    const r = e.currentTarget.getBoundingClientRect()
    const px = ((e.clientX - r.left) / r.width) * W
    const i = Math.round(((px - M.left) / (W - M.left - M.right)) * last)
    setHover(Math.max(0, Math.min(last, i)))
  }
  const h = hover === null ? null : days[hover]

  return (
    <ChartCard title={title} onToggle={() => setTable(v => !v)} table={table}
      legend={series.map(s => <span key={s.key}><i className={`key c${s.slot}`} />{s.label}</span>)}>
      {table ? <DataTable first="Jour" rows={days} series={series} /> : (
        <div className="chart-wrap" ref={ref}>
          <svg viewBox={`0 0 ${W} ${H}`} width={W} height={H} role="img" onMouseMove={onMove} onMouseLeave={() => setHover(null)}
            aria-label={`${title} : ${series.map(s => `${s.label} ${days[last][s.key]}${unit}`).join(', ')}`}>
            {yTicks.map(t => (
              <g key={t}>
                <line className="gridline" x1={M.left} x2={W - M.right} y1={y(t)} y2={y(t)} />
                <text className="tick" x={M.left - 6} y={y(t) + 4} textAnchor="end">{t}</text>
              </g>))}
            {days.map((d, i) => i % labelEvery === 0 &&
              <text key={d.date} className="tick" x={x(i)} y={H - 8} textAnchor="middle">{day(d.date)}</text>)}
            {series.map(s => <path key={s.key} className={`line c${s.slot}`} d={path(s.key)} />)}
            {ends.map(({ s, py }) => (
              <g key={s.key}>
                <circle className={`end-dot c${s.slot}`} cx={x(last)} cy={y(days[last][s.key])} r="4" />
                <text className="end-label" x={x(last) + 9} y={py + 4}>{days[last][s.key]}{unit} {s.short ?? ''}</text>
              </g>))}
            {h && <line className="crosshair" x1={x(hover)} x2={x(hover)} y1={M.top} y2={H - M.bottom} />}
            <rect x={M.left} y={M.top} width={W - M.left - M.right} height={H - M.top - M.bottom} fill="transparent" />
          </svg>
          {h && (
            <div className="chart-tip" style={{ left: `${(x(hover) / W) * 100}%` }} role="status">
              <b>{day(h.date)}</b>
              {series.map(s => <div key={s.key}><i className={`key c${s.slot}`} />{s.label} : {h[s.key]}{unit}</div>)}
            </div>
          )}
        </div>
      )}
    </ChartCard>
  )
}

function DataTable({ first, rows, series }) {
  return (
    <div className="table-scroll">
      <table className="compact">
        <thead><tr><th>{first}</th>{series.map(s => <th key={s.key}>{s.label}</th>)}</tr></thead>
        <tbody>{rows.map(d => <tr key={d.date}><td>{day(d.date)}</td>{series.map(s => <td key={s.key}>{d[s.key]}</td>)}</tr>)}</tbody>
      </table>
    </div>
  )
}

/**
 * Colonnes empilées : des comptes par jour (ou par semaine, voir bucketDays)
 * se lisent en barres, pas en courbe — une courbe de 0, 1, 2 zigzague. Total
 * de chaque série dans la légende ; détail au survol.
 */
export function ColumnChart({ title, days, series }) {
  const [hover, setHover] = useState(null)
  const [table, setTable] = useState(false)
  const [ref, W] = useWidth()
  const H = CHART_H, M = { top: 12, right: 8, bottom: 26, left: 32 }
  if (!days?.length) return <ChartCard title={title}><p className="muted small">Aucune donnée sur la période.</p></ChartCard>

  const { rows, unit } = bucketDays(days, series.map(s => s.key))
  const totals = Object.fromEntries(series.map(s => [s.key, rows.reduce((n, d) => n + (d[s.key] ?? 0), 0)]))
  // Tout à zéro : un axe vide n'apprend rien, on le dit.
  if (Object.values(totals).every(t => t === 0)) return <ChartCard title={title}><p className="muted small">Aucune donnée sur la période.</p></ChartCard>
  const sum = (d) => series.reduce((n, s) => n + (d[s.key] ?? 0), 0)
  const yTicks = ticks(Math.max(...rows.map(sum)))
  const top = yTicks[yTicks.length - 1]
  const band = (W - M.left - M.right) / rows.length
  const gap = Math.max(1, Math.min(8, band * 0.3))
  const y = (v) => M.top + (1 - v / top) * (H - M.top - M.bottom)
  // Segments empilés de bas en haut, dans l'ordre des séries (et de la légende).
  const stack = (d) => series.reduce((acc, s) => {
    const base = acc.length ? acc[acc.length - 1].to : 0
    return [...acc, { s, from: base, to: base + (d[s.key] ?? 0) }]
  }, [])
  const labelEvery = Math.ceil(rows.length / Math.max(2, Math.floor((W - M.left - M.right) / 64)))
  const onMove = (e) => {
    const r = e.currentTarget.getBoundingClientRect()
    const i = Math.floor(((e.clientX - r.left) - M.left) / band)
    setHover(i >= 0 && i < rows.length ? i : null)
  }
  const h = hover === null ? null : rows[hover]

  return (
    <ChartCard title={title} onToggle={() => setTable(v => !v)} table={table}
      legend={series.map(s => <span key={s.key}><i className={`swatch c${s.slot}`} />{s.label} <b>{totals[s.key]}</b></span>)}>
      {table ? <DataTable first={unit === 'jour' ? 'Jour' : 'Semaine du'} rows={rows} series={series} /> : (
        <div className="chart-wrap" ref={ref}>
          <svg viewBox={`0 0 ${W} ${H}`} width={W} height={H} role="img" onMouseMove={onMove} onMouseLeave={() => setHover(null)}
            aria-label={`${title}, par ${unit} : ${series.map(s => `${s.label} ${totals[s.key]} au total`).join(', ')}`}>
            {yTicks.map(t => (
              <g key={t}>
                <line className="gridline" x1={M.left} x2={W - M.right} y1={y(t)} y2={y(t)} />
                <text className="tick" x={M.left - 6} y={y(t) + 4} textAnchor="end">{t}</text>
              </g>))}
            {h && <rect className="col-hover" x={M.left + hover * band} y={M.top} width={band} height={H - M.top - M.bottom} />}
            {rows.map((d, i) => (
              <g key={d.date}>
                {stack(d).map(({ s, from, to }) => to > from &&
                  <rect key={s.key} className={`col c${s.slot}`} x={M.left + i * band + gap / 2} y={y(to)}
                    width={Math.max(1, band - gap)} height={y(from) - y(to) - (from > 0 ? 1 : 0)} />)}
                {i % labelEvery === 0 &&
                  <text className="tick" x={M.left + i * band + band / 2} y={H - 8} textAnchor="middle">{day(d.date)}</text>}
              </g>))}
            <line className="baseline" x1={M.left} x2={W - M.right} y1={y(0)} y2={y(0)} />
          </svg>
          {h && (
            <div className="chart-tip" style={{ left: `${((M.left + (hover + 0.5) * band) / W) * 100}%` }} role="status">
              <b>{unit === 'jour' ? day(h.date) : `Semaine du ${day(h.date)}`}</b>
              {series.map(s => <div key={s.key}><i className={`swatch c${s.slot}`} />{s.label} : {h[s.key]}</div>)}
            </div>
          )}
        </div>
      )}
    </ChartCard>
  )
}

/** Cadre commun : titre, légende, bascule graphique / données. */
export function ChartCard({ title, legend, onToggle, table, children, note }) {
  return (
    <figure className="card chart-card">
      <figcaption>
        <h3>{title}</h3>
        {onToggle && <button className="btn ghost small no-print" onClick={onToggle}>{table ? 'Voir le graphique' : 'Voir les données'}</button>}
      </figcaption>
      {legend && <div className="chart-legend">{legend}</div>}
      {children}
      {note && <p className="muted small chart-note">{note}</p>}
    </figure>
  )
}

/**
 * Barres horizontales étiquetées (valeur écrite au bout de chaque barre) :
 * répartitions, entonnoir. `data` : [{ label, count, hint? }].
 */
export function HBars({ title, data, empty = 'Aucune donnée sur la période.', unit = '', max, note, tone }) {
  const top = max ?? Math.max(1, ...data.map(d => d.count))
  return (
    <ChartCard title={title} note={note}>
      {data.length === 0 && <p className="muted small">{empty}</p>}
      <div className="hbars">
        {data.map(d => (
          <div className="bar-row" key={d.label}>
            <span className="lbl" title={d.label}>{d.label}</span>
            <div className="bar-track"><div className={`bar ${tone ?? ''}`} style={{ width: `${(d.count / top) * 100}%` }} /></div>
            <span className="val">{d.count}{unit}{d.hint && <small className="muted"> {d.hint}</small>}</span>
          </div>
        ))}
      </div>
    </ChartCard>
  )
}

/**
 * Chiffre clé : libellé au-dessus, chiffre, puis précision. `alert` : liseré
 * et chiffre rouges, et la mention écrite de l'alerte (la couleur n'est
 * jamais le seul repère).
 */
export function Kpi({ value, label, sub, alert, suffix = '' }) {
  return (
    <div className={`kpi${alert ? ' alert' : ''}${value == null ? ' empty' : ''}`}>
      <div className="l">{label}</div>
      <div className="n">{value ?? '—'}{value != null && <small>{suffix}</small>}</div>
      {alert && <div className="kpi-alert">{alert}</div>}
      {sub && <div className="kpi-sub">{sub}</div>}
    </div>
  )
}

/** Intertitre d'un onglet : regroupe ses widgets par thème. */
export const ReportHeading = ({ children, tone }) =>
  <h2 className={'report-heading' + (tone ? ' ' + tone : '')}><span className="dot" aria-hidden />{children}</h2>

/** Jauge circulaire d'un score sur 100 (santé des services). */
export function ScoreRing({ score, tone }) {
  const r = 34, c = 2 * Math.PI * r
  return (
    <div className={`score-ring ${tone ?? ''}`}>
      <svg viewBox="0 0 84 84" width="84" height="84" aria-hidden>
        <circle className="track" cx="42" cy="42" r={r} />
        <circle className="value" cx="42" cy="42" r={r} strokeDasharray={`${(Math.max(0, Math.min(100, score)) / 100) * c} ${c}`}
          transform="rotate(-90 42 42)" />
      </svg>
      <div className="score-ring-n"><b>{score}</b><span>/ 100</span></div>
    </div>
  )
}

const TOLERANCE_TONE = { 'Dans la cible': 't-done', 'À surveiller': 't-wait', 'Hors tolérance': 't-bad', 'Sans objet': 't-closed' }
/** État d'une tolérance de projet (PRJ-31/32). */
export const Tolerance = ({ value }) => <span className={`badge ${TOLERANCE_TONE[value] ?? 't-closed'}`}>{value}</span>
