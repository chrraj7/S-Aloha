import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import AccessTokens from './AccessTokens'

const token = { id: 7, name: 'Éditeur', username: 'tech.mcp', displayName: 'Tech MCP', createdAt: '2026-11-24T10:00:00Z', expiresAt: '2026-12-24T10:00:00Z', lastUsedAt: null }
const api = vi.hoisted(() => ({ tokens: {} }))
vi.mock('../../api', () => ({ api }))
afterEach(() => { cleanup(); vi.restoreAllMocks() })

it('MCP-03 : le jeton créé ne s’affiche qu’une fois ; la liste ne le montre jamais ; révocation', async () => {
  let list = []
  api.tokens = {
    mine: vi.fn(() => Promise.resolve(list)),
    create: vi.fn(dto => { list = [{ ...token, name: dto.name }]; return Promise.resolve({ token: list[0], secret: 'sat_secret-une-fois' }) }),
    revoke: vi.fn(() => { list = []; return Promise.resolve() }),
  }
  render(<AccessTokens />)
  expect(await screen.findByText('Aucun jeton actif.')).toBeTruthy()
  expect(screen.getByText('Créer un jeton').disabled).toBe(true)
  fireEvent.change(screen.getByLabelText('Nom du jeton'), { target: { value: ' Éditeur ' } })
  fireEvent.change(screen.getByLabelText('Durée'), { target: { value: '90' } })
  fireEvent.click(screen.getByText('Créer un jeton'))
  await waitFor(() => expect(api.tokens.create).toHaveBeenCalledWith({ name: 'Éditeur', days: 90 }))
  expect((await screen.findByTestId('token-secret')).textContent).toBe('sat_secret-une-fois')
  expect(await screen.findByText('jamais')).toBeTruthy()   // dernière utilisation
  fireEvent.click(screen.getByText('J’ai copié le jeton'))
  expect(screen.queryByTestId('token-secret')).toBeNull()
  expect(document.body.textContent).not.toContain('sat_secret-une-fois')

  vi.spyOn(window, 'confirm').mockReturnValue(true)
  fireEvent.click(screen.getByText('Révoquer'))
  await waitFor(() => expect(api.tokens.revoke).toHaveBeenCalledWith(7))
  expect(await screen.findByText('Aucun jeton actif.')).toBeTruthy()
})

it('MCP-03 : l’Admin voit les jetons de tous les comptes et les révoque, sans en créer', async () => {
  api.tokens = { all: vi.fn(() => Promise.resolve([token])), revokeAny: vi.fn(() => Promise.resolve()) }
  render(<AccessTokens admin />)
  expect(await screen.findByText('Tech MCP')).toBeTruthy()
  expect(screen.queryByText('Créer un jeton')).toBeNull()
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  fireEvent.click(screen.getByText('Révoquer'))
  await waitFor(() => expect(api.tokens.revokeAny).toHaveBeenCalledWith(7))
})
