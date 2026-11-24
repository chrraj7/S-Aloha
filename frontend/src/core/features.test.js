import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { api } from '../api'
import { refreshFeatures, resetFeatures } from './features'
import { SECTIONS, visibleFor } from '../modules/registry'

vi.mock('../api', () => ({ api: { features: vi.fn() } }))

beforeEach(() => resetFeatures())
afterEach(() => vi.clearAllMocks())

it('lit les fonctionnalités activées et les garde 30 secondes (OPT-04)', async () => {
  api.features.mockResolvedValue(['assisted-ticket-triage'])
  expect([...await refreshFeatures()]).toEqual(['assisted-ticket-triage'])
  await refreshFeatures()
  expect(api.features).toHaveBeenCalledTimes(1)
  // La connexion et l'ouverture de la console forcent la relecture.
  api.features.mockResolvedValue([])
  expect((await refreshFeatures(true)).size).toBe(0)
  expect(api.features).toHaveBeenCalledTimes(2)
})

it('une erreur vaut « aucune fonctionnalité » : rien d’optionnel ne s’affiche (OPT-04)', async () => {
  api.features.mockRejectedValue(new Error('HTTP 404'))
  expect((await refreshFeatures(true)).size).toBe(0)
})

it('une entrée liée à une fonctionnalité n’apparaît qu’activée (OPT-04)', () => {
  const entry = { href: '/x', label: 'X', feature: 'assisted-cmdb' }
  expect(visibleFor(entry, 'Admin', new Set())).toBe(false)
  expect(visibleFor(entry, 'Admin', undefined)).toBe(false)
  expect(visibleFor(entry, 'Admin', new Set(['assisted-cmdb']))).toBe(true)
  // Les entrées sans fonctionnalité ne changent pas.
  expect(visibleFor({ href: '/y', roles: ['Admin'] }, 'User', new Set(['assisted-cmdb']))).toBe(false)
  expect(visibleFor({ href: '/y' }, 'User')).toBe(true)
})

it('l’entrée d’administration des fonctionnalités optionnelles est réservée à l’Admin (OPT-02)', () => {
  const admin = SECTIONS.find(s => s.id === 'administration')
  expect(admin.roles).toEqual(['Admin'])
  expect(admin.entries.some(e => e.href === '/admin/features' && e.icon === 'features')).toBe(true)
})
