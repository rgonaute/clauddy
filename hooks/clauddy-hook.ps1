# Claude Code hook: POSTs session state to local Clauddy widget.
# Lives at $HOME\.clauddy\hooks\clauddy-hook.ps1 after install.

$ErrorActionPreference = 'SilentlyContinue'

$endpointFile = Join-Path $HOME ".clauddy/endpoint"
if (-not (Test-Path $endpointFile)) { exit 0 }
$endpoint = (Get-Content -Raw $endpointFile).Trim()
if ([string]::IsNullOrWhiteSpace($endpoint)) { exit 0 }

$payloadRaw = [Console]::In.ReadToEnd()
try { $payload = $payloadRaw | ConvertFrom-Json } catch { exit 0 }

$event = $payload.hook_event_name
$sessionId = $payload.session_id
$cwd = $payload.cwd
if (-not $event -or -not $sessionId) { exit 0 }

$state = $null; $action = 'update'
switch ($event) {
  'SessionStart'     { $state = 'chilling' }
  'UserPromptSubmit' { $state = 'working' }
  'Stop'             { $state = 'chilling' }
  'SubagentStop'     { $state = 'chilling' }
  'Notification'     { $state = 'alerting' }
  'SessionEnd'       { $action = 'remove' }
  default            { exit 0 }
}

$body = if ($action -eq 'remove') {
  @{ session_id=$sessionId; cwd=$cwd; label=$env:CLAUDDY_LABEL; action='remove' } | ConvertTo-Json -Compress
} else {
  @{ session_id=$sessionId; cwd=$cwd; label=$env:CLAUDDY_LABEL; state=$state } | ConvertTo-Json -Compress
}

try {
  Invoke-RestMethod -Method Post -Uri "$endpoint/state" `
    -ContentType 'application/json' -Body $body -TimeoutSec 1 | Out-Null
} catch { }
