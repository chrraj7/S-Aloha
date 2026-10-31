import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import StandardChanges from '../modules/change-enablement/pages/StandardChanges'
import { Freezes } from '../modules/change-enablement/pages/ChangeSchedule'
import RequestCatalog from '../modules/service-request-management/pages/RequestCatalog'
import DiscoverySettings from '../modules/service-configuration-management/pages/DiscoverySettings'

let role = 'User'
let perimeters = []
vi.mock('../api', () => ({
  getUser: () => ({ username: 'lova', displayName: 'Lova', role }),
  api: {
    standardChanges: { list: () => Promise.resolve([]) },
    changeFreezes: { list: () => Promise.resolve([]) },
    requestCatalog: { list: () => Promise.resolve([]) },
    services: { list: () => Promise.resolve([]) },
    discovery: { endOfLife: { list: () => Promise.resolve([]) } },
  }
}))
vi.mock('./usePerimeters', () => ({ default: () => perimeters, perimeterName: () => '' }))

afterEach(() => { cleanup(); role = 'User'; perimeters = [] })

// PER-13 (ADR-0014 D4) : comme la politique « Manager » de l'API, les référentiels sans
// périmètre suivent le rôle de gestionnaire d'au moins un périmètre, pas le rôle AD.
const MANAGER_SIEGE = { role: 'User', perimeters: [{ id: 1, name: 'Siège', role: 'Manager' }] }
const USER_PARTOUT = { role: 'Manager', perimeters: [{ id: 1, name: 'Siège', role: 'User' }] }
const as = ({ role: r, perimeters: p }) => { role = r; perimeters = p }

const sections = () => [...document.querySelectorAll('.sidebar .nav-section')].map((p) => p.textContent)

describe('Référentiels sans périmètre (PER-13)', () => {
  it.each([
    ['User AD, Manager du Siège', MANAGER_SIEGE, true],
    ['Manager AD, User partout', USER_PARTOUT, false],
  ])('%s : boutons des modèles, des gels et des fins de support', async (_, who, shown) => {
    as(who)
    render(<StandardChanges />)
    await screen.findByText('Modèles de changement standard')
    expect(!!screen.queryByText('+ Nouveau modèle')).toBe(shown)
    cleanup()

    render(<RequestCatalog />)
    await screen.findByRole('heading', { level: 1 })
    expect(!!screen.queryByText('+ Nouveau modèle')).toBe(shown)
    cleanup()

    render(<Freezes />)
    expect(!!screen.queryByLabelText('Début')).toBe(shown)
    cleanup()

    render(<MemoryRouter initialEntries={['/settings/discovery?tab=eol']}><DiscoverySettings /></MemoryRouter>)
    expect(!!screen.queryByLabelText('Produit')).toBe(shown)
  })

  it.each([
    ['User AD, Manager du Siège', MANAGER_SIEGE, true],
    ['Manager AD, User partout', USER_PARTOUT, false],
  ])('%s : section Paramétrage du menu', (_, who, shown) => {
    as(who)
    render(<MemoryRouter><Routes><Route path="*" element={<Layout />} /></Routes></MemoryRouter>)
    expect(sections().includes('Paramétrage')).toBe(shown)
    expect(!!document.querySelector('.sidebar a[href="/settings/categories"]')).toBe(shown)
    // Le référentiel NIS 2 reste à l'Admin.
    expect(document.querySelector('.sidebar a[href="/settings/nis2"]')).toBeNull()
  })

  it('liste non chargée : le rôle AD en repli', () => {
    as({ role: 'Manager', perimeters: [] })
    render(<Freezes />)
    expect(screen.getByLabelText('Début')).toBeTruthy()
  })
})
