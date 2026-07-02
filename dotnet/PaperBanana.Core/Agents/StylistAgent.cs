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

namespace PaperBanana.Core.Agents;

/// <summary>
/// Stylist Agent - enriches the description with aesthetic guidelines. Port of
/// <c>agents/stylist_agent.py</c>.
/// </summary>
public sealed class StylistAgent : BaseAgent
{
    private readonly string _taskName;
    private readonly string[] _contextLabels;

    public StylistAgent(ExpConfig expConfig, GenerationClient generation)
        : base(expConfig, generation)
    {
        ModelName = expConfig.ModelName;

        if (expConfig.TaskName == "plot")
        {
            SystemPrompt = AgentPrompts.PlotStylist;
            _taskName = "plot";
            _contextLabels = new[] { "Raw Data", "Visual Intent of the Desired Plot" };
        }
        else
        {
            SystemPrompt = AgentPrompts.DiagramStylist;
            _taskName = "diagram";
            _contextLabels = new[] { "Methodology Section", "Diagram Caption" };
        }
    }

    public override async Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default)
    {
        var inputDescKey = $"target_{_taskName}_desc0";
        var outputDescKey = $"target_{_taskName}_stylist_desc0";

        var detailedDescription = data.GetString(inputDescKey);
        var styleGuide = ReadStyleGuide(_taskName);

        var userPrompt = $"Detailed Description: {detailedDescription}\nStyle Guidelines: {styleGuide}\n" +
                         $"{_contextLabels[0]}: {data.GetContentAsString("content")}\n" +
                         $"{_contextLabels[1]}: {data.GetString("visual_intent")}\nYour Output:";

        var contentList = new List<ContentPart> { ContentPart.FromText(userPrompt) };

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

        data[outputDescKey] = responses[0];
        return data;
    }
}
