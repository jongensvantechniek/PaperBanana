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
/// Retriever Agent - selects relevant reference examples. Port of
/// <c>agents/retriever_agent.py</c>.
/// </summary>
public sealed class RetrieverAgent : BaseAgent
{
    private readonly bool _isPlot;
    private readonly int? _refLimit;
    private readonly string _taskName;
    private readonly string[] _targetLabels;
    private readonly string[] _candidateLabels;
    private readonly string _candidateType;
    private readonly string _instructionSuffix;

    public RetrieverAgent(ExpConfig expConfig, GenerationClient generation)
        : base(expConfig, generation)
    {
        ModelName = expConfig.ModelName;
        _isPlot = expConfig.TaskName == "plot";

        if (_isPlot)
        {
            SystemPrompt = AgentPrompts.PlotRetriever;
            _taskName = "plot";
            _refLimit = null;
            _targetLabels = new[] { "Visual Intent", "Raw Data" };
            _candidateLabels = new[] { "Plot ID", "Visual Intent", "Raw Data" };
            _candidateType = "Plot";
            _instructionSuffix =
                "select the Top 10 most relevant plots according to the instructions provided. Your output " +
                "should be a strictly valid JSON object containing a single list of the exact ids of the top 10 selected plots.";
        }
        else
        {
            SystemPrompt = AgentPrompts.DiagramRetriever;
            _taskName = "diagram";
            _refLimit = 200;
            _targetLabels = new[] { "Caption", "Methodology section" };
            _candidateLabels = new[] { "Diagram ID", "Caption", "Methodology section" };
            _candidateType = "Diagram";
            _instructionSuffix =
                "select the Top 10 most relevant diagrams according to the instructions provided. Your output " +
                "should be a strictly valid JSON object containing a single list of the exact ids of the top 10 selected diagrams.";
        }
    }

    public override Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default) =>
        ProcessAsync(data, ExpConfig.RetrievalSetting, cancellationToken);

    public async Task<QueryData> ProcessAsync(
        QueryData data, string retrievalSetting, CancellationToken cancellationToken = default)
    {
        if ((retrievalSetting is "auto" or "random") && !File.Exists(RefFilePath(_taskName)))
        {
            Console.WriteLine($"Warning: Reference file not found at {RefFilePath(_taskName)}. " +
                              "Falling back to retrieval_setting='none'.");
            retrievalSetting = "none";
        }

        if (retrievalSetting == "manual" && !File.Exists(ManualRefFilePath(_taskName)))
        {
            Console.WriteLine($"Warning: Manual reference file not found at {ManualRefFilePath(_taskName)}. " +
                              "Falling back to retrieval_setting='none'.");
            retrievalSetting = "none";
        }

        switch (retrievalSetting)
        {
            case "none":
                data["top10_references"] = new List<object?>();
                data["retrieved_examples"] = new List<object?>();
                break;

            case "manual":
                var (ids, examples) = LoadManualReferences();
                data["top10_references"] = ids.Cast<object?>().ToList();
                data["retrieved_examples"] = examples.Select(RefItemToDict).Cast<object?>().ToList();
                break;

            case "random":
                data["top10_references"] = LoadRandomReferences().Cast<object?>().ToList();
                data["retrieved_examples"] = new List<object?>();
                break;

            case "auto":
                data["top10_references"] = (await RetrieveAndParseAsync(data, cancellationToken)).Cast<object?>().ToList();
                data["retrieved_examples"] = new List<object?>();
                break;

            default:
                throw new ArgumentException($"Unknown retrieval_setting: {retrievalSetting}");
        }

        return data;
    }

    private (List<string> Ids, List<RefItem> Examples) LoadManualReferences()
    {
        if (_taskName == "diagram")
        {
            var examples = LoadRefItems(ManualRefFilePath("diagram")).Take(10).ToList();
            return (examples.Select(e => e.Id).ToList(), examples);
        }

        // Plot manual mode not yet prepared.
        return (new List<string>(), new List<RefItem>());
    }

    private List<string> LoadRandomReferences()
    {
        var pool = LoadRefItems(RefFilePath(_taskName));
        var ids = pool.Select(p => p.Id).ToList();
        int sampleSize = Math.Min(10, ids.Count);
        if (sampleSize == 0)
        {
            return new List<string>();
        }

        var random = new Random();
        return ids.OrderBy(_ => random.Next()).Take(sampleSize).ToList();
    }

    private async Task<List<string>> RetrieveAndParseAsync(QueryData data, CancellationToken cancellationToken)
    {
        var content = data.GetContentAsString("content");
        var visualIntent = data.GetString("visual_intent");

        var prompt = new System.Text.StringBuilder();
        prompt.Append($"**Target Input**\n- {_targetLabels[0]}: {visualIntent}\n- {_targetLabels[1]}: {content}\n\n**Candidate Pool**\n");

        var pool = LoadRefItems(RefFilePath(_taskName), _refLimit);
        for (int idx = 0; idx < pool.Count; idx++)
        {
            var item = pool[idx];
            prompt.Append($"Candidate {_candidateType} {idx + 1}:\n");
            prompt.Append($"- {_candidateLabels[0]}: {item.Id}\n");
            prompt.Append($"- {_candidateLabels[1]}: {item.VisualIntent}\n");
            prompt.Append($"- {_candidateLabels[2]}: {item.ContentString}\n\n");
        }

        prompt.Append($"Now, based on the Target Input and the Candidate Pool, {_instructionSuffix}");

        var contents = new List<ContentPart> { ContentPart.FromText(prompt.ToString()) };
        var responses = await Generation.CallGeminiWithRetryAsync(
            ModelName,
            contents,
            new GenerationOptions
            {
                SystemPrompt = SystemPrompt,
                Temperature = ExpConfig.Temperature,
                CandidateCount = 1,
                MaxOutputTokens = 50000,
            },
            maxAttempts: 5,
            retryDelaySeconds: 30,
            cancellationToken: cancellationToken);

        return ParseRetrievalResult(responses[0].Trim());
    }

    private List<string> ParseRetrievalResult(string raw)
    {
        var node = JsonRepair.TryParse(raw);
        if (node is null)
        {
            Console.WriteLine("Warning: Failed to parse retrieval result.");
            return new List<string>();
        }

        var field = _taskName == "plot" ? "top10_plots" : "top10_diagrams";
        return JsonRepair.GetStringList(node, field);
    }

    private static Dictionary<string, object?> RefItemToDict(RefItem item) => new()
    {
        ["id"] = item.Id,
        ["visual_intent"] = item.VisualIntent,
        ["content"] = item.ContentString,
        ["path_to_gt_image"] = item.PathToGtImage,
    };
}
