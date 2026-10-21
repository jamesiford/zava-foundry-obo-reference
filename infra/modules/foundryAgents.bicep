// Hosted-agent ARM resources for the two Compass agents.
//
// WHAT IS REAL: Microsoft.CognitiveServices/accounts/projects/applications and
// .../applications/agentDeployments (API 2026-09-15-preview; 2025-12-01 is not a registered API version for these
// nested resource types, only for `accounts` and `accounts/projects`) are real ARM types with Bicep type
// definitions; `agentDeployments` supports deploymentType 'Hosted' with protocols[] and min/maxReplicas.
//
// WHAT IS NOT IN ARM: the hosted agent *version* (container image reference, CPU/memory, env vars) is a
// Foundry data-plane object created with the Foundry SDK / azd agent tooling AFTER the container image is pushed.
// An agentDeployment can only point at an existing agent version, so:
//   - createApplications (default true)  creates one agent application per agent. The application resource is
//     the ARM anchor that rbac.bicep uses for PER-AGENT role assignments.
//   - createDeployments  (default false) creates the Hosted agentDeployment for agents listed in `agentVersions`.
//     Turn it on only after the agent versions exist (second `azd provision` or a manual deployment).
// Neither resource was exercised against a live Foundry account before this build's first live deployment.
// CONFIRMED LIVE (westus3, 2026-10-17): `applications@2026-09-15-preview` with `authorizationPolicy: { type:
// 'Default' }` was rejected by ARM with "Missing discriminator property [AuthorizationScheme]" even though that
// exact shape matches the `main`-branch TypeSpec for this resource (azure-rest-api-specs,
// Microsoft.CognitiveServices/CognitiveServices/models.tsp, `RoleBasedBuiltInAuthorizationPolicy`). The live service
// for this preview API version evidently expects a different wire shape than the published spec at the time this
// was run - re-check the spec/service parity before retrying. Until resolved, use the documented fallback: set
// createApplications=false (infra/main.parameters.json: CREATE_AGENT_APPLICATIONS=false); rbac.bicep then falls
// back to project-scoped assignments and no `applications`/`agentDeployments` resources are created at all.

@description('Foundry account name (created by foundryProject.bicep).')
param accountName string

@description('Foundry project name (created by foundryProject.bicep).')
param projectName string

@description('Hosted agent names, one application per agent.')
param agentNames array

@description('Prompt (non-hosted) agent names, e.g. the knowledge agent. They get an application scope like hosted agents but never a Hosted deployment.')
param promptAgentNames array = []

@description('Create the per-agent application resources.')
param createApplications bool = true

@description('Create Hosted agentDeployments (requires published agent versions listed in agentVersions).')
param createDeployments bool = false

@description('Map of agent name -> published agent version, e.g. { SharePointRetrievalAgent: \'1\' }.')
param agentVersions object = {}

@description('Container protocol exposed by the hosted agents.')
param protocol string = 'Responses'

@description('Container protocol version.')
param protocolVersion string = '2.0.0'

@description('Minimum replicas per hosted deployment.')
@minValue(0)
param minReplicas int = 1

@description('Maximum replicas per hosted deployment.')
@minValue(0)
param maxReplicas int = 2

resource account 'Microsoft.CognitiveServices/accounts@2025-12-01' existing = {
  name: accountName

  resource project 'projects' existing = {
    name: projectName
  }
}

resource applications 'Microsoft.CognitiveServices/accounts/projects/applications@2026-09-15-preview' = [for agentName in agentNames: if (createApplications) {
  parent: account::project
  name: agentName
  properties: {
    displayName: agentName
    description: 'Compass hosted agent ${agentName}.'
    agents: [
      {
        agentName: agentName
      }
    ]
    authorizationPolicy: {
      type: 'Default'
    }
  }
}]

resource promptApplications 'Microsoft.CognitiveServices/accounts/projects/applications@2026-09-15-preview' = [for agentName in promptAgentNames: if (createApplications) {
  parent: account::project
  name: agentName
  properties: {
    displayName: agentName
    description: 'Compass prompt agent ${agentName} (Foundry IQ knowledge base).'
    agents: [
      {
        agentName: agentName
      }
    ]
    authorizationPolicy: {
      type: 'Default'
    }
  }
}]

resource deployments 'Microsoft.CognitiveServices/accounts/projects/applications/agentDeployments@2026-09-15-preview' = [for (agentName, i) in agentNames: if (createApplications && createDeployments && contains(agentVersions, agentName)) {
  parent: applications[i]
  name: 'default'
  properties: {
    deploymentType: 'Hosted'
    displayName: '${agentName} (hosted)'
    agents: [
      {
        agentName: agentName
        agentVersion: string(agentVersions[agentName])
      }
    ]
    protocols: [
      {
        protocol: protocol
        version: protocolVersion
      }
    ]
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    state: 'Running'
  }
}]

output applicationsCreated bool = createApplications
output deploymentsCreated bool = createApplications && createDeployments
