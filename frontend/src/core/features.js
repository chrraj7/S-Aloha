import { useEffect, useState } from 'react'
import { api } from '../api'

// Fonctionnalités optionnelles activées par l'Admin (OPT-04) : l'interface ne montre
// que ce qui existe. Lues à la connexion et à l'ouverture de la console, puis gardées
// 30 secondes ; une erreur (API ancienne, réseau) vaut « aucune » : rien d'optionnel ne s'affiche.
const MAX_AGE_MS = 30_000
let cache = { keys: new Set(), at: 0 }
let pending = null
const listeners = new Set()

const publish = keys => {
  cache = { keys, at: Date.now() }
  listeners.forEach(l => l(keys))
  return keys
}

/** Relit la liste ; `force` ignore le cache (connexion, ouverture de la console, activation). */
export function refreshFeatures(force = false) {
  if (!force && Date.now() - cache.at < MAX_AGE_MS) return Promise.resolve(cache.keys)
  if (!pending) {
    pending = Promise.resolve()
      .then(() => api.features())
      .then(list => new Set(Array.isArray(list) ? list : []))
      .catch(() => new Set())
      .then(publish)
      .finally(() => { pending = null })
  }
  return pending
}

/** Ensemble des clés activées, à jour au rendu ; vide tant que la première lecture n'est pas revenue. */
export function useFeatures() {
  const [keys, setKeys] = useState(cache.keys)
  useEffect(() => {
    listeners.add(setKeys)
    refreshFeatures()
    return () => { listeners.delete(setKeys) }
  }, [])
  return keys
}

/** Pour les tests : repart d'un cache vide. */
export const resetFeatures = () => { cache = { keys: new Set(), at: 0 }; pending = null }
