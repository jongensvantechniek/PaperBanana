// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Models;
using PaperBanana.Core.Utils;

namespace PaperBanana.Core.Agents;

/// <summary>
/// Critic Agent - inspects the generated image and revises the description.
/// Port of <c>agents/critic_agent.py</c>.
/// </summary>
public sealed class CriticAgent : BaseAgent
{
    private readonly string _taskName;
    private readonly string _critiqueTarget;
    private readonly string[] _contextLabels;

    public CriticAgent(ExpConfig expConfig, GenerationClient generation)
        : base(expConfig, generation)
    {
        ModelName = expConfig.ModelName;

        if (expConfig.TaskName == "plot")
        {
            SystemPrompt = AgentPrompts.PlotCritic;
            _taskName = "plot";
            _critiqueTarget = "Target Plot for Critique:";
            _contextLabels = new[] { "Raw Data", "Visual Intent" };
        }
        else
        {
            SystemPrompt = AgentPrompts.DiagramCritic;
            _taskName = "diagram";
            _critiqueTarget = "Target Diagram for Critique:";
            _contextLabels = new[] { "Methodology Section", "Figure Caption" };
        }
    }

    public override Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default) =>
        ProcessAsync(data, "stylist", cancellationToken);

    public async Task<QueryData> ProcessAsync(QueryData data, string source, CancellationToken cancellationToken = default)
    {
        int roundIdx = data.GetInt("current_critic_round", 0);

        string descKey;
        string base64Key;
        if (roundIdx == 0)
        {
            switch (source)
            {
                case "stylist":
                    descKey = $"target_{_taskName}_stylist_desc0";
                    base64Key = $"target_{_taskName}_stylist_desc0_base64_jpg";
                    break;
                case "planner":
                    descKey = $"target_{_taskName}_desc0";
                    base64Key = $"target_{_taskName}_desc0_base64_jpg";
                    break;
                default:
                    throw new ArgumentException($"Invalid source '{source}'. Must be 'stylist' or 'planner'.");
            }
        }
        else
        {
            descKey = $"target_{_taskName}_critic_desc{roundIdx - 1}";
            base64Key = $"target_{_taskName}_critic_desc{roundIdx - 1}_base64_jpg";
        }

        var detailedDescription = data.GetString(descKey);
        var imageBase64 = data.GetString(base64Key);

        var content = data.GetContentAsString("content");
        var visualIntent = data.GetString("visual_intent");

        var contentList = new List<ContentPart> { ContentPart.FromText(_critiqueTarget) };

        if (!string.IsNullOrEmpty(imageBase64) && imageBase64.Length > 100)
        {
            contentList.Add(ContentPart.FromImage(imageBase64, "image/jpeg"));
        }
        else
        {
            Console.WriteLine($"⚠️ [Critic] No valid image found for round {roundIdx}. Using text-only critique mode.");
            contentList.Add(ContentPart.FromText(
                "\n[SYSTEM NOTICE] The plot image could not be generated based on the current description " +
                "(likely due to invalid code). Please check the description for errors (e.g., syntax issues, " +
                "missing data) and provide a revised version."));
        }

        contentList.Add(ContentPart.FromText(
            $"Detailed Description: {detailedDescription}\n{_contextLabels[0]}: {content}\n" +
            $"{_contextLabels[1]}: {visualIntent}\nYour Output:"));

        var responses = await Generation.CallGeminiWithRetryAsync(
            ModelName,
            contentList,
            new GenerationOptions
            {
                SystemPrompt = SystemPrompt,
                Temperature = ExpConfig.Temperature,
                CandidateCount = 1,
                MaxOutputTokens = 50000,
            },
            maxAttempts: 5,
            retryDelaySeconds: 5,
            cancellationToken: cancellationToken);

        var node = JsonRepair.TryParse(responses[0]);

        string criticSuggestions = node?["critic_suggestions"]?.ToString() ?? "No changes needed.";
        string revisedDescription = node?["revised_description"]?.ToString() ?? "No changes needed.";

        data[$"target_{_taskName}_critic_suggestions{roundIdx}"] = criticSuggestions;
        data[$"target_{_taskName}_critic_desc{roundIdx}"] = revisedDescription;

        if (revisedDescription.Trim() == "No changes needed.")
        {
            data[$"target_{_taskName}_critic_desc{roundIdx}"] = detailedDescription;
        }

        return data;
    }
}
