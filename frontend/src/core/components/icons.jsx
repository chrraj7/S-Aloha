import React, { useId } from 'react'

// Jeu d'icônes maison — une seule grammaire : 24×24, trait 1,7, bouts
// arrondis, couleur héritée (currentColor). Pas de bibliothèque d'icônes :
// quelques dizaines de tracés ne justifient pas une dépendance.

const Svg = ({ size = 18, children, ...rest }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
    strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden {...rest}>
    {children}
  </svg>
)

/** Emblème S-Aloha (ADR-0021) : un « S » hexagonal d'un seul trait dans un
 * hexagone en filet, et un losange au départ du trait — le « premier point » :
 * « Aloha », c'est ce qui passe avant (« Service alohan'ny zavatra rehetra »).
 * Trait en dégradé --mark-from → --mark-to (marque sur fond sombre par défaut). */
export const BrandMark = ({ size = 32 }) => {
  // Identifiant unique : l'emblème est rendu plusieurs fois (barre, tiroir, connexion).
  const gradient = useId()
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" fill="none" aria-hidden>
      <defs>
        <linearGradient id={gradient} x1="0" y1="8" x2="0" y2="40" gradientUnits="userSpaceOnUse">
          <stop offset="0" style={{ stopColor: 'var(--mark-from)' }} />
          <stop offset="1" style={{ stopColor: 'var(--mark-to)' }} />
        </linearGradient>
      </defs>
      <path d="M24 1.8 43.2 12.9v22.2L24 46.2 4.8 35.1V12.9z" style={{ stroke: 'var(--mark-from)' }} strokeOpacity=".4" strokeWidth="1.2" />
      <path d="M32 13.4 24 8.8l-8 4.6v6.9l8 4.6 8 4.6v6.9l-8 4.6-8-4.6" stroke={`url(#${gradient})`}
        strokeWidth="3.4" strokeLinejoin="round" strokeLinecap="round" />
      <path d="M35.2 9.2l2.2 2.2-2.2 2.2-2.2-2.2z" style={{ fill: 'var(--mark-to)' }} />
    </svg>
  )
}

export const IconConsole = (p) => <Svg {...p}><rect x="3.5" y="3.5" width="7" height="7" /><rect x="13.5" y="3.5" width="7" height="7" /><rect x="3.5" y="13.5" width="7" height="7" /><rect x="13.5" y="13.5" width="7" height="7" /></Svg>
export const IconReport = (p) => <Svg {...p}><path d="M4 20V10M10 20V4M16 20v-7M22 20H2" /></Svg>
export const IconIncident = (p) => <Svg {...p}><path d="M12 3 2.5 20h19L12 3Z" /><path d="M12 10v4M12 17v.01" /></Svg>
export const IconRequest = (p) => <Svg {...p}><path d="M4 5h16v11H8l-4 4V5Z" /><path d="M8 10h8" /></Svg>
export const IconProblem = (p) => <Svg {...p}><circle cx="11" cy="11" r="6.5" /><path d="m16 16 5 5M11 8v3l2 1.5" /></Svg>
export const IconChange = (p) => <Svg {...p}><path d="M4 8h13l-3-3M20 16H7l3 3" /></Svg>
export const IconConfig = (p) => <Svg {...p}><circle cx="6" cy="6" r="2.5" /><circle cx="18" cy="6" r="2.5" /><circle cx="12" cy="18" r="2.5" /><path d="M7.6 8 11 15.8M16.4 8 13 15.8M8.5 6h7" /></Svg>
export const IconSla = (p) => <Svg {...p}><circle cx="12" cy="13" r="8" /><path d="M12 9v4l2.5 2.5M9 2.5h6" /></Svg>
export const IconKnowledge = (p) => <Svg {...p}><path d="M4 5.5C6.5 4 9.5 4 12 6c2.5-2 5.5-2 8-.5V19c-2.5-1.5-5.5-1.5-8 .5-2.5-2-5.5-2-8-.5V5.5Z" /><path d="M12 6v13.5" /></Svg>
export const IconCsi = (p) => <Svg {...p}><path d="M20 12a8 8 0 1 1-2.3-5.7M20 4v4h-4" /></Svg>
export const IconCompliance = (p) => <Svg {...p}><path d="M12 3 4.5 6v5.5c0 4.6 3.1 8 7.5 9.5 4.4-1.5 7.5-4.9 7.5-9.5V6L12 3Z" /><path d="m8.8 12.2 2.2 2.2 4.2-4.6" /></Svg>
export const IconDeclaration = (p) => <Svg {...p}><path d="M6 3h9l4 4v14H6V3Z" /><path d="M15 3v4h4M12.5 10v4M12.5 17v.01" /></Svg>
export const IconProject = (p) => <Svg {...p}><rect x="3.5" y="4.5" width="17" height="15" /><path d="M3.5 9h17M8 13h3M8 16h6M14 13h2" /></Svg>
export const IconLogout = (p) => <Svg size={15} {...p}><path d="M15 4h4v16h-4M10 8l-4 4 4 4M6 12h10" /></Svg>
export const IconMenu = (p) => <Svg {...p}><path d="M4 6h16M4 12h16M4 18h16" /></Svg>
export const IconClose = (p) => <Svg {...p}><path d="M6 6l12 12M18 6 6 18" /></Svg>
export const IconSun = (p) => <Svg size={15} {...p}><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></Svg>
export const IconMoon = (p) => <Svg size={15} {...p}><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" /></Svg>
export const IconPreferences = (p) => <Svg size={15} {...p}><circle cx="12" cy="8" r="4" /><path d="M4 21c.8-4 4-6 8-6s7.2 2 8 6" /></Svg>
export const IconCode = (p) => <Svg size={15} {...p}><path d="m8 7-5 5 5 5M16 7l5 5-5 5M13.5 4l-3 16" /></Svg>
export const IconRail = (p) => <Svg size={15} {...p}><rect x="3.5" y="4.5" width="17" height="15" /><path d="M9 4.5v15M15.5 10 13.5 12l2 2" /></Svg>

