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

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Models;
using PaperBanana.Core.Utils;

namespace PaperBanana.Core.Evaluation;

/// <summary>
/// Referenced evaluation of a generated image against the ground-truth image
/// across four dimensions, then combined with a two-tier rule. Port of
/// <c>utils/eval_toolkits.py</c>. Judge prompts are embedded resources.
/// </summary>
public sealed class EvalToolkit
{
    private static readonly string[] Dimensions = { "faithfulness", "conciseness", "readability", "aesthetics" };
    private static readonly string[] ValidWinners = { "Human", "Model", "Both are good", "Both are bad" };
    private static readonly ConcurrentDictionary<string, string> PromptCache = new();

    private readonly GenerationClient _generation;

    public EvalToolkit(GenerationClient generation)
    {
        _generation = generation;
    }

    public async Task<QueryData> GetScoreForImageReferencedAsync(
        QueryData data, string taskName, string workDir, string modelName, CancellationToken cancellationToken = default)
    {
        var rawContent = data.GetContentAsString("content");
        var visualIntent = data.GetString("visual_intent");

        if (!data.ContainsKey("path_to_gt_image"))
        {
            Console.WriteLine("⚠️  No ground truth image path found. Skipping evaluation.");
            foreach (var dim in Dimensions.Append("overall"))
            {
                data[$"{dim}_outcome"] = "N/A - No GT";
            }

            return data;
        }

        var gtPath = Path.Combine(workDir, "data", "PaperBananaBench", taskName, data.GetString("path_to_gt_image"));
        var gtImageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(gtPath, cancellationToken));

        var evalImageField = data.GetString("eval_image_field");
        if (!data.ContainsKey(evalImageField))
        {
            Console.WriteLine($"⚠️  Image field '{evalImageField}' not found. Model generation failed - counting as Human win.");
            foreach (var dim in Dimensions.Append("overall"))
            {
                data[$"{dim}_reasoning"] = "Model failed to generate image - Human wins by default";
                data[$"{dim}_outcome"] = "Human";
            }

            return data;
        }

        var modelImageBase64 = data.GetString(evalImageField);

        var tasks = Dimensions.Select(dim => RunSingleEvalAsync(
            taskName, dim, rawContent, visualIntent, gtImageBase64, modelImageBase64, modelName, cancellationToken));
        var results = await Task.WhenAll(tasks);

        foreach (var (dim, reasoning, winner) in results)
        {
            data[$"{dim}_reasoning"] = reasoning;
            data[$"{dim}_outcome"] = winner;
        }

        var faithfulness = data.GetString("faithfulness_outcome", "Unknown");
        var readability = data.GetString("readability_outcome", "Unknown");
        var conciseness = data.GetString("conciseness_outcome", "Unknown");
        var aesthetics = data.GetString("aesthetics_outcome", "Unknown");

        var tier1 = DetermineTierOutcome(faithfulness, readability);
        string overall;
        string decisionPath;
        if (tier1 is "Model" or "Human")
        {
            overall = tier1;
            decisionPath = $"Tier1({faithfulness}, {readability}) -> {tier1} [Decided at Tier 1]";
        }
        else
        {
            var tier2 = DetermineTierOutcome(conciseness, aesthetics);
            overall = tier2;
            decisionPath = $"Tier1({faithfulness}, {readability}) -> Tie; " +
                           $"Tier2({conciseness}, {aesthetics}) -> {tier2} [Decided at Tier 2]";
        }

