import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Console from './Console'

const item = (type, id, reference, title, status) => ({ type, id, reference, title, status })
const data = {
  role: 'Manager', username: 'chef', myProblems: [], myActions: [], mySlaIncidents: [],
  myAssignments: [item('project', 4, 'PRJ-2026-0004', 'Portail RH', 'En cours')],
  myProjectAlerts: [item('project', 4, 'PRJ-2026-0004', 'Portail RH', '1 jalon(s) en retard')],
  myChangeFollowUps: [item('change', 9, 'CHG-2026-0009', 'Mise à jour du pare-feu', 'Fin planifiée passée sans mise en œuvre')],
  myCisToVerify: [item('ci', 6, 'CI-2026-0006', 'srv-paie', 'Dernière vérification : 02/10/2025')],
  management: {
    toQualify: [], inAnalysisNoRootCause: [], knownErrorsNoAction: [], overdueActions: [], majorIncidents: [], slaIncidents: [],
    decisions: [item('project', 5, 'PRJ-2026-0005', 'Refonte intranet', 'À valider')], reviewsDue: [],
    overdueRequests: [item('request', 2, 'REQ-2026-0002', 'Badge', 'Approuvée')],
    projectAlerts: [item('project', 4, 'PRJ-2026-0004', 'Portail RH', 'délai hors tolérance')],
    cisToVerify: [item('ci', 7, 'CI-2026-0007', 'sw-coeur', 'Dernière vérification : 15/09/2025')],
    expiringAgreements: [item('agreement', 3, 'SLA-2026-0003', 'SLA Paie', 'Expire le 12/12/2026')],
    changeFollowUps: [item('change', 8, 'CHG-2026-0008', 'Migration DNS', 'Mis en œuvre depuis plus de 2 jours ouvrés, sans clôture')],
    regulatory: [item('notification', 3, 'DRG-2026-0003', 'NIS 2 — INC-2026-0040', 'Alerte précoce : 4 h 10 restantes')]
  }
}
vi.mock('../../api', () => ({
  getUser: () => ({ username: 'chef', displayName: 'Chef Projet', role: 'Manager' }),
  api: { console: { get: () => Promise.resolve(data) } }
}))
vi.mock('../usePerimeters', () => ({ default: () => [] }))

afterEach(cleanup)

describe('Console : projets et demandes en retard (PRJ-34, REQ-30)', () => {
  it('montre projets à valider, projets et demandes à relancer, mes projets à relancer', async () => {
    render(<MemoryRouter><Console /></MemoryRouter>)
    expect(await screen.findByText('Projets à relancer')).toBeTruthy()
    expect(screen.getByText('Demandes en retard')).toBeTruthy()
    expect(screen.getByText('Mes projets à relancer')).toBeTruthy()
    expect(screen.getByText('À valider')).toBeTruthy()
    expect(screen.getByText('délai hors tolérance')).toBeTruthy()
    expect(screen.getByText('1 jalon(s) en retard')).toBeTruthy()
  })
})

describe('Console : changements à relancer (CHG-30, CHG-31), déclarations réglementaires (DRG-11)', () => {
  it('montre les changements à relancer du responsable et de l’équipe, et les échéances légales en Urgent', async () => {
    render(<MemoryRouter><Console /></MemoryRouter>)
    expect(await screen.findByText('Mes changements à relancer')).toBeTruthy()
    expect(screen.getByText('Fin planifiée passée sans mise en œuvre')).toBeTruthy()
    expect(screen.getByText('Changements à relancer')).toBeTruthy()
    expect(screen.getByText('Mis en œuvre depuis plus de 2 jours ouvrés, sans clôture')).toBeTruthy()
    expect(screen.getByText('Déclarations réglementaires')).toBeTruthy()
    expect(screen.getByText('Alerte précoce : 4 h 10 restantes')).toBeTruthy()
  })
})

describe('Console : CI à vérifier (CFG-15)', () => {
  it('montre mes CI à vérifier dans Mon travail et ceux des périmètres gérés dans Relances, compteurs compris', async () => {
    render(<MemoryRouter><Console /></MemoryRouter>)
    expect(await screen.findByText('Mes CI à vérifier')).toBeTruthy()
    expect(screen.getByText('Dernière vérification : 02/10/2025')).toBeTruthy()
    expect(screen.getByText('CI à vérifier')).toBeTruthy()
    expect(screen.getByText('Dernière vérification : 15/09/2025')).toBeTruthy()
    // Relances : demandes, projets, changements, CI à vérifier et SLA expirant ; Mon travail : 1 dossier, 1 projet, 1 changement, 1 CI.
    const tile = label => screen.getByText(label, { selector: '.c-tile-l' }).parentElement.querySelector('.c-tile-n').textContent
    expect(tile('Relances')).toBe('5')
    expect(tile('Mon travail')).toBe('4')
  })
})

describe('Console : SLA expirant (SLM-24)', () => {
  it('montre dans Relances les SLA dont la fin de validité approche', async () => {
    render(<MemoryRouter><Console /></MemoryRouter>)
    expect(await screen.findByText('SLA expirant')).toBeTruthy()
    expect(screen.getByText('SLA Paie')).toBeTruthy()
    expect(screen.getByText('Expire le 12/12/2026')).toBeTruthy()
  })
})
