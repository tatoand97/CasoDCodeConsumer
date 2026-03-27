using Azure.AI.Projects.Agents;

namespace CasoDCodeConsumer.Agents;

public static class ClarifierAgentFactory
{
    public static PromptAgentDefinition Create(string modelDeploymentName)
    {
        return new PromptAgentDefinition(modelDeploymentName)
        {
            Instructions =
                """
                You are ClarifierAgent.
                You receive a short summary of missing information.
                Return exactly one JSON object and nothing else:
                {"question":"single clear clarification question"}
                Ask only one concise question.
                Do not mention tools, systems, workflows, MCP, backend, or internal routing.
                """
        };
    }
}
