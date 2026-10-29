import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import RecordDetail from './RecordDetail'

// Lova : User dans l'AD, Manager du périmètre n° 1 seulement (scénario de PER-06).
let adRole = 'User'
vi.mock('../../api', () => ({
  getUser: () => ({ username: 'lova', displayName: 'Lova', role: adRole }),
  api: { perimeters: { mine: () => Promise.resolve([{ id: 1, name: 'Siège', role: 'Manager' }, { id: 2, name: 'Agence', role: 'User' }]) } }
}))

const config = perimeterId => ({
  title: 'Incidents', basePath: '/incidents', statuses: [{ value: 'Nouveau' }],
  fields: [{ key: 'isMajor', label: 'Gravité', type: 'checkbox', checkboxLabel: 'Incident majeur', managerOnly: true }],
  api: { get: () => Promise.resolve({ id: 7, reference: 'INC-2026-0007', title: 'Test', status: 'Nouveau', perimeterId,
    isMajor: false, createdByDisplayName: 'Lova', createdAt: '2026-10-07T08:00:00Z' }) }
})

const show = perimeterId => render(
  <MemoryRouter initialEntries={['/incidents/7']}>
    <Routes><Route path="/incidents/:id" element={<RecordDetail config={config(perimeterId)} />} /></Routes>
  </MemoryRouter>)

afterEach(() => { cleanup(); adRole = 'User' })

// INC-13, PER-06 : un champ réservé aux gestionnaires suit le rôle sur le périmètre de
// l'enregistrement (ADR-0014 D4), comme l'API, et non le rôle AD.
it('active « Incident majeur » pour le Manager du périmètre de l\'incident, User dans l\'AD', async () => {
  show(1)
  const box = await screen.findByRole('checkbox', { name: /Incident majeur/ })
  await waitFor(() => expect(box.disabled).toBe(false))
})

it('grise « Incident majeur » sur un périmètre où l\'on est User', async () => {
  show(2)
  const box = await screen.findByRole('checkbox', { name: /Incident majeur/ })
  await new Promise(r => setTimeout(r, 50))
  expect(box.disabled).toBe(true)
})

it('grise « Incident majeur » pour un Manager AD qui n\'est que User du périmètre', async () => {
  adRole = 'Manager'
  show(2)
  const box = await screen.findByRole('checkbox', { name: /Incident majeur/ })
  await waitFor(() => expect(box.disabled).toBe(true))
})
