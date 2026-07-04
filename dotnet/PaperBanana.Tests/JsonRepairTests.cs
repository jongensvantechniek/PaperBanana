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

using PaperBanana.Core.Utils;

namespace PaperBanana.Tests;

public class JsonRepairTests
{
    [Fact]
    public void TryParse_StripsMarkdownFences()
    {
        var raw = "```json\n{\"winner\": \"Model\"}\n```";
        var node = JsonRepair.TryParse(raw);

        Assert.NotNull(node);
        Assert.Equal("Model", node!["winner"]!.ToString());
    }

    [Fact]
    public void TryParse_ExtractsBalancedObjectFromChatter()
    {
        var raw = "Sure! Here is my answer:\n{\"critic_suggestions\": \"tighten spacing\"} \nHope that helps.";
        var node = JsonRepair.TryParse(raw);

        Assert.NotNull(node);
        Assert.Equal("tighten spacing", node!["critic_suggestions"]!.ToString());
    }

    [Fact]
    public void TryParse_ReturnsNullForNonJson()
    {
        Assert.Null(JsonRepair.TryParse("no json here at all"));
        Assert.Null(JsonRepair.TryParse(""));
    }

    [Fact]
    public void GetStringList_ExtractsIdArray()
    {
        var node = JsonRepair.TryParse("""{ "top10_diagrams": ["ref_1", "ref_25", "ref_100"] }""");
        var ids = JsonRepair.GetStringList(node, "top10_diagrams");

        Assert.Equal(new[] { "ref_1", "ref_25", "ref_100" }, ids);
    }

    [Fact]
    public void GetStringList_ReturnsEmptyForMissingField()
    {
        var node = JsonRepair.TryParse("""{ "other": [] }""");
        Assert.Empty(JsonRepair.GetStringList(node, "top10_plots"));
    }
}
