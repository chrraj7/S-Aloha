import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import AssistedCmdbSettings from './AssistedCmdbSettings'

const defaults = { tasks: ['rename', 'normalize', 'reclassify', 'merge', 'relate', 'criticality'], maxItems: 500, pairsPerRequest: 50, stagingHelp: true }
const LABELS = { rename: 'Renommer', normalize: 'Normaliser', reclassify: 'Reclasser', merge: 'Fusionner', relate: 'Relier', criticality: 'Criticité',
  staging: 'Aide au sas' }
const task = (t, extra) => ({ task: t, label: LABELS[t], decided: 0, accepted: 0, corrected: 0, rejected: 0, expired: 0, pending: 0, acceptedShare: 0,
  correctedShare: 0, rejectedShare: 0, expiredShare: 0, calls: 0, responses: 0, nonConforming: 0, nonConformingShare: 0, failedCalls: 0,
  averageCallMs: 0, ...extra })
const all = extra => Object.keys(LABELS).map(t => task(t, extra[t]))
const metrics = { windows: [
  { days: 30, tasks: all({ rename: { decided: 4, acceptedShare: 50, correctedShare: 25, rejectedShare: 25, pending: 1, calls: 4, responses: 5,
    nonConforming: 3, nonConformingShare: 60, failedCalls: 1, averageCallMs: 3000 }, merge: { decided: 2, rejectedShare: 100 } }),
  analyses: { done: 2, failed: 1, averageSeconds: 180, longestSeconds: 240 } },
  { days: 90, tasks: all({ rename: { decided: 5, acceptedShare: 40 } }), analyses: { done: 3, failed: 1, averageSeconds: 320, longestSeconds: 600 } },
] }
const save = vi.fn((key, s) => Promise.resolve(s))
vi.mock('../../../api', () => ({
  api: { admin: {
    featureSettings: () => Promise.resolve(defaults),
    saveFeatureSettings: (...a) => save(...a),
    assistedCmdbMetrics: () => Promise.resolve(metrics),
  } },
}))
afterEach(() => { cleanup(); vi.clearAllMocks() })

it('CMA-12 : les tâches proposées, les limites et l’aide au sas se règlent et s’enregistrent', async () => {
  render(<AssistedCmdbSettings />)
  const items = await screen.findByLabelText('CI au plus par analyse')
  expect([items.value, items.getAttribute('min'), items.getAttribute('max')]).toEqual(['500', '20', '500'])
  const tasks = within(screen.getByRole('group', { name: 'Tâches proposées aux gestionnaires' }))
  fireEvent.click(tasks.getByLabelText('Fusionner'))
  fireEvent.change(screen.getByLabelText('Paires au plus par requête (Fusionner)'), { target: { value: '20' } })
  fireEvent.click(screen.getByLabelText(/Aide au sas/))
  fireEvent.click(screen.getByText('Enregistrer les réglages'))
  await waitFor(() => expect(save).toHaveBeenCalled())
  expect(save.mock.calls[0]).toEqual(['assisted-cmdb', { tasks: ['rename', 'normalize', 'reclassify', 'relate', 'criticality'], maxItems: 500,
    pairsPerRequest: 20, stagingHelp: false }])
  expect(await screen.findByText('Réglages enregistrés.')).toBeTruthy()
})

it('CMA-12 : un réglage hors de ses bornes est refusé par l’API, le message s’affiche', async () => {
  save.mockImplementationOnce(() => Promise.reject(new Error('CI au plus par analyse : de 20 à 500.')))
  render(<AssistedCmdbSettings />)
  fireEvent.click(await screen.findByText('Enregistrer les réglages'))
  expect((await screen.findByRole('alert')).textContent).toBe('CI au plus par analyse : de 20 à 500.')
})

it('CMA-10 : la mesure par tâche sur 30 et 90 jours, réponses non conformes et durée des analyses ; une tâche surtout rejetée est signalée', async () => {
  render(<AssistedCmdbSettings />)
  const rename = (await screen.findByText('Renommer', { selector: 'td' })).closest('tr')
  expect(rename.textContent).toContain('60 % (3/5)')
  expect(rename.textContent).toContain('1 appel(s) en échec')
  expect(rename.textContent).toContain('3,0 s')
  expect(screen.getByText('Aide au sas', { selector: 'td' })).toBeTruthy()
  expect(screen.getByText(/durée moyenne 3 min 00 s, la plus longue 4 min 00 s/)).toBeTruthy()
  expect(screen.getByText('Surtout rejetée : envisager de retirer cette tâche.').closest('tr').textContent).toContain('Fusionner')
  fireEvent.click(screen.getByText('90 jours'))
  expect(await screen.findByText('40 %', { selector: 'td' })).toBeTruthy()
  expect(screen.getByText(/la plus longue 10 min 00 s/)).toBeTruthy()
  expect(screen.queryByText(/Surtout rejetée/)).toBeNull()
})
