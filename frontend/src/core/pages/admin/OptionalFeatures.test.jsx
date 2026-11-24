import React from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import OptionalFeatures from './OptionalFeatures'
import { api } from '../../../api'

vi.mock('../../../api', () => ({
  api: { features: vi.fn(() => Promise.resolve([])), admin: { features: vi.fn(), switchFeature: vi.fn() } }
}))

const container = (over = {}) => ({ container: 'features', configured: true, reachable: true, tokenAccepted: true,
  version: '1.0', features: ['assisted-ticket-triage'], message: 'Joignable, version 1.0.', checkedAt: '2026-11-23T08:00:00Z', ...over })
const triage = (state = 'Coupée') => ({ key: 'assisted-ticket-triage', name: 'Tri assisté des tickets', container: 'features', state,
  available: true, containerState: container(), updatedAt: null, updatedBy: null,
  dependencies: [{ key: 'embeddings', name: "Service d'embeddings", required: false, ok: false, message: 'Non configuré.' }] })
const cmdb = { key: 'assisted-cmdb', name: 'CMDB assistée', container: 'features', state: 'Indisponible', available: false,
  containerState: container(), updatedAt: null, updatedBy: null,
  dependencies: [{ key: 'language-model', name: 'Modèle de langage', required: true, ok: false, message: 'Non réglé.' }] }

beforeEach(() => {
  api.admin.switchFeature.mockReset()
  api.admin.features.mockResolvedValue([triage(), cmdb])
  vi.spyOn(window, 'confirm').mockReturnValue(true)
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })

it('montre l’état, le conteneur et les dépendances de chaque fonctionnalité (OPT-01, OPT-02)', async () => {
  render(<OptionalFeatures />)
  expect(await screen.findByText('Tri assisté des tickets')).toBeTruthy()
  expect(screen.getByText('Coupée')).toBeTruthy()
  expect(screen.getByText('Indisponible')).toBeTruthy()
  expect(screen.getByText(/Modèle de langage/)).toBeTruthy()
  expect(screen.getAllByText(/obligatoire|facultatif/)).toHaveLength(2)
  // Indisponible : pas d'activation possible depuis la page.
  const buttons = screen.getAllByRole('button', { name: 'Activer' })
  expect(buttons[1].disabled).toBe(true)
})

it('active après confirmation et affiche les avertissements (OPT-03)', async () => {
  api.admin.switchFeature.mockResolvedValue({ feature: triage('Activée'), warnings: ["Service d'embeddings : Non configuré."] })
  render(<OptionalFeatures />)
  fireEvent.click((await screen.findAllByRole('button', { name: 'Activer' }))[0])
  expect(window.confirm).toHaveBeenCalled()
  expect(api.admin.switchFeature).toHaveBeenCalledWith('assisted-ticket-triage', true)
  expect(await screen.findByText("Service d'embeddings : Non configuré.")).toBeTruthy()
  expect(screen.getByText('Activée')).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Couper' })).toBeTruthy()
})

it('affiche le motif d’un refus et ne change rien sans confirmation (OPT-03)', async () => {
  api.admin.switchFeature.mockRejectedValue(new Error('Conteneur « features » injoignable.'))
  render(<OptionalFeatures />)
  window.confirm.mockReturnValueOnce(false)
  fireEvent.click((await screen.findAllByRole('button', { name: 'Activer' }))[0])
  expect(api.admin.switchFeature).not.toHaveBeenCalled()
  fireEvent.click(screen.getAllByRole('button', { name: 'Activer' })[0])
  expect(await screen.findByRole('alert')).toBeTruthy()
  expect(screen.getByText('Conteneur « features » injoignable.')).toBeTruthy()
})

it('TRI-06 : l’état de l’index des incidents ouverts du tri s’affiche, construit ou non', async () => {
  const built = { ...triage(), containerState: container({ status: { 'assisted-ticket-triage': { indexBuilt: true, openIncidents: 12, rebuiltAt: '2026-11-23T08:00:00Z' } } }) }
  api.admin.features.mockResolvedValue([built, cmdb])
  render(<OptionalFeatures />)
  expect(await screen.findByText(/12 incident\(s\), reconstruit le/)).toBeTruthy()
  cleanup()
  api.admin.features.mockResolvedValue([{ ...built, containerState: container({ status: { 'assisted-ticket-triage': { indexBuilt: false, openIncidents: 0 } } }) }, cmdb])
  render(<OptionalFeatures />)
  expect(await screen.findByText(/pas encore construit/)).toBeTruthy()
})
