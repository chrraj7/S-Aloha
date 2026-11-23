import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import AuditLog from './AuditLog'

const audit = vi.fn(() => Promise.resolve({ total: 1, page: 1, pageSize: 100, items: [
  { id: 1, at: '2026-11-23T10:00:00Z', authorDisplayName: 'Fara', entityType: 'incident', entityId: 4, reference: 'INC-2026-0004',
    action: 'Modification', field: 'category', oldValue: '', newValue: 'Messagerie', reason: null, origin: 'Tri assisté des tickets' }] }))
vi.mock('../../../api', () => ({ api: { admin: { audit: (...a) => audit(...a), auditExport: () => Promise.resolve() } } }))
afterEach(() => { cleanup(); audit.mockClear() })

it('Journal d’audit : origine affichée et filtre envoyé à l’API (OPT-08)', async () => {
  render(<MemoryRouter><AuditLog /></MemoryRouter>)
  expect(await screen.findByText('Tri assisté des tickets', { selector: '.badge' })).toBeTruthy()
  fireEvent.change(screen.getByLabelText('Origine'), { target: { value: 'CMDB assistée' } })
  fireEvent.click(screen.getByRole('button', { name: 'Filtrer' }))
  await waitFor(() => expect(audit).toHaveBeenLastCalledWith(expect.objectContaining({ origin: 'CMDB assistée', page: 1 })))
})
