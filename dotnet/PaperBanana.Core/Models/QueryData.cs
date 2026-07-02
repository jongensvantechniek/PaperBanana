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

namespace PaperBanana.Core.Models;

/// <summary>
/// Dynamic bag of key/value pairs used to carry a single query through the
/// agent pipeline. This mirrors the Python <c>data: Dict[str, Any]</c> that is
/// threaded through every agent, where keys such as
/// <c>target_diagram_desc0</c> and <c>target_diagram_desc0_base64_jpg</c> are
/// created on the fly.
/// </summary>
public sealed class QueryData
{
    private readonly Dictionary<string, object?> _values;

    public QueryData()
    {
        _values = new Dictionary<string, object?>();
    }

    public QueryData(IDictionary<string, object?> values)
    {
        _values = new Dictionary<string, object?>(values);
    }

    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var v) ? v : null;
        set => _values[key] = value;
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key) && _values[key] is not null;

    public IReadOnlyDictionary<string, object?> Values => _values;

    public void Set(string key, object? value) => _values[key] = value;

    /// <summary>Returns the value as a string, or the fallback when absent/null.</summary>
    public string GetString(string key, string fallback = "")
    {
        if (!_values.TryGetValue(key, out var v) || v is null)
        {
            return fallback;
        }

        return v as string ?? StringifyValue(v);
    }

    public int GetInt(string key, int fallback)
    {
        if (!_values.TryGetValue(key, out var v) || v is null)
        {
            return fallback;
        }

        return v switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => fallback,
        };
    }

    /// <summary>
    /// Returns the raw <c>content</c> field serialized to a string. When the
    /// content is a dict/list (JSON), it is serialized back to JSON, matching
    /// <c>json.dumps(...)</c> in the Python agents.
    /// </summary>
    public string GetContentAsString(string key)
    {
        if (!_values.TryGetValue(key, out var v) || v is null)
        {
            return string.Empty;
        }

        return v is string s ? s : StringifyValue(v);
    }

    /// <summary>Reads a nested value from the <c>additional_info</c> object.</summary>
    public string? GetAdditionalInfo(string field)
    {
        if (_values.TryGetValue("additional_info", out var v) &&
            v is IDictionary<string, object?> dict &&
            dict.TryGetValue(field, out var inner) && inner is not null)
        {
            return inner as string ?? StringifyValue(inner);
        }

        return null;
    }

    private static string StringifyValue(object value)
    {
        return JsonSerializer.Serialize(value, JsonDefaults.Options);
    }

    /// <summary>Builds a <see cref="QueryData"/> from a JSON object node.</summary>
    public static QueryData FromJsonObject(JsonObject obj)
    {
        var values = new Dictionary<string, object?>();
        foreach (var kvp in obj)
        {
            values[kvp.Key] = ConvertNode(kvp.Value);
        }

        return new QueryData(values);
    }

    private static object? ConvertNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject o:
                var dict = new Dictionary<string, object?>();
                foreach (var kvp in o)
                {
                    dict[kvp.Key] = ConvertNode(kvp.Value);
                }

                return dict;
            case JsonArray a:
                var list = new List<object?>();
                foreach (var item in a)
                {
                    list.Add(ConvertNode(item));
                }

                return list;
            case JsonValue val:
                if (val.TryGetValue<string>(out var s))
                {
                    return s;
                }

                if (val.TryGetValue<bool>(out var b))
                {
                    return b;
                }

                if (val.TryGetValue<long>(out var l))
                {
                    return l;
                }

                if (val.TryGetValue<double>(out var d))
                {
                    return d;
                }

                return val.ToString();
            default:
                return node.ToString();
        }
    }

    public Dictionary<string, object?> ToDictionary() => new(_values);
}
