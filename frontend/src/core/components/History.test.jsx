import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import History from './History'

vi.mock('../../api', () => ({
  api: {
    audit: {
      list: () => Promise.resolve([
        { id: 4, at: '2026-10-10T09:00:00Z', authorDisplayName: 'Lova Rabe', action: 'Fusion', field: null, oldValue: null,
          newValue: 'CI-2026-0015 « Serveur paie prod » fusionné ici : 1 lien(s), 1 relation(s), 0 actif(s) du sas reportés.', origin: 'CMDB assistée' },
        { id: 3, at: '2026-10-10T08:00:00Z', authorDisplayName: 'Lova Rabe', action: 'Transition forcée',
          field: 'status', oldValue: 'Clos', newValue: 'En cours', reason: 'Clôture par erreur' },
        { id: 2, at: '2026-10-10T07:00:00Z', authorDisplayName: 'Fara Rasoa', action: 'Modification',
          field: 'impact', oldValue: 'Moyen', newValue: 'Élevé' },
        { id: 1, at: '2026-10-10T06:00:00Z', authorDisplayName: 'Fara Rasoa', action: 'Création', newValue: 'Nouveau' }
      ])
    }
  }
}))

afterEach(cleanup)

it('affiche les entrées du journal avec libellés, valeurs et motif (SOC-20)', async () => {
  render(<History type="incident" id={1} labels={{ impact: 'Impact' }} />)
  expect(await screen.findByText('Motif : Clôture par erreur')).toBeTruthy()
  expect(screen.getByText(/Impact :/)).toBeTruthy()
  expect(screen.getByText('Élevé')).toBeTruthy()
  expect(screen.getByText('Transition forcée')).toBeTruthy()
  expect(screen.getByText(/Statut initial/)).toBeTruthy()
  // ADM-41, CMA-05 : une fusion se lit d'une phrase, sans « champ : — → ».
  expect(screen.getByText('CI-2026-0015 « Serveur paie prod » fusionné ici : 1 lien(s), 1 relation(s), 0 actif(s) du sas reportés.')).toBeTruthy()
})
