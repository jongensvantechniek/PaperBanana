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

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Models;

namespace PaperBanana.Core.Llm;

/// <summary>
/// Multi-provider generation client. Port of <c>utils/generation_utils.py</c>,
/// talking to the Gemini, Anthropic and OpenAI REST APIs over
/// <see cref="HttpClient"/> with the same retry/backoff semantics.
/// </summary>
public sealed class GenerationClient
{
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string AnthropicUrl = "https://api.anthropic.com/v1/messages";
    private const string OpenAiChatUrl = "https://api.openai.com/v1/chat/completions";
    private const string OpenAiImageUrl = "https://api.openai.com/v1/images/generations";

    private readonly HttpClient _httpClient;
    private readonly ModelConfig _config;

    public GenerationClient(HttpClient httpClient, ModelConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public bool HasGoogleKey => !string.IsNullOrWhiteSpace(_config.GoogleApiKey);
    public bool HasAnthropicKey => !string.IsNullOrWhiteSpace(_config.AnthropicApiKey);
    public bool HasOpenAiKey => !string.IsNullOrWhiteSpace(_config.OpenAiApiKey);

    // ------------------------------------------------------------------
    // Gemini
    // ------------------------------------------------------------------

    /// <summary>
    /// Call the Gemini API with async retry. For image models (name contains
    /// "image" or "nanoviz") the returned strings are base64 image data;
    /// otherwise they are candidate texts. Mirrors
    /// <c>call_gemini_with_retry_async</c>.
    /// </summary>
    public async Task<List<string>> CallGeminiWithRetryAsync(
        string modelName,
        IReadOnlyList<ContentPart> contents,
        GenerationOptions options,
        int maxAttempts = 5,
        int retryDelaySeconds = 5,
        string errorContext = "",
        CancellationToken cancellationToken = default)
    {
        if (!HasGoogleKey)
        {
            throw new InvalidOperationException(
                "Gemini client was not initialized: missing Google API key. Set GOOGLE_API_KEY " +
                "or configure api_keys.google_api_key in configs/model_config.yaml.");
        }

        int target = Math.Max(1, options.CandidateCount);
        int perCall = Math.Min(target, 8); // Gemini max candidate count is 8.
        bool isImage = modelName.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                       modelName.Contains("nanoviz", StringComparison.OrdinalIgnoreCase);

        var results = new List<string>();

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var body = BuildGeminiBody(contents, options, perCall, isImage);
                var url = $"{GeminiBaseUrl}/{modelName}:generateContent";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("x-goog-api-key", _config.GoogleApiKey);
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

                if (isImage)
                {
                    var b64 = ExtractGeminiImage(json);
                    if (b64 is null)
                    {
                        Console.WriteLine($"[Warning]: Failed to generate image, retrying in {retryDelaySeconds}s...");
                        await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
                        continue;
                    }

                    results.Add(b64);
                }
                else
                {
                    foreach (var text in ExtractGeminiTexts(json))
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            results.Add(text);
                        }
                    }
                }

