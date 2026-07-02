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

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaperBanana.Core.Configuration;

namespace PaperBanana.Core.Utils;

/// <summary>
/// Executes matplotlib code for the "plot" task by delegating to the Python
/// sidecar service (see <c>python_sidecar/plot_service.py</c>). Matplotlib has
/// no faithful .NET equivalent, so the plot pipeline is preserved by reusing
/// the original Python execution worker over HTTP.
/// </summary>
public sealed class PlotExecutorClient
{
    private readonly HttpClient _httpClient;
    private readonly ModelConfig _modelConfig;

    public PlotExecutorClient(HttpClient httpClient, ModelConfig modelConfig)
    {
        _httpClient = httpClient;
        _modelConfig = modelConfig;
    }

    /// <summary>
    /// Send matplotlib code (optionally fenced in ```python ... ```) to the
    /// sidecar and receive a base64-encoded JPEG, or <c>null</c> on failure.
    /// </summary>
    public async Task<string?> ExecuteAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new JsonObject { ["code"] = code };
            using var content = new StringContent(
                payload.ToJsonString(), Encoding.UTF8, "application/json");

            var url = _modelConfig.PlotServiceUrl.TrimEnd('/') + "/execute";
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var node = JsonNode.Parse(body);
            var b64 = node?["image_base64"]?.ToString();
            return string.IsNullOrEmpty(b64) ? null : b64;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error executing plot code via sidecar: {e.Message}");
            return null;
        }
    }
}
