import React from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import StagingAsset from './StagingAsset'
import { resetFeatures } from '../../../core/features'

const asset = { id: 71, perimeterId: 7, status: 'À rapprocher', hostname: 'srvweb1.siege.local', ipAddress: '10.9.0.7', detectedType: 'Serveur',
  sources: 'IpSweep', inDirectory: true, lastSeenAt: '2026-11-24T08:00:00Z', firstSeenAt: '2026-11-24T08:00:00Z', confidenceScore: 0, matchRule: 'None' }
let proposals = []
const reconcile = vi.fn(() => Promise.resolve({ configurationItem: { id: 41 } }))
const reject = vi.fn(() => Promise.resolve(null))
vi.mock('../../../api', () => ({
  getUser: () => ({ role: 'Manager' }),
  api: {
    configurationItems: { list: () => Promise.resolve([]) },
    directory: { search: () => Promise.resolve([]) },
    features: () => Promise.resolve(['assisted-cmdb']),
    discovery: { staging: {
      get: () => Promise.resolve({ asset, suggestion: null, targetCi: null, proposal: { title: 'srvweb1', ciType: 'Serveur' }, software: [] }),
      proposals: () => Promise.resolve(proposals),
      reconcile: (...a) => reconcile(...a),
      rejectProposal: (...a) => reject(...a),
    } },
  },
}))
beforeEach(() => { resetFeatures(); vi.spyOn(window, 'confirm').mockReturnValue(true) })
afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks() })

const page = () => render(<MemoryRouter initialEntries={['/configuration/staging/71']}>
  <Routes><Route path="/configuration/staging/:id" element={<StagingAsset />} /><Route path="/configuration/:id" element={<p>Fiche CI</p>} /></Routes>
</MemoryRouter>)

it('CMA-08 : la fiche de l’actif montre le CI candidat de la CMDB assistée ; le suivre rapproche, selon CFG-40', async () => {
  proposals = [{ id: 5, field: 'matchedCiId', after: '41', reasons: 'Même nom d’hôte et même système.', stale: false,
    candidate: { id: 41, reference: 'CI-2026-0041', title: 'srvweb1' } }]
  page()
  const help = await screen.findByLabelText('CMDB assistée')
  expect(help.textContent).toContain('Même nom d’hôte et même système.')
  fireEvent.click(within(help).getByText('Rapprocher de CI-2026-0041'))
  await waitFor(() => expect(reconcile).toHaveBeenCalledWith(71, { action: 'update', targetCiId: 41 }))
})

it('CMA-08 : un type proposé préremplit la création ; une proposition se rejette sans décider de l’actif', async () => {
  proposals = [{ id: 6, field: 'ciType', after: 'Poste de travail', reasons: 'Ports d’impression ouverts.', stale: false, candidate: null }]
  page()
  const help = await screen.findByLabelText('CMDB assistée')
  fireEvent.click(within(help).getByText('Rejeter'))
  await waitFor(() => expect(reject).toHaveBeenCalledWith(71, 6))
  fireEvent.click(within(await screen.findByLabelText('CMDB assistée')).getByText('Créer en Poste de travail'))
  expect(await screen.findByText('Nouveau CI')).toBeTruthy()
  expect(screen.getByDisplayValue('Poste de travail')).toBeTruthy()
  expect(reconcile).not.toHaveBeenCalled()
})
