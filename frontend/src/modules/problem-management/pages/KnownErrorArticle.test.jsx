import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, waitFor, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { KnownErrorArticle } from './ProblemDetail'

const create = vi.fn(() => Promise.resolve({ id: 9, reference: 'KB-2026-0009' }))
const link = vi.fn(() => Promise.resolve({}))
vi.mock('../../../api', () => ({
  getUser: () => ({ role: 'Manager' }),
  api: {
    knowledge: { create: (...a) => create(...a) },
    links: { list: () => Promise.resolve([]), create: (...a) => link(...a) }
  }
}))
afterEach(() => { cleanup(); vi.clearAllMocks() })

it('PRB-30, PER-04 : l’article d’erreur connue naît en brouillon dans le périmètre du problème, puis lui est relié', async () => {
  const p = {
    id: 7, reference: 'PRB-2026-0007', title: 'Saturation du stockage', status: 'Erreur connue',
    description: 'Volume plein chaque nuit', knownErrorWorkaround: 'Purger les journaux', perimeterId: 2
  }
  render(<MemoryRouter><KnownErrorArticle p={p} /></MemoryRouter>)
  fireEvent.click(await screen.findByText("Créer l'article d'erreur connue"))
  await waitFor(() => expect(link).toHaveBeenCalledWith({ fromType: 'problem', fromId: 7, toReference: 'KB-2026-0009' }))
  const dto = create.mock.calls[0][0]
  expect(dto).toMatchObject({ title: 'Saturation du stockage', articleType: 'Erreur connue', perimeterId: 2 })
  expect(dto.content).toContain('Purger les journaux')
})
