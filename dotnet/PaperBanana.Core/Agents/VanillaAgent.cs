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
/// Vanilla Agent - direct generation without planning/refinement. Port of
/// <c>agents/vanilla_agent.py</c>.
/// </summary>
public sealed class VanillaAgent : BaseAgent
{
    private readonly PlotExecutorClient _plotExecutor;
    private readonly string _taskName;
    private readonly bool _useImageGeneration;
    private readonly string _contentLabel;
    private readonly string _visualIntentLabel;

    public VanillaAgent(ExpConfig expConfig, GenerationClient generation, PlotExecutorClient plotExecutor)
        : base(expConfig, generation)
    {
        _plotExecutor = plotExecutor;

        if (expConfig.TaskName.Contains("plot"))
        {
            ModelName = expConfig.ModelName;
            SystemPrompt = AgentPrompts.PlotVanilla;
            _taskName = "plot";
            _useImageGeneration = false;
            _contentLabel = "Plot Raw Data";
            _visualIntentLabel = "Visual Intent of the Desired Plot";
        }
        else
        {
            ModelName = expConfig.ImageModelName;
            SystemPrompt = AgentPrompts.DiagramVanilla;
            _taskName = "diagram";
            _useImageGeneration = true;
            _contentLabel = "Method Section";
            _visualIntentLabel = "Diagram Caption";
        }
    }

    public override async Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default)
    {
        var content = data.GetContentAsString("content");
        var visualIntent = data.GetString("visual_intent");

        var promptText = $"**{_contentLabel}**: {content}\n**{_visualIntentLabel}**: {visualIntent}\n";
        if (_taskName == "diagram")
        {
            promptText += "Note that do not include figure titles in the image.";
        }

        promptText += _useImageGeneration
            ? "**Generated Diagram**: "
            : "\nUse python matplotlib to generate a statistical plot based on the above information. " +
              "Only provide the code without any explanations. Code:";

        var contentList = new List<ContentPart> { ContentPart.FromText(promptText) };

        List<string> responses;
        if (ModelName.Contains("gemini"))
        {
            var aspectRatio = _useImageGeneration ? data.GetAdditionalInfo("rounded_ratio") : null;
            responses = await Generation.CallGeminiWithRetryAsync(
                ModelName,
                contentList,
                new GenerationOptions
                {
                    SystemPrompt = SystemPrompt,
                    Temperature = ExpConfig.Temperature,
                    CandidateCount = 1,
                    MaxOutputTokens = 50000,
                    GenerateImage = _useImageGeneration,
                    AspectRatio = aspectRatio,
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
                promptText.Length > 30000 ? promptText[..30000] : promptText,
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

        var outputKey = $"vanilla_{_taskName}_base64_jpg";
        if (responses.Count == 0 || string.IsNullOrEmpty(responses[0]))
        {
            return data;
        }

        if (_useImageGeneration)
        {
            data[outputKey] = await Task.Run(() => ImageUtils.ConvertPngB64ToJpgB64(responses[0]), cancellationToken);
        }
        else
        {
            var base64Jpg = await _plotExecutor.ExecuteAsync(responses[0], cancellationToken);
            if (base64Jpg is not null)
            {
                data[outputKey] = base64Jpg;
            }
        }

        return data;
    }
}
