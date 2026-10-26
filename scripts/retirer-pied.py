#!/usr/bin/env python3
"""Retire un pied de page automatique d'un texte publié sur GitHub.

Lancé par .github/workflows/commentaires.yml. Le motif vient de la variable PIED
(secret PIED_A_RETIRER) : toute ligne qui le contient est retirée, ainsi que le
séparateur « --- » qui la précède. Un commentaire d'issue ou de PR est supprimé puis
republié propre, pour que l'ancien texte ne reste pas dans son historique ; une
description de PR, une revue ou un commentaire de ligne est modifié.

Sans motif, ou si le texte ne le contient pas, rien n'est fait.
"""
import json
import os
import sys
import urllib.error
import urllib.request

API = os.environ.get('GITHUB_API_URL', 'https://api.github.com')
DEPOT = os.environ.get('GITHUB_REPOSITORY', '')


def nettoyer(texte, motif):
    lignes = [l for l in texte.split('\n') if motif not in l]
    while lignes and not lignes[-1].strip():
        lignes.pop()
    if lignes and lignes[-1].strip() == '---':
        lignes.pop()
        while lignes and not lignes[-1].strip():
            lignes.pop()
    return '\n'.join(lignes)


def appel(methode, chemin, corps=None):
    req = urllib.request.Request(API + chemin, method=methode,
                                 data=json.dumps(corps).encode() if corps is not None else None)
    req.add_header('Authorization', f'Bearer {os.environ["GH_TOKEN"]}')
    req.add_header('Accept', 'application/vnd.github+json')
    with urllib.request.urlopen(req, timeout=30) as r:
        return r.status


def main():
    motif = os.environ.get('PIED', '').strip()
    if not motif:
        print('Secret PIED_A_RETIRER absent : rien à faire.')
        return
    evenement = os.environ['GITHUB_EVENT_NAME']
    donnees = json.load(open(os.environ['GITHUB_EVENT_PATH']))
    if evenement == 'issue_comment':
        objet = donnees['comment']
    elif evenement == 'pull_request_review':
        objet = donnees['review']
    elif evenement == 'pull_request_review_comment':
        objet = donnees['comment']
    else:
        objet = donnees['pull_request']
    texte = objet.get('body') or ''
    if motif not in texte:
        print('Aucun pied de page.')
        return
    propre = nettoyer(texte, motif)
    if evenement == 'issue_comment':
        numero = donnees['issue']['number']
        appel('POST', f'/repos/{DEPOT}/issues/{numero}/comments', {'body': propre})
        appel('DELETE', f'/repos/{DEPOT}/issues/comments/{objet["id"]}')
        print(f'Commentaire de #{numero} republié sans pied de page.')
    elif evenement == 'pull_request_review':
        numero = donnees['pull_request']['number']
        appel('PUT', f'/repos/{DEPOT}/pulls/{numero}/reviews/{objet["id"]}', {'body': propre or '.'})
        print(f'Revue de #{numero} corrigée.')
    elif evenement == 'pull_request_review_comment':
        appel('PATCH', f'/repos/{DEPOT}/pulls/comments/{objet["id"]}', {'body': propre})
        print('Commentaire de ligne corrigé.')
    else:
        appel('PATCH', f'/repos/{DEPOT}/pulls/{objet["number"]}', {'body': propre})
        print(f'Description de #{objet["number"]} corrigée.')


if __name__ == '__main__':
    try:
        main()
    except urllib.error.HTTPError as e:
        sys.exit(f'GitHub a refusé la correction : HTTP {e.code} {e.read().decode()[:200]}')
