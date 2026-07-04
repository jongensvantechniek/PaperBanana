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

using System.Text;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Models;

namespace PaperBanana.Core.Agents;

/// <summary>
/// Planner Agent - turns the source content + intent into a detailed textual
/// description via in-context learning. Port of <c>agents/planner_agent.py</c>.
/// </summary>
public sealed class PlannerAgent : BaseAgent
{
    private readonly string _taskName;
    private readonly string _contentLabel;
    private readonly string _visualIntentLabel;

    public PlannerAgent(ExpConfig expConfig, GenerationClient generation)
        : base(expConfig, generation)
    {
        ModelName = expConfig.ModelName;

        if (expConfig.TaskName.Contains("plot"))
        {
            SystemPrompt = AgentPrompts.PlotPlanner;
            _taskName = "plot";
            _contentLabel = "Plot Raw Data";
            _visualIntentLabel = "Visual Intent of the Desired Plot";
        }
        else
        {
            SystemPrompt = AgentPrompts.DiagramPlanner;
            _taskName = "diagram";
            _contentLabel = "Methodology Section";
            _visualIntentLabel = "Diagram Caption";
        }
    }

    public override async Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default)
    {
        var content = data.GetContentAsString("content");
        var description = data.GetString("visual_intent");

        var examples = ResolveExamples(data);

        var contentList = new List<ContentPart>();
        var userPrompt = new StringBuilder();

        for (int idx = 0; idx < examples.Count; idx++)
        {
            var item = examples[idx];
            userPrompt.Append($"Example {idx + 1}:\n");
            userPrompt.Append($"{_contentLabel}: {item.ContentString}\n");
            userPrompt.Append($"{_visualIntentLabel}: {item.VisualIntent}\nReference {Capitalize(_taskName)}: ");
            contentList.Add(ContentPart.FromText(userPrompt.ToString()));

            var imagePath = Path.Combine(DataDir(_taskName), item.PathToGtImage);
            var refImageBase64 = ReadImageAsBase64(imagePath);
            contentList.Add(ContentPart.FromImage(refImageBase64, "image/jpeg"));
            userPrompt.Clear();
        }

        userPrompt.Append($"Now, based on the following {_contentLabel.ToLowerInvariant()} and " +
                          $"{_visualIntentLabel.ToLowerInvariant()}, provide a detailed description for the figure to be generated.\n");
        userPrompt.Append($"{_contentLabel}: {content}\n{_visualIntentLabel}: {description}\n");
        userPrompt.Append("Detailed description of the target figure to be generated");
        if (_taskName == "diagram")
        {
            userPrompt.Append(" (do not include figure titles)");
        }

        userPrompt.Append(':');
        contentList.Add(ContentPart.FromText(userPrompt.ToString()));

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

        for (int idx = 0; idx < responses.Count; idx++)
        {
            data[$"target_{_taskName}_desc{idx}"] = responses[idx].Trim();
        }

        return data;
    }

    private List<RefItem> ResolveExamples(QueryData data)
    {
        // Manual mode may already provide full examples.
        if (data["retrieved_examples"] is List<object?> provided && provided.Count > 0)
        {
            return provided.OfType<IDictionary<string, object?>>().Select(DictToRefItem).ToList();
        }

        var retrievedIds = (data["top10_references"] as List<object?>)?
            .Select(x => x?.ToString() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        if (retrievedIds.Count == 0)
        {
            return new List<RefItem>();
        }

        var pool = LoadRefItems(RefFilePath(_taskName));
        var byId = pool.ToDictionary(p => p.Id, p => p);
        return retrievedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private static RefItem DictToRefItem(IDictionary<string, object?> dict) => new(
        Id: dict.TryGetValue("id", out var id) ? id?.ToString() ?? string.Empty : string.Empty,
        VisualIntent: dict.TryGetValue("visual_intent", out var vi) ? vi?.ToString() ?? string.Empty : string.Empty,
        ContentString: dict.TryGetValue("content", out var c) ? c?.ToString() ?? string.Empty : string.Empty,
        PathToGtImage: dict.TryGetValue("path_to_gt_image", out var p) ? p?.ToString() ?? string.Empty : string.Empty);

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
