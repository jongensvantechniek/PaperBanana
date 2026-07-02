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
using PaperBanana.Core.Pipeline;

namespace PaperBanana.Tests;

public class ResultInspectorTests
{
    [Fact]
    public void GetFinalImageBase64_PrefersLatestCriticRound()
    {
        var data = new QueryData();
        data["target_diagram_desc0_base64_jpg"] = "planner-img";
        data["target_diagram_critic_desc0_base64_jpg"] = "critic0-img";
        data["target_diagram_critic_desc1_base64_jpg"] = "critic1-img";

        var final = ResultInspector.GetFinalImageBase64(data, "demo_planner_critic");

        Assert.Equal("critic1-img", final);
    }

    [Fact]
    public void GetFinalImageBase64_FallsBackToPlannerForPlannerCriticMode()
    {
        var data = new QueryData();
        data["target_diagram_desc0_base64_jpg"] = "planner-img";

        var final = ResultInspector.GetFinalImageBase64(data, "demo_planner_critic");

        Assert.Equal("planner-img", final);
    }

    [Fact]
    public void GetFinalImageBase64_FallsBackToStylistForFullMode()
    {
        var data = new QueryData();
        data["target_diagram_stylist_desc0_base64_jpg"] = "stylist-img";

        var final = ResultInspector.GetFinalImageBase64(data, "demo_full");

        Assert.Equal("stylist-img", final);
    }

    [Fact]
    public void GetEvolutionStages_IncludesPlannerAndCriticRounds()
    {
        var data = new QueryData();
        data["target_diagram_desc0_base64_jpg"] = "planner-img";
        data["target_diagram_desc0"] = "planner desc";
        data["target_diagram_critic_desc0_base64_jpg"] = "critic0-img";
        data["target_diagram_critic_suggestions0"] = "make it clearer";

        var stages = ResultInspector.GetEvolutionStages(data, "demo_planner_critic");

        Assert.Equal(2, stages.Count);
        Assert.Contains(stages, s => s.Name.Contains("Planner"));
        Assert.Contains(stages, s => s.Name.Contains("Critic Round 0") && s.Suggestions == "make it clearer");
    }

    [Fact]
    public void GetEvolutionStages_IncludesStylistOnlyForFullMode()
    {
        var data = new QueryData();
        data["target_diagram_desc0_base64_jpg"] = "planner-img";
        data["target_diagram_stylist_desc0_base64_jpg"] = "stylist-img";

        var full = ResultInspector.GetEvolutionStages(data, "demo_full");
        var plannerCritic = ResultInspector.GetEvolutionStages(data, "demo_planner_critic");

        Assert.Contains(full, s => s.Name.Contains("Stylist"));
        Assert.DoesNotContain(plannerCritic, s => s.Name.Contains("Stylist"));
    }
}
