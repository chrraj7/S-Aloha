import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, waitFor, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { OpenProblem } from './IncidentDetail'

const create = vi.fn(() => Promise.resolve({ id: 3, reference: 'PRB-2026-0003' }))
const link = vi.fn(() => Promise.resolve({}))
vi.mock('../../../api', () => ({
  getUser: () => ({ role: 'User' }),
  api: { problems: { create: (...a) => create(...a) }, links: { create: (...a) => link(...a) } }
}))
afterEach(() => { cleanup(); vi.clearAllMocks() })

it('INC-14, PER-04 : le problème lié est créé dans le périmètre de l’incident, puis relié', async () => {
  const incident = { id: 9, title: 'ERP lent', description: 'Fin de mois', impact: 'Moyen', urgency: 'Haute',
    category: 'Applications', affectedService: 'ERP', serviceId: 4, perimeterId: 2 }
  render(<MemoryRouter><OpenProblem incident={incident} /></MemoryRouter>)
  fireEvent.click(screen.getByText('Ouvrir un problème lié'))
  await waitFor(() => expect(link).toHaveBeenCalledWith({ fromType: 'incident', fromId: 9, toReference: 'PRB-2026-0003' }))
  expect(create).toHaveBeenCalledWith(expect.objectContaining({ title: 'ERP lent', serviceId: 4, perimeterId: 2 }))
})
