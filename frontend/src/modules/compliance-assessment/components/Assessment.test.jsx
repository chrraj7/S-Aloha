import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Questionnaire from './Questionnaire'
import Synthesis from './Synthesis'
import Comparison from './Comparison'

const answer = vi.fn((id, rid, dto) => Promise.resolve({
  requirementId: rid, ...dto, updatedBy: 'Tiana', updatedAt: '2026-10-08T10:00:00Z', assessmentStatus: 'En cours'
}))
const addEvidence = vi.fn(() => Promise.resolve({ id: 1, targetType: 'change', targetId: 7, reference: 'CHG-2026-0007', title: 'Inventaire' }))
const compare = vi.fn(() => Promise.resolve({
  current: { id: 5, reference: 'EVA-2026-0005', referentialVersion: 'ReCyF v2.5' },
  other: { id: 3, reference: 'EVA-2025-0003', referentialVersion: 'ReCyF v2.5' },
  global: { current: 66.7, other: 33.3, delta: 33.4 },
  pillars: [{ pillar: 'Gouvernance', score: { current: 66.7, other: 33.3, delta: 33.4 } }],
  objectives: [{ id: 1, title: 'Recensement', score: { current: 66.7, other: 33.3, delta: 33.4 }, themes: [] }],
  requirements: [{ code: '1.1-EI/EE', objectiveId: 1, theme: 'Recensement des SI', current: '3', other: '1', delta: 2 }] }))
const createImprovement = vi.fn(() => Promise.resolve({ id: 9, reference: 'AMI-2026-0009' }))

vi.mock('../../../api', () => ({
  api: { assessments: {
    questionnaire: () => Promise.resolve({ referentialVersion: 'ReCyF v2.5', objectives: [{
      id: 1, title: 'Recensement des systèmes d\'information', pillar: 'Gouvernance',
      themes: [{ theme: 'Recensement des SI', requirements: [
        { id: 11, code: '1.1-EI/EE', isoControls: '27002:2022-5.9', text: null, score: null, notApplicable: false, justification: '', evidence: [] },
      ] }] }] }),
    answer: (...a) => answer(...a),
    score: () => Promise.resolve({
      percent: 33.3, maturity: { level: 2, name: 'Géré' }, applicable: 76, answered: 1, notApplicable: 0,
      pillars: [{ pillar: 'Gouvernance', percent: 33.3 }],
      objectives: [{ id: 1, title: 'Recensement', pillar: 'Gouvernance', percent: 33.3, applicable: 3, answered: 1 }],
      gaps: [{ requirementId: 11, code: '1.1-EI/EE', theme: 'Recensement des SI', objectiveId: 1, score: 1, gap: 2 },
        { requirementId: 12, code: '1.2-EI/EE', theme: 'Cartographie', objectiveId: 1, score: 2, gap: 1,
          action: { id: 0, reference: '', status: '', hidden: true } }] }),
    createImprovement: (...a) => createImprovement(...a),
    addEvidence: (...a) => addEvidence(...a),
    removeEvidence: () => Promise.resolve(null),
    list: () => Promise.resolve([
      { id: 5, reference: 'EVA-2026-0005', perimeterId: 1, status: 'En cours', createdAt: '2026-10-08T10:00:00Z' },
      { id: 3, reference: 'EVA-2025-0003', title: 'SI 2025', perimeterId: 1, status: 'Validée', validatedAt: '2025-10-01T10:00:00Z', createdAt: '2025-09-01T10:00:00Z' },
      { id: 4, reference: 'EVA-2026-0004', title: 'Usine', perimeterId: 2, status: 'Validée', validatedAt: '2026-01-01T10:00:00Z', createdAt: '2026-01-01T10:00:00Z' }]),
    compare: (...a) => compare(...a),
    export: () => Promise.resolve({ reference: 'EVA-2026-0005', title: 'SI', entityCategory: 'Entité importante',
      referentialVersion: 'ReCyF v2.5', status: 'En cours', rows: [
        { code: '1.1-EI/EE', objectiveId: 1, objectiveTitle: 'Recensement', pillar: 'Gouvernance', theme: 'Recensement des SI',
          score: 1, notApplicable: false, justification: 'Partiel; à compléter', gap: 2, action: 'AMI-2026-0009', actionStatus: 'Proposée' }] }),
  } }
}))

afterEach(() => { cleanup(); answer.mockClear() })

const assessment = (status = 'En cours') => ({ id: 5, status, entityCategory: 'Entité importante' })

