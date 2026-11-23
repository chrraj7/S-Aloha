#!/usr/bin/env python3
"""Charge un jeu de données de démonstration dans S-Aloha, par l'API.

Passer par l'API (et non par la base) garantit que les données respectent les mêmes
règles que celles saisies dans l'interface : références, priorités, droits, statuts.
Toutes les données sont fictives.

Lancé par le service `donnees` de docker-compose.demo.yml ; utilisable seul :

    python3 demo/donnees/charger.py http://localhost

Idempotent : ne fait rien si des problèmes existent déjà.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://api:8080').rstrip('/')
COMPTES = {
    'admin': ('demo.admin', 'Demo-Admin-2026'),
    'manager': ('demo.manager', 'Demo-Manager-2026'),
    'user': ('demo.user', 'Demo-User-2026'),
}
MARC = {'ownerType': 'User', 'ownerId': 'demo.manager', 'ownerDisplayName': 'Marc Manager'}
URSULA = {'ownerType': 'User', 'ownerId': 'demo.user', 'ownerDisplayName': 'Ursula Utilisatrice'}
MAINTENANT = datetime.now(timezone.utc).replace(microsecond=0)
DSI = {'technicalOwnerType': 'User', 'technicalOwnerId': 'demo.manager', 'technicalOwnerDisplayName': 'Marc Manager'}
# Identification des CI (CFG-43 : portefeuille applicatif ; CFG-44 : actifs matériels). Valeurs fictives.
DETAILS_CI = {
    'VPN-GW-01': {'vendor': 'Fortinet', 'model': 'FortiGate 100F', 'serialNumber': 'FGT1HF-DEMO-0001', 'ipAddress': '10.0.0.1'},
    'FW-SIEGE-01': {'vendor': 'Fortinet', 'model': 'FortiGate 200F', 'serialNumber': 'FGT2HF-DEMO-0002', 'ipAddress': '10.0.0.254'},
    'SRV-MAIL-01': {'operatingSystem': 'Microsoft Windows Server 2016 Standard', 'ipAddress': '10.0.1.10'},
    'SAN-01': {'vendor': 'Synology', 'model': 'RackStation RS3621xs+', 'serialNumber': 'SYN-DEMO-0003', 'ipAddress': '10.0.1.20'},
    'SW-MAHAJANGA-01': {'vendor': 'Cisco', 'model': 'Catalyst 1000', 'serialNumber': 'FOC-DEMO-0004', 'ipAddress': '10.20.0.2'},
    'APP-EXCHANGE': {'appCode': 'MSG', 'businessUnit': 'Secrétariat général', 'criticality': 'Majeure', 'rtoHours': 8,
                     'rpoHours': 4, 'gdprSensitive': True, 'nis2Scope': True, **DSI, 'businessOwnerType': 'User',
                     'businessOwnerId': 'demo.user', 'businessOwnerDisplayName': 'Ursula Utilisatrice'},
    'APP-ERP': {'appCode': 'ERP', 'businessUnit': 'Direction financière', 'criticality': 'Vitale', 'rtoHours': 4,
                'rpoHours': 1, 'gdprSensitive': True, 'nis2Scope': True, **DSI, 'businessOwnerType': 'User',
                'businessOwnerId': 'demo.user', 'businessOwnerDisplayName': 'Ursula Utilisatrice'},
}


def jour(delta):
    return (MAINTENANT + timedelta(days=delta)).isoformat().replace('+00:00', 'Z')


def appel(methode, chemin, jeton=None, corps=None):
    req = urllib.request.Request(BASE + chemin, method=methode,
                                 data=json.dumps(corps).encode() if corps is not None else None)
    req.add_header('Content-Type', 'application/json')
    if jeton:
        req.add_header('Authorization', f'Bearer {jeton}')
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            texte = r.read().decode()
            return json.loads(texte) if texte else None
    except urllib.error.HTTPError as e:
        sys.exit(f'✖ {methode} {chemin} → {e.code} {e.read().decode()[:300]}')


def connexion(role):
    nom, mdp = COMPTES[role]
    for _ in range(60):  # l'API et l'annuaire peuvent encore démarrer
        try:
            return appel('POST', '/api/auth/login', corps={'username': nom, 'password': mdp, 'returnToken': True})['token']
        except (urllib.error.URLError, ConnectionError, OSError):
            time.sleep(5)
    sys.exit('✖ API injoignable')


def creer(jeton, chemin, corps, statut=None, par=None):
    """Crée un enregistrement, puis lui donne son statut. Les transitions sont
    contrôlées par l'API (SOC-05) : `statut` peut être une liste d'étapes, et `par`
    le jeton de qui les fait quand ce n'est pas l'auteur (quatre yeux)."""
    e = appel('POST', chemin, jeton, corps)
    for etape in ([statut] if isinstance(statut, str) else statut or []):
        e = appel('PUT', f'{chemin}/{e["id"]}', par or jeton, {**corps, 'status': etape})
    return e


def lier(jeton, type_, id_, reference):
    appel('POST', '/api/links', jeton, {'fromType': type_, 'fromId': id_, 'toReference': reference})


def main():
    m, u = connexion('manager'), connexion('user')
    # CHG-21 : le demandeur n'autorise pas son propre changement ; l'Admin autorise ceux du gestionnaire.
    a = connexion('admin')
    if appel('GET', '/api/problems', m):
        print('Données déjà présentes : rien à faire.')
        return

    # --- Catalogue des services et niveaux de service ---------------------------
    messagerie = creer(m, '/api/services', {**MARC, 'title': 'Messagerie',
        'description': 'Courriel et calendriers de toute l’organisation.',
        'criticality': 'Élevée', 'serviceHours': '24 h/24, 7 j/7'}, 'En service')
    vpn = creer(m, '/api/services', {**MARC, 'title': 'Accès distant (VPN)',
        'description': 'Connexion sécurisée au réseau interne depuis l’extérieur.',
        'criticality': 'Élevée', 'serviceHours': 'Jours ouvrés, 7 h – 20 h'}, 'En service')
    erp = creer(m, '/api/services', {**MARC, 'title': 'ERP Finances',
        'description': 'Comptabilité, achats et paie.',
        'criticality': 'Moyenne', 'serviceHours': 'Jours ouvrés, 8 h – 18 h'}, 'En service')
    creer(m, '/api/agreements', {**MARC, 'title': 'SLA Accès distant — Directions régionales',
        'description': 'Engagement de disponibilité et de résolution du VPN.',
        'serviceId': vpn['id'], 'customer': 'Directions régionales', 'availabilityTarget': 99.5,
        'resolutionHoursP1': 4, 'resolutionHoursP2': 8, 'resolutionHoursP3': 24, 'resolutionHoursP4': 72,
        'validFrom': jour(-90), 'validTo': jour(275), 'reviewDate': jour(-3)}, 'En vigueur')

    # --- Configuration (CMDB) ----------------------------------------------------
    def ci(titre, type_, desc):
        return creer(m, '/api/configuration-items', {**MARC, 'title': titre, 'description': desc,
            'ciType': type_, 'environment': 'Production', 'location': 'Salle serveurs — siège',
            **DETAILS_CI.get(titre, {})})
    passerelle = ci('VPN-GW-01', 'Réseau', 'Passerelle VPN principale.')
    parefeu = ci('FW-SIEGE-01', 'Réseau', 'Pare-feu périmétrique du siège.')
    mail = ci('SRV-MAIL-01', 'Serveur', 'Serveur de messagerie.')
    bdd = ci('BDD-ERP-01', 'Base de données', 'Base PostgreSQL de l’ERP.')
    for source, cible, type_ in [(passerelle, parefeu, 'Dépend de'), (mail, parefeu, 'Dépend de'),
                                 (bdd, parefeu, 'Se connecte à')]:
        appel('POST', f'/api/configuration-items/{source["id"]}/relations', m,
              {'targetReference': cible['reference'], 'type': type_})
    lier(m, 'ci', passerelle['id'], vpn['reference'])
    lier(m, 'ci', mail['id'], messagerie['reference'])
    lier(m, 'ci', bdd['id'], erp['reference'])

    # --- Incidents ---------------------------------------------------------------
    # Incident majeur : déclaré par un gestionnaire, responsable nominatif (INC-13).
    inc_vpn = creer(m, '/api/incidents', {**MARC, 'title': 'VPN inaccessible pour les directions régionales',
        'description': 'Depuis 8 h, aucune connexion VPN n’aboutit hors du siège.',
        'impact': 'Élevé', 'urgency': 'Élevée', 'category': 'Réseau',
        'affectedService': 'Accès distant (VPN)', 'isMajor': True, 'resolution': None}, 'En cours')
    lier(m, 'incident', inc_vpn['id'], passerelle['reference'])
    inc_vpn2 = creer(u, '/api/incidents', {**URSULA, 'title': 'Coupures VPN toutes les 20 minutes',
        'description': 'Les sessions VPN tombent régulièrement à Toamasina.',
        'impact': 'Moyen', 'urgency': 'Moyenne', 'category': 'Réseau',
        'affectedService': 'Accès distant (VPN)', 'isMajor': False,
        'resolution': 'Redémarrage de la passerelle : contournement en attendant l’analyse.',
        'resolutionCode': 'Contournement'}, 'Résolu')
    creer(u, '/api/incidents', {**URSULA, 'title': 'Messagerie lente le matin',
        'description': 'Ouverture des boîtes aux lettres très lente entre 8 h et 9 h.',
        'impact': 'Faible', 'urgency': 'Moyenne', 'category': 'Messagerie',
        'affectedService': 'Messagerie', 'isMajor': False, 'resolution': None})

    # --- Problèmes, analyse, actions ---------------------------------------------
    def probleme(titre, desc, impact, urgence, categorie, service):
        return appel('POST', '/api/problems', u, {'title': titre, 'description': desc, 'impact': impact,
            'urgency': urgence, 'category': categorie, 'affectedService': service,
            'status': None, 'knownErrorWorkaround': None, 'rootCause': None})

    p_vpn = probleme('Coupures récurrentes du VPN',
        'Plusieurs incidents de coupure VPN depuis la mise à jour du mois dernier.',
        'Élevé', 'Moyenne', 'Réseau', 'Accès distant (VPN)')
    # PRB-20 : un problème réactif passe en analyse une fois relié à un incident.
    lier(u, 'problem', p_vpn['id'], inc_vpn['reference'])
    lier(u, 'problem', p_vpn['id'], inc_vpn2['reference'])
    appel('POST', f'/api/problems/{p_vpn["id"]}/analyses', u, {'method': 'FIVE_WHYS',
        'dataJson': json.dumps({'problem': 'Les sessions VPN tombent toutes les 20 minutes',
            'whys': ['La passerelle renégocie les clés de session',
                     'La durée de vie des clés est passée de 8 h à 20 min',
                     'La mise à jour du firmware a réinitialisé la configuration',
                     'La configuration n’était pas sauvegardée avant la mise à jour',
                     'Aucune procédure de changement ne l’exigeait'],
            'rootCause': 'Mise à jour du firmware sans sauvegarde préalable de la configuration'},
            ensure_ascii=False),
        'conclusion': 'Mise à jour du firmware sans sauvegarde préalable de la configuration'})
    appel('PUT', f'/api/problems/{p_vpn["id"]}', m, {'title': p_vpn['title'],
        'description': p_vpn['description'], 'status': 'Erreur connue', 'impact': 'Élevé',
        'urgency': 'Moyenne', 'category': 'Réseau', 'affectedService': 'Accès distant (VPN)',
        'knownErrorWorkaround': 'Redémarrer la passerelle VPN-GW-01 ; les sessions tiennent ensuite plusieurs heures.',
        'rootCause': 'Mise à jour du firmware sans sauvegarde préalable de la configuration'})
    raci = [{'role': 'R', 'assigneeType': 'User', 'assigneeId': 'demo.user', 'assigneeDisplayName': 'Ursula Utilisatrice'},
            {'role': 'A', 'assigneeType': 'User', 'assigneeId': 'demo.manager', 'assigneeDisplayName': 'Marc Manager'},
            {'role': 'I', 'assigneeType': 'Group', 'assigneeId': 'GRP-SALOHA-USERS', 'assigneeDisplayName': 'GRP-SALOHA-USERS'}]
    appel('POST', f'/api/problems/{p_vpn["id"]}/actions', u, {'title': 'Restaurer la durée de vie des clés à 8 h',
        'description': 'Corriger la configuration de VPN-GW-01.', 'status': None, 'dueDate': jour(-2), 'raci': raci})
    appel('POST', f'/api/problems/{p_vpn["id"]}/actions', u, {'title': 'Sauvegarder la configuration avant toute mise à jour',
        'description': 'Ajouter l’étape au modèle de changement des équipements réseau.', 'status': None,
        'dueDate': jour(10), 'raci': raci})
    lier(u, 'problem', p_vpn['id'], passerelle['reference'])

    probleme('Échecs intermittents des sauvegardes nocturnes',
        'La sauvegarde de BDD-ERP-01 échoue une nuit sur trois.', 'Moyen', 'Moyenne',
        'Stockage', 'ERP Finances')

    # --- Changement lié au problème ----------------------------------------------
    chg = {**MARC, 'title': 'Corriger la configuration de la passerelle VPN',
        'description': 'Restaurer la durée de vie des clés et sauvegarder la configuration.',
        'changeType': 'Normal', 'risk': 'Moyen', 'plannedStart': jour(5), 'plannedEnd': jour(5),
        'implementationPlan': '1. Sauvegarder la configuration\n2. Corriger la durée de vie des clés\n3. Tester depuis une direction régionale',
        'backoutPlan': 'Restaurer la configuration sauvegardée.', 'outcome': None}
    changement = creer(m, '/api/changes', chg)
    # Un CI relié avant l'évaluation : l'analyse d'impact en part.
    lier(m, 'change', changement['id'], p_vpn['reference'])
    lier(m, 'change', changement['id'], passerelle['reference'])
    appel('PUT', f'/api/changes/{changement["id"]}', m, {**chg, 'status': 'Évalué'})
    appel('PUT', f'/api/changes/{changement["id"]}', a, {**chg, 'status': 'Autorisé'})
    appel('PUT', f'/api/changes/{changement["id"]}', m, {**chg, 'status': 'Planifié'})
    # Un changement standard naît d'un modèle du catalogue, préautorisé.
    modele = appel('POST', '/api/standard-changes', m, {'name': 'Boîte aux lettres partagée',
        'description': 'Création d’une boîte partagée pour un service.', 'implementationPlan': 'Procédure standard.',
        'backoutPlan': 'Supprimer la boîte.', 'risk': 'Faible'})
    creer(u, '/api/changes', {**URSULA, 'title': 'Ajout d’une boîte aux lettres partagée « Achats »',
        'description': 'Changement standard du catalogue.', 'changeType': 'Standard', 'risk': 'Faible',
        'plannedStart': jour(2), 'plannedEnd': jour(2), 'implementationPlan': 'Procédure standard.',
        'backoutPlan': 'Supprimer la boîte.', 'outcome': None, 'standardModelId': modele['id']})

    # --- Demandes ----------------------------------------------------------------
    creer(u, '/api/requests', {**URSULA, 'title': 'Accès VPN pour une nouvelle arrivante',
        'description': 'Prise de poste lundi à la direction régionale de Mahajanga.',
        'requestedItem': 'Compte VPN', 'requestedFor': 'demo.user',
        'requestedForDisplayName': 'Ursula Utilisatrice', 'dueDate': jour(4)})
    creer(m, '/api/requests', {**URSULA, 'title': 'Ordinateur portable pour le service Achats',
        'description': 'Remplacement d’un poste en panne.', 'requestedItem': 'Ordinateur portable',
        'requestedFor': 'demo.user', 'requestedForDisplayName': 'Ursula Utilisatrice',
        'dueDate': jour(7)}, 'Approuvée')

    # --- Connaissances -----------------------------------------------------------
    article = creer(m, '/api/knowledge', {**MARC, 'title': 'Rétablir une session VPN qui coupe',
        'description': 'Contournement de l’erreur connue sur VPN-GW-01.', 'articleType': 'Erreur connue',
        'content': 'Symptôme : la session VPN tombe toutes les 20 minutes.\n\nContournement : redémarrer la passerelle VPN-GW-01 depuis la console d’administration, puis demander aux utilisateurs de se reconnecter.\n\nCorrection définitive : changement planifié.',
        'keywords': 'vpn, coupure, passerelle', 'reviewDate': jour(60)}, 'Publié', par=a)
    lier(m, 'article', article['id'], p_vpn['reference'])

    # --- Amélioration continue ---------------------------------------------------
    creer(m, '/api/improvements', {**MARC, 'title': 'Réduire le délai de résolution des incidents P2',
        'description': 'Les incidents P2 dépassent souvent la cible de 8 h.', 'step': 2, 'priority': 'Élevée',
        'benefit': 'Respect du SLA Accès distant', 'baseline': '11 h en moyenne', 'target': '6 h',
        'outcome': None, 'dueDate': jour(45), 'value': 3, 'effort': 2}, 'Validée')

    complements(m, u, a, messagerie, erp, mail, bdd, parefeu)
    tri_assiste(m, a)
    print('✔ Données de démonstration chargées.')


def complements(m, u, a, messagerie, erp, mail, bdd, parefeu):
    """Second jeu : de quoi remplir registres, reporting et captures d'écran."""
    # --- Services et SLA ---------------------------------------------------------
    impression = creer(m, '/api/services', {**MARC, 'title': 'Impression',
        'description': 'Impression et numérisation sur les sites.',
        'criticality': 'Faible', 'serviceHours': 'Jours ouvrés, 8 h – 17 h'}, 'En service')
    creer(m, '/api/services', {**MARC, 'title': 'Téléphonie IP',
        'description': 'Postes téléphoniques et standard.', 'criticality': 'Moyenne',
        'serviceHours': '24 h/24, 7 j/7'}, 'En conception')
    sla = {**MARC, 'resolutionHoursP1': 4, 'resolutionHoursP2': 8, 'resolutionHoursP3': 24,
           'resolutionHoursP4': 72, 'validFrom': jour(-180), 'validTo': jour(185)}
    creer(m, '/api/agreements', {**sla, 'title': 'SLA Messagerie — toutes directions',
        'description': 'Disponibilité et résolution de la messagerie.', 'serviceId': messagerie['id'],
        'customer': 'Toutes directions', 'availabilityTarget': 99.5, 'reviewDate': jour(120)}, 'En vigueur')
    creer(m, '/api/agreements', {**sla, 'title': 'SLA ERP — Direction financière',
        'description': 'Engagement renforcé pendant les clôtures.', 'serviceId': erp['id'],
        'customer': 'Direction financière', 'availabilityTarget': 99.8, 'reviewDate': jour(60)}, 'En vigueur')
    creer(m, '/api/agreements', {**sla, 'title': 'SLA Impression — sites régionaux',
        'description': 'En cours de négociation.', 'serviceId': impression['id'],
        'customer': 'Sites régionaux', 'availabilityTarget': 98, 'reviewDate': None})

    # --- Configuration -----------------------------------------------------------
    def ci(titre, type_, lieu, desc, statut=None):
        corps = {**MARC, 'title': titre, 'description': desc, 'ciType': type_,
                 'environment': 'Production', 'location': lieu, **DETAILS_CI.get(titre, {})}
        if statut:
            corps['status'] = statut
        return appel('POST', '/api/configuration-items', m, corps)
    exchange = ci('APP-EXCHANGE', 'Application', 'Salle serveurs — siège', 'Messagerie Exchange.')
    san = ci('SAN-01', 'Stockage', 'Salle serveurs — siège', 'Baie de stockage partagée.')
    app_erp = ci('APP-ERP', 'Application', 'Salle serveurs — siège', 'Application ERP.')
    imp = ci('IMP-TOAMASINA-01', 'Autre', 'Agence de Toamasina', 'Imprimante multifonction.', 'Hors service')
    ci('SW-MAHAJANGA-01', 'Réseau', 'Agence de Mahajanga', 'Commutateur d’agence.')
    for source, cible, type_ in [(exchange, mail, 'Dépend de'), (mail, san, 'Dépend de'),
                                 (app_erp, bdd, 'Dépend de'), (bdd, san, 'Dépend de'),
                                 (imp, parefeu, 'Se connecte à')]:
        appel('POST', f'/api/configuration-items/{source["id"]}/relations', m,
              {'targetReference': cible['reference'], 'type': type_})
    lier(m, 'ci', exchange['id'], messagerie['reference'])
    lier(m, 'ci', app_erp['id'], erp['reference'])
    # CFG-47 : fin de support étendu de Windows Server 2016 (12 janvier 2027), pour l'inventaire.
    appel('POST', '/api/discovery/end-of-life', m, {'product': 'Windows Server 2016', 'endOfSupport': '2027-01-12',
          'notes': 'Fin du support étendu Microsoft.'})
    lier(m, 'ci', imp['id'], impression['reference'])

    # --- Incidents ---------------------------------------------------------------
    def incident(titre, desc, impact, urgence, cat, service, statut=None, resolution=None, par=None, **autres):
        return creer(u, '/api/incidents', {**URSULA, 'title': titre, 'description': desc,
            'impact': impact, 'urgency': urgence, 'category': cat, 'affectedService': service,
            'isMajor': False, 'resolution': resolution,
            'resolutionCode': 'Correctif appliqué' if resolution else None, **autres}, statut, par)
    incident('ERP inaccessible pendant la clôture mensuelle', 'Erreur de connexion à la base.',
             'Élevé', 'Élevée', 'Application', 'ERP Finances', 'Résolu',
             'Redémarrage du service de base de données ; surveillance renforcée.')
    incident('Imprimante de Toamasina hors ligne', 'Plus aucune impression depuis ce matin.',
             'Faible', 'Moyenne', 'Matériel', 'Impression', ['En cours', 'En attente'],
             onHoldReason='Attente fournisseur')
    incident('Boîte aux lettres pleine', 'Réception bloquée pour un utilisateur.',
             'Faible', 'Faible', 'Messagerie', 'Messagerie', ['Résolu', 'Clos'], 'Archivage et quota relevé.',
             par=m)  # INC-10 : la clôture revient au gestionnaire
    inc_erp = incident('Lenteurs ERP en fin de journée', 'Temps de réponse supérieurs à 10 s.',
                       'Moyen', 'Moyenne', 'Application', 'ERP Finances')

    # --- Problèmes ---------------------------------------------------------------
    def probleme(titre, desc, impact, urgence, cat, service):
        return appel('POST', '/api/problems', u, {'title': titre, 'description': desc,
            'impact': impact, 'urgency': urgence, 'category': cat, 'affectedService': service,
            'status': None, 'knownErrorWorkaround': None, 'rootCause': None})
    p_erp = probleme('Lenteurs récurrentes de l’ERP en fin de journée',
        'Temps de réponse dégradés chaque soir depuis trois semaines.', 'Moyen', 'Moyenne',
        'Application', 'ERP Finances')
    lier(u, 'problem', p_erp['id'], inc_erp['reference'])  # PRB-20
    appel('POST', f'/api/problems/{p_erp["id"]}/analyses', u, {'method': 'ISHIKAWA',
        'dataJson': json.dumps({'categories': {
            'Méthode': ['Traitements par lots lancés à 17 h'], 'Matériel': ['Baie SAN saturée en écriture'],
            'Main-d’œuvre': [], 'Milieu': ['Pic de saisie avant la clôture'],
            'Matière': ['Index manquant sur les écritures'], 'Mesure': ['Pas de supervision des temps de réponse']},
            'rootCause': ''}, ensure_ascii=False), 'conclusion': None})
    p_mail = probleme('Boîtes aux lettres saturées', 'Quotas atteints sans alerte préalable.',
        'Faible', 'Moyenne', 'Messagerie', 'Messagerie')
    appel('PUT', f'/api/problems/{p_mail["id"]}', m, {'title': p_mail['title'],
        'description': p_mail['description'], 'status': 'Clos', 'impact': 'Faible', 'urgency': 'Moyenne',
        'category': 'Messagerie', 'affectedService': 'Messagerie', 'knownErrorWorkaround': None,
        'rootCause': 'Aucune alerte de quota configurée sur le serveur de messagerie.',
        'closureCode': 'Non retenu'})

    # --- Changements -------------------------------------------------------------
    chg = {**MARC, 'title': 'Mise à jour du serveur de messagerie', 'description': 'Correctifs de sécurité mensuels.',
        'changeType': 'Normal', 'risk': 'Moyen', 'plannedStart': jour(-12), 'plannedEnd': jour(-12),
        'implementationPlan': 'Appliquer les correctifs, redémarrer, vérifier les flux.',
        'backoutPlan': 'Désinstaller les correctifs.', 'outcome': None}
    c = creer(m, '/api/changes', chg)
    lier(m, 'change', c['id'], mail['reference'])
    appel('PUT', f'/api/changes/{c["id"]}', m, {**chg, 'status': 'Évalué'})
    appel('PUT', f'/api/changes/{c["id"]}', a, {**chg, 'status': 'Autorisé'})
    appel('PUT', f'/api/changes/{c["id"]}', m, {**chg, 'status': 'Planifié'})
    # Début et fin réels exigés pour la mise en œuvre et la clôture.
    chg = {**chg, 'actualStart': jour(-12), 'actualEnd': jour(-12)}
    appel('PUT', f'/api/changes/{c["id"]}', m, {**chg, 'status': 'Mis en œuvre'})
    appel('PUT', f'/api/changes/{c["id"]}', m, {**chg, 'status': 'Clos', 'outcome': 'Réussi'})
    ajout = creer(u, '/api/changes', {**URSULA, 'title': 'Ajout d’un index sur les écritures comptables',
        'description': 'Correctif des lenteurs de l’ERP.', 'changeType': 'Normal', 'risk': 'Faible',
        'plannedStart': jour(7), 'plannedEnd': jour(7), 'implementationPlan': 'Créer l’index hors production, puis en production.',
        'backoutPlan': 'Supprimer l’index.', 'outcome': None})
    lier(u, 'change', ajout['id'], p_erp['reference'])
    lier(u, 'change', ajout['id'], bdd['reference'])
    appel('PUT', f'/api/changes/{ajout["id"]}', m, {**URSULA, 'title': ajout['title'], 'description': ajout['description'],
        'changeType': 'Normal', 'risk': 'Faible', 'plannedStart': jour(7), 'plannedEnd': jour(7),
        'implementationPlan': ajout['implementationPlan'], 'backoutPlan': ajout['backoutPlan'],
        'outcome': None, 'status': 'Évalué'})

    # --- Demandes, connaissances, amélioration -----------------------------------
    creer(u, '/api/requests', {**URSULA, 'title': 'Accès à l’ERP pour le contrôle de gestion',
        'description': 'Profil lecture seule.', 'requestedItem': 'Habilitation ERP',
        'requestedFor': 'demo.user', 'requestedForDisplayName': 'Ursula Utilisatrice', 'dueDate': jour(3)})
    for titre, type_, contenu in [
        ('Configurer la messagerie sur un téléphone', 'Procédure', 'Paramètres du compte, serveur et authentification.'),
        ('Que faire si l’ERP est lent ?', 'FAQ', 'Vérifier l’heure : les traitements par lots tournent à 17 h.'),
        ('Libérer de l’espace dans sa boîte aux lettres', 'Solution', 'Archiver les messages de plus d’un an.')]:
        creer(m, '/api/knowledge', {**MARC, 'title': titre, 'description': '', 'articleType': type_,
            'content': contenu, 'keywords': '', 'reviewDate': jour(200)}, 'Publié', par=a)
    for titre, etape, priorite, statut in [
        ('Superviser les temps de réponse des applications', 4, 'Élevée', 'Validée'),
        ('Former les agences à la résolution de premier niveau', 1, 'Moyenne', None)]:
        creer(m, '/api/improvements', {**MARC, 'title': titre, 'description': '', 'step': etape,
            'priority': priorite, 'benefit': 'Moins d’incidents escaladés', 'baseline': 'À mesurer',
            'target': 'À définir', 'outcome': None, 'dueDate': jour(60), 'value': 3, 'effort': 2}, statut)



