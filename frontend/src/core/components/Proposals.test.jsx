import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Proposals from './Proposals'

afterEach(() => { cleanup(); vi.clearAllMocks() })

const fields = [
  { key: 'title', label: 'Titre' },
  { key: 'impact', label: 'Impact', type: 'select', options: ['Faible', 'Moyen', 'Élevé'] },
  { key: 'serviceId', label: 'Service affecté', type: 'select', numeric: true, options: [{ value: 4, label: 'Messagerie' }, { value: 5, label: 'Réseau' }] },
  { key: 'ownerId', nameKey: 'ownerDisplayName', typeKey: 'ownerType', label: 'Assigné à', type: 'person' },
  { key: 'escalationReason', label: 'Motif d’escalade', type: 'textarea' },
]
const record = { id: 9, title: 'Outlook muet', status: 'Nouveau', impact: 'Moyen', serviceId: null, ownerType: 'Group', ownerId: 'GRP-SUPPORT',
  ownerDisplayName: 'GRP-SUPPORT', escalationReason: '', occurredAt: '2026-11-23T08:00:00Z', updatedAt: '2026-11-23T09:00:00Z' }

const setup = proposals => {
  const config = {
    fields, keep: r => ({ occurredAt: r.occurredAt }),
    api: {
      proposals: vi.fn(() => Promise.resolve(proposals)),
      applyProposal: vi.fn(() => Promise.resolve({})),
      rejectProposal: vi.fn(() => Promise.resolve(null)),
    },
  }
  const reload = vi.fn()
  render(<MemoryRouter><Proposals config={config} record={record} reload={reload} title="Suggestions du tri assisté" reasonPrefix="Tri assisté : " /></MemoryRouter>)
  return { config, reload }
}

const service = { id: 31, field: 'serviceId', before: null, after: '4', reasons: '7 des 10 incidents résolus les plus proches portent le service Messagerie',
  cited: [{ type: 'incident', id: 12, reference: 'INC-2026-0012', title: 'Voisin' }], stale: false }

it('OPT-15, TRI-08 : une ligne par champ, valeur lisible, motif et sources ; appliquer passe par la modification', async () => {
  const { config, reload } = setup([service])
  expect(await screen.findByText('Service affecté : Messagerie')).toBeTruthy()
  expect(screen.getByText(/7 des 10 incidents résolus/)).toBeTruthy()
  expect(screen.getByText('INC-2026-0012').getAttribute('href')).toBe('/incidents/12')
  fireEvent.click(screen.getByText('Appliquer'))
  await waitFor(() => expect(reload).toHaveBeenCalled())
  const [id, proposal, dto] = config.api.applyProposal.mock.calls[0]
  expect([id, proposal]).toEqual([9, 31])
  // Toute la fiche repart, avec la valeur proposée et les champs gardés (INC-27).
  expect(dto).toMatchObject({ serviceId: 4, impact: 'Moyen', title: 'Outlook muet', status: 'Nouveau', occurredAt: '2026-11-23T08:00:00Z' })
})

it('OPT-15 : corriger retient une autre valeur ; rejeter écarte la proposition', async () => {
  const { config } = setup([{ ...service, id: 32, field: 'impact', before: 'Moyen', after: 'Élevé', reasons: 'service Messagerie, criticité Élevée', cited: [] }])
  fireEvent.click(await screen.findByText('Corriger'))
  fireEvent.change(screen.getByLabelText('Impact'), { target: { value: 'Faible' } })
  fireEvent.click(screen.getByText('Enregistrer'))
  await waitFor(() => expect(config.api.applyProposal).toHaveBeenCalled())
  expect(config.api.applyProposal.mock.calls[0][2]).toMatchObject({ impact: 'Faible' })
  fireEvent.click(await screen.findByText('Rejeter'))
  await waitFor(() => expect(config.api.rejectProposal).toHaveBeenCalledWith(9, 32))
})

it('TRI-09 : accepter un groupe est une escalade au motif prérempli, modifiable', async () => {
  const { config } = setup([{ ...service, id: 33, field: 'owner', before: 'GRP-SUPPORT', after: 'GRP-RESEAU',
    reasons: '7 des 10 incidents résolus les plus proches ont été traités par GRP-RESEAU' }])
  fireEvent.click(await screen.findByText('Appliquer'))
  const reason = screen.getByLabelText('Motif d’escalade')
  expect(reason.value).toBe('Tri assisté : 7 des 10 incidents résolus les plus proches ont été traités par GRP-RESEAU')
  fireEvent.change(reason, { target: { value: 'Tri assisté : panne réseau du site' } })
  fireEvent.click(screen.getByText('Enregistrer'))
  await waitFor(() => expect(config.api.applyProposal).toHaveBeenCalled())
  expect(config.api.applyProposal.mock.calls[0][2]).toMatchObject({
    ownerType: 'Group', ownerId: 'GRP-RESEAU', ownerDisplayName: 'GRP-RESEAU', escalationReason: 'Tri assisté : panne réseau du site' })
})

it('OPT-15 : une proposition périmée ne s’applique plus ; sans proposition, rien ne s’affiche', async () => {
  setup([{ ...service, stale: true }])
  expect(await screen.findByText(/Périmée/)).toBeTruthy()
  expect(screen.queryByText('Appliquer')).toBeNull()
  cleanup()
  setup([])
  await waitFor(() => expect(screen.queryByLabelText('Suggestions du tri assisté')).toBeNull())
})

it('CMA-05 : une fusion cite l’autre CI et se décide dans la file de la CMDB assistée, jamais par la modification', async () => {
  const merge = { id: 41, field: 'merge', before: null, after: '16', reasons: 'Même serveur de paie.',
    cited: [{ type: 'ci', id: 16, reference: 'CI-2026-0016', title: 'SRV-PAIE-01' }], stale: false }
  const { config } = setup([merge])
  // L'autre CI, en titre de la ligne et parmi les sources.
  expect((await screen.findAllByText('CI-2026-0016')).map(a => a.getAttribute('href'))).toEqual(['/configuration/16', '/configuration/16'])
  expect(screen.getByText('Décider dans la file').getAttribute('href')).toBe('/configuration/assisted')
  expect(screen.queryByText('Appliquer')).toBeNull()
  fireEvent.click(screen.getByText('Rejeter'))
  await waitFor(() => expect(config.api.rejectProposal).toHaveBeenCalledWith(9, 41))
  expect(config.api.applyProposal).not.toHaveBeenCalled()
})

it('CMA-02 : une relation proposée montre son type et l’autre CI, et se décide dans la file', async () => {
  const relation = { id: 42, field: 'relation', before: null, after: '12|Dépend de', reasons: 'La description cite la base.',
    cited: [{ type: 'ci', id: 12, reference: 'CI-2026-0012', title: 'BDD-PAIE' }], stale: false }
  setup([relation])
  expect((await screen.findByText(/Relation : Dépend de/)).textContent).toContain('CI-2026-0012')
  expect(screen.getByText('Décider dans la file').getAttribute('href')).toBe('/configuration/assisted')
  expect(screen.queryByText('Appliquer')).toBeNull()
})
