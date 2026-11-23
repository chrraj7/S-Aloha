import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import History from './History'

vi.mock('../../api', () => ({
  api: {
    audit: {
      list: () => Promise.resolve([
        { id: 2, at: '2026-11-23T08:00:00Z', authorDisplayName: 'Fara Rasoa', action: 'Modification',
          field: 'category', oldValue: '', newValue: 'Messagerie', origin: 'Tri assisté des tickets' },
        { id: 1, at: '2026-11-23T07:00:00Z', authorDisplayName: 'Fara Rasoa', action: 'Création', newValue: 'Nouveau', origin: 'Saisie' }
      ])
    }
  }
}))

afterEach(cleanup)

it('marque l’origine d’une proposition appliquée et filtre par origine (OPT-08)', async () => {
  render(<History type="incident" id={1} />)
  expect(await screen.findByText('Tri assisté des tickets', { selector: '.badge' })).toBeTruthy()
  expect(screen.queryByText('Saisie', { selector: '.badge' })).toBeNull()
  expect(screen.getByText(/Statut initial/)).toBeTruthy()
  fireEvent.change(screen.getByLabelText('Origine'), { target: { value: 'Tri assisté des tickets' } })
  expect(screen.queryByText(/Statut initial/)).toBeNull()
  expect(screen.getByText('Messagerie')).toBeTruthy()
})
