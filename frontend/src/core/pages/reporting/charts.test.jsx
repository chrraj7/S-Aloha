import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { bucketDays, ColumnChart, ScoreRing } from './charts'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const days = (n) => Array.from({ length: n }, (_, i) =>
  ({ date: new Date(Date.UTC(2026, 0, 1 + i)).toISOString(), a: 1, b: i % 2 }))

it('regroupe les comptes par semaine au-delà de 45 jours, sans rien perdre', () => {
  expect(bucketDays(days(30), ['a', 'b']).unit).toBe('jour')
  const { rows, unit } = bucketDays(days(90), ['a', 'b'])
  expect(unit).toBe('semaine')
  expect(rows).toHaveLength(13)
  expect(rows[0].a).toBe(7)
  expect(rows.reduce((n, r) => n + r.a, 0)).toBe(90)
})

it('trace des colonnes, totalise chaque série dans la légende, bascule en tableau', () => {
  render(<ColumnChart title="Volumétrie" days={days(10)} series={[
    { key: 'a', label: 'Incidents créés', slot: 1 }, { key: 'b', label: 'Demandes créées', slot: 2 }]} />)
  expect(document.querySelectorAll('rect.col.c1')).toHaveLength(10)
  expect(document.querySelectorAll('rect.col.c2')).toHaveLength(5)   // pas de colonne pour un zéro
  expect([...document.querySelectorAll('.chart-legend span')].map(l => l.textContent)).toEqual(['Incidents créés 10', 'Demandes créées 5'])
  fireEvent.click(screen.getByRole('button', { name: 'Voir les données' }))
  expect(screen.getByRole('table')).toBeTruthy()
})

// ResizeObserver simulé : chaque nœud observé mesure 1 000 px.
function stubResizeObserver() {
  const observed = new Set()
  vi.stubGlobal('ResizeObserver', class {
    constructor(cb) { this.cb = cb; this.nodes = [] }
    observe(n) { observed.add(n); this.nodes.push(n); this.cb([{ contentRect: { width: 1000 } }]) }
    disconnect() { this.nodes.forEach(n => observed.delete(n)) }
  })
  return observed
}
const series = [{ key: 'a', label: 'Incidents créés', slot: 1 }]

it('suit la largeur de la carte quand les données arrivent après le montage', () => {
  stubResizeObserver()
  const { rerender } = render(<ColumnChart title="Volumétrie" days={[]} series={series} />)
  rerender(<ColumnChart title="Volumétrie" days={days(10)} series={series} />)
  expect(document.querySelector('.chart-wrap svg').getAttribute('width')).toBe('1000')
})

it('observe le graphique réaffiché après « Voir les données »', () => {
  const observed = stubResizeObserver()
  render(<ColumnChart title="Volumétrie" days={days(10)} series={series} />)
  fireEvent.click(screen.getByRole('button', { name: 'Voir les données' }))
  fireEvent.click(screen.getByRole('button', { name: 'Voir le graphique' }))
  expect(observed.size).toBe(1)
  expect(observed.has(document.querySelector('.chart-wrap'))).toBe(true)
})

it('la jauge de santé affiche le score', () => {
  render(<ScoreRing score={35} tone="t-bad" />)
  expect(screen.getByText('35')).toBeTruthy()
  expect(document.querySelector('.score-ring').className).toContain('t-bad')
})
