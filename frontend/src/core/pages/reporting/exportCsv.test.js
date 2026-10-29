import { expect, it } from 'vitest'
import { fmt, toCsv } from './exportCsv'

it('neutralise une cellule texte qui commence par un signe de formule, pas un nombre', () => {
  const csv = toCsv([{ title: 't', header: ['nom', 'valeur'], rows: [
    ['=1+1', -3.5], ['@SUM(1)', fmt(-3.5)], ['+SUM(1)', '-x'], ['\tonglet', 'ordinaire'], ['-', fmt(-1234.5)],
  ] }])
  expect(csv.split('\r\n').slice(2)).toEqual([
    "'=1+1;-3,5", "'@SUM(1);-3,5", "'+SUM(1);'-x", "'\tonglet;ordinaire", "'-;" + fmt(-1234.5),
  ])
})
