import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import LanguageModelSettings from './LanguageModelSettings'

const stored = {
  enabled: false, location: 'Dans le SI', url: 'http://modele.interne:11434/v1', model: 'qwen2.5:7b', hasKey: true, timeoutSeconds: 120,
  maxResponseKb: 64, dataLeavesSi: false, hostedConfirmedAt: null, hostedConfirmedBy: null, updatedAt: '2026-11-24T08:00:00Z', updatedBy: 'Hery Rakoto',
}
const current = { value: stored }
const save = vi.fn(dto => Promise.resolve({ ...current.value, ...dto, hasKey: true, dataLeavesSi: dto.location === 'Hébergé hors du SI',
  hostedConfirmedAt: dto.confirmHosted ? '2026-11-24T09:00:00Z' : null, hostedConfirmedBy: dto.confirmHosted ? 'Hery Rakoto' : null }))
const test = vi.fn(() => Promise.resolve({ ok: true, message: 'Modèle qwen2.5:7b joignable : réponse JSON valide en 840 ms.', outcome: 'valide', durationMs: 840 }))
const usage = vi.fn(() => Promise.resolve({ days: 30, rows: [
  { feature: 'language-model', outcome: 'valide', count: 2, averageMs: 900, rejected: 0, last: '2026-11-24T09:00:00Z' },
  { feature: 'assisted-cmdb', outcome: 'non conforme', count: 1, averageMs: 4200, rejected: 2, last: '2026-11-24T09:05:00Z' },
] }))
vi.mock('../../../api', () => ({
  api: { admin: {
    languageModel: () => Promise.resolve(current.value),
    saveLanguageModel: (...a) => save(...a),
    testLanguageModel: () => test(),
    languageModelUsage: d => usage(d),
  } },
}))
afterEach(() => { cleanup(); vi.clearAllMocks(); current.value = stored })

it('OPT-20, OPT-23 : les réglages s’enregistrent, la clé n’est jamais réaffichée et reste inchangée sans saisie', async () => {
  render(<LanguageModelSettings />)
  const url = await screen.findByLabelText('Adresse de l’API du modèle')
  expect(url.value).toBe('http://modele.interne:11434/v1')
  expect(screen.getByLabelText('Clé (facultative)').value).toBe('')
  expect(screen.getByLabelText('Clé (facultative)').getAttribute('placeholder')).toContain('Enregistré')
  fireEvent.click(screen.getByLabelText('Modèle activé'))
  fireEvent.change(screen.getByLabelText('Délai d’une requête (s)'), { target: { value: '60' } })
  fireEvent.click(screen.getByText('Enregistrer'))
  await waitFor(() => expect(save).toHaveBeenCalled())
  expect(save.mock.calls[0][0]).toEqual({ enabled: true, location: 'Dans le SI', url: 'http://modele.interne:11434/v1', model: 'qwen2.5:7b',
    key: undefined, timeoutSeconds: 60, maxResponseKb: 64, confirmHosted: false })
  expect(await screen.findByText('Réglages enregistrés.')).toBeTruthy()
})

it('OPT-22 : passer à « hébergé » demande une confirmation, puis l’avertissement reste affiché', async () => {
  render(<LanguageModelSettings />)
  fireEvent.change(await screen.findByLabelText('Emplacement'), { target: { value: 'Hébergé hors du SI' } })
  expect(screen.getByText('Enregistrer').disabled).toBe(true)
  fireEvent.click(screen.getByLabelText(/Je confirme que les données envoyées au modèle quittent le SI/))
  fireEvent.click(screen.getByText('Enregistrer'))
  await waitFor(() => expect(save).toHaveBeenCalled())
  expect(save.mock.calls[0][0].confirmHosted).toBe(true)
  expect((await screen.findByText('Les données envoyées au modèle quittent le SI.')).closest('[role=status]').textContent)
    .toContain('registre des traitements')
  // Déjà hébergé : plus de confirmation à redemander.
  expect(screen.queryByLabelText(/Je confirme/)).toBeNull()
})

it('OPT-26, OPT-27 : l’essai affiche son résultat, puis les compteurs des appels se relisent', async () => {
  render(<LanguageModelSettings />)
  expect((await screen.findByText('CMDB assistée')).closest('tr').textContent).toContain('non conforme')
  fireEvent.click(screen.getByText('Essayer le modèle'))
  expect(await screen.findByText('Modèle qwen2.5:7b joignable : réponse JSON valide en 840 ms.')).toBeTruthy()
  await waitFor(() => expect(usage).toHaveBeenCalledTimes(2))
  expect(screen.getByText('Essai').closest('tr').textContent).toContain('0.9 s')
})
