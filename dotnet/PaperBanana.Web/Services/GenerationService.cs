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

using PaperBanana.Core;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Models;

namespace PaperBanana.Web.Services;

/// <summary>Parameters for a candidate-generation run (Generate Candidates tab).</summary>
public sealed record GenerateRequest
{
    public required string MethodContent { get; init; }
    public required string Caption { get; init; }
    public string ExpMode { get; init; } = "demo_planner_critic";
    public string RetrievalSetting { get; init; } = "auto";
    public int NumCandidates { get; init; } = 10;
    public string AspectRatio { get; init; } = "16:9";
    public int MaxCriticRounds { get; init; } = 3;
    public string ModelName { get; init; } = string.Empty;
}

/// <summary>
/// Web-facing service that drives the PaperBanana pipeline. Equivalent to the
/// orchestration logic in <c>demo.py</c>.
/// </summary>
public sealed class GenerationService
{
    private readonly PaperVizFactory _factory;
    private readonly ModelConfig _modelConfig;
    private readonly GenerationClient _generation;
    private readonly PaperBananaOptions _options;

    public GenerationService(
        PaperVizFactory factory, ModelConfig modelConfig, GenerationClient generation, PaperBananaOptions options)
    {
        _factory = factory;
        _modelConfig = modelConfig;
        _generation = generation;
        _options = options;
    }

    public bool HasGoogleKey => _generation.HasGoogleKey;

    public string DefaultModelName => _modelConfig.ModelName;

    public string DefaultImageModelName => _modelConfig.ImageModelName;

    /// <summary>
    /// Generate the requested number of candidate diagrams in parallel,
    /// invoking <paramref name="onResult"/> as each one completes.
    /// </summary>
    public async Task GenerateCandidatesAsync(
        GenerateRequest request,
        Func<QueryData, int, Task> onResult,
        CancellationToken cancellationToken = default)
    {
        var expConfig = new ExpConfig
        {
            DatasetName = "Demo",
            TaskName = "diagram",
            SplitName = "demo",
            ExpMode = request.ExpMode,
            RetrievalSetting = request.RetrievalSetting,
            MaxCriticRounds = request.MaxCriticRounds,
            ModelName = request.ModelName,
            WorkDir = _options.WorkDir,
        };
        expConfig.Initialize(_modelConfig);

        var processor = _factory.CreateProcessor(expConfig);
        var inputs = BuildInputs(request);

        int completed = 0;
        await foreach (var result in processor.ProcessQueriesBatchAsync(
            inputs, maxConcurrent: request.NumCandidates, doEval: false, cancellationToken))
        {
            await onResult(result, completed++);
        }
    }

    /// <summary>
    /// Refine/upscale an uploaded image using the Gemini image model. Port of
    /// <c>refine_image_with_nanoviz</c>.
    /// </summary>
    public async Task<byte[]?> RefineImageAsync(
        byte[] imageBytes, string editPrompt, string aspectRatio, string imageSize,
        CancellationToken cancellationToken = default)
    {
        var imageModel = _modelConfig.ImageModelName;
        if (string.IsNullOrEmpty(imageModel))
        {
            return null;
        }

        var contents = new List<ContentPart>
        {
            ContentPart.FromText(editPrompt),
            ContentPart.FromImage(Convert.ToBase64String(imageBytes), "image/jpeg"),
        };

        var responses = await _generation.CallGeminiWithRetryAsync(
            imageModel,
            contents,
            new GenerationOptions
            {
                Temperature = 1.0,
                CandidateCount = 1,
                MaxOutputTokens = 8192,
                GenerateImage = true,
                AspectRatio = aspectRatio,
                ImageSize = imageSize,
            },
            maxAttempts: 5,
            retryDelaySeconds: 10,
            cancellationToken: cancellationToken);

        if (responses.Count == 0 || string.IsNullOrEmpty(responses[0]) || responses[0] == "Error")
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(responses[0]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static List<QueryData> BuildInputs(GenerateRequest request)
    {
        var inputs = new List<QueryData>();
        for (int i = 0; i < request.NumCandidates; i++)
        {
            var data = new QueryData();
            data["filename"] = $"demo_input_candidate_{i}";
            data["caption"] = request.Caption;
            data["content"] = request.MethodContent;
            data["visual_intent"] = request.Caption;
            data["additional_info"] = new Dictionary<string, object?> { ["rounded_ratio"] = request.AspectRatio };
            data["max_critic_rounds"] = request.MaxCriticRounds;
            data["candidate_id"] = i;
            inputs.Add(data);
        }

        return inputs;
    }
}
