#!/bin/bash
set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

echo "=========================================================="
echo "📦 Building VCC Package Zips & Updating index.json..."
echo "=========================================================="

mkdir -p dist
rm -f dist/*.zip

for pkg in Packages/*/; do
    [ -f "${pkg}package.json" ] || continue
    name=$(node -p "require('./${pkg}package.json').name")
    version=$(node -p "require('./${pkg}package.json').version")
    tag="${name}-${version}"
    echo "  - Packaging ${tag}.zip..."
    ( cd "$pkg" && zip -r -q -X "$REPO_ROOT/dist/${tag}.zip" . -x '.git*' )
done

export REPO="${REPO:-Bluscream/unity-editor-scripts}"
export LISTING_URL="${LISTING_URL:-https://bluscream.github.io/unity-editor-scripts}"

node .github/scripts/build-index.mjs

echo "=========================================================="
echo "✅ VCC listing updated successfully."
echo "=========================================================="
