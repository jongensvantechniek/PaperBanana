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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaperBanana.Core.Utils;

/// <summary>
/// Lightweight, best-effort JSON extraction/repair used by the Retriever and
/// Critic agents. Replaces the Python <c>json_repair</c> dependency: it strips
/// Markdown code fences and extracts the first balanced JSON object/array.
/// </summary>
public static class JsonRepair
{
    /// <summary>Parse a possibly-messy model response into a <see cref="JsonNode"/>.</summary>
    public static JsonNode? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = StripFences(raw).Trim();

        // First try the whole string.
        if (TryParseNode(cleaned, out var node))
        {
            return node;
        }

        // Fall back to extracting the first balanced { ... } or [ ... ].
        var extracted = ExtractBalanced(cleaned);
        if (extracted is not null && TryParseNode(extracted, out node))
        {
            return node;
        }

        return null;
    }

    private static bool TryParseNode(string text, out JsonNode? node)
    {
        node = null;
        try
        {
            node = JsonNode.Parse(text);
            return node is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripFences(string text)
    {
        return text
            .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty);
    }

    private static string? ExtractBalanced(string text)
    {
        int start = -1;
        char open = '\0';
        char close = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' || text[i] == '[')
            {
                start = i;
                open = text[i];
                close = open == '{' ? '}' : ']';
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }

        return null;
    }

    /// <summary>Extract a string list field (e.g. top10_diagrams) from a node.</summary>
    public static List<string> GetStringList(JsonNode? node, string field)
    {
        var result = new List<string>();
        if (node is JsonObject obj && obj.TryGetPropertyValue(field, out var arr) && arr is JsonArray a)
        {
            foreach (var item in a)
            {
                if (item is not null)
                {
                    result.Add(item.ToString());
                }
            }
        }

        return result;
    }
}
