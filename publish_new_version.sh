#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# --- Arguments ---
BUMP_TYPE="patch"
DESCRIPTION=""
AUTO_CONFIRM=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --description)
            DESCRIPTION="$2"
            shift 2
            ;;
        --yes|-y)
            AUTO_CONFIRM=true
            shift
            ;;
        patch|minor|major)
            BUMP_TYPE="$1"
            shift
            ;;
        *)
            echo "Usage: ./publish_new_version.sh [patch|minor|major] --description \"Release notes\" [--yes]"
            exit 1
            ;;
    esac
done

if [[ -z "$DESCRIPTION" ]]; then
    echo "Error: --description is required"
    echo "Usage: ./publish_new_version.sh [patch|minor|major] --description \"Release notes\" [--yes]"
    exit 1
fi

# --- Guard: must be on main ---
BRANCH="$(git branch --show-current)"
if [[ "$BRANCH" != "main" ]]; then
    echo "Error: you must be on the 'main' branch (currently on '$BRANCH')"
    exit 1
fi

# --- Guard: working tree must be clean ---
if ! git diff --quiet || ! git diff --cached --quiet; then
    echo "Error: working tree has uncommitted changes. Commit or stash them first."
    exit 1
fi

# --- Read current version from .csproj ---
CSPROJ="GlueyKeys/GlueyKeys.csproj"
CURRENT_VERSION="$(grep -oP '<Version>\K[^<]+' "$CSPROJ")"
if [[ -z "$CURRENT_VERSION" ]]; then
    echo "Error: could not read <Version> from $CSPROJ"
    exit 1
fi

IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"

# --- Bump ---
case "$BUMP_TYPE" in
    major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
    minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
    patch) PATCH=$((PATCH + 1)) ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"
TAG="v$NEW_VERSION"

echo ""
echo "  Current version:  $CURRENT_VERSION"
echo "  New version:      $NEW_VERSION ($BUMP_TYPE bump)"
echo "  Tag:              $TAG"
echo "  Description:      $DESCRIPTION"
echo ""

# --- Confirmation ---
if [[ "$AUTO_CONFIRM" != true ]]; then
    read -rp "Type 'release' to confirm: " CONFIRM
    if [[ "$CONFIRM" != "release" ]]; then
        echo "Aborted."
        exit 1
    fi
fi

echo ""
echo "==> Updating version in source files..."

# .csproj
sed -i "s|<Version>$CURRENT_VERSION</Version>|<Version>$NEW_VERSION</Version>|" "$CSPROJ"

# app.manifest (uses 4-part version: X.Y.Z.0)
sed -i "s|version=\"$CURRENT_VERSION.0\"|version=\"$NEW_VERSION.0\"|" GlueyKeys/app.manifest

# InstallationService.cs
sed -i "s|AppVersion = \"$CURRENT_VERSION\"|AppVersion = \"$NEW_VERSION\"|" GlueyKeys/Services/InstallationService.cs

# installer.iss
sed -i "s|#define MyAppVersion \"$CURRENT_VERSION\"|#define MyAppVersion \"$NEW_VERSION\"|" installer.iss

echo "==> Building..."
dotnet publish GlueyKeys/GlueyKeys.csproj \
    -c Release \
    -r win-x64 \
    --self-contained false \
    -o publish/ \
    /p:Version="$NEW_VERSION"

echo "==> Committing..."
git add "$CSPROJ" GlueyKeys/app.manifest GlueyKeys/Services/InstallationService.cs installer.iss
git commit -m "Release $TAG"

echo "==> Pushing..."
git push

echo "==> Creating GitHub release..."
gh release create "$TAG" \
    publish/GlueyKeys.exe \
    --title "GlueyKeys $TAG" \
    --notes "$DESCRIPTION"

echo ""
echo "Done! Released $TAG"
echo "https://github.com/raulconchello/GlueyKeys/releases/tag/$TAG"
