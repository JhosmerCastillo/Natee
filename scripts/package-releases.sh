#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.0.0}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_BASE="${ROOT_DIR}/artifacts/publish"
RELEASE_DIR="${ROOT_DIR}/artifacts/releases"

mkdir -p "${PUBLISH_BASE}" "${RELEASE_DIR}"

DOTNET_CMD="dotnet"
if ! "${DOTNET_CMD}" --list-sdks 2>/dev/null | grep -q '^10\.' && [ -x "${HOME}/.dotnet/dotnet" ]; then
  DOTNET_CMD="${HOME}/.dotnet/dotnet"
fi

echo "==> Building and packaging Natee v${VERSION} (linux-x64)..."
"${DOTNET_CMD}" publish "${ROOT_DIR}/src/Natee.App/Natee.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o "${PUBLISH_BASE}/linux-x64"

cp "${ROOT_DIR}/README.md" "${ROOT_DIR}/LICENSE" "${PUBLISH_BASE}/linux-x64/" 2>/dev/null || true
tar -czf "${RELEASE_DIR}/natee-v${VERSION}-linux-x64.tar.gz" -C "${PUBLISH_BASE}/linux-x64" .

echo "==> Building and packaging Natee v${VERSION} (win-x64)..."
"${DOTNET_CMD}" publish "${ROOT_DIR}/src/Natee.App/Natee.App.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o "${PUBLISH_BASE}/win-x64"

cp "${ROOT_DIR}/README.md" "${ROOT_DIR}/LICENSE" "${PUBLISH_BASE}/win-x64/" 2>/dev/null || true
python3 -c "
import shutil
shutil.make_archive('${RELEASE_DIR}/natee-v${VERSION}-win-x64', 'zip', '${PUBLISH_BASE}/win-x64')
"

echo "==> Release artifacts created in ${RELEASE_DIR}:"
ls -lh "${RELEASE_DIR}"
