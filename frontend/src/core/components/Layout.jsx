import React, { useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { api, clearSession, getUser } from '../../api'
import { GROUPS, MODULES, SECTIONS, groupOf, visibleFor } from '../../modules/registry'
import ThemeToggle from './ThemeToggle'
import NotificationBell from './NotificationBell'
import ErrorBoundary from './ErrorBoundary'
import useManagesPerimeter from '../useManagesPerimeter'
import { SOURCE_URL, LICENSE_LABEL } from '../about'
import {
  BrandMark, IconConsole, IconReport, IconLogout, IconMenu, IconClose, IconPreferences, IconCode, IconRail,
  IconSettings, IconAdmin, MODULE_ICONS, SECTION_ICONS,
} from './icons'

const ROLE_LABEL = { Admin: 'Administrateur', Manager: 'Gestionnaire', User: 'Utilisateur' }
const SECTION_ICON = { settings: IconSettings, administration: IconAdmin }

const PILOTAGE = [
  { href: '/', label: 'Ma console', description: 'Ce qui requiert votre intervention', icon: IconConsole, end: true },
  { href: '/reports', label: 'Reporting', description: 'Indicateurs des processus', icon: IconReport },
]

function SectionLabel({ children, divider, tone }) {
  return <p className={'nav-section' + (divider ? ' divider' : '') + (tone ? ' ' + tone : '')}><span className="dot" /><span className="nav-section-label">{children}</span></p>
}

const COLLAPSED_KEY = 's-aloha-nav-collapsed'
const RAIL_KEY = 's-aloha-nav-rail'

/** Barre réduite à un rail d'icônes, mémorisée dans ce navigateur (confort, pas une donnée). */
function useRail() {
  const [rail, setRail] = useState(() => {
    try { return localStorage.getItem(RAIL_KEY) === '1' } catch { return false }
  })
  const toggle = () => setRail((prev) => {
    try { localStorage.setItem(RAIL_KEY, prev ? '0' : '1') } catch { /* stockage indisponible : l'état reste en mémoire */ }
    return !prev
  })
  return [rail, toggle]
}

/** Groupes repliés par l'utilisateur, mémorisés dans ce navigateur (confort, pas une donnée). */
function useCollapsed() {
  const [collapsed, setCollapsed] = useState(() => {
    try { return new Set(JSON.parse(localStorage.getItem(COLLAPSED_KEY) || '[]')) } catch { return new Set() }
  })
  const toggle = (id) => setCollapsed((prev) => {
    const next = new Set(prev)
    if (next.has(id)) next.delete(id); else next.add(id)
    try { localStorage.setItem(COLLAPSED_KEY, JSON.stringify([...next])) } catch { /* stockage indisponible : l'état reste en mémoire */ }
    return next
  })
  return [collapsed, toggle]
}

const Chevron = () => (
  <svg className="nav-chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden><path d="m6 9 6 6 6-6" /></svg>
)

/**
 * Section repliable de la barre latérale (Pratiques, Conformité &
 * réglementation, Paramétrage, Administration). La section qui contient la
 * page ouverte reste dépliée : on ne cache jamais où l'on se trouve. Dans le
 * rail, toutes les sections sont dépliées : le titre n'y est qu'un filet.
 */
function NavGroup({ id, label, tone, current, collapsed, onToggle, rail, children }) {
  const expanded = rail || current || !collapsed.has(id)
  return (
    <div className="nav-group">
      <button type="button" className={'nav-section divider collapsible' + (tone ? ' ' + tone : '')}
        aria-expanded={expanded} aria-controls={'nav-group-' + id} aria-disabled={current || rail || undefined}
        title={rail ? label : current ? 'Contient la page ouverte' : expanded ? 'Replier la section' : 'Déplier la section'}
        tabIndex={rail ? -1 : undefined} onClick={() => !current && !rail && onToggle(id)}>
        <span className="dot" /><span className="nav-section-label">{label}</span><Chevron />
      </button>
      {expanded && <div id={'nav-group-' + id} className="nav-group-items">{children}</div>}
    </div>
  )
}

/** La page ouverte relève-t-elle de ce préfixe d'URL ? */
const within = (pathname, prefix) => pathname === prefix || pathname.startsWith(prefix + '/')

/**
 * Menu volant du rail : au survol ou au focus d'une
 * icône, le libellé, la description et les sous-entrées s'ouvrent à droite du
 * rail. Position fixe : le défilement de la barre ne le rogne pas.
 */
function RailItem({ rail, label, description, children, flyout }) {
  const [place, setPlace] = useState(null)
  if (!rail) return children
  // Collé au bord droit de l'entrée (pas d'interstice où le survol se perdrait) ;
  // dans le bas de l'écran, il s'ouvre vers le haut.
  const show = (e) => {
    const r = e.currentTarget.getBoundingClientRect()
    setPlace(r.top > window.innerHeight * 0.6
      ? { left: r.right, bottom: window.innerHeight - r.bottom }
      : { left: r.right, top: r.top })
  }
  // relatedTarget n'est pas toujours un nœud (sortie de la fenêtre : window, ou null).
  const hide = (e) => { if (!(e.relatedTarget instanceof Node && e.currentTarget.contains(e.relatedTarget))) setPlace(null) }
  return (
    <div className="rail-item" onMouseEnter={show} onMouseLeave={hide} onFocus={show} onBlur={hide}>
      {children}
      {place && (
        // Le cadre extérieur, transparent, comble l'écart jusqu'au panneau.
        <div className="rail-flyout" style={place}>
          <div className="rail-flyout-panel">
            <p className="rail-flyout-title">{label}</p>
            {description && <p className="rail-flyout-desc">{description}</p>}
            {flyout}
          </div>
        </div>
      )}
    </div>
  )
}

function NavItem({ href, label, description, icon: Icon, end, active, rail, onClick }) {
  return (
    <RailItem rail={rail} label={label} description={description}>
      <NavLink to={href} end={end} onClick={onClick} aria-label={rail ? label : undefined}
        title={rail ? undefined : description}
        className={({ isActive }) => 'nav-link' + ((active ?? isActive) ? ' active' : '')}>
        <span className="nav-icon"><Icon /></span>
        <span className="nav-label">{label}</span>
      </NavLink>
    </RailItem>
  )
}

/** Chemin d'une sous-entrée, sans son filtre (`/problems?status=…` → `/problems`). */
const pathOf = (href) => href.split('?')[0]

function ModuleItem({ module, location, role, rail, onClick }) {
  const Icon = MODULE_ICONS[module.id]
  const { pathname, search } = location

  // Un processus sans interface reste visible : la feuille de route se lit
  // dans la navigation elle-même, sans laisser croire qu'il est utilisable.
  if (module.status !== 'active') {
    return (
      <div className="nav-link soon" aria-disabled="true" title={module.label + ' — processus en préparation'}>
        <span className="nav-icon"><Icon /></span>
        <span className="nav-label">{module.label}</span>
        <span className="soon-badge">Bientôt</span>
      </div>
    )
  }

  const open = module.routes.some((r) => pathname === r || pathname.startsWith(r + '/'))
  const pages = (module.pages ?? []).filter((p) => visibleFor(p, role))
  // Une seule sous-entrée active : l'URL complète d'abord (« Erreurs connues »
  // est le registre filtré), puis le chemin seul (registre avec un autre
  // filtre), puis le préfixe le plus long (fiche /problems/12 ; fiche d'actif
  // /configuration/staging/3 sous « Sas de staging », pas sous le registre).
  const active = pages.find((p) => p.href === pathname + search)
    ?? pages.find((p) => p.href === pathname)
    ?? [...pages].sort((a, b) => pathOf(b.href).length - pathOf(a.href).length)
      .find((p) => pathname.startsWith(pathOf(p.href) + '/'))
  // Link, pas NavLink : NavLink marquerait « active » et aria-current sur le
  // seul chemin, donc le registre ET « Erreurs connues » (même chemin, autre
  // filtre). L'état actif est calculé ci-dessus.
  const sublinks = pages.map((p) => (
    <Link key={p.href} to={p.href} onClick={onClick}
      aria-current={p === active ? 'page' : undefined}
      className={'nav-sublink' + (p === active ? ' active' : '')}>
      {p.label}
    </Link>
  ))
  // Dans le rail, les sous-entrées vivent dans le menu volant, quelle que soit la page ouverte.
  if (rail) {
    return (
      <RailItem rail label={module.label} description={module.description} flyout={sublinks}>
        <NavLink to={module.href} onClick={onClick} aria-label={module.label}
          className={'nav-link' + (open ? ' active' : '')}>
          <span className="nav-icon"><Icon /></span>
          <span className="nav-label">{module.label}</span>
        </NavLink>
      </RailItem>
    )
  }
  return (
    <>
      <NavItem href={module.href} label={module.label} description={module.description}
        icon={Icon} active={open} onClick={onClick} />
      {open && sublinks.length > 0 && <div className="nav-sublinks">{sublinks}</div>}
    </>
  )
}

function SidebarContent({ user, location, collapsed, onToggle, rail, onRail, onClose, onLogout }) {
  const role = user?.role
  // PER-13 : Paramétrage suit le rôle de gestionnaire d'un périmètre, pas le rôle AD ; ses
  // entrées réservées à l'Admin (Référentiel NIS 2) le restent.
  const manages = useManagesPerimeter()
  const sectionRole = (s) => (s.id === 'settings' && role !== 'Admin' ? (manages ? 'Manager' : 'User') : role)
  const group = (id, label, tone, current, children) => (
    <NavGroup key={id} id={id} label={label} tone={tone} current={current}
      collapsed={collapsed} onToggle={onToggle} rail={rail}>
      {children}
    </NavGroup>
  )
  return (
    <div className="sidebar-inner">
      <div className="sidebar-glow" aria-hidden />
      <div className="brand">
        <div className="brand-mark"><BrandMark /></div>
        <div className="brand-text">
          <p className="brand-name">S-Aloha</p>
          <p className="brand-sub">Plateforme ITIL</p>
        </div>
      </div>

      <nav className="nav" aria-label="Navigation principale">
        <SectionLabel>Pilotage</SectionLabel>
        {PILOTAGE.filter((item) => visibleFor(item, role))
          .map((item) => <NavItem key={item.href} {...item} rail={rail} onClick={onClose} />)}

        {GROUPS.map((g) => {
          // Pas de section vide : sans module visible pour ce rôle, ni titre ni filet.
          const modules = MODULES.filter((m) => groupOf(m) === g.id && visibleFor(m, role))
          if (modules.length === 0) return null
          const current = modules.some((m) => m.routes?.some((r) => within(location.pathname, r)))
          return group(g.id, g.label, g.tone, current,
            modules.map((m) => <ModuleItem key={m.id} module={m} location={location} role={role} rail={rail} onClick={onClose} />))
        })}

        {/* Paramétrage et Administration au pied de la navigation, à l'écart
            du travail quotidien. */}
        <div className="nav-bottom">
          {SECTIONS.filter((s) => visibleFor(s, sectionRole(s))).map((s) => {
            const entries = s.entries.filter((e) => visibleFor(e, sectionRole(s)))
            if (entries.length === 0) return null
            const current = entries.some((e) => within(location.pathname, e.href))
            const tone = s.id === 'administration' ? 'admin' : undefined
            // Rail : la section tient en une icône, ses entrées dans le menu volant.
            if (rail) return (
              <div key={s.id} className={'rail-section' + (tone ? ' ' + tone : '')}>
                <SectionLabel divider tone={tone}>{s.label}</SectionLabel>
                <RailItem rail label={s.label} flyout={entries.map((e) =>
                  <Link key={e.href} to={e.href} onClick={onClose}
                    aria-current={within(location.pathname, e.href) ? 'page' : undefined}
                    className={'nav-sublink' + (within(location.pathname, e.href) ? ' active' : '')}>{e.label}</Link>)}>
                  <NavLink to={entries[0].href} onClick={onClose} aria-label={s.label}
                    className={'nav-link' + (current ? ' active' : '')}>
                    <span className="nav-icon">{React.createElement(SECTION_ICON[s.id] ?? IconConsole)}</span>
                    <span className="nav-label">{s.label}</span>
                  </NavLink>
                </RailItem>
              </div>
            )
            return group(s.id, s.label, tone, current,
              entries.map((e) =>
                <NavItem key={e.href} href={e.href} label={e.label} description={e.description}
                  icon={SECTION_ICONS[e.icon] ?? IconConsole} rail={rail} onClick={onClose} />))
          })}
        </div>
      </nav>

      <div className="sidebar-foot">
        <div className="me">
          <div className="avatar" title={rail ? `${user?.displayName ?? ''} · ${ROLE_LABEL[role] ?? role ?? ''}` : undefined}>
            {(user?.displayName || '?').charAt(0).toUpperCase()}
          </div>
          <div className="me-text">
            <p className="me-name">{user?.displayName}</p>
            <p className="me-role">{ROLE_LABEL[role] ?? role}</p>
          </div>
          <NotificationBell placement="side" onNavigate={onClose} />
          <ThemeToggle />
        </div>
        <div className="foot-actions">
          {/* SOC-22 : courriels de notification. */}
          <Link className="foot-action" to="/preferences" onClick={onClose} title="Mes préférences" aria-label="Mes préférences">
            <IconPreferences /><span className="foot-label">Mes préférences</span>
          </Link>
          <button type="button" className="foot-action" onClick={onLogout} title="Se déconnecter" aria-label="Se déconnecter">
            <IconLogout /><span className="foot-label">Se déconnecter</span>
          </button>
          {onRail && (
            <button type="button" className="foot-action rail-toggle" onClick={onRail} aria-pressed={rail}
              aria-label={rail ? 'Déployer le menu' : 'Réduire le menu'} title={rail ? 'Déployer le menu' : 'Réduire le menu'}>
              <IconRail /><span className="foot-label">Réduire le menu</span>
            </button>
          )}
        </div>
        <a className="source-link" href={SOURCE_URL} target="_blank" rel="noreferrer" title={'Code source · ' + LICENSE_LABEL}>
          <IconCode /><span className="foot-label">Code source · {LICENSE_LABEL}</span>
        </a>
      </div>
    </div>
  )
}

export default function Layout() {
  const user = getUser()
  const nav = useNavigate()
  const location = useLocation()
  const [open, setOpen] = useState(false)
  // Un seul état pour la barre latérale et le tiroir mobile.
  const [collapsed, toggleCollapsed] = useCollapsed()
  // Le rail ne concerne que la barre du bureau : le tiroir mobile reste déployé.
  const [rail, toggleRail] = useRail()
  // S5 : l'API efface le cookie de session ; l'utilisateur affiché est oublié.
  const logout = () => api.logout().catch(() => {}).finally(() => { clearSession(); nav('/login') })

  return (
    <div className={'shell' + (rail ? ' rail' : '')}>
      <aside className="sidebar">
        <SidebarContent user={user} location={location} collapsed={collapsed} onToggle={toggleCollapsed}
          rail={rail} onRail={toggleRail} onLogout={logout} />
      </aside>

      {open && (
        <div className="drawer" role="dialog" aria-modal="true" aria-label="Navigation">
          <div className="drawer-backdrop" onClick={() => setOpen(false)} />
          <div className="drawer-panel">
            <button className="drawer-close" onClick={() => setOpen(false)} aria-label="Fermer la navigation"><IconClose /></button>
            <SidebarContent user={user} location={location} collapsed={collapsed} onToggle={toggleCollapsed} onClose={() => setOpen(false)} onLogout={logout} />
          </div>
        </div>
      )}

      <div className="content">
        <header className="topbar">
          <button className="topbar-btn" onClick={() => setOpen(true)} aria-label="Ouvrir la navigation"><IconMenu /></button>
          <div className="brand-mark small"><BrandMark size={24} /></div>
          <span className="brand-name">S-Aloha</span>
          <span className="push"><NotificationBell placement="top" /></span>
          <button className="topbar-btn" onClick={logout} aria-label="Se déconnecter" title="Se déconnecter"><IconLogout /></button>
        </header>
        <main className="main bg-canvas">
          {/* Une page qui échoue n'emporte pas la navigation ; changer de page réarme la garde. */}
          <div className="main-inner"><ErrorBoundary key={location.pathname}><Outlet /></ErrorBoundary></div>
        </main>
      </div>
    </div>
  )
}
