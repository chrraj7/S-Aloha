#!/usr/bin/env python3
"""Calibrage du tri assisté des tickets (TRI-12), sur une pile de démonstration chargée.

    python3 demo/donnees/calibrer-tri.py http://localhost:8080 [--embeddings http://localhost:11434]

Le jeu d'essai (tri-assiste.json) est fictif. Deux mesures :

- **vote** (TRI-03) : les 30 essais, formulés autrement que les incidents résolus chargés,
  passent par la saisie (POST /api/triage/suggest) pour 5, 10 et 15 voisins. Pour chaque
  champ, la précision (suggestions justes parmi les suggestions) et la couverture (essais qui
  reçoivent une suggestion) ;
- **corrélation par le sens** (TRI-06) : les similarités cosinus bge-m3 entre incidents
  ouverts d'une même panne, et entre incidents sans rapport (ouverts isolés, résolus de thèmes
  différents), pour situer le seuil.

Le script remet les réglages du tri à leurs valeurs d'avant. Sortie en Markdown.
"""
import argparse
import itertools
import json
import math
import os
import time
import urllib.error
import urllib.request

ICI = os.path.dirname(os.path.abspath(__file__))


def appel(base, methode, chemin, jeton=None, corps=None):
    req = urllib.request.Request(base + chemin, method=methode, data=json.dumps(corps).encode() if corps is not None else None)
    req.add_header('Content-Type', 'application/json')
    if jeton:
        req.add_header('Authorization', f'Bearer {jeton}')
    with urllib.request.urlopen(req, timeout=60) as r:
        texte = r.read().decode()
        return json.loads(texte) if texte else None


def connexion(base, nom, mdp):
    return appel(base, 'POST', '/api/auth/login', corps={'username': nom, 'password': mdp, 'returnToken': True})['token']


def vecteurs(url, textes):
    req = urllib.request.Request(url.rstrip('/') + '/api/embed', method='POST',
                                 data=json.dumps({'model': 'bge-m3', 'input': textes}).encode())
    req.add_header('Content-Type', 'application/json')
    with urllib.request.urlopen(req, timeout=300) as r:
        vs = json.loads(r.read().decode())['embeddings']
    return [[x / (math.sqrt(sum(y * y for y in v)) or 1) for x in v] for v in vs]


def cosinus(a, b):
    return sum(x * y for x, y in zip(a, b))


def mesure_vote(base, admin, agent, jeu, voisins):
    reglages = appel(base, 'GET', '/api/admin/features/assisted-ticket-triage/settings', admin)
    appel(base, 'PUT', '/api/admin/features/assisted-ticket-triage/settings', admin, {**reglages, 'neighbours': voisins})
    time.sleep(65)   # le conteneur relit ses réglages une fois par minute au plus
    champs = {'category': 'categorie', 'serviceId': 'service', 'owner': 'groupe'}
    compte = {c: {'suggérées': 0, 'justes': 0} for c in champs}
    for essai in jeu['essais']:
        attendu = jeu['themes'][essai['theme']]
        reponse = appel(base, 'POST', '/api/triage/suggest', agent, {'title': essai['titre'], 'description': essai['description']})
        for s in reponse['suggestions']:
            if s['field'] in champs:
                compte[s['field']]['suggérées'] += 1
                valeur = s['label'] if s['field'] == 'serviceId' else s['value']
                compte[s['field']]['justes'] += valeur == attendu[champs[s['field']]]
    return reglages, compte


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('base', nargs='?', default='http://localhost:8080')
    ap.add_argument('--embeddings', default='http://localhost:11434')
    a = ap.parse_args()
    base = a.base.rstrip('/')
    with open(os.path.join(ICI, 'tri-assiste.json'), encoding='utf-8') as f:
        jeu = json.load(f)
    admin = connexion(base, 'demo.admin', 'Demo-Admin-2026')
    agent = connexion(base, 'demo.manager', 'Demo-Manager-2026')

    print('## Vote des voisins (TRI-03)\n')
    print(f'{len(jeu["essais"])} essais, {len(jeu["resolus"])} incidents résolus chargés.\n')
    print('| Voisins | Champ | Suggérées | Justes | Précision | Couverture |')
    print('|---|---|---|---|---|---|')
    initiaux = None
    try:
        for n in (5, 10, 15):
            reglages, compte = mesure_vote(base, admin, agent, jeu, n)
            initiaux = initiaux or reglages
            for champ, c in compte.items():
                precision = f'{100 * c["justes"] / c["suggérées"]:.0f} %' if c['suggérées'] else '—'
                print(f'| {n} | {champ} | {c["suggérées"]} | {c["justes"]} | {precision} | {100 * c["suggérées"] / len(jeu["essais"]):.0f} % |')
    finally:
        if initiaux:
            appel(base, 'PUT', '/api/admin/features/assisted-ticket-triage/settings', admin, initiaux)

    print('\n## Corrélation par le sens (TRI-06)\n')
    ouverts = jeu['ouverts']
    textes = [f'{o["titre"]}\n{o["description"]}' for o in ouverts]
    vs = vecteurs(a.embeddings, textes)
    memes, autres = [], []
    for (i, x), (j, y) in itertools.combinations(enumerate(ouverts), 2):
        (memes if x['panne'] and x['panne'] == y['panne'] else autres).append(cosinus(vs[i], vs[j]))
    resolus = jeu['resolus']
    vr = vecteurs(a.embeddings, [f'{r["titre"]}\n{r["description"]}' for r in resolus])
    sans_rapport = [cosinus(vr[i], vr[j]) for i, j in itertools.combinations(range(len(resolus)), 2)
                    if resolus[i]['theme'] != resolus[j]['theme']]
    print(f'- Même panne : {len(memes)} paires, de {min(memes):.2f} à {max(memes):.2f} (médiane {sorted(memes)[len(memes) // 2]:.2f}).')
    print(f'- Ouverts sans rapport : {len(autres)} paires, de {min(autres):.2f} à {max(autres):.2f}.')
    print(f'- Résolus de thèmes différents : {len(sans_rapport)} paires, de {min(sans_rapport):.2f} à {max(sans_rapport):.2f}.\n')
    print('| Seuil | Paires de la panne retrouvées | Paires sans rapport liées à tort (ouverts, résolus) |')
    print('|---|---|---|')
    for k in range(40, 96, 5):
        seuil = k / 100
        vrais = sum(c >= seuil for c in memes)
        faux = sum(c >= seuil for c in autres), sum(c >= seuil for c in sans_rapport)
        print(f'| {seuil:.2f} | {vrais} / {len(memes)} | {faux[0]} / {len(autres)}, {faux[1]} / {len(sans_rapport)} |')


if __name__ == '__main__':
    main()