def activer_tri(admin):
    """Active le tri assisté (OPT-03) dès que le conteneur « features » répond : il peut démarrer après l'API."""
    for _ in range(36):
        req = urllib.request.Request(BASE + '/api/admin/features/assisted-ticket-triage', method='PUT',
                                     data=json.dumps({'enabled': True}).encode())
        req.add_header('Content-Type', 'application/json')
        req.add_header('Authorization', f'Bearer {admin}')
        try:
            with urllib.request.urlopen(req, timeout=30):
                return True
        except urllib.error.HTTPError as e:
            if e.code != 400:
                return False
        except (urllib.error.URLError, OSError):
            pass
        time.sleep(5)
    return False


def attendre_index(admin, minimum):
    """Les voisins du vote sont les incidents résolus de l'index de recherche (RAG-03), tenu en tâche de fond :
    on attend qu'il les contienne, vecteurs compris quand le service d'embeddings répond, 3 minutes au plus."""
    for _ in range(36):
        etat = appel('GET', '/api/search/status', admin)
        incidents = next((s for s in etat['sources'] if s['type'] == 'incident'), None)
        if incidents and incidents['records'] >= minimum and (not etat['embeddings']['reachable']
                                                             or incidents['withVector'] >= incidents['passages']):
            return
        time.sleep(5)


