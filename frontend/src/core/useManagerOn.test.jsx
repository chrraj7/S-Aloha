import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Attachments from './components/Attachments'
import { Lifecycle } from '../modules/knowledge-management/pages/ArticleDetail'
import { FollowUp } from '../modules/continual-improvement/pages/ImprovementDetail'
import { Verification } from '../modules/service-configuration-management/pages/ConfigurationItemDetail'

let role = 'User'
let perimeters = []
vi.mock('../api', () => ({
  getUser: () => ({ username: 'lova', displayName: 'Lova', role }),
  api: {
    knowledge: { usage: () => Promise.resolve(null) },
    attachments: {
      list: () => Promise.resolve({ enabled: true, maxFileMb: 1, extensions: ['.pdf'], items: [
        { id: 2, fileName: 'capture.pdf', contentType: 'application/pdf', size: 10, createdBy: 'autre',
          createdByDisplayName: 'Autre', createdAt: '2026-10-23T09:00:00Z' }] }),
      contentUrl: id => `/api/attachments/${id}/content`
    }
  }
}))
vi.mock('./usePerimeters', () => ({ default: () => perimeters, perimeterName: () => '' }))

afterEach(() => { cleanup(); role = 'User'; perimeters = [] })

// PER-06 (ADR-0014 D4) : comme `IsManager` de l'API, les décisions de gestionnaire d'une fiche
// suivent le rôle sur le périmètre de l'enregistrement, pas le rôle AD. Responsable : un autre.
const SIEGE = 1
const MANAGER_SIEGE = { role: 'User', perimeters: [{ id: SIEGE, name: 'Siège', role: 'Manager' }] }
const USER_SIEGE = { role: 'Manager', perimeters: [{ id: SIEGE, name: 'Siège', role: 'User' }] }
const as = ({ role: r, perimeters: p }) => { role = r; perimeters = p }
const owner = { perimeterId: SIEGE, ownerType: 'User', ownerId: 'autre' }

describe('Décisions de gestionnaire d\'une fiche (PER-06)', () => {
  it.each([
    ['User AD, Manager du Siège', MANAGER_SIEGE, true],
    ['Manager AD, User du Siège', USER_SIEGE, false],
  ])('%s', async (_, who, manager) => {
    as(who)
    render(<Lifecycle article={{ id: 3, status: 'Publié', reviewDate: '2027-01-01', ...owner }} onSaved={() => {}} />)
    expect(!!screen.queryByText("Confirmer l'article")).toBe(manager)
    cleanup()
    // KB-21 : la soumission est montrée à qui n'est pas gestionnaire du périmètre.
    render(<Lifecycle article={{ id: 4, status: 'Brouillon', ...owner }} onSaved={() => {}} />)
    expect(!!screen.queryByText('Soumettre à publication')).toBe(!manager)
    cleanup()

    render(<MemoryRouter><FollowUp improvement={{ id: 5, status: 'Réalisée', followUpDate: '2026-01-01', ...owner }}
      onDone={() => {}} /></MemoryRouter>)
    expect(!!screen.queryByText('Le résultat tient')).toBe(manager)
    cleanup()

    render(<Verification ci={{ id: 6, ...owner }} onSaved={() => {}} />)
    expect(!!screen.queryByText('Attester que la fiche est exacte')).toBe(manager)
    cleanup()

    render(<Attachments type="incident" id={7} perimeterId={SIEGE} />)
    await screen.findByText('capture.pdf')
    expect(!!screen.queryByText('Retirer')).toBe(manager)
  })

  it('liste non chargée : le rôle AD en repli', () => {
    as({ role: 'Manager', perimeters: [] })
    render(<Verification ci={{ id: 6, ...owner }} onSaved={() => {}} />)
    expect(screen.getByText('Attester que la fiche est exacte')).toBeTruthy()
  })
})
