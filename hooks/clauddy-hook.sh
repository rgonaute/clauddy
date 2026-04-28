#!/usr/bin/env bash
# Claude Code hook: POSTs session state to local Clauddy widget.
# Lives at ~/.clauddy/hooks/clauddy-hook.sh after install.

set -e
# Find endpoint file (native Windows home or WSL view of it)
endpoint_file="$HOME/.clauddy/endpoint"
if [ ! -f "$endpoint_file" ] && [ -n "${WIN_USERNAME:-}" ] && [ -d "/mnt/c/Users/$WIN_USERNAME" ]; then
  endpoint_file="/mnt/c/Users/$WIN_USERNAME/.clauddy/endpoint"
fi
[ -f "$endpoint_file" ] || exit 0
endpoint=$(cat "$endpoint_file")
[ -n "$endpoint" ] || exit 0

# Read hook payload
payload=$(cat)
event=$(echo "$payload" | jq -r '.hook_event_name // empty')
session_id=$(echo "$payload" | jq -r '.session_id // empty')
cwd=$(echo "$payload" | jq -r '.cwd // empty')
transcript_path=$(echo "$payload" | jq -r '.transcript_path // empty')
[ -n "$event" ] && [ -n "$session_id" ] || exit 0

# Map event → state/action
state=""; action="update"
case "$event" in
  SessionStart)        state="chilling" ;;
  UserPromptSubmit)    state="working" ;;
  Stop|SubagentStop)   state="chilling" ;;
  Notification)        state="alerting" ;;
  SessionEnd)          action="remove" ;;
  *) exit 0 ;;
esac

# Compute per-bucket token counts for this session by reading the transcript JSONL.
# Buckets: input (full-price input), output, cache_creation (1.25x input cost),
# cache_read (0.1x input cost). Widget aggregates these into total + cache hit rate.
input_tokens=0; output_tokens=0; cache_creation=0; cache_read=0
if [ -n "$transcript_path" ] && [ -f "$transcript_path" ]; then
  read -r input_tokens output_tokens cache_creation cache_read <<< "$(jq -s -r '
    [.[] | select(.message.role == "assistant") | .message.usage // {}] as $u
    | (($u | map(.input_tokens // 0) | add // 0) | tostring) + " "
    + (($u | map(.output_tokens // 0) | add // 0) | tostring) + " "
    + (($u | map(.cache_creation_input_tokens // 0) | add // 0) | tostring) + " "
    + (($u | map(.cache_read_input_tokens // 0) | add // 0) | tostring)
  ' "$transcript_path" 2>/dev/null)" || true
  : "${input_tokens:=0}" "${output_tokens:=0}" "${cache_creation:=0}" "${cache_read:=0}"
fi

# Build JSON
if [ "$action" = "remove" ]; then
  body=$(jq -n --arg sid "$session_id" --arg cwd "$cwd" --arg lbl "${CLAUDDY_LABEL:-}" \
    '{session_id:$sid, cwd:$cwd, label:$lbl, action:"remove"}')
else
  body=$(jq -n --arg sid "$session_id" --arg cwd "$cwd" --arg lbl "${CLAUDDY_LABEL:-}" --arg s "$state" \
    --argjson it "$input_tokens" --argjson ot "$output_tokens" --argjson cc "$cache_creation" --argjson cr "$cache_read" \
    '{session_id:$sid, cwd:$cwd, label:$lbl, state:$s, input_tokens:$it, output_tokens:$ot, cache_creation_tokens:$cc, cache_read_tokens:$cr}')
fi

# POST. Hard 1s timeout. Failure is silent.
curl --max-time 1 -sS -X POST "$endpoint/state" \
  -H 'Content-Type: application/json' \
  -d "$body" >/dev/null 2>&1 || true
