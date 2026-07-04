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

using System.Runtime.CompilerServices;
using PaperBanana.Core.Agents;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Evaluation;
using PaperBanana.Core.Models;

namespace PaperBanana.Core.Pipeline;

/// <summary>
/// Main processing pipeline. Port of <c>utils/paperviz_processor.py</c>. Wires
/// the agents together and drives each supported experiment mode, with
/// concurrent batch processing that streams results as they complete.
/// </summary>
public sealed class PaperVizProcessor
{
    private readonly ExpConfig _expConfig;
    private readonly VanillaAgent _vanilla;
    private readonly PlannerAgent _planner;
    private readonly VisualizerAgent _visualizer;
    private readonly StylistAgent _stylist;
    private readonly CriticAgent _critic;
    private readonly RetrieverAgent _retriever;
    private readonly PolishAgent _polish;
    private readonly EvalToolkit _evalToolkit;

    public PaperVizProcessor(
        ExpConfig expConfig,
        VanillaAgent vanilla,
        PlannerAgent planner,
        VisualizerAgent visualizer,
        StylistAgent stylist,
        CriticAgent critic,
        RetrieverAgent retriever,
        PolishAgent polish,
        EvalToolkit evalToolkit)
    {
        _expConfig = expConfig;
        _vanilla = vanilla;
        _planner = planner;
        _visualizer = visualizer;
        _stylist = stylist;
        _critic = critic;
        _retriever = retriever;
        _polish = polish;
        _evalToolkit = evalToolkit;
    }

    public async Task<QueryData> ProcessSingleQueryAsync(
        QueryData data, bool doEval = true, CancellationToken cancellationToken = default)
    {
        var expMode = _expConfig.ExpMode;
        var taskName = _expConfig.TaskName.ToLowerInvariant();
        var retrievalSetting = _expConfig.RetrievalSetting;

        switch (expMode)
        {
            case "vanilla":
                data = await _vanilla.ProcessAsync(data, cancellationToken);
                data["eval_image_field"] = $"vanilla_{taskName}_base64_jpg";
                break;

            case "dev_planner":
                data = await _retriever.ProcessAsync(data, retrievalSetting, cancellationToken);
                data = await _planner.ProcessAsync(data, cancellationToken);
                data = await _visualizer.ProcessAsync(data, cancellationToken);
                data["eval_image_field"] = $"target_{taskName}_desc0_base64_jpg";
                break;

            case "dev_planner_stylist":
                data = await _retriever.ProcessAsync(data, retrievalSetting, cancellationToken);
                data = await _planner.ProcessAsync(data, cancellationToken);
                data = await _stylist.ProcessAsync(data, cancellationToken);
                data = await _visualizer.ProcessAsync(data, cancellationToken);
                data["eval_image_field"] = $"target_{taskName}_stylist_desc0_base64_jpg";
                break;

            case "dev_planner_critic":
            case "demo_planner_critic":
                data = await _retriever.ProcessAsync(data, retrievalSetting, cancellationToken);
                data = await _planner.ProcessAsync(data, cancellationToken);
                data = await _visualizer.ProcessAsync(data, cancellationToken);
                data = await RunCriticIterationsAsync(
                    data, taskName, data.GetInt("max_critic_rounds", 3), source: "planner", cancellationToken);
                if (expMode.Contains("demo"))
                {
                    doEval = false;
                }

                break;

            case "dev_full":
            case "demo_full":
                data = await _retriever.ProcessAsync(data, retrievalSetting, cancellationToken);
                data = await _planner.ProcessAsync(data, cancellationToken);
                data = await _stylist.ProcessAsync(data, cancellationToken);
                data = await _visualizer.ProcessAsync(data, cancellationToken);
                data = await RunCriticIterationsAsync(
                    data, taskName, data.GetInt("max_critic_rounds", _expConfig.MaxCriticRounds), source: "stylist", cancellationToken);
                if (expMode.Contains("demo"))
                {
                    doEval = false;
                }

                break;

            case "dev_polish":
                data = await _polish.ProcessAsync(data, cancellationToken);
                data["eval_image_field"] = $"polished_{taskName}_base64_jpg";
                break;

            case "dev_retriever":
                data = await _retriever.ProcessAsync(data, retrievalSetting, cancellationToken);
                doEval = false;
                break;

            default:
                throw new ArgumentException($"Unknown experiment name: {expMode}");
        }

        if (doEval)
        {
            data = await _evalToolkit.GetScoreForImageReferencedAsync(
                data, _expConfig.TaskName, _expConfig.WorkDir, _expConfig.ModelName, cancellationToken);
        }

        return data;
    }

    private async Task<QueryData> RunCriticIterationsAsync(
        QueryData data, string taskName, int maxRounds, string source, CancellationToken cancellationToken)
    {
        var currentBestImageKey = source == "planner"
            ? $"target_{taskName}_desc0_base64_jpg"
            : $"target_{taskName}_stylist_desc0_base64_jpg";

        for (int roundIdx = 0; roundIdx < maxRounds; roundIdx++)
        {
            data["current_critic_round"] = roundIdx;
            data = await _critic.ProcessAsync(data, source, cancellationToken);

            var criticSuggestions = data.GetString($"target_{taskName}_critic_suggestions{roundIdx}");
            if (criticSuggestions.Trim() == "No changes needed.")
            {
                Console.WriteLine($"[Critic Round {roundIdx}] No changes needed. Stopping iteration.");
                break;
            }

            data = await _visualizer.ProcessAsync(data, cancellationToken);

            var newImageKey = $"target_{taskName}_critic_desc{roundIdx}_base64_jpg";
            if (data.ContainsKey(newImageKey))
            {
                currentBestImageKey = newImageKey;
                Console.WriteLine($"[Critic Round {roundIdx}] Completed iteration. Visualization SUCCESS.");
            }
            else
            {
                Console.WriteLine($"[Critic Round {roundIdx}] Visualization FAILED. Rolling back to {currentBestImageKey}");
                break;
            }
        }

        data["eval_image_field"] = currentBestImageKey;
        return data;
    }

    /// <summary>
    /// Process a batch of queries concurrently, yielding each result as it
    /// completes (mirrors the Python async generator with a semaphore).
    /// </summary>
    public async IAsyncEnumerable<QueryData> ProcessQueriesBatchAsync(
        IReadOnlyList<QueryData> dataList,
        int maxConcurrent = 10,
        bool doEval = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrent);

        async Task<QueryData> ProcessWithSemaphoreAsync(QueryData doc)
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessSingleQueryAsync(doc, doEval, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }

        var pending = dataList.Select(ProcessWithSemaphoreAsync).ToList();
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            yield return await finished;
        }
    }
}
