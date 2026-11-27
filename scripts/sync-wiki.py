#!/usr/bin/env python3
"""Génère le wiki GitHub à partir de docs/ — le wiki est un miroir, jamais une source.

La documentation fait foi dans le dépôt (ADR-0002) ; le wiki en offre une lecture
navigable. Chaque page est régénérée à l'identique : une modification faite à la main
dans le wiki est écrasée au passage suivant.

    python3 scripts/sync-wiki.py <dossier-du-wiki>

Le dossier est un clone de https://github.com/chrraj7/S-Aloha.wiki.git. Le script
réécrit les pages générées, sans toucher au reste ; il ne committe pas. Procédure
complète : docs/protocole-documentation.md § Wiki.
"""
import pathlib
import posixpath
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
GITHUB = 'https://github.com/chrraj7/S-Aloha'
BLOB = f'{GITHUB}/blob/main'
RAW = 'https://raw.githubusercontent.com/chrraj7/S-Aloha/main'

# Pages du wiki : nom de page → fichier source. L'ordre est celui de la barre latérale.
# Troisième champ : ce que le lecteur vient y chercher (page d'accueil).
REFERENCE = [
    ('Produit', 'docs/reference/produit.md', 'Comprendre la vision, les publics visés et les pratiques couvertes'),
    ('Règles-métier', 'docs/reference/regles-metier.md', 'Connaître les rôles, la console, les cycles de vie, la priorité, le RACI'),
    ('Architecture', 'docs/reference/architecture.md', 'Voir comment tout s’assemble : containers, socle, modules, API'),
    ('Base-de-données', 'docs/reference/base-de-donnees.md', 'Lire le modèle de données et les migrations'),
    ('Frontend', 'docs/reference/frontend.md', 'Ajouter un écran ou un module, respecter le design'),
    ('Exploitation', 'docs/reference/exploitation.md', 'Installer, brancher l’Active Directory, mettre à jour'),
    ('Recherche', 'docs/reference/recherche.md', 'Comprendre la recherche hybride et le service d’embeddings'),
    ('Périmètres', 'docs/reference/perimetres.md', 'Droits scopés par direction, site ou entité'),
    ('Sécurité', 'docs/reference/securite.md', 'Préparer une mise en production'),
    ('Glossaire', 'docs/reference/glossaire.md', 'Retrouver un terme ITIL, technique ou malgache'),
    ('Pratiques-ITIL', 'docs/reference/processus/readme.md', 'Spécifier ou implémenter une pratique : règles numérotées, statut, lot ; choisir une règle à prendre'),
    ('Pratique-Problèmes', 'docs/reference/processus/gestion-des-problemes.md', 'Gestion des problèmes'),
    ('Pratique-Incidents', 'docs/reference/processus/gestion-des-incidents.md', 'Gestion des incidents'),
    ('Pratique-Demandes', 'docs/reference/processus/gestion-des-demandes.md', 'Gestion des demandes de service'),
    ('Pratique-Changements', 'docs/reference/processus/habilitation-des-changements.md', 'Habilitation des changements'),
    ('Pratique-Configuration', 'docs/reference/processus/gestion-de-la-configuration.md', 'Gestion de la configuration'),
    ('Pratique-Niveaux-de-service', 'docs/reference/processus/gestion-des-niveaux-de-service.md', 'Gestion des niveaux de service'),
    ('Pratique-Connaissances', 'docs/reference/processus/gestion-des-connaissances.md', 'Gestion des connaissances'),
    ('Pratique-Amélioration-continue', 'docs/reference/processus/amelioration-continue.md', 'Amélioration continue'),
    ('Pratique-Conformité-NIS-2', 'docs/reference/processus/conformite-nis2.md', 'Conformité NIS 2'),
    ('Pratique-Déclarations-réglementaires', 'docs/reference/processus/declarations-reglementaires.md', 'Déclarations réglementaires'),
]
PILOTAGE = [
    ('Plan-d’action', 'docs/plan-action.md', 'Les chantiers ouverts, la feuille de route et leur priorité'),
    ('Décisions', 'docs/decisions/readme.md', 'Les choix structurants et pourquoi (ADR)'),
    ('Protocole-documentaire', 'docs/protocole-documentation.md', 'La routine qui tient la documentation à jour'),
    ('Soutenir', 'docs/soutenir.md', 'Financer la feuille de route : sponsoring, chantiers à sponsoriser, services'),
]


