import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { OutageBanner } from './IncidentDetail'

let outage = null
const setParent = vi.fn(() => Promise.resolve({}))
const update = vi.fn(() => Promise.resolve({}))
const parentRecord = { id: 4, title: 'Messagerie coupée', status: 'En cours', impact: 'Élevé', isMajor: false, occurredAt: null }
vi.mock('../../../api', () => ({
  getUser: () => ({ role: 'Manager' }),
  api: {
    incidents: {
      triage: () => Promise.resolve({ outage }),
      setParent: (...a) => setParent(...a),
      get: () => Promise.resolve(parentRecord),
      update: (...a) => update(...a),
    },
  },
}))
afterEach(() => { cleanup(); vi.clearAllMocks(); outage = null })

const config = {
  fields: [{ key: 'title', label: 'Titre' }, { key: 'impact', label: 'Impact', type: 'select', options: ['Faible', 'Moyen', 'Élevé'] },
    { key: 'isMajor', label: 'Gravité', type: 'checkbox' }],
  keep: r => ({ occurredAt: r.occurredAt }),
}
const page = (incident = { id: 7, parentId: null, updatedAt: 'x' }, reload = () => {}) =>
  render(<MemoryRouter><OutageBanner incident={incident} config={config} reload={reload} /></MemoryRouter>)

const group = (extra = {}) => ({
  id: 1, since: new Date().toISOString(), count: 4, basis: 'service', service: { id: 2, title: 'Messagerie' }, reasons: '4 incidents ouverts sur le service Messagerie',
  members: [{ id: 4, reference: 'INC-2026-0004', title: 'Messagerie coupée', status: 'En cours' }, { id: 7, reference: 'INC-2026-0007', title: 'Outlook muet', status: 'Nouveau' }],
  parent: { id: 4, reference: 'INC-2026-0004', title: 'Messagerie coupée', isMajor: false }, canDeclareMajor: false, ...extra,
})

it('TRI-07 : bandeau « 4 incidents ouverts sur Messagerie depuis… », membres, rattacher au parent (INC-16)', async () => {
  outage = group()
  const reload = vi.fn()
  page(undefined, reload)
  expect(await screen.findByText(/4 incidents ouverts sur Messagerie depuis \d\d:\d\d/)).toBeTruthy()
  expect(screen.getByText('INC-2026-0004').closest('a').getAttribute('href')).toBe('/incidents/4')
  expect(screen.queryByText(/incident majeur/)).toBeNull()   // pas gestionnaire
  fireEvent.click(screen.getByText('Rattacher à INC-2026-0004'))
  await waitFor(() => expect(reload).toHaveBeenCalled())
  expect(setParent).toHaveBeenCalledWith(7, 'INC-2026-0004')
})

it('TRI-07 : un gestionnaire déclare le parent majeur (INC-13) ; le parent lui-même ne se rattache pas', async () => {
  outage = group({ canDeclareMajor: true })
  page({ id: 4, parentId: null, updatedAt: 'x' })
  fireEvent.click(await screen.findByText('Déclarer INC-2026-0004 incident majeur'))
  await waitFor(() => expect(update).toHaveBeenCalled())
  expect(update.mock.calls[0][0]).toBe(4)
  expect(update.mock.calls[0][1]).toMatchObject({ isMajor: true, title: 'Messagerie coupée', status: 'En cours', impact: 'Élevé' })
  expect(screen.queryByText(/Rattacher à/)).toBeNull()
})

it('TRI-07 : sans panne probable, pas de bandeau ; lien par le sens dit autrement', async () => {
  page()
  await waitFor(() => expect(screen.queryByLabelText('Panne probable')).toBeNull())
  cleanup()
  outage = group({ basis: 'sens', service: null, count: 3 })
  page()
  expect(await screen.findByText(/3 incidents ouverts aux descriptions proches depuis/)).toBeTruthy()
})
