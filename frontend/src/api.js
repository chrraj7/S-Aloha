const API = '/api'

// S5 (ADR-0018) : le jeton de session vit dans un cookie HttpOnly posé par l'API, illisible
// par les scripts de la page ; le navigateur ne garde ici que l'utilisateur affiché (non secret).
export function getUser() {
  const raw = sessionStorage.getItem('user')
  return raw ? JSON.parse(raw) : null
}
export function setUser(user) { sessionStorage.setItem('user', JSON.stringify(user)) }
export function clearSession() { sessionStorage.clear() }

async function request(path, options = {}) {
  const res = await fetch(API + path, {
    ...options,
    credentials: 'same-origin',
    headers: {
      // Un formulaire de fichier (pièce jointe, SOC-25) laisse le navigateur poser son type multipart.
      ...(options.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
      // Protection CSRF : exigé par l'API sur toute modification authentifiée par le cookie.
      'X-S-Aloha': '1',
      ...options.headers
    }
  })
  // Session expirée (sauf à la connexion et à la reprise de session, qui gèrent leur 401).
  if (res.status === 401 && !options.quiet) {
    clearSession()
    window.location.href = '/login'
    throw new Error('Session expirée')
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    if (res.status === 413 && !body.message) throw new Error('Fichier trop volumineux pour le serveur (413) : vérifier la taille admise par le proxy.')
    // Le corps reste joint à l'erreur : un rapport détaillé (import CSV, CFG-30) s'y lit.
    throw Object.assign(new Error(body.message || 'Une erreur est survenue (' + res.status + ')'), { body })
  }
  if (options.raw) return res
  return res.status === 204 ? null : res.json()
}

/** Téléchargement d'un fichier servi par l'API (export CSV), sous le nom qu'elle propose. */
async function download(path) {
  const res = await request(path, { raw: true })
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? 'export.csv'
  const a = document.createElement('a')
  a.href = URL.createObjectURL(await res.blob())
  a.download = decodeURIComponent(name)
  a.click()
  URL.revokeObjectURL(a.href)
}

/** Client CRUD d'un processus servi par le RecordController commun de l'API. */
/** Transition forcée par un Admin (SOC-05) : motif transmis à l'API, tracé au journal. */
const forced = (opts) => (opts?.force ? `?force=true&reason=${encodeURIComponent(opts.reason ?? '')}` : '')

const records = (path) => ({
  list: (params = {}) => request(path + '?' + new URLSearchParams(params)),
  get: (id) => request(`${path}/${id}`),
  create: (dto) => request(path, { method: 'POST', body: JSON.stringify(dto) }),
  update: (id, dto, opts) => request(`${path}/${id}${forced(opts)}`, { method: 'PUT', body: JSON.stringify(dto) }),
  remove: (id) => request(`${path}/${id}`, { method: 'DELETE' }),
  // SOC-14 : ce que la suppression emporterait (Admin).
  deletion: (id) => request(`${path}/${id}/deletion`),
  transitions: () => request(`${path}/transitions`),
  // OPT-15 : propositions ouvertes des fonctionnalités optionnelles ; accepter passe par la modification,
  // avec l'en-tête X-S-Aloha-Proposal (acceptée si la valeur retenue est la proposée, corrigée sinon).
  proposals: (id) => request(`${path}/${id}/proposals`),
  applyProposal: (id, proposal, dto) => request(`${path}/${id}`, { method: 'PUT', body: JSON.stringify(dto), headers: { 'X-S-Aloha-Proposal': String(proposal) } }),
  rejectProposal: (id, proposal) => request(`${path}/${id}/proposals/${proposal}/reject`, { method: 'POST' })
})

export const api = {
  audit: { list: (type, id) => request(`/audit?type=${type}&id=${id}`) },
  // OPT-04 : clés des fonctionnalités optionnelles activées.
  features: () => request('/features'),
  // TRI-02 : suggestions du tri assisté à la saisie d'un incident.
  triage: { suggest: draft => request('/triage/suggest', { method: 'POST', body: JSON.stringify(draft) }) },
  // Administration (ADM, Admin) : santé, journal d'audit global.
  // SOC-25 : pièces jointes d'un enregistrement (le téléchargement est un simple lien).
  attachments: {
    list: (type, id) => request(`/attachments?type=${type}&id=${id}`),
    upload: (type, id, file) => {
      const form = new FormData()
      form.append('type', type); form.append('id', String(id)); form.append('file', file)
      return request('/attachments', { method: 'POST', body: form })
    },
    remove: (id) => request(`/attachments/${id}`, { method: 'DELETE' }),
    contentUrl: (id) => `${API}/attachments/${id}/content`
  },
  // SOC-22 : mes courriels de notification. Pas sous `me` : `api.me()` (session
  // courante, plus bas) écraserait la clé, et la page Mes préférences planterait.
  preferences: {
    mail: () => request('/me/mail-preferences'),
    saveMail: (dto) => request('/me/mail-preferences', { method: 'PUT', body: JSON.stringify(dto) })
  },
  admin: {
    health: () => request('/admin/health'),
    users: (params = {}) => request('/admin/users?' + new URLSearchParams(params)),
    duplicates: () => request('/admin/duplicates'),
    mergeDuplicate: (dto) => request('/admin/duplicates/merge', { method: 'POST', body: JSON.stringify(dto) }),
    dismissDuplicate: (dto) => request('/admin/duplicates/dismiss', { method: 'POST', body: JSON.stringify(dto) }),
    audit: (params = {}) => request('/admin/audit?' + new URLSearchParams(params)),
    auditExport: (params = {}) => download('/admin/audit/export?' + new URLSearchParams(params)),
    mail: () => request('/admin/settings/mail'),
    saveMail: (dto) => request('/admin/settings/mail', { method: 'PUT', body: JSON.stringify(dto) }),
    testMail: (to) => request('/admin/settings/mail/test', { method: 'POST', body: JSON.stringify({ to }) }),
    storage: () => request('/admin/settings/storage'),
    saveStorage: (dto) => request('/admin/settings/storage', { method: 'PUT', body: JSON.stringify(dto) }),
    testStorage: () => request('/admin/settings/storage/test', { method: 'POST' }),
    // OPT-01 à OPT-03 : fonctionnalités optionnelles, activation et coupure.
    features: () => request('/admin/features'),
    switchFeature: (key, enabled) => request(`/admin/features/${key}`, { method: 'PUT', body: JSON.stringify({ enabled }) })
  },
  login: (username, password) =>
    request('/auth/login', { method: 'POST', body: JSON.stringify({ username, password }), quiet: true }),
  me: () => request('/auth/me', { quiet: true }),
  logout: () => request('/auth/logout', { method: 'POST', quiet: true }),
  problems: {
    list: (params = {}) => request('/problems?' + new URLSearchParams(params)),
    get: (id) => request('/problems/' + id),
    create: (dto) => request('/problems', { method: 'POST', body: JSON.stringify(dto) }),
    update: (id, dto, opts) => request('/problems/' + id + forced(opts), { method: 'PUT', body: JSON.stringify(dto) }),
    remove: (id) => request('/problems/' + id, { method: 'DELETE' }),
    deletion: (id) => request(`/problems/${id}/deletion`),
    transitions: () => request('/problems/transitions')
  },
  analyses: {
    create: (pid, dto) => request(`/problems/${pid}/analyses`, { method: 'POST', body: JSON.stringify(dto) }),
    update: (pid, id, dto) => request(`/problems/${pid}/analyses/${id}`, { method: 'PUT', body: JSON.stringify(dto) }),
    remove: (pid, id) => request(`/problems/${pid}/analyses/${id}`, { method: 'DELETE' }),
    // PRB-07 : conclusion validée comme cause racine (gestionnaire).
    validate: (pid, id) => request(`/problems/${pid}/analyses/${id}/validate`, { method: 'POST' })
  },
  actions: {
    all: (params = {}) => request('/actions?' + new URLSearchParams(params)),
    create: (pid, dto) => request(`/problems/${pid}/actions`, { method: 'POST', body: JSON.stringify(dto) }),
    update: (id, dto) => request('/actions/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
    remove: (id) => request('/actions/' + id, { method: 'DELETE' }),
    requestApproval: id => request(`/actions/${id}/request-approval`, { method: 'POST' }),
    setChange: (id, reference) => request(`/actions/${id}/change`, { method: 'PUT', body: JSON.stringify({ reference }) })
  },
  incidents: {
    ...records('/incidents'),
    // INC-41 : qualification réglementaire d'un incident de sécurité (gestionnaire).
    qualify: (id, dto) => request(`/incidents/${id}/qualification`, { method: 'POST', body: JSON.stringify(dto) }),
    // INC-16 : parent et enfants (même panne).
    family: id => request(`/incidents/${id}/family`),
    setParent: (id, parentReference) => request(`/incidents/${id}/parent`, { method: 'PUT', body: JSON.stringify({ parentReference }) }),
    // TRI-07 : panne probable de l'incident (bandeau de la fiche).
    triage: id => request(`/incidents/${id}/triage`)
  },
  regulatoryNotifications: records('/regulatory-notifications'),
  requests: {
    ...records('/requests'),
    // REQ-12 : requalification en changement ou en incident (la demande est annulée).
    requalify: (id, dto) => request(`/requests/${id}/requalify`, { method: 'POST', body: JSON.stringify(dto) }),
    // REQ-24 : tâches d'exécution ; REQ-32 : enquête de satisfaction.
    tasks: id => request(`/requests/${id}/tasks`),
    addTask: (id, dto) => request(`/requests/${id}/tasks`, { method: 'POST', body: JSON.stringify(dto) }),
    updateTask: (id, taskId, dto) => request(`/requests/${id}/tasks/${taskId}`, { method: 'PUT', body: JSON.stringify(dto) }),
    removeTask: (id, taskId) => request(`/requests/${id}/tasks/${taskId}`, { method: 'DELETE' }),
    rate: (id, dto) => request(`/requests/${id}/satisfaction`, { method: 'POST', body: JSON.stringify(dto) })
  },
  changes: {
    ...records('/changes'),
    schedule: () => request('/changes/schedule'),
    conflicts: (id) => request(`/changes/${id}/conflicts`),
    // CHG-20 : décisions du CAB.
    approvals: (id) => request(`/changes/${id}/approvals`),
    decide: (id, dto) => request(`/changes/${id}/approvals`, { method: 'POST', body: JSON.stringify(dto) })
  },
  // CHG-25 : périodes de gel.
  changeFreezes: {
    list: () => request('/change-freezes'),
    create: dto => request('/change-freezes', { method: 'POST', body: JSON.stringify(dto) }),
    remove: id => request('/change-freezes/' + id, { method: 'DELETE' })
  },
  // CHG-26 : modèles de changement standard.
  standardChanges: {
    list: (all = false) => request('/standard-changes' + (all ? '?all=true' : '')),
    create: dto => request('/standard-changes', { method: 'POST', body: JSON.stringify(dto) }),
    update: (id, dto) => request('/standard-changes/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
    // CHG-27 : revue d'un modèle suspendu après un échec.
    review: (id, dto) => request(`/standard-changes/${id}/review`, { method: 'POST', body: JSON.stringify(dto) })
  },
  configurationItems: {
    impact: (id) => request(`/configuration-items/${id}/impact`),
    impactOf: (type, id) => request(`/configuration-items/impact-of?type=${type}&id=${id}`),
    verify: (id) => request(`/configuration-items/${id}/verify`, { method: 'POST' }),
    // CFG-16 : attributs définis par type ; CFG-30 : import CSV (Admin), contrôle seul avec dryRun.
    attributeTypes: () => request('/configuration-items/attribute-types'),
    importCsv: (csv, dryRun) => request('/configuration-items/import' + (dryRun ? '?dryRun=true' : ''), { method: 'POST', body: JSON.stringify({ csv }) }),
    ...records('/configuration-items'),
    addRelation: (id, dto) => request(`/configuration-items/${id}/relations`, { method: 'POST', body: JSON.stringify(dto) }),
    removeRelation: (relId) => request('/configuration-items/relations/' + relId, { method: 'DELETE' }),
    // CFG-39 : rapport d'inventaire (portefeuille applicatif, actifs matériels).
    inventory: (retired = false) => request('/configuration-items/inventory' + (retired ? '?retired=true' : ''))
  },
  // Découverte automatique (CFG-40 à CFG-47) : sas de staging, tâches de scan, coffre, fins de support.
  discovery: {
    // CFG-50 : base des constructeurs par préfixe MAC (registre OUI de l'IEEE, importé par l'Admin).
    // CFG-53 : clés d'hôte SSH retenues au premier contact.
    // CFG-55 : relations proposées par la découverte.
    relations: {
      list: (params = {}) => request('/discovery/relations?' + new URLSearchParams(params)),
      accept: id => request(`/discovery/relations/${id}/accept`, { method: 'POST' }),
      dismiss: id => request(`/discovery/relations/${id}/dismiss`, { method: 'POST' })
    },
    sshHostKeys: {
      list: () => request('/discovery/ssh-host-keys'),
      accept: id => request(`/discovery/ssh-host-keys/${id}/accept`, { method: 'POST' }),
      forget: id => request('/discovery/ssh-host-keys/' + id, { method: 'DELETE' })
    },
    macVendors: {
      get: () => request('/discovery/mac-vendors'),
      import: csv => request('/discovery/mac-vendors/import', { method: 'POST', body: JSON.stringify({ csv }) })
    },
    staging: {
      list: (params = {}) => request('/discovery/staging?' + new URLSearchParams(params)),
      summary: () => request('/discovery/staging/summary'),
      get: (id) => request('/discovery/staging/' + id),
      reconcile: (id, dto) => request(`/discovery/staging/${id}/reconcile`, { method: 'POST', body: JSON.stringify(dto) }),
      ignore: (id) => request(`/discovery/staging/${id}/ignore`, { method: 'POST' }),
      restore: (id) => request(`/discovery/staging/${id}/restore`, { method: 'POST' }),
      remove: (id) => request('/discovery/staging/' + id, { method: 'DELETE' })
    },
    jobs: {
      list: () => request('/discovery/jobs'),
      get: (id) => request('/discovery/jobs/' + id),
      create: (dto) => request('/discovery/jobs', { method: 'POST', body: JSON.stringify(dto) }),
      update: (id, dto) => request('/discovery/jobs/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
      remove: (id) => request('/discovery/jobs/' + id, { method: 'DELETE' }),
      run: (id) => request(`/discovery/jobs/${id}/run`, { method: 'POST' })
    },
    credentials: {
      list: () => request('/discovery/credentials'),
      create: (dto) => request('/discovery/credentials', { method: 'POST', body: JSON.stringify(dto) }),
      update: (id, dto) => request('/discovery/credentials/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
      remove: (id) => request('/discovery/credentials/' + id, { method: 'DELETE' })
    },
    endOfLife: {
      list: () => request('/discovery/end-of-life'),
      create: (dto) => request('/discovery/end-of-life', { method: 'POST', body: JSON.stringify(dto) }),
      update: (id, dto) => request('/discovery/end-of-life/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
      remove: (id) => request('/discovery/end-of-life/' + id, { method: 'DELETE' })
    }
  },
  services: {
    ...records('/services'),
    // CFG-18 : CI hors service qui dégradent le service.
    degradation: id => request(`/services/${id}/degradation`)
  },
  // ADM-50 : catégories d'incident et de problème (lecture par tous, tenue par les gestionnaires).
  categories: {
    list: practice => request('/categories?practice=' + practice),
    create: dto => request('/categories', { method: 'POST', body: JSON.stringify(dto) }),
    update: (id, dto) => request('/categories/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
    remove: id => request('/categories/' + id, { method: 'DELETE' })
  },
  // REQ-20 : catalogue de demandes (modèles).
  requestCatalog: {
    list: (all = false) => request('/request-catalog' + (all ? '?all=true' : '')),
    create: dto => request('/request-catalog', { method: 'POST', body: JSON.stringify(dto) }),
    update: (id, dto) => request('/request-catalog/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
    remove: id => request('/request-catalog/' + id, { method: 'DELETE' })
  },
  agreements: {
    ...records('/agreements'),
    review: (id, dto) => request(`/agreements/${id}/reviews`, { method: 'POST', body: JSON.stringify(dto) }),
    compliance: (id, params = {}) => request(`/agreements/${id}/compliance?` + new URLSearchParams(params))
  },
  knowledge: {
    ...records('/knowledge'),
    submit: (id) => request(`/knowledge/${id}/submit`, { method: 'POST' }),
    review: (id, nextReviewDate) => request(`/knowledge/${id}/review`, { method: 'POST', body: JSON.stringify({ nextReviewDate }) }),
    requestArchive: (id, reason) => request(`/knowledge/${id}/archive-request`, { method: 'POST', body: JSON.stringify({ reason }) }),
    usage: (id) => request(`/knowledge/${id}/usage`),
    // KB-15 : versions publiées ; KB-13 : retour des lecteurs.
    versions: (id) => request(`/knowledge/${id}/versions`),
    myFeedback: (id) => request(`/knowledge/${id}/feedback`),
    feedback: (id, helpful) => request(`/knowledge/${id}/feedback`, { method: 'POST', body: JSON.stringify({ helpful }) })
  },
  improvements: {
    ...records('/improvements'),
    // CSI-15 : conclusion du contrôle de durabilité.
    followUp: (id, dto) => request(`/improvements/${id}/follow-up`, { method: 'POST', body: JSON.stringify(dto) })
  },
  assessments: {
    ...records('/assessments'),
    questionnaire: (id) => request(`/assessments/${id}/questionnaire`),
    answer: (id, requirementId, dto) =>
      request(`/assessments/${id}/responses/${requirementId}`, { method: 'PUT', body: JSON.stringify(dto) }),
    score: (id) => request(`/assessments/${id}/score`),
    export: (id) => request(`/assessments/${id}/export`),
    addEvidence: (id, requirementId, reference) =>
      request(`/assessments/${id}/responses/${requirementId}/evidence`, { method: 'POST', body: JSON.stringify({ reference }) }),
    removeEvidence: (id, evidenceId) => request(`/assessments/${id}/evidence/${evidenceId}`, { method: 'DELETE' }),
    compare: (id, otherId) => request(`/assessments/${id}/compare/${otherId}`),
    createImprovement: (id, code) =>
      request(`/assessments/${id}/improvements`, { method: 'POST', body: JSON.stringify({ code }) })
  },
  projects: {
    ...records('/projects'),
    summary: (id) => request(`/projects/${id}/summary`),
    /** Éléments d'un projet : phases, milestones, deliverables, sprints, workitems, risks, issues. */
    parts: (id, segment) => ({
      list: () => request(`/projects/${id}/${segment}`),
      create: (dto) => request(`/projects/${id}/${segment}`, { method: 'POST', body: JSON.stringify(dto) }),
      update: (partId, dto) => request(`/projects/${id}/${segment}/${partId}`, { method: 'PUT', body: JSON.stringify(dto) }),
      remove: (partId) => request(`/projects/${id}/${segment}/${partId}`, { method: 'DELETE' })
    }),
    approveStage: (id, phaseId) => request(`/projects/${id}/phases/${phaseId}/approve`, { method: 'POST' }),
    burndown: (id, sprintId) => request(`/projects/${id}/sprints/${sprintId}/burndown`)
  },
  compliance: {
    referential: () => request('/compliance/referential'),
    importText: (csv) => request('/compliance/referential/import', { method: 'POST', body: JSON.stringify({ csv }) })
  },
  perimeters: {
    mine: () => request('/perimeters'),
    // Administration › Périmètres (Admin, PER-01, PER-02, PER-12)
    list: () => request('/admin/perimeters'),
    get: (id) => request('/admin/perimeters/' + id),
    create: (dto) => request('/admin/perimeters', { method: 'POST', body: JSON.stringify(dto) }),
    update: (id, dto) => request('/admin/perimeters/' + id, { method: 'PUT', body: JSON.stringify(dto) }),
    remove: (id) => request('/admin/perimeters/' + id, { method: 'DELETE' }),
    restore: (id) => request(`/admin/perimeters/${id}/restore`, { method: 'POST' }),
    purge: (id) => request(`/admin/perimeters/${id}/purge`, { method: 'DELETE' }),
    assign: (id, dto) => request(`/admin/perimeters/${id}/assignments`, { method: 'POST', body: JSON.stringify(dto) }),
    unassign: (assignmentId) => request('/admin/assignments/' + assignmentId, { method: 'DELETE' })
  },
  notifications: {
    list: () => request('/notifications'),
    read: (id) => request(`/notifications/${id}/read`, { method: 'POST' }),
    readAll: () => request('/notifications/read-all', { method: 'POST' })
  },
  comments: {
    list: (type, id) => request(`/comments?type=${type}&id=${id}`),
    create: (dto) => request('/comments', { method: 'POST', body: JSON.stringify(dto) }),
    remove: (id) => request('/comments/' + id, { method: 'DELETE' })
  },
  links: {
    list: (type, id) => request(`/links?type=${type}&id=${id}`),
    create: (dto) => request('/links', { method: 'POST', body: JSON.stringify(dto) }),
    remove: (id) => request('/links/' + id, { method: 'DELETE' })
  },
  search: {
    query: (q, types) => request('/search?' + new URLSearchParams({ q, ...(types ? { types } : {}) })),
    similar: (type, id) => request('/search/similar?' + new URLSearchParams({ type, id })),
    status: () => request('/search/status'),
    reindex: () => request('/search/reindex', { method: 'POST' })
  },
  console: { get: (perimeter) => request('/console' + (perimeter ? '?perimeter=' + perimeter : '')) },
  directory: { search: (q) => request('/directory/search?q=' + encodeURIComponent(q)) },
  reports: {
    summary: () => request('/reports/summary'),
    dashboard: (tab, params = {}) => request(`/reports/dashboard/${tab}?` + new URLSearchParams(params))
  }
}
