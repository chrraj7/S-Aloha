import React from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Login from './Login'

vi.mock('../../api', () => ({ api: { login: vi.fn() }, setUser: vi.fn() }))
afterEach(cleanup)

it('F14 : la page de connexion présente d’abord le tri assisté, la CMDB assistée et le serveur MCP', () => {
  render(<MemoryRouter><Login /></MemoryRouter>)
  const blocs = screen.getAllByRole('listitem').map(li => li.querySelector('b').textContent)
  expect(blocs.slice(0, 3)).toEqual(['Des tickets triés dès la saisie', 'Une CMDB tenue à jour', 'S-Aloha dans votre assistant'])
  expect(screen.getByText(/Par le serveur MCP/)).toBeTruthy()
  expect(blocs).toHaveLength(4)
})
