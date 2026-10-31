import { getUser } from '../api'
import usePerimeters from './usePerimeters'

/**
 * PER-06 : gestionnaire du périmètre d'un enregistrement, comme `IsManager` de l'API
 * (ADR-0014 D4 : le rôle par périmètre prime sur le rôle AD) ; tant que la liste n'est pas
 * chargée, ou sans périmètre connu, le rôle AD.
 */
export default function useManagerOn(perimeterId) {
  const here = usePerimeters().find(p => p.id === perimeterId)?.role
  return ['Admin', 'Manager'].includes(here ?? getUser()?.role)
}
