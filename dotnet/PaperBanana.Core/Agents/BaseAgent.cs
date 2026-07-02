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

using System.Text.Json.Nodes;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Models;

namespace PaperBanana.Core.Agents;

/// <summary>A reference example loaded from ref.json / agent_selected_12.json.</summary>
public sealed record RefItem(string Id, string VisualIntent, string ContentString, string PathToGtImage);

/// <summary>
/// Base class for all agents. Port of <c>agents/base_agent.py</c>, plus shared
/// helpers for loading dataset files relative to the work directory.
/// </summary>
public abstract class BaseAgent
{
    protected BaseAgent(ExpConfig expConfig, GenerationClient generation)
    {
        ExpConfig = expConfig;
        Generation = generation;
    }

    protected ExpConfig ExpConfig { get; }

    protected GenerationClient Generation { get; }

    protected string ModelName { get; set; } = string.Empty;

    protected string SystemPrompt { get; set; } = string.Empty;

    /// <summary>Process the input data and return the (mutated) result.</summary>
    public abstract Task<QueryData> ProcessAsync(QueryData data, CancellationToken cancellationToken = default);

    // ---- Shared helpers ---------------------------------------------------

    protected string DataDir(string taskName) =>
        Path.Combine(ExpConfig.WorkDir, "data", "PaperBananaBench", taskName);

    protected string RefFilePath(string taskName) => Path.Combine(DataDir(taskName), "ref.json");

    protected string ManualRefFilePath(string taskName) =>
        Path.Combine(DataDir(taskName), "agent_selected_12.json");

    protected string ReadStyleGuide(string taskName)
    {
        var path = Path.Combine(ExpConfig.WorkDir, "style_guides", $"neurips2025_{taskName}_style_guide.md");
        return File.ReadAllText(path);
    }

    /// <summary>Load and normalize a reference JSON file into <see cref="RefItem"/>s.</summary>
    protected static List<RefItem> LoadRefItems(string path, int? limit = null)
    {
        var items = new List<RefItem>();
        if (!File.Exists(path))
        {
            return items;
        }

        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray array)
        {
            return items;
        }

        foreach (var node in array)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            items.Add(new RefItem(
                Id: obj["id"]?.ToString() ?? string.Empty,
                VisualIntent: obj["visual_intent"]?.ToString() ?? string.Empty,
                ContentString: StringifyContent(obj["content"]),
                PathToGtImage: obj["path_to_gt_image"]?.ToString() ?? string.Empty));

            if (limit.HasValue && items.Count >= limit.Value)
            {
                break;
            }
        }

        return items;
    }

    /// <summary>String form of a content field, JSON-serializing non-string values.</summary>
    protected static string StringifyContent(JsonNode? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        return content is JsonValue value && value.TryGetValue<string>(out var s)
            ? s
            : content.ToJsonString();
    }

    protected static string ReadImageAsBase64(string path) =>
        Convert.ToBase64String(File.ReadAllBytes(path));
}
