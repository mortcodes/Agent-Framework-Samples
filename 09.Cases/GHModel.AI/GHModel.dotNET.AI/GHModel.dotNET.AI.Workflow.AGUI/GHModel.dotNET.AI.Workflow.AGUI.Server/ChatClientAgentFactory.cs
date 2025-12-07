
using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using GHModel.dotNET.AI.Workflow.AGUI.Server;

using Microsoft.Agents.AI;  
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.HttpLogging;
using OpenAI;
using DotNetEnv;


internal static class ChatClientAgentFactory
{
    private static OpenAIClient? _openAIClient;
    private static string? github_model_id;

    public static void Initialize()
    {

        Env.Load(".env");
        string github_endpoint = Environment.GetEnvironmentVariable("GITHUB_ENDPOINT") ?? throw new InvalidOperationException("GITHUB_ENDPOINT is not set.");
        github_model_id =  Environment.GetEnvironmentVariable("GITHUB_MODEL_ID") ?? throw new InvalidOperationException("GITHUB_MODEL_ID is not set.");
        string github_token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? throw new InvalidOperationException("GITHUB_TOKEN is not set.");


        var openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(github_endpoint)
        };
                
        _openAIClient = new OpenAIClient(new ApiKeyCredential(github_token), openAIOptions);
    }
    public static AIAgent CreateTravelAgenticChat()
    {
        var chatClient = _openAIClient!.GetChatClient(github_model_id!).AsIChatClient();


                string ArchitectureAgentName = "SolutionArchitect";
                string ArchitectureAgentInstructions = """
                        You are the principal software solution architect for the studio.
                        Your output must always be a Markdown specification that a downstream software development agent can implement directly.
                        For every concept you receive:
                            1. Start with an **Executive Summary** section describing the gameplay objective and KPIs.
                            2. Add **System Diagram Notes** that list required services (simulation, networking, persistence, telemetry, tooling) and explicit integration points.
                            3. Provide an **API & Data Contracts** table, including method signatures, payload schemas, and reliability/scalability callouts.
                            4. Document an **Event & Sequence Flow** section in ordered steps so engineers understand runtime orchestration.
                            5. Close with **Cross-Cutting Concerns** covering save/load, localization, accessibility, live ops instrumentation, testing hooks, and open risks.
                        Use concise Markdown headings, bullet lists, and tables so another agent can copy/paste the spec into a ticket.
                        Reject incomplete upstream proposals by adding a "Blocked" subsection that lists missing inputs.
                        """;

                string GameDesignerAgentName = "GameDesigner";
                string GameDesignerAgentInstructions = """
                        You are the lead systems designer on a premium hex-based strategy game.
                        Every game concept must be transformed into a compelling in-game feature, quest hook, or progression beat.
                        Collaborate with the SolutionArchitect by:
                            • Identifying the core player fantasy and the gameplay problem it solves.
                            • Defining the mechanics: victory conditions, resource flows, hazards, or unit abilities tied to the concept.
                            • Highlighting UX moments that make the encounter memorable (camera cues, UI prompts, feedback effects).
                            • Suggesting how the feature scales across early, mid, and late game without overwhelming the player.
                        Respond crisply with numbered design beats so downstream artists and engineers can implement without guesswork.
                        """;

        // Create the Flux image generation tool using the web API
        var fluxImageTool = new FluxImageGenerationTool();
        
        var imageAgent = chatClient.CreateAIAgent(
            name: "HexForge",
            instructions: """
                You are HexForge, the concept artist responsible for every visual asset in a premium hex-based strategy game.
                When the concierge approves a travel experience, reinterpret it as in-world content (terrain tile, hero token, structure, spell effect, or UI overlay) that could appear on a hex map.
                Always call GenerateImageAsync with:
                  • a concise gameplay brief that ties the travel idea to the game's lore,
                  • the correct assetType (TerrainTile, CharacterSprite, BuiltFeature, SpellEffect, UiOverlay),
                  • background selection (Transparent for sprites/overlays, FlatAlbedo for marketing plates, AtmosphericPlate only when context is needed),
                  • explicit pixel dimensions (multiples of 64 between 256 and 1024) that match the asset's purpose,
                  • optional styleGuide notes for palette or faction motifs.
                Return only a short confirmation with the path to the rendered asset—no additional prose.
                """,
            tools: [fluxImageTool.GetTool()]);
        

        AIAgent architectureAgent = chatClient.CreateAIAgent(
            name:ArchitectureAgentName,instructions:ArchitectureAgentInstructions);
        AIAgent gameDesignerAgent  = chatClient.CreateAIAgent(
            name:GameDesignerAgentName,instructions:GameDesignerAgentInstructions);

        var workflow = new WorkflowBuilder(gameDesignerAgent)
                    .AddEdge(gameDesignerAgent, architectureAgent)
                    .AddEdge(architectureAgent, imageAgent)
                    .Build();


        AIAgent workflow_agent = workflow.AsAgent("game-design-workflow","game design workflow");

        return workflow_agent;
    }
    // public static AIAgent CreateSharedState(JsonSerializerOptions options)
    // {
    //     var chatClient = _openAIClient!.GetChatClient(s_deploymentName!).AsIChatClient();

    //     var baseAgent = chatClient.CreateAIAgent(
    //         name: "SharedStateAgent",
    //         description: "An agent that demonstrates shared state patterns using Azure OpenAI");

    //     return new SharedStateAgent(baseAgent, options);
    // }`
}