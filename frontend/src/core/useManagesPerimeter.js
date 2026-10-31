import { getUser } from '../api'
import usePerimeters from './usePerimeters'

/**
 * PER-13 : les référentiels sans périmètre (gels, modèles standard, catalogue, fins de support,
 * catégories) se tiennent en Admin ou en gestionnaire d'au moins un périmètre, comme la
 * politique « Manager » de l'API. ADR-0014 D4 : le rôle par périmètre prime sur le rôle AD ;
 * tant que la liste n'est pas chargée, le rôle AD.
 */
export default function useManagesPerimeter() {
  const role = getUser()?.role
  const perimeters = usePerimeters()
  if (role === 'Admin') return true
  if (perimeters.length === 0) return role === 'Manager'
  return perimeters.some(p => ['Admin', 'Manager'].includes(p.role))
}
