#!/usr/bin/env bash
# Installs the Clauddy hook inside the current WSL distro.
# Args: $1 = Windows username (used to resolve /mnt/c/Users/<user>/.clauddy)
set -e
WIN_USERNAME="$1"
[ -n "$WIN_USERNAME" ] || { echo "usage: install-wsl.sh <win-username>" >&2; exit 2; }

mkdir -p "$HOME/.clauddy/hooks"
cp "/mnt/c/Users/$WIN_USERNAME/.clauddy/hooks/clauddy-hook.sh" "$HOME/.clauddy/hooks/"
chmod +x "$HOME/.clauddy/hooks/clauddy-hook.sh"

# Plant WIN_USERNAME so the hook can find /mnt/c endpoint
profile="$HOME/.profile"
marker="# clauddy-managed"
if ! grep -q "$marker" "$profile" 2>/dev/null; then
  printf '\n%s\nexport WIN_USERNAME=%q\n' "$marker" "$WIN_USERNAME" >> "$profile"
fi

# Patch ~/.claude/settings.json (created by HookInstaller via second wsl invoke)
echo "wsl-install: hook copied to $HOME/.clauddy/hooks/" >&2
