import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import AssistedCmdb, { NamingConventions } from './AssistedCmdb'

const ci = { id: 12, reference: 'CI-2026-0012', title: 'srvpaie1', ciType: 'Serveur', environment: 'Production', status: 'En service',
  operatingSystem: 'Win2019 std', ownerType: 'User', ownerId: 'ci.owner', ownerDisplayName: 'Owner', perimeterId: 7 }
const queue = [
  { id: 1, ciId: 12, ciReference: 'CI-2026-0012', ciTitle: 'srvpaie1', perimeterId: 7, task: 'rename', field: 'title', before: 'srvpaie1',
    after: 'SRV-PAIE-01', reasons: 'Convention des serveurs.', createdAt: '2026-11-24T09:00:00Z', stale: false },
  { id: 2, ciId: 12, ciReference: 'CI-2026-0012', ciTitle: 'srvpaie1', perimeterId: 7, task: 'normalize', field: 'operatingSystem', before: 'Win2019 std',
    after: 'Windows Server 2019 Standard', reasons: 'Écriture normalisée.', createdAt: '2026-11-24T09:00:00Z', stale: false },
  { id: 3, ciId: 14, ciReference: 'CI-2026-0014', ciTitle: 'appli', perimeterId: 7, task: 'reclassify', field: 'ciType', before: 'Autre',
    after: 'Application', reasons: 'Une application.', createdAt: '2026-11-24T09:00:00Z', stale: true },
  { id: 4, ciId: 15, ciReference: 'CI-2026-0015', ciTitle: 'Serveur paie prod', perimeterId: 7, task: 'merge', field: 'merge', before: null,
    after: '16', reasons: 'Même serveur de paie.', createdAt: '2026-11-24T09:00:00Z', stale: false,
    other: { id: 16, reference: 'CI-2026-0016', title: 'SRV-PAIE-01' } },
  { id: 5, ciId: 17, ciReference: 'CI-2026-0017', ciTitle: 'APP-PAIE', perimeterId: 7, task: 'relate', field: 'relation', before: null,
    after: '18|Dépend de', reasons: 'La description cite la base.', createdAt: '2026-11-24T09:00:00Z', stale: false,
    other: { id: 18, reference: 'CI-2026-0018', title: 'BDD-PAIE' } },
]
const launch = vi.fn(dto => Promise.resolve({ id: 9, ...dto }))
const proposals = vi.fn(() => Promise.resolve(queue))
const apply = vi.fn((id, proposal) => (proposal === 1 ? Promise.reject(new Error('Ce nom existe déjà en Production (CFG-17).')) : Promise.resolve({})))
const reject = vi.fn(() => Promise.resolve())
const merge = vi.fn(() => Promise.resolve({}))
const relate = vi.fn(() => Promise.resolve({}))
const saveNaming = vi.fn(() => Promise.resolve({}))
vi.mock('../../../api', () => ({
  api: {
    perimeters: { mine: () => Promise.resolve([{ id: 7, name: 'Siège', role: 'Manager' }]) },
    assistedCmdb: {
      analyses: () => Promise.resolve([{ id: 8, perimeterId: 7, perimeterName: 'Siège', tasks: ['normalize'], wholePerimeter: true, itemCount: 40,
        status: 'En échec', reason: 'Pas de réponse du modèle en 120 s.', proposalCount: 0, rejectedCount: 0, requestedBy: 'Hery Rakoto',
        requestedAt: '2026-11-24T08:00:00Z' }]),
      launch: d => launch(d),
      proposals: p => proposals(p),
      naming: () => Promise.resolve([{ ciType: 'Serveur', description: 'SRV-<RÔLE>-<NN>', examples: ['SRV-PAIE-01'], updatedAt: '2026-11-24T08:00:00Z',
        updatedBy: 'Hery Rakoto' }]),
      saveNaming: (t, d) => saveNaming(t, d),
      merge: (...a) => merge(...a),
      relate: (...a) => relate(...a),
    },
    configurationItems: {
      get: () => Promise.resolve(ci),
      applyProposal: (...a) => apply(...a),
      rejectProposal: (...a) => reject(...a),
    },
  },
  getUser: () => ({ role: 'Manager' }),
}))
afterEach(() => { cleanup(); vi.clearAllMocks() })

const page = () => render(<MemoryRouter><AssistedCmdb /></MemoryRouter>)

