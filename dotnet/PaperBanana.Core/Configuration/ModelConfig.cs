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

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PaperBanana.Core.Configuration;

/// <summary>
/// Model and API configuration, loaded from a YAML file (equivalent to
/// <c>configs/model_config.yaml</c>) and/or environment variables. Environment
/// variables take precedence, matching the Python <c>get_config_val</c> logic.
/// </summary>
public sealed class ModelConfig
{
    public string ModelName { get; set; } = string.Empty;
    public string ImageModelName { get; set; } = string.Empty;

    public string GoogleApiKey { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string AnthropicApiKey { get; set; } = string.Empty;

    /// <summary>Base URL of the Python matplotlib sidecar (for the plot task).</summary>
    public string PlotServiceUrl { get; set; } = "http://localhost:8500";

    // ---- Raw YAML DTOs -----------------------------------------------------

    private sealed class RawConfig
    {
        public RawDefaults? Defaults { get; set; }
        public RawApiKeys? ApiKeys { get; set; }
        public RawServices? Services { get; set; }
    }

    private sealed class RawDefaults
    {
        public string? ModelName { get; set; }
        public string? ImageModelName { get; set; }
    }

    private sealed class RawApiKeys
    {
        public string? GoogleApiKey { get; set; }
        public string? OpenAiApiKey { get; set; }
        public string? AnthropicApiKey { get; set; }
    }

    private sealed class RawServices
    {
        public string? PlotServiceUrl { get; set; }
    }

    /// <summary>
    /// Loads configuration from the given YAML path (if present), then overlays
    /// environment variables. Missing values fall back to empty strings.
    /// </summary>
    public static ModelConfig Load(string? yamlPath = null)
    {
        var config = new ModelConfig();
        RawConfig? raw = null;

        if (!string.IsNullOrEmpty(yamlPath) && File.Exists(yamlPath))
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var yaml = File.ReadAllText(yamlPath);
            raw = deserializer.Deserialize<RawConfig>(yaml);
        }

        config.ModelName = Pick("MODEL_NAME", raw?.Defaults?.ModelName);
        config.ImageModelName = Pick("IMAGE_MODEL_NAME", raw?.Defaults?.ImageModelName);
        config.GoogleApiKey = Pick("GOOGLE_API_KEY", raw?.ApiKeys?.GoogleApiKey);
        config.OpenAiApiKey = Pick("OPENAI_API_KEY", raw?.ApiKeys?.OpenAiApiKey);
        config.AnthropicApiKey = Pick("ANTHROPIC_API_KEY", raw?.ApiKeys?.AnthropicApiKey);

        var plotUrl = Pick("PLOT_SERVICE_URL", raw?.Services?.PlotServiceUrl);
        if (!string.IsNullOrWhiteSpace(plotUrl))
        {
            config.PlotServiceUrl = plotUrl;
        }

        return config;
    }

    private static string Pick(string envVar, string? yamlValue)
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return yamlValue ?? string.Empty;
    }
}
