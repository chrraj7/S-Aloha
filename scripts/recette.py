#!/usr/bin/env python3
"""Recette de la pile de démonstration, une fois les données d'exemple chargées.

    python3 scripts/recette.py http://localhost

Passe par l'interface (nginx, port 80) comme un navigateur : page d'accueil servie,
connexion de chaque rôle de démonstration, jeu de données complet lu par l'API, santé
de la plateforme vue par l'Admin. Code 1 au premier écart. Lancé par le job Recette de
la CI (docs/reference/architecture.md § Intégration continue).
"""
import json
import sys
import urllib.error
import urllib.request

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://localhost').rstrip('/')
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
ecarts = []


def appel(chemin, jeton=None, corps=None):
    req = urllib.request.Request(BASE + chemin, method='POST' if corps is not None else 'GET',
                                 data=json.dumps(corps).encode() if corps is not None else None)
    req.add_header('Content-Type', 'application/json')
    if jeton:
        req.add_header('Authorization', f'Bearer {jeton}')
    with urllib.request.urlopen(req, timeout=30) as r:
        texte = r.read().decode()
        return texte if 'json' not in r.headers.get('Content-Type', '') else json.loads(texte)


def controle(libelle, ok, detail=''):
    print(('✔ ' if ok else '✖ ') + libelle + (f' — {detail}' if detail else ''))
    if not ok:
        ecarts.append(libelle)


def main():
    accueil = appel('/')
    controle('Interface servie', '<div id="root"' in accueil)

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
    for c in sante['components']:
        essentiel = c['name'] in ESSENTIELS
        controle(f"Santé : {c['name']} {c['state']}", c['state'] == 'OK' or (not essentiel and c['state'] != 'Hors service'),
                 c['message'])

    if ecarts:
        sys.exit(f'Recette : {len(ecarts)} écart(s).')
    print('Recette : aucun écart.')


if __name__ == '__main__':
    main()
