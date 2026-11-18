#!/usr/bin/env python3
"""Dépendances vulnérables de l'interface : `npm audit`, avec des exceptions datées.

    python3 scripts/check-audit-npm.py                      # lance npm audit dans frontend/
    python3 scripts/check-audit-npm.py --rapports COMPLET LIVRE [--date AAAA-MM-JJ]

Deux audits : `npm audit` (toutes les dépendances) et `npm audit --omit=dev` (celles du
paquet livré). Sortie non nulle si un avis de gravité haute ou critique :

- touche une dépendance livrée : aucune exception ne s'y applique ;
- touche l'outillage de build et de test sans figurer dans EXCEPTIONS, ou avec une
  exception échue.

Une exception porte sur un seul avis (identifiant GHSA), jusqu'à une date au plus : elle
attend une version corrigée, elle ne l'ignore pas. Ce fichier est de l'outillage de
vérification : l'ajouter, la prolonger ou la retirer demande l'étiquette `outillage`.
Une exception sans objet (avis plus relevé) est signalée, sans échec : la retirer.

`--rapports` lit deux sorties `npm audit --json` déjà produites (essais du script).
"""
import datetime
import json
import pathlib
import subprocess
import sys

FRONTEND = pathlib.Path(__file__).resolve().parents[1] / 'frontend'
GRAVES = ('high', 'critical')

# Identifiant GHSA : (dernier jour de validité, raison).
EXCEPTIONS = {
    'GHSA-fc5q-39xc-vjpf': (
        '2026-12-18',
        'picomatch <= 4.0.7, ReDoS par extglob répété ; aucune version corrigée publiée '
        'au 2026-11-18 (4.0.7 est la dernière). Tiré par vite et vitest, absent du paquet '
        'livré ; les motifs glob sont ceux du projet, jamais saisis par un utilisateur.',
    ),
}


def audit(*options):
    p = subprocess.run(['npm', 'audit', '--json', *options], cwd=FRONTEND,
                       capture_output=True, text=True)
    return p.stdout


def avis(texte, origine):
    """{GHSA: description} des avis hauts ou critiques d'une sortie `npm audit --json`."""
    try:
        rapport = json.loads(texte)
    except json.JSONDecodeError:
        sys.exit(f'::error::npm audit ({origine}) : sortie illisible.')
    if 'error' in rapport or 'vulnerabilities' not in rapport:
        erreur = rapport.get('error', {}).get('summary', 'rapport sans « vulnerabilities »')
        sys.exit(f'::error::npm audit ({origine}) a échoué : {erreur}')
    trouves = {}
    for paquet in rapport['vulnerabilities'].values():
        for v in paquet['via']:
            # Une chaîne nomme le paquet intermédiaire ; l'avis lui-même est un objet.
            if isinstance(v, dict) and v.get('severity') in GRAVES:
                ghsa = v['url'].rstrip('/').rsplit('/', 1)[-1]
                trouves[ghsa] = f"{v['name']} {v['range']}, {v['severity']} : {v['title']}"
    return trouves


def main():
    args = sys.argv[1:]
    aujourdhui = datetime.date.today()
    if '--date' in args:
        aujourdhui = datetime.date.fromisoformat(args[args.index('--date') + 1])
    if '--rapports' in args:
        i = args.index('--rapports')
        complet, livre = (pathlib.Path(f).read_text(encoding='utf-8') for f in args[i + 1:i + 3])
    else:
        complet, livre = audit(), audit('--omit=dev')
    tous, livres = avis(complet, 'complet'), avis(livre, '--omit=dev')

    problemes = 0
    for ghsa, d in sorted(livres.items()):
        print(f'::error::{ghsa} ({d}) touche une dépendance livrée : aucune exception possible.')
        problemes += 1
    for ghsa, d in sorted(tous.items()):
        if ghsa in livres:
            continue
        if ghsa not in EXCEPTIONS:
            print(f'::error::{ghsa} ({d}) : outillage de build ou de test vulnérable, sans exception.')
            problemes += 1
            continue
        fin, raison = EXCEPTIONS[ghsa]
        if aujourdhui > datetime.date.fromisoformat(fin):
            print(f'::error::{ghsa} ({d}) : exception échue le {fin}, à revoir (version corrigée ?).')
            problemes += 1
        else:
            print(f'::notice::{ghsa} excepté jusqu\'au {fin} : {raison}')
    for ghsa in sorted(set(EXCEPTIONS) - set(tous)):
        print(f'::warning::Exception {ghsa} sans objet (avis plus relevé) : la retirer de scripts/check-audit-npm.py.')

    print(f'{len(tous)} avis haut ou critique, dont {len(livres)} sur le paquet livré ; '
          f'{problemes} problème(s).')
    return 1 if problemes else 0


if __name__ == '__main__':
    sys.exit(main())