def adr_pages():
    out = []
    for p in sorted((REPO / 'docs/decisions').glob('adr-*.md')):
        num = re.match(r'adr-(\d{4})', p.name).group(1)
        out.append((f'ADR-{num}', f'docs/decisions/{p.name}', ''))
    return out


def pages():
    return [(n, s) for n, s, _ in REFERENCE + PILOTAGE + adr_pages()]


def first_heading(text):
    m = re.search(r'^# (.+)$', text, re.M)
    return m.group(1).strip() if m else ''


def convert(src, text, by_source):
    """Adapte un document de docs/ au wiki : titre, navigation, liens."""
    base = posixpath.dirname(src)

    # Le wiki affiche le nom de la page en titre, et sa barre latérale remplace la
    # ligne de navigation <sub>…</sub> des documents.
    text = re.sub(r'\A# .+\n+', '', text)
    text = re.sub(r'^<sub>\[← Documentation\].*</sub>\n+', '', text, flags=re.M)

    def link(m):
        label, target = m.group(1), m.group(2)
        if re.match(r'^(https?:|mailto:|#)', target):
            return m.group(0)
        path, _, frag = target.partition('#')
        anchor = f'#{frag}' if frag else ''
        resolved = posixpath.normpath(posixpath.join(base, path))
        if resolved in by_source:
            return f'[{label}]({by_source[resolved]}{anchor})'
        if resolved == 'docs/readme.md':
            return f'[{label}](Home)'
        if resolved.endswith(('.png', '.jpg', '.svg', '.gif')):
            return f'[{label}]({RAW}/{resolved})'
        kind = 'tree' if (REPO / resolved).is_dir() else 'blob'
        return f'[{label}]({GITHUB}/{kind}/main/{resolved}{anchor})'

    # Images d'abord (![…](…)), puis liens.
    text = re.sub(r'!\[([^\]]*)\]\(([^)\s]+)\)',
                  lambda m: '!' + link(m)[0:], text)
    text = re.sub(r'(?<!!)\[([^\]]*)\]\(([^)\s]+)\)', link, text)
    text = re.sub(r'<img src="(?!https?:)([^"]+)"',
                  lambda m: f'<img src="{RAW}/{posixpath.normpath(posixpath.join(base, m.group(1)))}"', text)

    banner = (f'> 📄 Page générée depuis [`{src}`]({BLOB}/{src}) — la documentation fait foi '
              f'dans le dépôt ; toute modification se fait là-bas, pas dans ce wiki.\n\n')
    return banner + text


