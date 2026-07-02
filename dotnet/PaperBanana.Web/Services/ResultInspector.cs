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

using PaperBanana.Core.Models;

namespace PaperBanana.Web.Services;

/// <summary>One stage of a candidate's evolution timeline.</summary>
public sealed record EvolutionStage(string Name, string? ImageBase64, string Description, string? Suggestions);

/// <summary>
/// Extracts the final image and evolution timeline from a pipeline result.
/// Port of <c>get_evolution_stages</c> / <c>display_candidate_result</c> in
/// demo.py (diagram task).
/// </summary>
public static class ResultInspector
{
    private const string Task = "diagram";

    public static string? GetFinalImageBase64(QueryData result, string expMode)
    {
        for (int roundIdx = 3; roundIdx >= 0; roundIdx--)
        {
            var key = $"target_{Task}_critic_desc{roundIdx}_base64_jpg";
            if (result.ContainsKey(key))
            {
                return result.GetString(key);
            }
        }

        var fallbackKey = expMode == "demo_full"
            ? $"target_{Task}_stylist_desc0_base64_jpg"
            : $"target_{Task}_desc0_base64_jpg";

        return result.ContainsKey(fallbackKey) ? result.GetString(fallbackKey) : null;
    }

    public static List<EvolutionStage> GetEvolutionStages(QueryData result, string expMode)
    {
        var stages = new List<EvolutionStage>();

        var plannerImg = $"target_{Task}_desc0_base64_jpg";
        if (result.ContainsKey(plannerImg))
        {
            stages.Add(new EvolutionStage(
                "📋 Planner", result.GetString(plannerImg),
                "Initial diagram plan based on method content",
                result.ContainsKey($"target_{Task}_desc0") ? result.GetString($"target_{Task}_desc0") : null));
        }

        if (expMode == "demo_full")
        {
            var stylistImg = $"target_{Task}_stylist_desc0_base64_jpg";
            if (result.ContainsKey(stylistImg))
            {
                stages.Add(new EvolutionStage(
                    "✨ Stylist", result.GetString(stylistImg),
                    "Stylistically refined description",
                    result.ContainsKey($"target_{Task}_stylist_desc0") ? result.GetString($"target_{Task}_stylist_desc0") : null));
            }
        }

        for (int roundIdx = 0; roundIdx < 4; roundIdx++)
        {
            var criticImg = $"target_{Task}_critic_desc{roundIdx}_base64_jpg";
            if (result.ContainsKey(criticImg))
            {
                var suggKey = $"target_{Task}_critic_suggestions{roundIdx}";
                stages.Add(new EvolutionStage(
                    $"🔍 Critic Round {roundIdx}", result.GetString(criticImg),
                    $"Refined after critic feedback (iteration {roundIdx})",
                    result.ContainsKey(suggKey) ? result.GetString(suggKey) : null));
            }
        }

        return stages;
    }
}
