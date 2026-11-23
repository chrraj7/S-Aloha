#!/usr/bin/env python3
"""Preuve d'une pull request : ses tests échouent-ils sans son changement ?

Pour chaque côté dont la PR modifie le code (API : `backend/**/*.cs`, tests dans
`tests/SAloha.Api.Tests` ; conteneur des fonctionnalités optionnelles : `features/**/*.cs`,
tests dans `tests/SAloha.Features.Tests` ; interface : `frontend/src/**/*.js(x)` hors tests),
les tests ajoutés ou modifiés par la PR sont rejoués sur le code de la base (copie de travail
jetable, tests de la PR posés dessus). Au moins un doit échouer (ou ne pas compiler : il
dépend du code ajouté) ; qu'ils passent à la tête de la PR, les jobs « API » et
« Interface » le prouvent déjà.

  - aucun code modifié               → rien à prouver ;
  - code modifié sans test modifié   → échec ;
  - tests qui passent déjà sur la base → échec : ils ne prouvent pas le changement.

Un changement sans effet observable (refactorisation, renommage) passe par l'étiquette
`preuve-non-requise`, posée par le mainteneur, puis en relançant le job.

    python3 scripts/preuve.py BASE HEAD [--etiquettes a,b]

Sortie non nulle si la preuve manque.
"""
import argparse
import fnmatch
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

REPO = pathlib.Path(__file__).resolve().parents[1]
# Projets de tests .NET, par côté : l'API et le conteneur des fonctionnalités optionnelles (ADR-0023).
PROJETS = {'api': 'tests/SAloha.Api.Tests', 'features': 'tests/SAloha.Features.Tests'}
COTES = ('api', 'features', 'web')
NOMS = {'api': 'API', 'features': 'Conteneur features', 'web': 'Interface'}
DE = {'api': 'de l API', 'features': 'du conteneur features', 'web': 'de l interface'}


def git(*args, cwd=REPO):
    return subprocess.run(['git', *args], cwd=cwd, capture_output=True, text=True, check=True).stdout


def lancer(cmd, cwd):
    print(f'$ {" ".join(cmd)}', flush=True)
    return subprocess.run(cmd, cwd=cwd, env={**os.environ, 'TESTCONTAINERS_RYUK_DISABLED': 'true'}).returncode


def est_test_front(p):
    return fnmatch.fnmatchcase(p, 'frontend/src/*.test.js') or fnmatch.fnmatchcase(p, 'frontend/src/*.test.jsx')


def cote(p):
    """('code', côté) pour un fichier de code, ('test', côté) pour un test, (None, None) sinon."""
    if p.startswith(PROJETS['features'] + '/') and p.endswith('.cs'):
        return 'test', 'features'
    if p.startswith('tests/') and p.endswith('.cs'):
        return 'test', 'api'
    if est_test_front(p):
        return 'test', 'web'
    if p.startswith('backend/') and p.endswith('.cs'):
        return 'code', 'api'
    if p.startswith('features/') and p.endswith('.cs'):
        return 'code', 'features'
    if p.startswith('frontend/src/') and p.endswith(('.js', '.jsx')):
        return 'code', 'web'
    return None, None


def filtre_dotnet(head, fichiers):
    """Filtre `dotnet test` : les classes des fichiers *Tests.cs touchés."""
    classes = []
    for p in fichiers:
        texte = git('show', f'{head}:{p}')
        ns = re.search(r'^\s*namespace\s+([\w.]+)', texte, re.M)
        for c in re.findall(r'^\s*(?:public\s+|internal\s+|sealed\s+|partial\s+)*class\s+(\w+)', texte, re.M):
            classes.append(f'{ns.group(1)}.{c}.' if ns else f'.{c}.')
    return '|'.join(f'FullyQualifiedName~{c}' for c in classes)


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument('base')
    ap.add_argument('head')
    ap.add_argument('--etiquettes', default='')
    a = ap.parse_args(argv)

    if 'preuve-non-requise' in {e.strip() for e in a.etiquettes.split(',')}:
        print('Étiquette `preuve-non-requise` : preuve non exigée (décision du mainteneur).')
        return 0

    base = git('merge-base', a.base, a.head).strip()
    code, tests = {c: [] for c in COTES}, {c: [] for c in COTES}
    for ligne in git('diff', '--name-status', '--no-renames', f'{base}...{a.head}').splitlines():
        statut, p = ligne.split('\t', 1)
        genre, c = cote(p)
        if genre == 'code':
            code[c].append(p)
        elif genre == 'test' and statut != 'D':
            tests[c].append(p)

    a_prouver = [c for c in COTES if code[c]]
    if not a_prouver:
        print('Aucun code de l\'API, du conteneur features ni de l\'interface modifié : rien à prouver.')
        return 0

    manque = [c for c in a_prouver if not tests[c]]
    for c in manque:
        print(f'::error::Code {DE[c]} modifié ({", ".join(code[c][:3])}…) sans test ajouté ni modifié.')
    if manque:
        return 1

    copie = pathlib.Path(tempfile.mkdtemp(prefix='preuve-')) / 'base'
    git('worktree', 'add', '--detach', str(copie), base)
    echecs = []
    try:
        for c in a_prouver:
            for p in tests[c]:
                cible = copie / p
                cible.parent.mkdir(parents=True, exist_ok=True)
                cible.write_text(git('show', f'{a.head}:{p}'), encoding='utf-8')
            if c in PROJETS:
                classes = [p for p in tests[c] if p.endswith('Tests.cs')]
                if not classes:
                    print(f'::error::{NOMS[c]} : aucun fichier *Tests.cs touché, seulement des aides de test.')
                    echecs.append(c)
                    continue
                # Projet de tests absent de la base (il arrive avec la PR) : dotnet test échoue, ce qui prouve.
                code_retour = lancer(['dotnet', 'test', PROJETS[c], '--filter', filtre_dotnet(a.head, classes)], copie)
            else:
                if lancer(['npm', 'ci', '--no-audit', '--no-fund'], copie / 'frontend'):
                    print('::error::Interface : npm ci a échoué sur la base.')
                    echecs.append(c)
                    continue
                fichiers = [p.removeprefix('frontend/') for p in tests['web']]
                code_retour = lancer(['npx', 'vitest', 'run', *fichiers], copie / 'frontend')
            if code_retour == 0:
                print(f'::error::{NOMS[c]} : les tests de la PR passent '
                      f'déjà sans son changement ({", ".join(tests[c])}) ; ils ne le prouvent pas.')
                echecs.append(c)
            else:
                print(f'✔ {NOMS[c]} : les tests de la PR échouent sans '
                      'son changement (échec ou compilation impossible sur la base).')
    finally:
        git('worktree', 'remove', '--force', str(copie))
        shutil.rmtree(copie.parent, ignore_errors=True)

    return 1 if echecs else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
