# Calls a hosted agent that is running locally, the way the bot does, minus the platform.
#   Terminal A:  dotnet run --project src/Agents/Compass.Agents.Hosted.ZavaApi
#   Terminal B:  ./tools/Call-LocalAgent.ps1
# With no token the agent must fail closed: status "failed", code "missing_token". Pass -ZavaApiToken to go further: the agent then
# calls the MCP server named by ZavaApi__McpEndpoint (default http://localhost:7071/runtime/webhooks/mcp), so run the Functions host first.
param(
    [string]$Url = 'http://localhost:8088',
    [string]$MemberNumber = 'ZCU-100234',
    [string]$PersonObjectId = '00000000-0000-0000-0000-000000000001',
    [string]$ZavaApiToken
)

# On Foundry the platform injects the person's id. Locally there is no platform, so the handler falls back to x-agent-user-id.
# (x-ms-user-identity, which the bot sends to Foundry, is not surfaced to a handler by the adapter, so it cannot be used here.)
$headers = @{ 'x-agent-user-id' = $PersonObjectId }
if ($ZavaApiToken) { $headers['x-client-zava-api-token'] = $ZavaApiToken }

# The agent's typed request travels as JSON text in the Responses "input".
$request = @{ memberNumber = $MemberNumber } | ConvertTo-Json -Compress
$body = @{ model = 'local'; input = $request } | ConvertTo-Json -Compress

$response = Invoke-RestMethod -Method Post -Uri "$Url/responses" -Headers $headers -ContentType 'application/json' -Body $body
"status  : $($response.status)"
if ($response.error) {
    "code    : $($response.error.code)"
    "message : $($response.error.message)"
}
else {
    $response.output | ConvertTo-Json -Depth 6
}