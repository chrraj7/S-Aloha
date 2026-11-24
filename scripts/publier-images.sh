#!/usr/bin/env bash
# Construit et pousse les images sur ghcr.io (registre déjà connecté).
#   VERSION=… COMMIT=… bash scripts/publier-images.sh <tag> [<tag>…]
# VERSION : affichée par Administration › Santé (ADM-21) et portée en étiquette OCI.
# Appelé par .github/workflows/publication.yml (version : vX.Y.Z et latest) et par
# ci.yml à chaque push sur main (développement : edge).
set -euo pipefail
: "${VERSION:?VERSION manquante}" "${COMMIT:?COMMIT manquant}"
[ $# -gt 0 ] || { echo "Usage : $0 <tag> [<tag>…]" >&2; exit 2; }
prefixe=ghcr.io/chrraj7/s-aloha
source=https://github.com/chrraj7/S-Aloha
for image in api:backend web:frontend annuaire-demo:demo/annuaire features:features/SAloha.Features mcp:features/SAloha.Mcp; do
  nom=${image%%:*}; dossier=${image#*:}
  etiquettes=()
  for tag in "$@"; do etiquettes+=(-t "$prefixe-$nom:$tag"); done
  docker build "$dossier" \
    --build-arg VERSION="${VERSION#v}" --build-arg COMMIT="$COMMIT" \
    --label org.opencontainers.image.source="$source" \
    --label org.opencontainers.image.version="$VERSION" \
    --label org.opencontainers.image.revision="$COMMIT" \
    --label org.opencontainers.image.licenses=AGPL-3.0-or-later \
    "${etiquettes[@]}"
  for tag in "$@"; do docker push "$prefixe-$nom:$tag"; done
done