// Entrées de Paramétrage et d'Administration : une icône par page, jamais
// une icône commune à toute la section (on ne distinguerait plus les entrées
// dans la barre réduite).
export const IconTag = (p) => <Svg {...p}><path d="M3.5 3.5h8l9 9-8 8-9-9v-8Z" /><circle cx="8" cy="8" r="1.4" /></Svg>
export const IconRadar = (p) => <Svg {...p}><circle cx="12" cy="12" r="8.5" /><circle cx="12" cy="12" r="4.5" /><path d="m12 12 6-6" /></Svg>
export const IconFramework = (p) => <Svg {...p}><path d="M9 6h11M9 12h11M9 18h11" /><path d="m3.5 6 1.3 1.3L7 5M3.5 12l1.3 1.3L7 11M3.5 18l1.3 1.3L7 17" /></Svg>
export const IconUsers = (p) => <Svg {...p}><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20c.6-3.6 3.1-5.5 6.5-5.5s5.9 1.9 6.5 5.5M16 4.8a3.5 3.5 0 0 1 0 6.4M18 14.8c2 .7 3.2 2.4 3.5 5.2" /></Svg>
export const IconPerimeter = (p) => <Svg {...p}><path d="M12 21s-7-6.2-7-11.5a7 7 0 0 1 14 0C19 14.8 12 21 12 21Z" /><circle cx="12" cy="9.5" r="2.5" /></Svg>
export const IconIndex = (p) => <Svg {...p}><ellipse cx="12" cy="5.5" rx="7.5" ry="2.5" /><path d="M4.5 5.5v13c0 1.4 3.4 2.5 7.5 2.5s7.5-1.1 7.5-2.5v-13M4.5 12c0 1.4 3.4 2.5 7.5 2.5s7.5-1.1 7.5-2.5" /></Svg>
export const IconMail = (p) => <Svg {...p}><rect x="3" y="5" width="18" height="14" /><path d="m3 6 9 7 9-7" /></Svg>
export const IconHealth = (p) => <Svg {...p}><path d="M2.5 12h4l2.5-6 4 12 2.5-6h6" /></Svg>
export const IconAudit = (p) => <Svg {...p}><path d="M8 4H5v17h14V4h-3" /><rect x="8" y="2.5" width="8" height="3.5" /><path d="M8.5 11h7M8.5 15h7M8.5 19h4" /></Svg>
export const IconFeatures = (p) => <Svg {...p}><rect x="3" y="5" width="18" height="6" /><circle cx="15" cy="8" r="1.5" /><rect x="3" y="13" width="18" height="6" /><circle cx="9" cy="16" r="1.5" /></Svg>
export const IconDuplicates = (p) => <Svg {...p}><rect x="8.5" y="8.5" width="12" height="12" /><path d="M15.5 8.5v-5h-12v12h5" /></Svg>

// Sections entières, dans le rail (une icône par section, menu volant des entrées).
export const IconSettings = (p) => <Svg {...p}><path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0" /><circle cx="16" cy="6" r="2" /><circle cx="10" cy="12" r="2" /><circle cx="18" cy="18" r="2" /></Svg>
export const IconAdmin = (p) => <Svg {...p}><rect x="3" y="4" width="18" height="12" /><path d="M8 20h8M12 16v4M7 8h4M7 11h7" /></Svg>

/** Icône de chaque entrée de section (Paramétrage, Administration) du registre. */
export const SECTION_ICONS = {
  categories: IconTag,
  discovery: IconRadar,
  'nis2-framework': IconFramework,
  users: IconUsers,
  perimeters: IconPerimeter,
  'search-index': IconIndex,
  platform: IconMail,
  features: IconFeatures,
  health: IconHealth,
  audit: IconAudit,
  duplicates: IconDuplicates,
}

/** Icône de chaque processus du registre (modules/registry.js). */
export const MODULE_ICONS = {
  incident: IconIncident,
  request: IconRequest,
  problem: IconProblem,
  change: IconChange,
  configuration: IconConfig,
  'service-level': IconSla,
  knowledge: IconKnowledge,
  csi: IconCsi,
  compliance: IconCompliance,
  'regulatory-notifications': IconDeclaration,
  project: IconProject,
}
