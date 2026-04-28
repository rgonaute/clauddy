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

# Build JSON
if [ "$action" = "remove" ]; then
  body=$(jq -n --arg sid "$session_id" --arg cwd "$cwd" --arg lbl "${CLAUDDY_LABEL:-}" \
    '{session_id:$sid, cwd:$cwd, label:$lbl, action:"remove"}')
else
  body=$(jq -n --arg sid "$session_id" --arg cwd "$cwd" --arg lbl "${CLAUDDY_LABEL:-}" --arg s "$state" \
    '{session_id:$sid, cwd:$cwd, label:$lbl, state:$s}')
fi

# POST. Hard 1s timeout. Failure is silent.
curl --max-time 1 -sS -X POST "$endpoint/state" \
  -H 'Content-Type: application/json' \
  -d "$body" >/dev/null 2>&1 || true
