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
/// Polish Agent - applies style guidelines to a ground-truth image in two
/// steps (suggest, then re-render). Port of <c>agents/polish_agent.py</c>.
/// </summary>
public sealed class PolishAgent : BaseAgent
{
    private readonly string _imageModelName;
    private readonly string _textModelName;
    private readonly string _taskName;
    private readonly string _styleGuideFilename;
    private readonly string _suggestionSystemPrompt;

    public PolishAgent(ExpConfig expConfig, GenerationClient generation)
        : base(expConfig, generation)
    {
        _imageModelName = expConfig.ImageModelName;
        _textModelName = expConfig.ModelName;

        if (expConfig.TaskName == "plot")
        {
            _taskName = "plot";
            _styleGuideFilename = "neurips2025_plot_style_guide.md";
            _suggestionSystemPrompt = AgentPrompts.PlotSuggestion;
        }
        else
        {
            _taskName = "diagram";
            _styleGuideFilename = "neurips2025_diagram_style_guide.md";
            _suggestionSystemPrompt = AgentPrompts.DiagramSuggestion;
        }
    }

    public override async Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default)
    {
        var gtImagePathRel = data.GetString("path_to_gt_image");
        if (string.IsNullOrEmpty(gtImagePathRel))
        {
            Console.WriteLine("⚠️  No GT image path found in data");
            return data;
        }

        var gtImagePath = Path.Combine(DataDir(_taskName), gtImagePathRel);
        string gtImageB64;
        try
        {
            gtImageB64 = ReadImageAsBase64(gtImagePath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"⚠️  Failed to load GT image from {gtImagePath}: {e.Message}");
            return data;
        }

        string styleGuide;
        try
        {
            styleGuide = File.ReadAllText(Path.Combine(ExpConfig.WorkDir, "style_guides", _styleGuideFilename));
        }
        catch (Exception e)
        {
            Console.WriteLine($"❌ Error loading style guide: {e.Message}");
            return data;
        }

        Console.WriteLine($"🎨 [Step 1] Generating suggestions for {_taskName}...");
        var suggestions = await GenerateSuggestionsAsync(gtImageB64, styleGuide, cancellationToken);

        if (!string.IsNullOrEmpty(suggestions))
        {
            data[$"suggestions_{_taskName}"] = suggestions;
        }

        Console.WriteLine($"🎨 [Step 2] Polishing image with suggestions...");
        var userPrompt = $"Please polish this image based on the following suggestions:\n\n{suggestions}\n\nPolished Image:";
        var contentList = new List<ContentPart>
        {
            ContentPart.FromText(userPrompt),
            ContentPart.FromImage(gtImageB64, "image/jpeg"),
        };

        try
        {
            var responses = await Generation.CallGeminiWithRetryAsync(
                _imageModelName,
                contentList,
                new GenerationOptions
                {
                    SystemPrompt = string.Empty, // Matches the Python (self.system_prompt is unset).
                    Temperature = ExpConfig.Temperature,
                    CandidateCount = 1,
                    MaxOutputTokens = 50000,
                    GenerateImage = true,
                    AspectRatio = data.GetAdditionalInfo("rounded_ratio") ?? "16:9",
                    ImageSize = "1k",
                },
                maxAttempts: 5,
                retryDelaySeconds: 30,
                cancellationToken: cancellationToken);

            if (responses.Count > 0 && !string.IsNullOrEmpty(responses[0]))
            {
                var convertedJpg = ImageUtils.ConvertPngB64ToJpgB64(responses[0]);
                if (convertedJpg is not null)
                {
                    data[$"polished_{_taskName}_base64_jpg"] = convertedJpg;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"❌ Error during image generation: {e.Message}");
        }

        return data;
    }

    private async Task<string> GenerateSuggestionsAsync(
        string gtImageB64, string styleGuide, CancellationToken cancellationToken)
    {
        var userPrompt =
            $"Here is the style guide:\n{styleGuide}\n\nPlease analyze the provided image against this style guide " +
            "and list up to 10 specific improvement suggestions to make the image visually more appealing. " +
            "If the image is already perfect, just say 'No changes needed'.";

        var contentList = new List<ContentPart>
        {
            ContentPart.FromText(userPrompt),
            ContentPart.FromImage(gtImageB64, "image/jpeg"),
        };

        try
        {
            var responses = await Generation.CallGeminiWithRetryAsync(
                _textModelName,
                contentList,
                new GenerationOptions
                {
                    SystemPrompt = _suggestionSystemPrompt,
                    Temperature = 1,
                    CandidateCount = 1,
                    MaxOutputTokens = 50000,
                },
                maxAttempts: 3,
                retryDelaySeconds: 10,
                cancellationToken: cancellationToken);
            return responses.Count > 0 ? responses[0] : string.Empty;
        }
        catch (Exception e)
        {
            Console.WriteLine($"❌ Error during suggestion generation: {e.Message}");
            return string.Empty;
        }
    }
}
