import React, { useState } from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import TriageSuggestions from './TriageSuggestions'
import { resetFeatures } from '../../core/features'

let enabled = ['assisted-ticket-triage']
let answer = { suggestions: [], related: [] }
const suggest = vi.fn(() => Promise.resolve(answer))
vi.mock('../../api', () => ({
  api: { features: () => Promise.resolve(enabled), triage: { suggest: (...a) => suggest(...a) } },
}))
beforeEach(() => resetFeatures())
afterEach(() => { cleanup(); vi.clearAllMocks(); enabled = ['assisted-ticket-triage']; answer = { suggestions: [], related: [] } })

/** Formulaire minimal : le titre se tape, les champs reçoivent ce qu'on applique. */
function Form({ initial = {} }) {
  const [form, setForm] = useState({ title: '', description: '', impact: 'Moyen', category: '', serviceId: '', ownerId: '', ownerType: '', ...initial })
  const merge = patch => setForm(f => ({ ...f, ...patch }))
  return <MemoryRouter>
    <input aria-label="Titre" value={form.title} onChange={e => merge({ title: e.target.value })} />
    <output aria-label="Formulaire">{JSON.stringify(form)}</output>
    <TriageSuggestions form={form} merge={merge} delay={0} />
  </MemoryRouter>
}
const form = () => JSON.parse(screen.getByLabelText('Formulaire').textContent)

it('TRI-02 : dès 4 caractères, une ligne par champ avec son motif ; appliquer remplit le champ (TRI-09)', async () => {
  answer = {
    suggestions: [
      { field: 'category', value: 'Messagerie', label: 'Messagerie', reasons: '7 des 10 incidents résolus les plus proches portent la catégorie Messagerie', cited: [] },
      { field: 'serviceId', value: '4', label: 'Messagerie Exchange', reasons: '6 des 10 incidents résolus les plus proches portent le service Messagerie Exchange', cited: [] },
      { field: 'owner', value: 'GRP-MESSAGERIE', label: 'GRP-MESSAGERIE', reasons: '5 des 10 incidents résolus les plus proches ont été traités par GRP-MESSAGERIE', cited: [] },
    ],
    related: [{ id: 21, reference: 'INC-2026-0021', title: 'Outlook muet au siège', status: 'Nouveau' }],
  }
  render(<Form />)
  fireEvent.change(screen.getByLabelText('Titre'), { target: { value: 'Out' } })
  await act(() => new Promise(r => setTimeout(r, 10)))
  expect(suggest).not.toHaveBeenCalled()   // moins de 4 caractères
  fireEvent.change(screen.getByLabelText('Titre'), { target: { value: 'Outlook ne reçoit plus' } })
  expect(await screen.findByText('Catégorie : Messagerie')).toBeTruthy()
  expect(suggest).toHaveBeenLastCalledWith(expect.objectContaining({ title: 'Outlook ne reçoit plus', impact: 'Moyen' }))
  expect(screen.getByText(/7 des 10 incidents résolus/)).toBeTruthy()
  expect(screen.getByText('Service : Messagerie Exchange')).toBeTruthy()
  // Incidents ouverts proches, en liens.
  expect(screen.getByText('INC-2026-0021').closest('a').getAttribute('href')).toBe('/incidents/21')

  // Rien ne s'applique seul ; « Appliquer » remplit le champ, et la ligne disparaît.
  expect(form().category).toBe('')
  fireEvent.click(screen.getAllByText('Appliquer')[0])
  await waitFor(() => expect(form().category).toBe('Messagerie'))
  await waitFor(() => expect(screen.queryByText('Catégorie : Messagerie')).toBeNull())
  // Un groupe s'applique en responsable de type groupe.
  fireEvent.click(screen.getAllByText('Appliquer').at(-1))
  await waitFor(() => expect(form()).toMatchObject({ ownerId: 'GRP-MESSAGERIE', ownerType: 'Group', ownerDisplayName: 'GRP-MESSAGERIE' }))
})

it('OPT-04, OPT-16 : tri coupé, ou réponse vide ou en échec, l’encadré ne s’affiche pas', async () => {
  enabled = []
  render(<Form initial={{ title: 'Outlook ne reçoit plus' }} />)
  await act(() => new Promise(r => setTimeout(r, 10)))
  expect(suggest).not.toHaveBeenCalled()
  expect(screen.queryByLabelText('Suggestions du tri assisté')).toBeNull()
  cleanup()
  resetFeatures()
  enabled = ['assisted-ticket-triage']
  suggest.mockImplementationOnce(() => Promise.reject(new Error('Conteneur injoignable')))
  render(<Form initial={{ title: 'Outlook ne reçoit plus' }} />)
  await waitFor(() => expect(suggest).toHaveBeenCalled())
  expect(screen.queryByLabelText('Suggestions du tri assisté')).toBeNull()
})