it('CMA-01 : un gestionnaire lance une analyse avec ses tâches et suit les analyses, motif d’un échec compris', async () => {
  page()
  expect((await screen.findByText('En échec')).closest('tr').textContent).toContain('Pas de réponse du modèle en 120 s.')
  fireEvent.click(screen.getByLabelText('Reclasser'))
  fireEvent.click(screen.getByText('Lancer l’analyse'))
  await waitFor(() => expect(launch).toHaveBeenCalled())
  expect(launch.mock.calls[0][0]).toEqual({ perimeterId: undefined, tasks: ['rename', 'normalize', 'criticality', 'relate', 'merge'] })
})

it('CMA-03 : la file se décide par sélection ; un refus des règles du CI reste affiché, la proposition reste à décider', async () => {
  page()
  expect(await screen.findByText('SRV-PAIE-01')).toBeTruthy()
  // Une proposition périmée ne se choisit pas.
  expect(screen.getByLabelText('Choisir CI-2026-0014 Type').disabled).toBe(true)
  fireEvent.click(screen.getByLabelText('Choisir CI-2026-0012 Nom'))
  fireEvent.click(screen.getByLabelText('Choisir CI-2026-0012 Système'))
  fireEvent.click(screen.getByText('Appliquer la sélection'))
  await waitFor(() => expect(apply).toHaveBeenCalledTimes(2))
  // Accepter passe par la modification du CI : la valeur proposée, l'en-tête de la proposition (OPT-15).
  expect(apply.mock.calls[1][0]).toBe(12)
  expect(apply.mock.calls[1][1]).toBe(2)
  expect(apply.mock.calls[1][2]).toMatchObject({ operatingSystem: 'Windows Server 2019 Standard', title: 'srvpaie1', status: 'En service' })
  expect((await screen.findByRole('alert')).textContent).toContain('Ce nom existe déjà en Production (CFG-17).')
  fireEvent.change(screen.getByLabelText('Tâche'), { target: { value: 'rename' } })
  await waitFor(() => expect(proposals).toHaveBeenLastCalledWith({ task: 'rename' }))
  fireEvent.click((await screen.findAllByText('Rejeter'))[0])
  await waitFor(() => expect(reject).toHaveBeenCalledWith(12, 1))
})

it('CMA-05 : une fusion garde le CI suggéré ou l’autre, au choix du gestionnaire, et ne passe pas par la modification du CI', async () => {
  page()
  const row = (await screen.findByText('Même serveur de paie.')).closest('tr')
  expect(row.textContent).toContain('CI-2026-0015 retiré')
  expect(row.textContent).toContain('fusionné dans CI-2026-0016 SRV-PAIE-01')
  fireEvent.click(screen.getByText('Garder CI-2026-0016'))
  await waitFor(() => expect(merge).toHaveBeenCalledWith(4, 16))
  fireEvent.click(await screen.findByText('Garder CI-2026-0015'))
  await waitFor(() => expect(merge).toHaveBeenLastCalledWith(4, 15))
  // Par sélection, la fusion garde le CI suggéré.
  fireEvent.click(screen.getByLabelText('Choisir CI-2026-0015 Fusion'))
  fireEvent.click(screen.getByText('Appliquer la sélection'))
  await waitFor(() => expect(merge).toHaveBeenCalledTimes(3))
  expect(merge.mock.calls[2]).toEqual([4, 16])
  expect(apply).not.toHaveBeenCalled()
})

it('CMA-02 : une relation proposée montre son type et sa cible, et s’accepte par sa route', async () => {
  page()
  const row = (await screen.findByText('La description cite la base.')).closest('tr')
  expect(row.textContent).toContain('Dépend de CI-2026-0018 BDD-PAIE')
  fireEvent.click(within(row).getByText('Appliquer'))
  await waitFor(() => expect(relate).toHaveBeenCalledWith(5))
  expect(apply).not.toHaveBeenCalled()
})

it('CMA-04 : la convention de nommage se tient par type ; une description vide la retire', async () => {
  render(<MemoryRouter><NamingConventions /></MemoryRouter>)
  const server = await screen.findByLabelText('Description (Serveur)')
  expect(server.value).toBe('SRV-<RÔLE>-<NN>')
  fireEvent.change(screen.getByLabelText('Exemples (Serveur), un par ligne'), { target: { value: 'SRV-PAIE-01\nSRV-MAIL-02' } })
  fireEvent.click(screen.getAllByText('Enregistrer')[1])   // Applications d'abord, puis Serveurs
  await waitFor(() => expect(saveNaming).toHaveBeenCalledWith('Serveur', { description: 'SRV-<RÔLE>-<NN>', examples: ['SRV-PAIE-01', 'SRV-MAIL-02'] }))
  expect(await screen.findByText('Convention « Serveur » enregistrée.')).toBeTruthy()
})
