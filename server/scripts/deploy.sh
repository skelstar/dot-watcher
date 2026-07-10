#!/usr/bin/env bash
# Deploys the dot-watcher server to Tatooine (home k3s cluster). Run this ON Tatooine, not from a
# dev machine — it builds against the local Docker registry at localhost:5000 and talks to the
# in-cluster kubectl context directly. See server/README.md#deployment-tatooine--home-k3s-cluster
# for the one-time cluster setup (namespace, secrets, DNS, ingress) this script does not create.
#
# Usage: server/scripts/deploy.sh [git-ref]
#   git-ref defaults to "main". Pass a tag (e.g. a GitHub release tag) to deploy that exact ref.
set -euo pipefail

REF="${1:-main}"
REPO_URL="https://github.com/skelstar/dot-watcher.git"
NAMESPACE="dot-watcher-server"
DEPLOYMENT="dot-watcher-server"
IMAGE="localhost:5000/dot-watcher-server:latest"
WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

echo "==> Cloning $REPO_URL @ $REF"
git clone --quiet --depth 1 --branch "$REF" "$REPO_URL" "$WORKDIR" \
  || { git clone --quiet "$REPO_URL" "$WORKDIR" && git -C "$WORKDIR" checkout --quiet "$REF"; }

RESOLVED_SHA="$(git -C "$WORKDIR" rev-parse --short HEAD)"
echo "==> Building image $IMAGE (commit $RESOLVED_SHA)"
docker build -t "$IMAGE" -f "$WORKDIR/server/Dockerfile" "$WORKDIR"

echo "==> Pushing $IMAGE"
docker push "$IMAGE"

echo "==> Restarting deployment/$DEPLOYMENT in namespace $NAMESPACE"
kubectl rollout restart "deployment/$DEPLOYMENT" -n "$NAMESPACE"
kubectl rollout status "deployment/$DEPLOYMENT" -n "$NAMESPACE" --timeout=120s

echo "==> Deployed $REF (commit $RESOLVED_SHA)"
