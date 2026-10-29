// Export de l'onglet actif (REP-08) : CSV à séparateur « ; » et BOM UTF-8,
// ouvert tel quel par Excel en français (accents et décimales conservés).

// Un texte qui commence par = + - @, tabulation ou retour chariot serait évalué
// comme une formule par le tableur (OWASP « CSV Injection ») : l'apostrophe le
// garde en texte. Un nombre, même formaté (« -3,5 »), reste un nombre.
const formula = /^[=+\-@\t\r]/
const number = /^[+-]?\d[\d\s]*([.,]\d+)?%?$/

const cell = (v) => {
  if (v == null) return ''
  let s = typeof v === 'number' ? v.toLocaleString('fr-FR', { useGrouping: false }) : String(v)
  if (formula.test(s) && !number.test(s)) s = `'${s}`
  return /[";\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
}

/** Sections [{ title, header, rows }] → texte CSV, une ligne vide entre deux sections. */
export function toCsv(sections) {
  return sections.map(({ title, header, rows }) =>
    [[title], header, ...rows].map(r => r.map(cell).join(';')).join('\r\n')).join('\r\n\r\n')
}

export function downloadCsv(name, sections) {
  const blob = new Blob(['﻿' + toCsv(sections)], { type: 'text/csv;charset=utf-8' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = name
  a.click()
  URL.revokeObjectURL(a.href)
}

export const fmt = (v, digits = 1) => v == null ? null : v.toLocaleString('fr-FR', { maximumFractionDigits: digits })
export const date = (iso) => iso ? new Date(iso).toLocaleDateString('fr-FR') : ''
export const dateTime = (iso) => iso ? new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' }) : ''
