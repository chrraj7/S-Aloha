import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import TriageSettings from './TriageSettings'

const defaults = { neighbours: 10, minVoters: 3, minSharePercent: 50, correlationWindowMinutes: 60, outageThreshold: 3,
  correlationMinSimilarity: 0.6, suggestCategory: true, suggestService: true, suggestGroup: true, suggestImpact: true, suggestOutage: true }
const field = (f, extra) => ({ field: f, label: { category: 'Catégorie', serviceId: 'Service', owner: 'Groupe responsable', impact: 'Impact' }[f],
  decided: 0, accepted: 0, corrected: 0, rejected: 0, left: 0, pending: 0, acceptedShare: 0, correctedShare: 0, rejectedShare: 0, leftShare: 0, ...extra })
const metrics = { windows: [
  { days: 30, fields: [field('category', { decided: 6, acceptedShare: 33, correctedShare: 17, rejectedShare: 17, leftShare: 33, pending: 1 }),
    field('serviceId'), field('owner', { decided: 4, correctedShare: 75 }), field('impact')] },
  { days: 90, fields: [field('category', { decided: 9, acceptedShare: 44 }), field('serviceId'), field('owner'), field('impact')] },
] }
const save = vi.fn(s => Promise.resolve(s))
vi.mock('../../../api', () => ({
  api: { admin: {
    featureSettings: () => Promise.resolve(defaults),
    saveFeatureSettings: (...a) => save(...a),
    triageMetrics: () => Promise.resolve(metrics),
  } },
}))
afterEach(() => { cleanup(); vi.clearAllMocks() })

it('TRI-13 : les réglages se lisent, se modifient et s’enregistrent, chaque suggestion activable à part', async () => {
  render(<TriageSettings />)
  const neighbours = await screen.findByLabelText('Voisins qui votent')
  expect(neighbours.value).toBe('10')
  expect(neighbours.getAttribute('min')).toBe('5')
  expect(neighbours.getAttribute('max')).toBe('20')
  fireEvent.change(neighbours, { target: { value: '15' } })
  fireEvent.click(screen.getByLabelText('Groupe responsable'))
  fireEvent.click(screen.getByText('Enregistrer les réglages'))
  await waitFor(() => expect(save).toHaveBeenCalled())
  expect(save.mock.calls[0]).toEqual(['assisted-ticket-triage', { ...defaults, neighbours: 15, suggestGroup: false }])
  expect(await screen.findByText('Réglages enregistrés.')).toBeTruthy()
})

it('TRI-13 : un réglage hors de ses bornes est refusé par l’API, le message s’affiche', async () => {
  save.mockImplementationOnce(() => Promise.reject(new Error('Voisins : de 5 à 20.')))
  render(<TriageSettings />)
  fireEvent.click(await screen.findByText('Enregistrer les réglages'))
  expect((await screen.findByRole('alert')).textContent).toBe('Voisins : de 5 à 20.')
})

it('TRI-10 : la mesure par champ sur 30 et 90 jours ; un champ surtout corrigé est signalé', async () => {
  render(<TriageSettings />)
  expect(await screen.findAllByText('33 %', { selector: 'td' })).toHaveLength(2)   // acceptées et laissées
  const rows = screen.getAllByRole('row')
  expect(rows[1].textContent).toContain('Catégorie')
  expect(rows[1].textContent).toContain('6')
  // Groupe responsable : 75 % de corrigées sur 30 jours, à couper peut-être (TRI-13).
  expect(screen.getByText('Surtout corrigée : envisager de couper cette suggestion.').closest('tr').textContent).toContain('Groupe responsable')
  fireEvent.click(screen.getByText('90 jours'))
  expect(await screen.findByText('44 %', { selector: 'td' })).toBeTruthy()
  expect(screen.queryByText(/Surtout corrigée/)).toBeNull()
})