        data["overall_outcome"] = overall;
        data["overall_reasoning"] = $"Rule-based calculation: {decisionPath}";
        return data;
    }

    private async Task<(string Dim, string Reasoning, string Winner)> RunSingleEvalAsync(
        string taskName, string evalDim, string rawContent, string visualIntent,
        string gtImageBase64, string modelImageBase64, string modelName, CancellationToken cancellationToken)
    {
        var sysPrompt = LoadPrompt(taskName, evalDim);
        var (visualIntentLabel, rawContentLabel, humanLabel, modelLabel) = GetLabels(taskName);

        var inputText = evalDim is "readability" or "aesthetics"
            ? $"{visualIntentLabel}: {visualIntent}\n{humanLabel}: "
            : $"{rawContentLabel}: {rawContent}\n{visualIntentLabel}: {visualIntent}\n{humanLabel}: ";

        var contentList = new List<ContentPart>
        {
            ContentPart.FromText(inputText),
            ContentPart.FromImage(gtImageBase64, "image/jpeg"),
            ContentPart.FromText($"\n{modelLabel}: "),
            ContentPart.FromImage(modelImageBase64, "image/jpeg"),
        };

        string cleanJson = string.Empty;
        try
        {
            var options = new GenerationOptions
            {
                SystemPrompt = sysPrompt,
                Temperature = 1,
                CandidateCount = 1,
                MaxOutputTokens = modelName.Contains("gemini") ? 50000 : 10000,
            };

            List<string> responses;
            if (modelName.Contains("gemini"))
            {
                responses = await _generation.CallGeminiWithRetryAsync(modelName, contentList, options, cancellationToken: cancellationToken);
            }
            else if (modelName.Contains("gpt") || modelName.Contains("o1") || modelName.Contains("o3"))
            {
                responses = await _generation.CallOpenAiWithRetryAsync(modelName, contentList, options, cancellationToken: cancellationToken);
            }
            else
            {
                responses = await _generation.CallClaudeWithRetryAsync(modelName, contentList, options, cancellationToken: cancellationToken);
            }

            cleanJson = responses[0].Replace("```json", string.Empty).Replace("```", string.Empty).Trim();
            var node = JsonRepair.TryParse(cleanJson);

            var winner = node?["winner"]?.ToString();
            var reasoning = node?["comparison_reasoning"]?.ToString();

            if (string.IsNullOrEmpty(winner))
            {
                winner = ExtractWinnerWithFallback(cleanJson, evalDim);
            }

            reasoning ??= cleanJson;
            return (evalDim, reasoning, winner);
        }
        catch (Exception e)
        {
            Console.WriteLine($"❌ {evalDim}: Evaluation failed - {Truncate(e.Message, 100)}");
            var extracted = TryRegexExtractWinner(cleanJson);
            var winner = extracted is not null && ValidWinners.Contains(extracted) ? extracted : "Error";
            return (evalDim, e.Message, winner);
        }
    }

    private static (string VisualIntentLabel, string RawContentLabel, string HumanLabel, string ModelLabel) GetLabels(
        string taskName) => taskName switch
    {
        "plot" => ("Visual Intent of the Desired Plot", "Raw Data",
                   "Human-Drawn Plot (Human)", "Model-Generated Plot (Model)"),
        "diagram" => ("Diagram Caption", "Methodology Section",
                      "Human-Drawn Diagram (Human)", "Model-Generated Diagram (Model)"),
        _ => throw new ArgumentException($"Invalid task name: {taskName}"),
    };

    private static string DetermineTierOutcome(string dim1, string dim2)
    {
        var o1 = dim1.Trim();
        var o2 = dim2.Trim();

        if (o1 == o2)
        {
            return o1 is "Both are good" or "Both are bad" ? "Tie" : o1;
        }

        bool IsNeutral(string o) => o is "Both are good" or "Both are bad";

        if ((o1 == "Model" && IsNeutral(o2)) || (o2 == "Model" && IsNeutral(o1)))
        {
            return "Model";
        }

        if ((o1 == "Human" && IsNeutral(o2)) || (o2 == "Human" && IsNeutral(o1)))
        {
            return "Human";
        }

        return "Tie";
    }

    private string ExtractWinnerWithFallback(string cleanJson, string evalDim)
    {
        var extracted = TryRegexExtractWinner(cleanJson);
        if (extracted is not null && ValidWinners.Contains(extracted))
        {
            Console.WriteLine($"⚠️  {evalDim}: regex extracted '{extracted}'");
            return extracted;
        }

        Console.WriteLine($"⚠️  {evalDim}: failed to extract valid winner");
        return "Error";
    }

    private static string? TryRegexExtractWinner(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string[] patterns =
        {
            "\"winner\"\\s*:\\s*\"([^\"]+)\"",
            "\\*\\*winner\\*\\*\\s*:\\s*\"([^\"]+)\"",
            "\\*\\*winner\\*\\*\\s*:\\s*([A-Za-z][A-Za-z\\s]+?)(?:,|\\n|$)",
            "\"winner\"\\s*:\\s*([A-Za-z][A-Za-z\\s]+?)(?:,|\\n|$)",
            "(?:\\*\\*|\")winner(?:\\*\\*|\")\\s*:\\s*(?:\\*\\*|\")?([A-Za-z][A-Za-z\\s]+?)(?:\\*\\*|\"|,|\\n|$)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim().TrimEnd('*', '"').Trim();
            }
        }

        return null;
    }

    private static string LoadPrompt(string taskName, string evalDim)
    {
        var key = $"{taskName}_{evalDim}";
        return PromptCache.GetOrAdd(key, static k =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"PaperBanana.Core.Resources.EvalPrompts.{k}.txt";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded eval prompt: {resourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
