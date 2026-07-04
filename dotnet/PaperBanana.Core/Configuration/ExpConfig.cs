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

namespace PaperBanana.Core.Configuration;

/// <summary>
/// Experiment configuration, the C# equivalent of the Python
/// <c>utils.config.ExpConfig</c> dataclass.
/// </summary>
public sealed class ExpConfig
{
    public string DatasetName { get; init; } = "PaperBananaBench";

    /// <summary>"diagram" or "plot".</summary>
    public string TaskName { get; init; } = "diagram";

    public string SplitName { get; init; } = "test";

    public double Temperature { get; init; } = 1.0;

    public string ExpMode { get; init; } = string.Empty;

    /// <summary>"auto", "manual", "random" or "none".</summary>
    public string RetrievalSetting { get; init; } = "auto";

    public int MaxCriticRounds { get; init; } = 3;

    public string ModelName { get; set; } = string.Empty;

    public string ImageModelName { get; set; } = string.Empty;

    /// <summary>Root directory that contains data/, style_guides/, results/.</summary>
    public required string WorkDir { get; init; }

    public string Timestamp { get; set; } = string.Empty;

    public string ExpName { get; private set; } = string.Empty;

    public string ResultDir { get; private set; } = string.Empty;

    /// <summary>
    /// Applies model fallbacks from <see cref="ModelConfig"/> and computes the
    /// derived experiment name and result directory (mirrors <c>__post_init__</c>).
    /// </summary>
    public void Initialize(ModelConfig modelConfig)
    {
        if (string.IsNullOrEmpty(ModelName))
        {
            ModelName = modelConfig.ModelName;
        }

        if (string.IsNullOrEmpty(ImageModelName))
        {
            ImageModelName = modelConfig.ImageModelName;
        }

        if (string.IsNullOrEmpty(Timestamp))
        {
            Timestamp = DateTime.Now.ToString("MMdd_HHmm");
        }

        ExpName = $"{Timestamp}_{RetrievalSetting}ret_{ExpMode}_{SplitName}";
        ResultDir = Path.Combine(WorkDir, "results", $"{DatasetName}_{TaskName}");
        Directory.CreateDirectory(ResultDir);
    }
}
