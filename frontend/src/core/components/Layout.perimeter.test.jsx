import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, render, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'

// ADR-0014 D4, PER-03 : le sas (CFG-40) et la CMDB assistée (CMA-01, CMA-03) suivent le rôle
// de gestionnaire d'un périmètre, comme l'API (StagingController, AssistedCmdb), pas le rôle
// AD. Registre réel ; `usePerimeters` et `features` gardent leur liste en cache de module :
// chaque cas recharge les modules (developpement.md, #206).
let role = 'User'
let mine = []
vi.mock('../../api', () => ({
  api: {
    features: () => Promise.resolve(['assisted-cmdb']),
    perimeters: { mine: () => Promise.resolve(mine) },
  },
  getUser: () => ({ username: 'u', displayName: 'U', role }),
}))

afterEach(cleanup)

const sublinks = () => [...document.querySelectorAll('.sidebar .nav-sublink')].map((a) => a.textContent)
const MANAGERS = ['Sas de staging', 'CMDB assistée']

const renderAs = async (adRole, perimeterRole) => {
  role = adRole
  mine = [{ id: 1, name: 'Siège', role: perimeterRole }]
  vi.resetModules()
  const { default: Layout } = await import('./Layout')
  render(<MemoryRouter initialEntries={['/configuration']}>
    <Routes><Route path="*" element={<Layout />} /></Routes>
  </MemoryRouter>)
  // Témoin : la liste des fonctionnalités est revenue (« CMDB assistée » en dépend).
  await waitFor(() => expect(sublinks()).toContain('Inventaire'))
}

it('un User AD gestionnaire d\'un périmètre voit le sas et la CMDB assistée', async () => {
  await renderAs('User', 'Manager')
  await waitFor(() => expect(sublinks()).toEqual(expect.arrayContaining(MANAGERS)))
})

it('un Manager AD simple utilisateur de ses périmètres ne les voit pas', async () => {
  await renderAs('Manager', 'User')
  await waitFor(() => expect(sublinks()).not.toContain('Sas de staging'))
  expect(sublinks()).not.toContain('CMDB assistée')
  expect(sublinks()).toEqual(expect.arrayContaining(['Registre des CI', 'Inventaire']))
})

it('témoins : Manager partout et Admin AD les voient, User partout non', async () => {
  await renderAs('Manager', 'Manager')
  await waitFor(() => expect(sublinks()).toEqual(expect.arrayContaining(MANAGERS)))
  cleanup()
  await renderAs('Admin', 'User')
  await waitFor(() => expect(sublinks()).toEqual(expect.arrayContaining(MANAGERS)))
  cleanup()
  await renderAs('User', 'User')
  await waitFor(() => expect(sublinks()).toContain('Inventaire'))
  expect(sublinks()).not.toContain('Sas de staging')
})
