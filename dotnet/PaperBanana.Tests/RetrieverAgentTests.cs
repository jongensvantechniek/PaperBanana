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

using PaperBanana.Core.Agents;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Models;

namespace PaperBanana.Tests;

public class RetrieverAgentTests
{
    [Fact]
    public async Task Process_NoneSetting_ReturnsEmptyReferencesWithoutNetwork()
    {
        // "none" retrieval must not touch the filesystem or any LLM API.
        var workDir = Path.Combine(Path.GetTempPath(), "pb-ret-" + Guid.NewGuid().ToString("N"));
        var expConfig = new ExpConfig { TaskName = "diagram", WorkDir = workDir };
        using var http = new HttpClient();
        var generation = new GenerationClient(http, new ModelConfig());
        var agent = new RetrieverAgent(expConfig, generation);

        var data = new QueryData();
        data["content"] = "some method";
        data["visual_intent"] = "a caption";

        var result = await agent.ProcessAsync(data, "none");

        Assert.Empty((List<object?>)result["top10_references"]!);
        Assert.Empty((List<object?>)result["retrieved_examples"]!);
    }

    [Fact]
    public async Task Process_AutoSetting_FallsBackToNoneWhenRefFileMissing()
    {
        // With no ref.json present, "auto" must gracefully degrade to "none"
        // rather than throwing or calling the model.
        var workDir = Path.Combine(Path.GetTempPath(), "pb-ret-" + Guid.NewGuid().ToString("N"));
        var expConfig = new ExpConfig { TaskName = "diagram", WorkDir = workDir };
        using var http = new HttpClient();
        var generation = new GenerationClient(http, new ModelConfig());
        var agent = new RetrieverAgent(expConfig, generation);

        var data = new QueryData();
        data["content"] = "some method";
        data["visual_intent"] = "a caption";

        var result = await agent.ProcessAsync(data, "auto");

        Assert.Empty((List<object?>)result["top10_references"]!);
    }
}