def tri_assiste(m, a):
    """Mode démonstration du tri assisté (ADR-0023) : ses voisins (incidents résolus du jeu d'essai de TRI-12), le
    tri activé, puis une panne probable de quatre incidents ouverts. Sans conteneur « features » joignable, le tri
    reste coupé et seule la première partie est chargée."""
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tri-assiste.json'), encoding='utf-8') as f:
        jeu = json.load(f)
    services = {s['title']: s['id'] for s in appel('GET', '/api/services', m)}
    def corps(titre, desc, theme, groupe=True):
        t = jeu['themes'][theme] if theme else {}
        return {'title': titre, 'description': desc, 'impact': 'Moyen', 'urgency': 'Moyenne', 'isMajor': False,
                'category': t.get('categorie', ''), 'serviceId': services.get(t.get('service')),
                **({'ownerType': 'Group', 'ownerId': t['groupe'], 'ownerDisplayName': t['groupe']} if groupe and t else {})}
    for r in jeu['resolus']:
        creer(m, '/api/incidents', {**corps(r['titre'], r['description'], r['theme']), 'resolution': r['resolution'],
                                    'resolutionCode': 'Correctif appliqué'}, 'Résolu')
    if not activer_tri(a):
        print('Tri assisté non activé : conteneur « features » injoignable (profil « fonctionnalites »).')
        return
    attendre_index(a, len(jeu['resolus']))
    # Après l'activation seulement : un événement n'est inscrit au fil que si une fonctionnalité le lit (OPT-12).
    # La panne porte son service ; les incidents isolés n'en ont pas, pour ne rien relier par les règles.
    for o in jeu['ouverts']:
        c = corps(o['titre'], o['description'], o['theme'] if o['panne'] else None, groupe=False)
        if o['panne'] is None:
            c['category'] = ''
        creer(m, '/api/incidents', c)
    print('Tri assisté activé : voisins chargés, panne probable en formation.')


if __name__ == '__main__':
    main()
