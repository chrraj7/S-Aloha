#!/usr/bin/env python3
"""Recette de la pile de démonstration, une fois les données d'exemple chargées.

    python3 scripts/recette.py http://localhost [--fonctionnalites] [--mcp]

Passe par l'interface (nginx, port 80) comme un navigateur : page d'accueil servie,
connexion de chaque rôle de démonstration, jeu de données complet lu par l'API, santé
de la plateforme vue par l'Admin. Avec --fonctionnalites (pile lancée avec le profil
« fonctionnalites ») : le conteneur « features » doit être joignable par l'API, jeton
reconnu. Avec --mcp (profil « mcp ») : /mcp répond 404 tant que l'Admin n'a pas activé le
serveur MCP ; activé, un jeton personnel liste par nginx les six outils de lecture et en
appelle un ; coupé de nouveau, 404. Code 1 au premier écart. Lancé par le job Recette de
la CI (docs/reference/architecture.md § Intégration continue).
"""
import json
import sys
import urllib.error
import urllib.request

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://localhost').rstrip('/')
FONCTIONNALITES = '--fonctionnalites' in sys.argv[2:]
MCP = '--mcp' in sys.argv[2:]
COMPTES = {
    'admin': ('demo.admin', 'Demo-Admin-2026'),
    'manager': ('demo.manager', 'Demo-Manager-2026'),
    'user': ('demo.user', 'Demo-User-2026'),
}
# Au moins ce que charge demo/donnees/charger.py (docs/reference/exploitation.md § Mode démonstration).
ATTENDU = {
    '/api/services': 5, '/api/agreements': 4, '/api/configuration-items': 9, '/api/incidents': 7,
    '/api/problems': 4, '/api/changes': 4, '/api/requests': 3, '/api/knowledge': 4,
    '/api/improvements': 3,
}
# Composants qui doivent être « OK » sur une pile neuve ; les autres peuvent être dégradés
# (pas de service d'embeddings dans la recette, tâches de fond pas encore passées).
ESSENTIELS = ('Base de données', 'Annuaire')
CONTENEUR = 'Conteneur « features »'
CONTENEUR_MCP = 'Conteneur « mcp »'
OUTILS_MCP = {'search', 'get_record', 'similar_cases', 'my_work', 'ci_impact', 'triage_suggestions'}
ecarts = []


def appel(chemin, jeton=None, corps=None, methode=None, entetes=None):
    req = urllib.request.Request(BASE + chemin, method=methode or ('POST' if corps is not None else 'GET'),
                                 data=json.dumps(corps).encode() if corps is not None else None)
    req.add_header('Content-Type', 'application/json')
    if jeton:
        req.add_header('Authorization', f'Bearer {jeton}')
    for nom, valeur in (entetes or {}).items():
        req.add_header(nom, valeur)
    with urllib.request.urlopen(req, timeout=30) as r:
        texte = r.read().decode()
        return texte if 'json' not in r.headers.get('Content-Type', '') else json.loads(texte)


def mcp(jeton, methode, params=None):
    """Une requête du protocole MCP sur /mcp, par nginx ; la réponse (JSON ou flux SSE) décodée."""
    corps = {'jsonrpc': '2.0', 'id': 1, 'method': methode, 'params': params or {}}
    texte = appel('/mcp', jeton, corps, entetes={'Accept': 'application/json, text/event-stream'})
    if isinstance(texte, dict):
        return texte
    donnees = [ligne[len('data:'):].strip() for ligne in texte.splitlines() if ligne.startswith('data:')]
    return json.loads(donnees[-1])


def code_mcp(jeton=None):
    try:
        mcp(jeton, 'tools/list')
        return 200
    except urllib.error.HTTPError as e:
        return e.code


