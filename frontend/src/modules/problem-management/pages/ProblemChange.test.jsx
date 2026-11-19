import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, waitFor, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ProblemChange } from './ProblemDetail'

const create = vi.fn(() => Promise.resolve({ id: 5, reference: 'CHG-2026-0005' }))
const link = vi.fn(() => Promise.resolve({}))
vi.mock('../../../api', () => ({
  getUser: () => ({ role: 'User' }),
  api: { changes: { create: (...a) => create(...a) }, links: { create: (...a) => link(...a) } }
}))
afterEach(() => { cleanup(); vi.clearAllMocks() })

it('CHG-23, PER-04 : le changement créé depuis le problème en reprend titre et description, dans son périmètre, puis lui est relié', async () => {
  const p = { id: 7, reference: 'PRB-2026-0007', title: 'Saturation du stockage', description: 'Volume plein chaque nuit', perimeterId: 2 }
  render(<MemoryRouter><ProblemChange p={p} /></MemoryRouter>)
  fireEvent.click(screen.getByText('Créer un changement'))
  await waitFor(() => expect(link).toHaveBeenCalledWith({ fromType: 'problem', fromId: 7, toReference: 'CHG-2026-0005' }))
  const dto = create.mock.calls[0][0]
  expect(dto).toMatchObject({ title: 'Saturation du stockage', perimeterId: 2 })
  expect(dto.description).toContain('Volume plein chaque nuit')
  expect(dto.description).toContain('PRB-2026-0007')
})

it('CHG-23 : un refus de l’API s’affiche et aucun lien n’est créé', async () => {
  create.mockImplementationOnce(() => Promise.reject(new Error('Périmètre requis')))
  render(<MemoryRouter><ProblemChange p={{ id: 7, reference: 'PRB-2026-0007', title: 'T', perimeterId: 2 }} /></MemoryRouter>)
  fireEvent.click(screen.getByText('Créer un changement'))
  await screen.findByText('Périmètre requis')
  expect(link).not.toHaveBeenCalled()
})
