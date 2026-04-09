#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

get_property_signature() {
  local pattern="$1"
  local prop="$2"
  awk -v target="$prop" '
    /^[[:space:]]*public[[:space:]]+/ && /\{ get; set; \}/ {
      line=$0;
      sub(/^[[:space:]]*public[[:space:]]+/, "", line);
      split(line, a, " { get; set; }");
      left=a[1];
      right=a[2];

      n=split(left, b, " ");
      p=b[n];
      if (p != target) next;

      type="";
      for (i=1; i<n; i++) {
        type = type (i==1 ? "" : " ") b[i];
      }

      def="";
      if (right ~ /=/) {
        sub(/^[[:space:]]*=[[:space:]]*/, "", right);
        sub(/;[[:space:]]*$/, "", right);
        def=right;
      }

      printf "%s|%s\n", type, def;
      exit 0;
    }
  ' $pattern
}

resolve_default() {
  local type="$1"
  local def="$2"

  if [[ -z "$def" || "$def" == "null!" ]]; then
    if [[ "$type" == List* || "$type" == HashSet* ]]; then
      echo "[]"
      return
    fi
    if [[ "$type" == Dictionary* ]]; then
      echo "{}"
      return
    fi
    if [[ "$type" == "bool" ]]; then
      echo "false"
      return
    fi
    if [[ "$type" == *"?" ]]; then
      echo "null"
      return
    fi
    echo "(runtime default)"
    return
  fi

  if [[ "$def" == "new()" ]]; then
    echo "[]"
    return
  fi

  echo "$def"
}

print_entry_block() {
  local entry="$1"
  local folder="$2"
  local lang="$3"
  local kind="$4"

  local expected_file="$folder/expected.json"
  if [[ ! -f "$expected_file" ]]; then
    expected_file="$(find "$folder" -maxdepth 1 -type f -name 'expected*.json' ! -name 'expected.bindings*.json' | sort | head -n1)"
  fi

  local expected_prop=""
  local expected_value="(not specified in snapshot)"
  if [[ -n "${expected_file:-}" && -f "$expected_file" ]]; then
    expected_prop="$(jq -r '.Property // empty' "$expected_file")"
    expected_value="$(jq -c '.Expected' "$expected_file")"
  fi

  local prop_for_type="$entry"
  local type=""
  local raw_default=""

  if [[ "$kind" == "BGCS" ]]; then
    local sig
    sig="$(get_property_signature "$ROOT_DIR/src/BGCS/CsCodeGeneratorConfig*.cs" "$prop_for_type" || true)"
    if [[ -z "$sig" && -n "$expected_prop" ]]; then
      prop_for_type="$expected_prop"
      sig="$(get_property_signature "$ROOT_DIR/src/BGCS/CsCodeGeneratorConfig*.cs" "$prop_for_type" || true)"
    fi
    type="${sig%%|*}"
    raw_default="${sig#*|}"
  else
    local sig
    sig="$(get_property_signature "$ROOT_DIR/src/BGCS.Cpp2C/Cpp2CGeneratorConfig*.cs" "$prop_for_type" || true)"
    if [[ -z "$sig" && -n "$expected_prop" ]]; then
      prop_for_type="$expected_prop"
      sig="$(get_property_signature "$ROOT_DIR/src/BGCS.Cpp2C/Cpp2CGeneratorConfig*.cs" "$prop_for_type" || true)"
    fi
    type="${sig%%|*}"
    raw_default="${sig#*|}"
  fi

  if [[ -z "$type" || "$type" == "$raw_default" ]]; then
    type="unknown"
    raw_default=""
  fi

  local default_value
  default_value="$(resolve_default "$type" "$raw_default")"

  local config_file="$folder/config.json"
  if [[ ! -f "$config_file" ]]; then
    config_file="$(find "$folder" -maxdepth 1 -type f -name 'config*.json' | sort | head -n1)"
  fi

  local binding_file="$folder/expected.bindings.json"
  if [[ ! -f "$binding_file" ]]; then
    binding_file="$(find "$folder" -maxdepth 1 -type f -name 'expected.bindings*.json' | sort | head -n1)"
  fi

  echo "## $entry"
  echo
  echo "### 1. Explanation"
  if [[ -n "$expected_prop" && "$expected_prop" != "$entry" ]]; then
    echo "**$entry** is validated through composition behavior, and tests assert the final **$expected_prop** after **$entry** is applied."
  else
    echo "**$entry** controls the **$entry** behavior and is validated by both property snapshots and generated-output checks."
  fi
  echo
  echo "### 2. Type, Example, and Default Value"
  echo "- Type: \`$type\`"
  echo "- Default value: \`$default_value\`"
  echo "- Example expected value: \`$expected_value\`"
  echo
  echo "### 3. Example Config and Generated Output"
  echo "#### Example config"
  echo '```json'
  if [[ -n "${config_file:-}" && -f "$config_file" ]]; then
    cat "$config_file"
  else
    echo "{}"
  fi
  echo '```'
  echo
  echo "#### Example generated output markers"
  echo "\`\`\`$lang"
  if [[ -n "${binding_file:-}" && -f "$binding_file" ]]; then
    echo "// Contains"
    jq -r '.Contains // [] | .[]' "$binding_file"
    echo "// NotContains"
    jq -r '.NotContains // [] | .[]' "$binding_file"
  else
    echo "// No expected.bindings snapshot for this entry"
  fi
  echo '```'
  echo
}

generate_bgcs_doc() {
  local output="$ROOT_DIR/docs/config.md"
  {
    echo "# Configuration"
    echo
    echo "This is an entry-by-entry specification generated from \`BGCS.Configuration.Tests\`."
    echo
    while IFS= read -r folder; do
      print_entry_block "$(basename "$folder")" "$folder" "csharp" "BGCS"
    done < <(find "$ROOT_DIR/tests/BGCS.Configuration.Tests/entryTests" -mindepth 1 -maxdepth 1 -type d | sort)
  } > "$output"
}

generate_cpp2c_doc() {
  local output="$ROOT_DIR/docs/cpp2c.config.md"
  {
    echo "# Configuration"
    echo
    echo "This is an entry-by-entry specification generated from \`BGCS.Cpp2C.Configuration.Tests\`."
    echo
    while IFS= read -r folder; do
      print_entry_block "$(basename "$folder")" "$folder" "cpp" "CPP2C"
    done < <(find "$ROOT_DIR/tests/BGCS.Cpp2C.Configuration.Tests/entryTests" -mindepth 1 -maxdepth 1 -type d | sort)
  } > "$output"
}

generate_bgcs_doc
generate_cpp2c_doc