def recette_mcp(jetons):
    """MCP-01, MCP-03, MCP-05 : coupé, 404 ; activé, les six outils par un jeton personnel ; recoupé, 404."""
    controle('/mcp absent tant que le serveur MCP est coupé', code_mcp() == 404)
    try:
        appel('/api/admin/features/mcp-server', jetons['admin'], {'enabled': True}, methode='PUT')
        controle('Serveur MCP activé par l\'Admin', True)
    except urllib.error.HTTPError as e:
        controle('Serveur MCP activé par l\'Admin', False, f'HTTP {e.code} : {e.read().decode()[:200]}')
        return
    secret = appel('/api/me/tokens', jetons['user'], {'name': 'Recette', 'days': 1})['secret']
    controle('/mcp sans jeton : 401', code_mcp() == 401)
    outils = {o['name'] for o in mcp(secret, 'tools/list')['result']['tools']}
    controle(f'/mcp : {len(outils)} outil(s) de lecture', outils == OUTILS_MCP, ', '.join(sorted(outils)))
    resultat = mcp(secret, 'tools/call', {'name': 'search', 'arguments': {'query': 'messagerie'}})['result']
    controle('Outil search appelé avec le jeton personnel', not resultat.get('isError'),
             resultat['content'][0]['text'][:200] if resultat.get('isError') else '')
    sante = appel('/api/admin/health', jetons['admin'])
    etat = next((c for c in sante['components'] if c['name'] == CONTENEUR_MCP), None)
    controle(f'Santé : {CONTENEUR_MCP} OK', etat is not None and etat['state'] == 'OK', etat['message'] if etat else 'absent')
    appel('/api/admin/features/mcp-server', jetons['admin'], {'enabled': False}, methode='PUT')
    controle('/mcp absent de nouveau une fois le serveur MCP coupé', code_mcp(secret) == 404)


def controle(libelle, ok, detail=''):
    print(('✔ ' if ok else '✖ ') + libelle + (f' — {detail}' if detail else ''))
    if not ok:
        ecarts.append(libelle)


def main():
    accueil = appel('/')
    controle('Interface servie', '<div id="root"' in accueil)
    # OPT-11 : les routes internes n'existent que sur le port interne de l'API, jamais par nginx.
    try:
        appel('/internal/events')
        controle('Routes internes absentes de l\'interface', False, '/internal/events répond 200')
    except urllib.error.HTTPError as e:
        controle('Routes internes absentes de l\'interface', e.code == 404, f'/internal/events : HTTP {e.code}')

    jetons = {}
    for role, (nom, mdp) in COMPTES.items():
        try:
            jetons[role] = appel('/api/auth/login', corps={'username': nom, 'password': mdp, 'returnToken': True})['token']
            controle(f'Connexion {nom}', True)
        except urllib.error.HTTPError as e:
            controle(f'Connexion {nom}', False, f'HTTP {e.code}')
    if len(jetons) < len(COMPTES):
        sys.exit(1)

    for chemin, minimum in ATTENDU.items():
        liste = appel(chemin, jetons['manager'])
        n = len(liste if isinstance(liste, list) else liste.get('items', []))
        controle(f'{chemin} : {n} enregistrement(s)', n >= minimum, f'au moins {minimum} attendu(s)')

    for role in ('user', 'admin'):
        try:
            appel('/api/incidents', jetons[role])
            controle(f'Lecture des incidents par {role}', True)
        except urllib.error.HTTPError as e:
            controle(f'Lecture des incidents par {role}', False, f'HTTP {e.code}')

    sante = appel('/api/admin/health', jetons['admin'])
    if FONCTIONNALITES:
        present = any(c['name'] == CONTENEUR for c in sante['components'])
        controle(f'Santé : {CONTENEUR} contrôlé par l\'API', present, '' if present else 'FEATURES_URL fourni à l\'API ?')
    for c in sante['components']:
        essentiel = c['name'] in ESSENTIELS or (FONCTIONNALITES and c['name'] == CONTENEUR)
        controle(f"Santé : {c['name']} {c['state']}", c['state'] == 'OK' or (not essentiel and c['state'] != 'Hors service'),
                 c['message'])

    if MCP:
        recette_mcp(jetons)

    if ecarts:
        sys.exit(f'Recette : {len(ecarts)} écart(s).')
    print('Recette : aucun écart.')


if __name__ == '__main__':
    main()
