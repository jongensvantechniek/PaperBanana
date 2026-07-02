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

namespace PaperBanana.Core.Models;

/// <summary>
/// Configuration for a text or image generation request. Mirrors the various
/// config dicts / GenerateContentConfig objects passed around in the Python
/// generation utilities.
/// </summary>
public sealed record GenerationOptions
{
    public string SystemPrompt { get; init; } = string.Empty;

    public double Temperature { get; init; } = 1.0;

    /// <summary>Number of candidates to sample (Python <c>candidate_count</c>).</summary>
    public int CandidateCount { get; init; } = 1;

    public int MaxOutputTokens { get; init; } = 50000;

    /// <summary>When true, request image output (Gemini responseModalities=["IMAGE"]).</summary>
    public bool GenerateImage { get; init; }

    /// <summary>Aspect ratio for image generation (e.g. 16:9).</summary>
    public string? AspectRatio { get; init; }

    /// <summary>Image size hint for Gemini image generation (e.g. 1k, 2K, 4K).</summary>
    public string ImageSize { get; init; } = "1k";
}

/// <summary>Options for OpenAI image generation (GPT-Image family).</summary>
public sealed record OpenAiImageOptions
{
    public string Size { get; init; } = "1536x1024";
    public string Quality { get; init; } = "high";
    public string Background { get; init; } = "opaque";
    public string OutputFormat { get; init; } = "png";
}
