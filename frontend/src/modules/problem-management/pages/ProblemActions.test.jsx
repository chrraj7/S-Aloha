import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ProblemDetail from './ProblemDetail'

const action = {
  id: 7, title: 'Corriger la fuite', description: 'Patch mémoire', status: 'En cours', dueDate: null,
  approvalRequestedAt: null, changeId: null, changeReference: null,
  raci: [{ id: 1, role: 'R', assigneeType: 'User', assigneeId: 'hery.rakoto', assigneeDisplayName: 'Hery Rakoto' }]
}
const changesCreate = vi.fn(() => Promise.resolve({ id: 42, reference: 'CHG-2026-0042' }))
const setChange = vi.fn(() => Promise.resolve({}))
const requestApproval = vi.fn(() => Promise.resolve({}))
const removeAction = vi.fn(() => Promise.resolve(null))
let role = 'User'

vi.mock('../../../api', () => ({
  getUser: () => ({ username: 'hery.rakoto', displayName: 'Hery Rakoto', role }),
  api: {
    problems: {
      get: () => Promise.resolve({
        id: 1, reference: 'PRB-2026-0001', title: 'Fuite mémoire', description: '', status: 'En analyse',
        impact: 'Moyen', urgency: 'Moyenne', priority: 'P3', category: '', affectedService: '',
        createdByDisplayName: 'Hery Rakoto', createdAt: '2026-10-06T08:00:00Z', analyses: [], actions: [action],
        perimeterId: 3
      })
    },
    actions: { requestApproval: (...a) => requestApproval(...a), setChange: (...a) => setChange(...a), update: vi.fn(),
      remove: (...a) => removeAction(...a) },
    changes: { create: (...a) => changesCreate(...a) },
    links: { list: () => Promise.resolve([]), create: vi.fn(), remove: vi.fn() },
    search: { similar: () => Promise.resolve([]) }
  }
}))

const renderActions = async () => {
  render(
    <MemoryRouter initialEntries={['/problems/1']}>
      <Routes>
        <Route path="/problems/:id" element={<ProblemDetail />} />
        <Route path="/changes/:id" element={<p>Fiche du changement</p>} />
      </Routes>
    </MemoryRouter>
  )
  fireEvent.click(await screen.findByText('Actions correctives (1)'))
}

afterEach(() => { cleanup(); role = 'User'; removeAction.mockClear() })

describe('Actions correctives', () => {
  it('PRB-25 : le R demande l\'approbation de l\'action en cours', async () => {
    await renderActions()
    fireEvent.click(screen.getByText("Demander l'approbation"))
    await waitFor(() => expect(requestApproval).toHaveBeenCalledWith(7))
  })

  it('PRB-27, PER-04 : créer un changement depuis l\'action, dans le périmètre du problème, rattaché puis ouvert', async () => {
    await renderActions()
    fireEvent.click(screen.getByText('Créer un changement'))
    expect(await screen.findByText('Fiche du changement')).toBeTruthy()
    expect(changesCreate.mock.calls[0][0]).toMatchObject({ title: 'Corriger la fuite', changeType: 'Normal', perimeterId: 3 })
    expect(changesCreate.mock.calls[0][0].description).toContain('PRB-2026-0001')
    expect(setChange).toHaveBeenCalledWith(7, 'CHG-2026-0042')
  })
})

// SOC-14 : supprimer une action corrective revient au gestionnaire, après confirmation.
describe('Suppression d\'une action (SOC-14)', () => {
  it('un utilisateur ne voit pas le bouton', async () => {
    await renderActions()
    expect(screen.queryByRole('button', { name: 'Supprimer l\'action Corriger la fuite' })).toBeNull()
  })
  it('un gestionnaire supprime après confirmation, RACI annoncé', async () => {
    role = 'Manager'
    await renderActions()
    fireEvent.click(await screen.findByRole('button', { name: 'Supprimer l\'action Corriger la fuite' }))
    expect((await screen.findByRole('alertdialog')).textContent).toContain('1 affectations RACI')
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer définitivement' }))
    await waitFor(() => expect(removeAction).toHaveBeenCalledWith(7))
  })
})