describe('Questionnaire NIS 2', () => {
  it('enregistre un score, puis « non applicable »', async () => {
    render(<Questionnaire assessment={assessment()} onChanged={() => {}} />)
    fireEvent.click(await screen.findByText(/Recensement des systèmes/))
    const select = screen.getByLabelText('Réponse 1.1-EI/EE')
    // Sans texte importé, l'utilisateur sait où l'obtenir.
    expect(screen.getByText(/Texte de l'exigence non importé/)).toBeTruthy()

    fireEvent.change(select, { target: { value: '2' } })
    await waitFor(() => expect(answer).toHaveBeenLastCalledWith(5, 11, { score: 2, notApplicable: false, justification: '' }))
    fireEvent.change(select, { target: { value: 'na' } })
    await waitFor(() => expect(answer).toHaveBeenLastCalledWith(5, 11, { score: null, notApplicable: true, justification: '' }))
  })

  it('est en lecture seule une fois l\'évaluation validée', async () => {
    render(<Questionnaire assessment={assessment('Validée')} onChanged={() => {}} />)
    fireEvent.click(await screen.findByText(/Recensement des systèmes/))
    expect(screen.getByLabelText('Réponse 1.1-EI/EE').disabled).toBe(true)
    expect(screen.getByText(/lecture seule/)).toBeTruthy()
  })
})

describe('Synthèse NIS 2', () => {
  it('affiche score et maturité, et transforme un écart en action d\'amélioration', async () => {
    render(<MemoryRouter><Synthesis assessment={assessment()} onChanged={() => {}} /></MemoryRouter>)
    expect(await screen.findByText('2/5')).toBeTruthy()
    fireEvent.click(screen.getByText('Créer une action d\'amélioration'))
    await waitFor(() => expect(createImprovement).toHaveBeenCalledWith(5, '1.1-EI/EE'))
    expect((await screen.findByText('AMI-2026-0009 →')).getAttribute('href')).toBe('/improvements/9')
  })
})

describe('Synthèse NIS 2 — action d\'un autre périmètre', () => {
  it('PER-08 : l\'écart traité ailleurs est signalé, sans lien ni bouton de création', async () => {
    render(<MemoryRouter><Synthesis assessment={assessment()} onChanged={() => {}} /></MemoryRouter>)
    const row = (await screen.findByText('1.2-EI/EE')).closest('tr')
    expect(row.textContent).toContain('Action d\'un autre périmètre')
    expect(row.querySelector('a')).toBeNull()
    expect(row.querySelector('button')).toBeNull()
    expect(screen.getAllByText('Créer une action d\'amélioration')).toHaveLength(1)
  })
})

describe('Synthèse NIS 2 — plan de traitement et export', () => {
  it('NIS-17 : l\'export produit une ligne par exigence, action liée comprise', async () => {
    const { exportAssessment } = await import('./Synthesis')
    let csv = ''
    const OriginalBlob = global.Blob
    global.Blob = class { constructor(parts) { csv = parts.join('') } }
    URL.createObjectURL = () => 'blob:x'; URL.revokeObjectURL = () => {}
    try { await exportAssessment(5) } finally { global.Blob = OriginalBlob }
    expect(csv).toContain('Exigence;Objectif;Pilier')
    expect(csv).toContain('1.1-EI/EE;1. Recensement;Gouvernance;Recensement des SI;1;;"Partiel; à compléter";2;AMI-2026-0009;Proposée')
  })
})

describe('NIS-08 et NIS-09', () => {
  it('NIS-08 : ajoute une preuve par sa référence', async () => {
    render(<MemoryRouter><Questionnaire assessment={assessment()} onChanged={() => {}} /></MemoryRouter>)
    fireEvent.click(await screen.findByText(/Recensement des systèmes/))
    fireEvent.change(screen.getByLabelText('Preuve 1.1-EI/EE'), { target: { value: 'chg-2026-0007' } })
    fireEvent.click(screen.getByText('Ajouter'))
    expect(await screen.findByText('CHG-2026-0007')).toBeTruthy()
    expect(addEvidence).toHaveBeenCalledWith(5, 11, 'chg-2026-0007')
  })

  it('NIS-09 : compare par défaut avec la dernière validée du même périmètre', async () => {
    render(<MemoryRouter><Comparison assessment={{ id: 5, perimeterId: 1 }} /></MemoryRouter>)
    expect(await screen.findByText('Exigences dont la réponse a changé (1)')).toBeTruthy()
    expect(compare).toHaveBeenCalledWith(5, '3')
    expect(screen.queryByText(/EVA-2026-0004/)).toBeNull()
    expect(screen.getAllByText('+2').length).toBe(1)
  })
})
