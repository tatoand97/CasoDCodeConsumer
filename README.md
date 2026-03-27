# CasoDCodeConsumer

`CasoDCodeConsumer` is a .NET 8 ASP.NET Core API for pure agent consumption over HTTP. Routing and final response composition stay code-first in C#, while Foundry is used only as the runtime for agents that already exist.

This repo does not create agents, does not reconcile agents, does not publish new versions, and does not perform bootstrap or IaC work. Another repo must prepare the agents first.

## What this API does

- Exposes `POST /orchestrate`
- Exposes `GET /health`
- Validates configuration at startup
- Validates the Foundry project endpoint and project access
- Validates that `OrderAgentId`, `RefundAgentId`, and `ClarifierAgentId` exist and are accessible
- Stores the resolved agent id, name, and version in memory
- Routes each request in code to `Order`, `Refund`, `Clarify`, or `Reject`
- Invokes only the required specialist agent
- Validates structured outputs and builds the final HTTP response in C#

## What this API does not do

- It does not create `RefundAgent`
- It does not create `ClarifierAgent`
- It does not reconcile any agent
- It does not modify Foundry infrastructure
- It does not introduce Workflows, `ManagerAgent`, or app-layer agent tools

## Prerequisite

Run the equivalent bootstrap repo first so that these agents already exist in Foundry and are accessible to this API:

- `OrderAgent`
- `RefundAgent`
- `ClarifierAgent`

`RefundAgent` and `ClarifierAgent` must already be prepared before this API starts.

The configured values for `OrderAgentId`, `RefundAgentId`, and `ClarifierAgentId` must be Foundry agent version IDs produced by that bootstrap flow.

## Code-first runtime

- `IntentRouter` decides the route in code
- `Program.cs` selects the execution branch
- `AgentRunner` invokes the chosen agent
- `OutputValidators` enforce strict JSON contracts
- Final response composition happens in C#

Foundry does not decide the global orchestration flow.

## Configuration

Configure `appsettings.json` like this:

```json
{
  "CasoDCodeConsumer": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "<order-agent-version-id>",
    "RefundAgentId": "<refund-agent-version-id>",
    "ClarifierAgentId": "<clarifier-agent-version-id>",
    "ResponsesTimeoutSeconds": 60,
    "ResponsesMaxBackoffSeconds": 8
  }
}
```

Requirements:

- `ProjectEndpoint` must be HTTPS and contain `/api/projects/`
- `ModelDeploymentName` is required
- `OrderAgentId`, `RefundAgentId`, and `ClarifierAgentId` are required
- Each agent reference must be an existing Foundry agent version ID
- Timeout values must be greater than zero

## Endpoints

### `POST /orchestrate`

Request:

```json
{
  "prompt": "Where is order ORD-000123?"
}
```

Successful response example:

```json
{
  "prompt": "Where is order ORD-000123?",
  "route": "Order",
  "response": "Order ORD-000123 has been shipped.",
  "orderId": "ORD-000123",
  "refundReason": null,
  "reason": "Order status intent detected with an order reference."
}
```

### `GET /health`

Response:

```json
{
  "status": "ok",
  "orderAgent": { "id": "...", "name": "...", "version": "..." },
  "refundAgent": { "id": "...", "name": "...", "version": "..." },
  "clarifierAgent": { "id": "...", "name": "...", "version": "..." }
}
```

The health status reflects validated external dependencies. It does not create or reconcile anything.

## Running locally

Start the API:

```bash
dotnet run
```

Sample request:

```bash
curl -X POST http://localhost:5183/orchestrate \
  -H "Content-Type: application/json" \
  -d '{"prompt":"Where is order ORD-000123?"}'
```

## Example prompts

- `Where is order ORD-000123?`
- `I want a refund for order ORD-000123 because it arrived damaged.`
- `Can you help with my order?`
- `Delete all orders.`

## Operational notes

- Startup logs report bootstrap validation start and completion
- Startup logs confirm order, refund, and clarifier agent validation against existing Foundry agents
- Runtime logs report request receipt, routing completion, and request failure
- Agent output is validated strictly with `System.Text.Json`
- Startup does not create agents, reconcile agents, or publish new versions
