import React, { useEffect, useState } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import { api, getUser, setUser } from './api'
import Layout from './core/components/Layout'
import Login from './core/pages/Login'
import Console from './core/pages/Console'
import Reporting from './core/pages/Reporting'
import SearchIndexAdmin from './core/pages/SearchIndexAdmin'
import Perimeters from './core/pages/admin/Perimeters'
import PerimeterDetail from './core/pages/admin/PerimeterDetail'
import Health from './core/pages/admin/Health'
import PlatformSettings from './core/pages/admin/PlatformSettings'
import OptionalFeatures from './core/pages/admin/OptionalFeatures'
import Preferences from './core/pages/Preferences'
import Users from './core/pages/admin/Users'
import AuditLog from './core/pages/admin/AuditLog'
import Duplicates from './core/pages/admin/Duplicates'
import Categories from './core/pages/settings/Categories'
import Problems from './modules/problem-management/pages/Problems'
import ProblemDetail from './modules/problem-management/pages/ProblemDetail'
import Actions from './modules/problem-management/pages/Actions'
import Incidents from './modules/incident-management/pages/Incidents'
import IncidentDetail from './modules/incident-management/pages/IncidentDetail'
import Requests from './modules/service-request-management/pages/Requests'
import RequestCatalog from './modules/service-request-management/pages/RequestCatalog'
import RequestDetail from './modules/service-request-management/pages/RequestDetail'
import Changes from './modules/change-enablement/pages/Changes'
import ChangeDetail from './modules/change-enablement/pages/ChangeDetail'
import ChangeSchedule from './modules/change-enablement/pages/ChangeSchedule'
import StandardChanges from './modules/change-enablement/pages/StandardChanges'
import ConfigurationItems from './modules/service-configuration-management/pages/ConfigurationItems'
import ConfigurationItemDetail from './modules/service-configuration-management/pages/ConfigurationItemDetail'
import Inventory from './modules/service-configuration-management/pages/Inventory'
import Staging from './modules/service-configuration-management/pages/Staging'
import StagingAsset from './modules/service-configuration-management/pages/StagingAsset'
import DiscoverySettings from './modules/service-configuration-management/pages/DiscoverySettings'
import Services from './modules/service-level-management/pages/Services'
import ServiceDetail from './modules/service-level-management/pages/ServiceDetail'
import Agreements from './modules/service-level-management/pages/Agreements'
import AgreementDetail from './modules/service-level-management/pages/AgreementDetail'
import Articles from './modules/knowledge-management/pages/Articles'
import ArticleDetail from './modules/knowledge-management/pages/ArticleDetail'
import Improvements from './modules/continual-improvement/pages/Improvements'
import ImprovementDetail from './modules/continual-improvement/pages/ImprovementDetail'
import Projects from './modules/project-management/pages/Projects'
import ProjectDetail from './modules/project-management/pages/ProjectDetail'
import Assessments from './modules/compliance-assessment/pages/Assessments'
import AssessmentDetail from './modules/compliance-assessment/pages/AssessmentDetail'
import Declarations from './modules/regulatory-notifications/pages/Declarations'
import DeclarationDetail from './modules/regulatory-notifications/pages/DeclarationDetail'
import ReferentialSettings from './modules/compliance-assessment/pages/ReferentialSettings'

/**
 * Pages réservées : l'utilisateur affiché, ou la session reprise par le cookie HttpOnly
 * (nouvel onglet, rechargement : S5) ; sans session valide, la connexion.
 */
function Private({ children }) {
  const [state, setState] = useState(getUser() ? 'ok' : 'checking')
  useEffect(() => {
    if (state !== 'checking') return
    api.me().then(user => { setUser(user); setState('ok') }).catch(() => setState('none'))
  }, [])
  if (state === 'checking') return <p className="muted" style={{ padding: 24 }}>Chargement…</p>
  return state === 'ok' ? children : <Navigate to="/login" replace />
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<Private><Layout /></Private>}>
        {/* Pilotage — transverse à tous les processus */}
        <Route index element={<Console />} />
        <Route path="reports" element={<Reporting />} />
        {/* Administration */}
        <Route path="admin/search" element={<SearchIndexAdmin />} />
        <Route path="admin/perimeters" element={<Perimeters />} />
        <Route path="admin/perimeters/:id" element={<PerimeterDetail />} />
        <Route path="admin/users" element={<Users />} />
        <Route path="admin/health" element={<Health />} />
        <Route path="admin/platform" element={<PlatformSettings />} />
        <Route path="admin/features" element={<OptionalFeatures />} />
        <Route path="preferences" element={<Preferences />} />
        <Route path="admin/audit" element={<AuditLog />} />
        <Route path="admin/duplicates" element={<Duplicates />} />
        {/* Pratique — Gestion des incidents */}
        <Route path="incidents" element={<Incidents />} />
        <Route path="incidents/:id" element={<IncidentDetail />} />
        {/* Pratique — Gestion des demandes de service */}
        <Route path="requests" element={<Requests />} />
        <Route path="requests/catalog" element={<RequestCatalog />} />
        <Route path="requests/:id" element={<RequestDetail />} />
        {/* Pratique — Gestion des problèmes */}
        <Route path="problems" element={<Problems />} />
        <Route path="problems/:id" element={<ProblemDetail />} />
        <Route path="actions" element={<Actions />} />
        {/* Pratique — Habilitation des changements */}
        <Route path="changes" element={<Changes />} />
        <Route path="changes/schedule" element={<ChangeSchedule />} />
        <Route path="changes/models" element={<StandardChanges />} />
        <Route path="changes/:id" element={<ChangeDetail />} />
        {/* Pratique — Gestion de la configuration des services */}
        <Route path="configuration" element={<ConfigurationItems />} />
        <Route path="configuration/inventory" element={<Inventory />} />
        <Route path="configuration/staging" element={<Staging />} />
        <Route path="configuration/staging/:id" element={<StagingAsset />} />
        <Route path="configuration/:id" element={<ConfigurationItemDetail />} />
        {/* Pratique — Gestion des niveaux de service */}
        <Route path="services" element={<Services />} />
        <Route path="services/:id" element={<ServiceDetail />} />
        <Route path="agreements" element={<Agreements />} />
        <Route path="agreements/:id" element={<AgreementDetail />} />
        {/* Pratique — Gestion des connaissances */}
        <Route path="knowledge" element={<Articles />} />
        <Route path="knowledge/:id" element={<ArticleDetail />} />
        {/* Pratique — Amélioration continue */}
        <Route path="improvements" element={<Improvements />} />
        <Route path="improvements/:id" element={<ImprovementDetail />} />
        {/* Projets — la méthode active les composants (Scrum, Kanban, cycle en V, PRINCE2, hybride) */}
        <Route path="projects" element={<Projects />} />
        <Route path="projects/:id" element={<ProjectDetail />} />
        {/* Conformité & réglementation — NIS 2 (évaluation au ReCyF) */}
        <Route path="assessments" element={<Assessments />} />
        <Route path="assessments/:id" element={<AssessmentDetail />} />
        <Route path="regulatory-notifications" element={<Declarations />} />
        <Route path="regulatory-notifications/:id" element={<DeclarationDetail />} />
        {/* Paramétrage */}
        <Route path="settings/categories" element={<Categories />} />
        <Route path="settings/discovery" element={<DiscoverySettings />} />
        <Route path="settings/nis2" element={<ReferentialSettings />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