def home(by_source):
    def row(name, src, desc):
        title = first_heading((REPO / src).read_text(encoding='utf-8'))
        return f'| [{title}]({name}) | {desc} |'

    ref = '\n'.join(row(*p) for p in REFERENCE)
    pil = '\n'.join(row(*p) for p in PILOTAGE)
    return f'''<div align="center">

<img src="{RAW}/frontend/public/favicon.svg" width="64" alt="S-Aloha" />

Plateforme ITIL libre (AGPLv3) : tri assisté des tickets, CMDB assistée, serveur MCP.

[Dépôt]({GITHUB}) · [README]({GITHUB}#readme) · [Application mobile](https://github.com/chrraj7/S-Aloha-mobile) · [Issues]({GITHUB}/issues) · [Licence]({BLOB}/LICENSE)

</div>

> 📄 Ce wiki est **généré** depuis le dossier [`docs/`]({GITHUB}/tree/main/docs) du dépôt,
> qui fait foi ([ADR-0002](ADR-0002)). Pour corriger une page, modifiez le document source.

## En bref

**Tri assisté des tickets**, **CMDB assistée** et **serveur MCP**, activés par l'Admin et
exécutés hors de l'API, sur **10 pratiques ITIL** reliées entre elles — incidents, demandes,
problèmes, changements, configuration, niveaux de service, connaissances, amélioration
continue, conformité NIS 2, projets — et un socle commun : console qui dit à chacun quoi
faire, notifications, commentaires et historique sur chaque fiche, recherche hybride,
reporting, connexion par l'Active Directory de l'organisation. **Plus de 400 règles de
gestion** en place, on-prem, libre sous AGPLv3.

- **Découvrir** : [fonctionnalités et captures]({GITHUB}#fonctionnalités), [Produit](Produit).
- **Essayer** : le [mode démonstration]({GITHUB}#essayer-en-5-minutes) démarre sans Active
  Directory, en une commande.
- **En mobilité** : [S-Aloha Mobile](https://github.com/chrraj7/S-Aloha-mobile), client iOS et Android (Expo), dans
  son propre dépôt, en anglais.
- **Contribuer** : chaque pratique est spécifiée règle par règle ; les règles 🔜 restantes
  attendent leur implémentation ([pratiques ITIL](Pratiques-ITIL),
  [guide de contribution]({BLOB}/CONTRIBUTING.md)).

## Référence

Ce qui est vrai en permanence, mis à jour avec le code.

| Page | Pour |
|---|---|
{ref}

## Décisions et pilotage

| Page | Pour |
|---|---|
{pil}

Les constats datés et les journaux des sujets traités restent dans le dépôt :
[`docs/archives/`]({GITHUB}/tree/main/docs/archives).
'''


def sidebar():
    lines = ['**[🏠 Accueil](Home)**', '', '**Référence**', '']
    lines += [f'- [{n.replace("-", " ")}]({n})' for n, _, _ in REFERENCE]
    lines += ['', '**Pilotage**', '']
    lines += [f'- [{n.replace("-", " ")}]({n})' for n, _, _ in PILOTAGE]
    lines += ['', '**Décisions**', '']
    for n, src, _ in adr_pages():
        title = first_heading((REPO / src).read_text(encoding='utf-8'))
        short = title.split('—', 1)[1].strip() if '—' in title else title
        lines.append(f'- [{n}]({n}) · {short}')
    lines += ['', '---', f'[Dépôt]({GITHUB}) · [Archives]({GITHUB}/tree/main/docs/archives)']
    return '\n'.join(lines) + '\n'


FOOTER = (f'<sub>S-Aloha — logiciel libre sous [AGPLv3]({BLOB}/LICENSE) · '
          f'Wiki généré depuis [`docs/`]({GITHUB}/tree/main/docs) par `scripts/sync-wiki.py`.</sub>\n')


def main(argv):
    if len(argv) != 1:
        sys.exit(__doc__)
    out = pathlib.Path(argv[0])
    if not (out / '.git').exists():
        sys.exit(f'{out} n’est pas un clone du wiki (pas de .git).')

    by_source = {src: name for name, src in pages()}
    generated = {'Home.md', '_Sidebar.md', '_Footer.md'}

    for name, src in pages():
        text = (REPO / src).read_text(encoding='utf-8')
        (out / f'{name}.md').write_text(convert(src, text, by_source), encoding='utf-8')
        generated.add(f'{name}.md')
    (out / 'Home.md').write_text(home(by_source), encoding='utf-8')
    (out / '_Sidebar.md').write_text(sidebar(), encoding='utf-8')
    (out / '_Footer.md').write_text(FOOTER, encoding='utf-8')

    # Une page dont le document source a disparu (ADR renommé…) est retirée.
    for p in out.glob('*.md'):
        if p.name not in generated:
            print(f'retirée : {p.name}')
            p.unlink()
    print(f'{len(generated)} pages générées dans {out}')


if __name__ == '__main__':
    main(sys.argv[1:])