                if (results.Count >= target)
                {
                    return results.GetRange(0, target);
                }
            }
            catch (Exception e)
            {
                var context = string.IsNullOrEmpty(errorContext) ? string.Empty : $" for {errorContext}";
                var delay = Math.Min(retryDelaySeconds * (int)Math.Pow(2, attempt), 30);
                Console.WriteLine($"Attempt {attempt + 1} for model {modelName} failed{context}: {e.Message}. " +
                                  $"Retrying in {delay}s...");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }
            }
        }

        while (results.Count < target)
        {
            results.Add("Error");
        }

        return results;
    }

    private JsonObject BuildGeminiBody(
        IReadOnlyList<ContentPart> contents, GenerationOptions options, int candidateCount, bool isImage)
    {
        var parts = new JsonArray();
        foreach (var part in contents)
        {
            if (part.Type == "text" && part.Text is not null)
            {
                parts.Add(new JsonObject { ["text"] = part.Text });
            }
            else if (part.Type == "image" && part.ImageBase64 is not null)
            {
                parts.Add(new JsonObject
                {
                    ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = part.MediaType,
                        ["data"] = part.ImageBase64,
                    },
                });
            }
        }

        var generationConfig = new JsonObject
        {
            ["temperature"] = options.Temperature,
            ["candidateCount"] = candidateCount,
            ["maxOutputTokens"] = options.MaxOutputTokens,
        };

        if (isImage && options.GenerateImage)
        {
            generationConfig["responseModalities"] = new JsonArray { "IMAGE" };
            var imageConfig = new JsonObject { ["imageSize"] = options.ImageSize };
            if (!string.IsNullOrEmpty(options.AspectRatio))
            {
                imageConfig["aspectRatio"] = options.AspectRatio;
            }

            generationConfig["imageConfig"] = imageConfig;
        }

        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["parts"] = parts },
            },
            ["generationConfig"] = generationConfig,
        };

        if (!string.IsNullOrEmpty(options.SystemPrompt))
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = options.SystemPrompt } },
            };
        }

        return body;
    }

    private static IEnumerable<string> ExtractGeminiTexts(JsonNode? json)
    {
        if (json?["candidates"] is not JsonArray candidates)
        {
            yield break;
        }

        foreach (var candidate in candidates)
        {
            if (candidate?["content"]?["parts"] is JsonArray parts)
            {
                foreach (var part in parts)
                {
                    var text = part?["text"]?.ToString();
                    if (text is not null)
                    {
                        yield return text;
                    }
                }
            }
        }
    }

    private static string? ExtractGeminiImage(JsonNode? json)
    {
        if (json?["candidates"] is not JsonArray candidates || candidates.Count == 0)
        {
            return null;
        }

        if (candidates[0]?["content"]?["parts"] is not JsonArray parts)
        {
            return null;
        }

        foreach (var part in parts)
        {
            var data = part?["inlineData"]?["data"]?.ToString();
            if (!string.IsNullOrEmpty(data))
            {
                return data;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Anthropic (Claude)
    // ------------------------------------------------------------------

    /// <summary>Call the Claude messages API. Mirrors <c>call_claude_with_retry_async</c>.</summary>
    public async Task<List<string>> CallClaudeWithRetryAsync(
        string modelName,
        IReadOnlyList<ContentPart> contents,
        GenerationOptions options,
        int maxAttempts = 5,
        int retryDelaySeconds = 30,
        string errorContext = "",
        CancellationToken cancellationToken = default)
    {
        if (!HasAnthropicKey)
        {
            throw new InvalidOperationException("Anthropic client was not initialized: missing ANTHROPIC_API_KEY.");
        }

        int candidateNum = Math.Max(1, options.CandidateCount);
        var responses = new List<string>();

        // Validation phase: one successful call proves the input is valid.
        bool valid = false;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var first = await SendClaudeMessageAsync(modelName, contents, options, cancellationToken);
                responses.Add(first);
                valid = true;
                break;
            }
            catch (Exception e)
            {
                var context = string.IsNullOrEmpty(errorContext) ? string.Empty : $" for {errorContext}";
                Console.WriteLine($"Validation attempt {attempt + 1} failed{context}: {e.Message}. " +
                                  $"Retrying in {retryDelaySeconds}s...");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
                }
            }
        }

        if (!valid)
        {
            return Enumerable.Repeat("Error", candidateNum).ToList();
        }

        int remaining = candidateNum - 1;
        if (remaining > 0)
        {
            var tasks = Enumerable.Range(0, remaining)
                .Select(_ => SendClaudeMessageAsync(modelName, contents, options, cancellationToken))
                .ToList();
            var settled = await Task.WhenAll(tasks.Select(SafeAsync));
            responses.AddRange(settled);
        }

        return responses;
    }

    private async Task<string> SendClaudeMessageAsync(
        string modelName, IReadOnlyList<ContentPart> contents, GenerationOptions options, CancellationToken ct)
    {
        var contentArray = new JsonArray();
        foreach (var part in contents)
        {
            if (part.Type == "text" && part.Text is not null)
            {
                contentArray.Add(new JsonObject { ["type"] = "text", ["text"] = part.Text });
            }
            else if (part.Type == "image" && part.ImageBase64 is not null)
            {
                contentArray.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = part.MediaType,
                        ["data"] = part.ImageBase64,
                    },
                });
            }
        }

        var body = new JsonObject
        {
            ["model"] = modelName,
            ["max_tokens"] = options.MaxOutputTokens,
            ["temperature"] = options.Temperature,
            ["system"] = options.SystemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = contentArray },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, AnthropicUrl);
        request.Headers.Add("x-api-key", _config.AnthropicApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["content"]?[0]?["text"]?.ToString() ?? "Error";
    }

    // ------------------------------------------------------------------
    // OpenAI chat
    // ------------------------------------------------------------------

    /// <summary>Call the OpenAI chat completions API. Mirrors <c>call_openai_with_retry_async</c>.</summary>
    public async Task<List<string>> CallOpenAiWithRetryAsync(
        string modelName,
        IReadOnlyList<ContentPart> contents,
        GenerationOptions options,
        int maxAttempts = 5,
        int retryDelaySeconds = 30,
        string errorContext = "",
        CancellationToken cancellationToken = default)
    {
        if (!HasOpenAiKey)
        {
            throw new InvalidOperationException("OpenAI client was not initialized: missing OPENAI_API_KEY.");
        }

        int candidateNum = Math.Max(1, options.CandidateCount);
        var responses = new List<string>();

        bool valid = false;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                responses.Add(await SendOpenAiChatAsync(modelName, contents, options, cancellationToken));
                valid = true;
                break;
            }
            catch (Exception e)
            {
                var context = string.IsNullOrEmpty(errorContext) ? string.Empty : $" for {errorContext}";
                Console.WriteLine($"Validation attempt {attempt + 1} failed{context}: {e.Message}. " +
                                  $"Retrying in {retryDelaySeconds}s...");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
                }
            }
        }

        if (!valid)
        {
            return Enumerable.Repeat("Error", candidateNum).ToList();
        }

        int remaining = candidateNum - 1;
        if (remaining > 0)
        {
            var tasks = Enumerable.Range(0, remaining)
                .Select(_ => SendOpenAiChatAsync(modelName, contents, options, cancellationToken));
            var settled = await Task.WhenAll(tasks.Select(SafeAsync));
            responses.AddRange(settled);
        }

        return responses;
    }

    private async Task<string> SendOpenAiChatAsync(
        string modelName, IReadOnlyList<ContentPart> contents, GenerationOptions options, CancellationToken ct)
    {
        var userContent = new JsonArray();
        foreach (var part in contents)
        {
            if (part.Type == "text" && part.Text is not null)
            {
                userContent.Add(new JsonObject { ["type"] = "text", ["text"] = part.Text });
            }
            else if (part.Type == "image" && part.ImageBase64 is not null)
            {
                userContent.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject
                    {
                        ["url"] = $"data:{part.MediaType};base64,{part.ImageBase64}",
                    },
                });
            }
        }

        var body = new JsonObject
        {
            ["model"] = modelName,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = options.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userContent },
            },
            ["temperature"] = options.Temperature,
            ["max_completion_tokens"] = options.MaxOutputTokens,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiChatUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["choices"]?[0]?["message"]?["content"]?.ToString() ?? "Error";
    }

    // ------------------------------------------------------------------
    // OpenAI image generation (GPT-Image)
    // ------------------------------------------------------------------

    /// <summary>Mirrors <c>call_openai_image_generation_with_retry_async</c>.</summary>
    public async Task<List<string>> CallOpenAiImageGenerationWithRetryAsync(
        string modelName,
        string prompt,
        OpenAiImageOptions options,
        int maxAttempts = 5,
        int retryDelaySeconds = 30,
        string errorContext = "",
        CancellationToken cancellationToken = default)
    {
        if (!HasOpenAiKey)
        {
            throw new InvalidOperationException("OpenAI client was not initialized: missing OPENAI_API_KEY.");
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var body = new JsonObject
                {
                    ["model"] = modelName,
                    ["prompt"] = prompt,
                    ["n"] = 1,
                    ["size"] = options.Size,
                    ["quality"] = options.Quality,
                    ["background"] = options.Background,
                    ["output_format"] = options.OutputFormat,
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiImageUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var b64 = json?["data"]?[0]?["b64_json"]?.ToString();
                if (!string.IsNullOrEmpty(b64))
                {
                    return new List<string> { b64 };
                }

                Console.WriteLine("[Warning]: Failed to generate image via OpenAI, no data returned.");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
                }
            }
            catch (Exception e)
            {
                var context = string.IsNullOrEmpty(errorContext) ? string.Empty : $" for {errorContext}";
                Console.WriteLine($"Attempt {attempt + 1} for OpenAI image model {modelName} failed{context}: " +
                                  $"{e.Message}. Retrying in {retryDelaySeconds}s...");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
                }
            }
        }

        return new List<string> { "Error" };
    }

    private static async Task<string> SafeAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error generating a subsequent candidate: {e.Message}");
            return "Error";
        }
    }
}
