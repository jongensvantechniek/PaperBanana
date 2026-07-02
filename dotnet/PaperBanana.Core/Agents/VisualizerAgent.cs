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
/// Visualizer Agent - renders images (diagram) or matplotlib code (plot). Port
/// of <c>agents/visualizer_agent.py</c>. Plot code is executed by the Python
/// sidecar via <see cref="PlotExecutorClient"/>.
/// </summary>
public sealed class VisualizerAgent : BaseAgent
{
    private readonly PlotExecutorClient _plotExecutor;
    private readonly string _taskName;
    private readonly bool _useImageGeneration;
    private readonly string _promptTemplate;
    private readonly int _maxOutputTokens;

    public VisualizerAgent(ExpConfig expConfig, GenerationClient generation, PlotExecutorClient plotExecutor)
        : base(expConfig, generation)
    {
        _plotExecutor = plotExecutor;

        if (expConfig.TaskName.Contains("plot"))
        {
            ModelName = expConfig.ModelName;
            SystemPrompt = AgentPrompts.PlotVisualizer;
            _taskName = "plot";
            _useImageGeneration = false;
            _promptTemplate =
                "Use python matplotlib to generate a statistical plot based on the following detailed description: {0}\n " +
                "Only provide the code without any explanations. Code:";
            _maxOutputTokens = 50000;
        }
        else
        {
            ModelName = expConfig.ImageModelName;
            SystemPrompt = AgentPrompts.DiagramVisualizer;
            _taskName = "diagram";
            _useImageGeneration = true;
            _promptTemplate =
                "Render an image based on the following detailed description: {0}\n " +
                "Note that do not include figure titles in the image. Diagram: ";
            _maxOutputTokens = 50000;
        }
    }

    public override async Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default)
    {
        var descKeysToProcess = new List<string>();

        foreach (var key in new[] { $"target_{_taskName}_desc0", $"target_{_taskName}_stylist_desc0" })
        {
            if (data.ContainsKey(key) && !data.ContainsKey($"{key}_base64_jpg"))
            {
                descKeysToProcess.Add(key);
            }
        }

        for (int roundIdx = 0; roundIdx < 3; roundIdx++)
        {
            var key = $"target_{_taskName}_critic_desc{roundIdx}";
            if (!data.ContainsKey(key) || data.ContainsKey($"{key}_base64_jpg"))
            {
                continue;
            }

            var criticSuggestions = data.GetString($"target_{_taskName}_critic_suggestions{roundIdx}");
            if (criticSuggestions.Trim() == "No changes needed." && roundIdx > 0)
            {
                var prevKey = $"target_{_taskName}_critic_desc{roundIdx - 1}_base64_jpg";
                if (data.ContainsKey(prevKey))
                {
                    data[$"{key}_base64_jpg"] = data[prevKey];
                    Console.WriteLine($"[Visualizer] Reused base64 from round {roundIdx - 1} for {key}");
                    continue;
                }
            }

            descKeysToProcess.Add(key);
        }

        foreach (var descKey in descKeysToProcess)
        {
            var promptText = string.Format(_promptTemplate, data.GetString(descKey));
            var contentList = new List<ContentPart> { ContentPart.FromText(promptText) };

            var aspectRatio = "1:1";
            if (_useImageGeneration && ModelName.Contains("gemini"))
            {
                aspectRatio = data.GetAdditionalInfo("rounded_ratio") ?? "1:1";
            }

            List<string> responses;
            if (ModelName.Contains("gemini"))
            {
                responses = await Generation.CallGeminiWithRetryAsync(
                    ModelName,
                    contentList,
                    new GenerationOptions
                    {
                        SystemPrompt = SystemPrompt,
                        Temperature = ExpConfig.Temperature,
                        CandidateCount = 1,
                        MaxOutputTokens = _maxOutputTokens,
                        GenerateImage = _useImageGeneration,
                        AspectRatio = _useImageGeneration ? aspectRatio : null,
                        ImageSize = "1k",
                    },
                    maxAttempts: 5,
                    retryDelaySeconds: 30,
                    cancellationToken: cancellationToken);
            }
            else if (ModelName.Contains("gpt-image"))
            {
                responses = await Generation.CallOpenAiImageGenerationWithRetryAsync(
                    ModelName,
                    promptText,
                    new OpenAiImageOptions
                    {
                        Size = "1536x1024",
                        Quality = "high",
                        Background = "opaque",
                        OutputFormat = "png",
                    },
                    maxAttempts: 5,
                    retryDelaySeconds: 30,
                    cancellationToken: cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported model: {ModelName}");
            }

            if (responses.Count == 0 || string.IsNullOrEmpty(responses[0]))
            {
                continue;
            }

            if (_useImageGeneration)
            {
                var convertedJpg = await Task.Run(() => ImageUtils.ConvertPngB64ToJpgB64(responses[0]), cancellationToken);
                if (convertedJpg is not null)
                {
                    data[$"{descKey}_base64_jpg"] = convertedJpg;
                }
                else
                {
                    Console.WriteLine($"⚠️  Skipping {descKey}: image conversion failed");
                }
            }
            else
            {
                var rawCode = responses[0];
                var base64Jpg = await _plotExecutor.ExecuteAsync(rawCode, cancellationToken);
                data[$"{descKey}_code"] = rawCode;
                if (base64Jpg is not null)
                {
                    data[$"{descKey}_base64_jpg"] = base64Jpg;
                }
            }
        }

        return data;
    }
}
