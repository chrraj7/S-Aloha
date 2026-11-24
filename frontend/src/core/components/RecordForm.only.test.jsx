import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import RecordForm from './RecordForm'

vi.mock('../../api', () => ({ getUser: () => ({ role: 'User' }), api: {} }))
afterEach(cleanup)

const fields = [
  { key: 'title', label: 'Titre' },
  { key: 'originReference', label: "Enregistrement d'origine", onlyCreate: true },
  { key: 'value', label: 'Valeur', create: false },
]

describe('Champs selon le formulaire', () => {
  it('à la création : champ de proposition, sans les champs de suite', () => {
    render(<RecordForm fields={fields} form={{ title: '', originReference: '', value: '' }} onChange={() => {}} creating />)
    expect(screen.getByText("Enregistrement d'origine")).toBeTruthy()
    expect(screen.queryByText('Valeur')).toBeNull()
  })
  it('sur la fiche : sans le champ de proposition', () => {
    render(<RecordForm fields={fields} form={{ title: '', originReference: '', value: '' }} onChange={() => {}} />)
    expect(screen.queryByText("Enregistrement d'origine")).toBeNull()
    expect(screen.getByText('Valeur')).toBeTruthy()
  })
  it('champ réservé selon les valeurs saisies (CSI-12 : note d\'une amélioration validée)', () => {
    const note = [{ key: 'value', label: 'Valeur', managerOnly: form => form.status === 'Validée' }]
    const { container, rerender } = render(<RecordForm fields={note} form={{ value: '2', status: 'Proposée' }} onChange={() => {}} />)
    expect(container.querySelector('input').disabled).toBe(false)
    rerender(<RecordForm fields={note} form={{ value: '2', status: 'Validée' }} onChange={() => {}} />)
    expect(container.querySelector('input').disabled).toBe(true)
    rerender(<RecordForm fields={note} form={{ value: '2', status: 'Validée' }} onChange={() => {}} isManager />)
    expect(container.querySelector('input').disabled).toBe(false)
  })
  it('champ heure (SLM-14 : ouverture et fermeture du service)', () => {
    const { container } = render(<RecordForm fields={[{ key: 'openingTime', label: 'Ouverture', type: 'time' }]}
      form={{ openingTime: '08:00' }} onChange={() => {}} />)
    const input = container.querySelector('input[type="time"]')
    expect(input).toBeTruthy()
    expect(input.value).toBe('08:00')
  })
})
