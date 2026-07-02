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
/// Provider-agnostic content block, mirroring the "generic content list" used
/// in the Python <c>generation_utils</c> module. Each provider client converts
/// this into its own wire format.
/// </summary>
public sealed record ContentPart
{
    public required string Type { get; init; }

    /// <summary>Text payload when <see cref="Type"/> == "text".</summary>
    public string? Text { get; init; }

    /// <summary>Base64 image data when <see cref="Type"/> == "image".</summary>
    public string? ImageBase64 { get; init; }

    /// <summary>MIME type for image parts (e.g. image/jpeg).</summary>
    public string MediaType { get; init; } = "image/jpeg";

    public static ContentPart FromText(string text) => new() { Type = "text", Text = text };

    public static ContentPart FromImage(string base64, string mediaType = "image/jpeg") =>
        new() { Type = "image", ImageBase64 = base64, MediaType = mediaType };
}
