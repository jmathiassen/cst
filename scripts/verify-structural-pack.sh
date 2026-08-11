#!/usr/bin/env bash
# verify-structural-pack.sh <PackName>
# Battery gate for Structural promotion (packs-quality-bar §3.5 / agent finish gates).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

PACK="${1:-}"
if [[ -z "$PACK" ]]; then
  echo "usage: $0 <PackName>   e.g. M4 Hcl Shell"
  exit 2
fi

PACK_DIR="TgrDevelopments.Cst.Packs.${PACK}"
TEST_DIR="TgrDevelopments.Cst.Packs.${PACK}.Tests"
LANG_PACK=$(find "$PACK_DIR" -name '*LanguagePack.cs' 2>/dev/null | head -1)
TEST_FILE=$(find "$TEST_DIR" -name '*LanguagePackTests.cs' -o -name '*PackTests.cs' 2>/dev/null | head -1)

pass=0
fail=0
check() {
  local name="$1"
  shift
  if "$@"; then
    echo "PASS  $name"
    pass=$((pass + 1))
  else
    echo "FAIL  $name"
    fail=$((fail + 1))
  fi
}

echo "=== verify-structural-pack: $PACK ==="

check "01_pack_dir_exists" test -d "$PACK_DIR"
check "02_tests_dir_exists" test -d "$TEST_DIR"
check "03_language_pack_cs" test -n "$LANG_PACK" -a -f "$LANG_PACK"
check "04_test_file_cs" test -n "$TEST_FILE" -a -f "$TEST_FILE"
check "05_structural_base" rg -q 'StructuralPackTestBase' "$TEST_DIR" --glob '*.cs'
check "06_parse_happy" rg -q 'Parse_Happy_' "$TEST_DIR" --glob '*.cs'
check "07_nested_or_name" bash -c "rg -q 'Parse_Nested_|Parse_NameSpan_' '$TEST_DIR' --glob '*.cs'"
check "08_string_safety" rg -q 'StringSafety|string_safety|Parse_StringSafety' "$TEST_DIR" --glob '*.cs'
check "09_comment_safety" rg -q 'CommentSafety|comment_safety|Parse_CommentSafety' "$TEST_DIR" --glob '*.cs'
check "10_concurrent" rg -q 'Concurrent|Parse_Concurrent' "$TEST_DIR" --glob '*.cs'
check "11_dotnet_test" bash -c "dotnet test '$TEST_DIR' -v q --nologo >/tmp/verify-${PACK}-test.log 2>&1"
check "12_default_quality_honest" bash -c "rg -q 'DefaultQuality => SyntaxQuality\.(Structural|Preview|Experimental)' '$LANG_PACK'"

echo "=== result: $pass/12 passed, $fail failed ==="
if [[ "$fail" -ne 0 ]]; then
  exit 1
fi
exit 0
