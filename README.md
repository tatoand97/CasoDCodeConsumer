# CasoDCodeConsumer

`CasoDCodeConsumer` es una PoC ASP.NET API en .NET 8 que implementa un orchestrator code-first en C#. La lógica de routing y composición vive en código, mientras que Microsoft Foundry se usa sólo como runtime de agentes.

Este proyecto no usa Workflows, no usa `ManagerAgent`, no serializa agent tools en la app y no mezcla bootstrap de infraestructura con lógica innecesaria. `OrderAgent` es externo y se valida por id versionado. `RefundAgent` y `ClarifierAgent` se crean o reconcilian localmente por nombre.

## Qué hace

- Expone `POST /orchestrate`
- Recibe `{ "prompt": "..." }`
- Valida configuración y acceso al proyecto Foundry al arrancar
- Valida `OrderAgentId`
- Reconcilia `refund-agent-casedcodeconsumer`
- Reconcilia `clarifier-agent-casedcodeconsumer`
- Enruta en código a `Order`, `Refund`, `Clarify` o `Reject`
- Invoca sólo el especialista necesario
- Valida JSON de salida y construye la respuesta final en C#

## Code-First

En este proyecto, code-first significa:

- El router está implementado en `IntentRouter`
- La selección de rama ocurre en `Program.cs`
- La composición final del mensaje ocurre en C#
- Foundry no decide el flujo general de orquestación

## Runtime de agentes

- `OrderAgent` es externo y se resuelve con `OrderAgentId` en formato `AgentName:Version`
- `RefundAgent` es un prompt agent sin tools
- `ClarifierAgent` es un prompt agent sin tools
- Foundry actúa como runtime administrado para ejecutar esos agentes

## Configuración

Configura `appsettings.json` con esta forma:

```json
{
  "CasoDCodeConsumer": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "OrderAgent:5",
    "ResponsesTimeoutSeconds": 60,
    "ResponsesMaxBackoffSeconds": 8,
    "DefaultPrompt": "Where is order ORD-000123?"
  }
}
```

Requisitos principales:

- `ProjectEndpoint` debe ser HTTPS y contener `/api/projects/`
- `ModelDeploymentName` es obligatorio
- `OrderAgentId` es obligatorio
- Los timeouts deben ser mayores a cero

## Ejecución

Inicia la API:

```bash
dotnet run
```

Ejecuta una solicitud:

```bash
curl -X POST http://localhost:5183/orchestrate ^
  -H "Content-Type: application/json" ^
  -d "{\"prompt\":\"Where is order ORD-000123?\"}"
```

La respuesta exitosa tiene esta forma:

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

## Comparación

| Enfoque | Workflow-first | Code-first |
| --- | --- | --- |
| Routing | Vive en workflow | Vive en C# |
| Control de flujo | Declarativo/orquestado por runtime | Explícito en `Program.cs` |
| Dependencia de herramientas Foundry | Alta | Acotada al runtime de agentes |
| Debugging | Más distribuido | Más directo en código |
| PoC de consumo | Menos enfocada | Más enfocada |

## Prompts de ejemplo

- `Where is order ORD-000123?`
- `I want a refund for order ORD-000123 because it arrived damaged.`
- `Can you help with my order?`
- `Delete all orders.`

## Notas operativas

- Las trazas de consola usan prefijos deterministas: `[CONFIG]`, `[VALIDATION]`, `[RECONCILE]`, `[ROUTER]`, `[AGENT]`, `[FINAL]`
- La app falla en startup si la configuración o el `OrderAgentId` son inválidos
- La salida de agentes se valida estrictamente con `System.Text.Json`
