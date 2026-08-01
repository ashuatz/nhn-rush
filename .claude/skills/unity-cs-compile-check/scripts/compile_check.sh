#!/bin/bash
# Unity C# Assembly Compile Check
# Usage: compile_check.sh <cs-file-path> [project-root]
#
# Finds the .asmdef assembly for a C# file, maps it to a .csproj,
# and runs dotnet build -nologo to verify compilation.

set -euo pipefail

CS_FILE="$1"
PROJECT_ROOT="${2:-$(pwd)}"

if [ ! -f "$CS_FILE" ]; then
  echo "ERROR: File not found: $CS_FILE"
  exit 1
fi

CS_DIR="$(cd "$(dirname "$CS_FILE")" && pwd)"

# Step 1: Find the nearest .asmdef by walking up from the .cs file directory
find_asmdef() {
  local dir="$1"
  local root="$2"

  while [ "$dir" != "$root" ] && [ "$dir" != "/" ] && [ "$dir" != "." ]; do
    local asmdef
    asmdef=$(find "$dir" -maxdepth 1 -name "*.asmdef" 2>/dev/null | head -1)
    if [ -n "$asmdef" ]; then
      echo "$asmdef"
      return 0
    fi
    dir="$(dirname "$dir")"
  done

  # Check project root too
  local asmdef
  asmdef=$(find "$root" -maxdepth 1 -name "*.asmdef" 2>/dev/null | head -1)
  if [ -n "$asmdef" ]; then
    echo "$asmdef"
    return 0
  fi

  return 1
}

# Step 2: Extract assembly name from .asmdef JSON
extract_assembly_name() {
  local asmdef_file="$1"
  # Parse "name" field from .asmdef JSON
  # Using grep+sed for portability (no jq dependency)
  grep -o '"name"[[:space:]]*:[[:space:]]*"[^"]*"' "$asmdef_file" | head -1 | sed 's/.*"name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/'
}

# Step 3: Find matching .csproj
find_csproj() {
  local assembly_name="$1"
  local root="$2"

  # Unity generates .csproj files at the project root level
  local csproj_path="$root/${assembly_name}.csproj"

  if [ -f "$csproj_path" ]; then
    echo "$csproj_path"
    return 0
  fi

  # Fallback: search recursively
  local found
  found=$(find "$root" -maxdepth 3 -name "${assembly_name}.csproj" 2>/dev/null | head -1)
  if [ -n "$found" ]; then
    echo "$found"
    return 0
  fi

  return 1
}

echo "=== Unity C# Compile Check ==="
echo "File: $CS_FILE"
echo "Project Root: $PROJECT_ROOT"
echo ""

# Find .asmdef
ASMDEF=$(find_asmdef "$CS_DIR" "$PROJECT_ROOT") || {
  echo "WARNING: No .asmdef found for $CS_FILE"
  echo "Falling back to Assembly-CSharp.csproj"
  ASMDEF=""
}

if [ -n "$ASMDEF" ]; then
  ASSEMBLY_NAME=$(extract_assembly_name "$ASMDEF")
  echo "Assembly Definition: $ASMDEF"
  echo "Assembly Name: $ASSEMBLY_NAME"
else
  ASSEMBLY_NAME="Assembly-CSharp"
  echo "Assembly Name: $ASSEMBLY_NAME (default)"
fi

echo ""

# Find .csproj
CSPROJ=$(find_csproj "$ASSEMBLY_NAME" "$PROJECT_ROOT") || {
  echo "ERROR: Could not find ${ASSEMBLY_NAME}.csproj"
  echo ""
  echo "Available .csproj files:"
  find "$PROJECT_ROOT" -maxdepth 2 -name "*.csproj" 2>/dev/null | while read -r f; do
    echo "  - $f"
  done
  exit 1
}

echo "Building: $CSPROJ"
echo "---"

# Step 4: Compile
dotnet build "$CSPROJ" -nologo 2>&1

BUILD_EXIT=$?

echo "---"
if [ $BUILD_EXIT -eq 0 ]; then
  echo "BUILD SUCCEEDED"
else
  echo "BUILD FAILED (exit code: $BUILD_EXIT)"
fi

exit $BUILD_EXIT
