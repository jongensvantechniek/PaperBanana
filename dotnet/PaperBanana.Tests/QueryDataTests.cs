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

using System.Text.Json.Nodes;
using PaperBanana.Core.Models;

namespace PaperBanana.Tests;

public class QueryDataTests
{
    [Fact]
    public void FromJsonObject_ParsesScalarsAndNesting()
    {
        var obj = (JsonObject)JsonNode.Parse("""
        {
            "content": "hello",
            "visual_intent": "a caption",
            "candidate_id": 3,
            "additional_info": { "rounded_ratio": "16:9" }
        }
        """)!;

        var data = QueryData.FromJsonObject(obj);

        Assert.Equal("hello", data.GetString("content"));
        Assert.Equal("a caption", data.GetString("visual_intent"));
        Assert.Equal(3, data.GetInt("candidate_id", -1));
        Assert.Equal("16:9", data.GetAdditionalInfo("rounded_ratio"));
    }

    [Fact]
    public void GetContentAsString_SerializesObjectContent()
    {
        var obj = (JsonObject)JsonNode.Parse("""{ "content": { "rows": [1, 2, 3] } }""")!;
        var data = QueryData.FromJsonObject(obj);

        var content = data.GetContentAsString("content");

        Assert.Contains("\"rows\"", content);
        Assert.Contains("1", content);
    }

    [Fact]
    public void GetString_ReturnsFallbackForMissingKey()
    {
        var data = new QueryData();
        Assert.Equal("fallback", data.GetString("missing", "fallback"));
        Assert.False(data.ContainsKey("missing"));
    }

    [Fact]
    public void Set_RoundTripsThroughIndexer()
    {
        var data = new QueryData();
        data["target_diagram_desc0"] = "some description";

        Assert.True(data.ContainsKey("target_diagram_desc0"));
        Assert.Equal("some description", data.GetString("target_diagram_desc0"));
    }

    [Fact]
    public void GetInt_ParsesNumericStringsAndLongs()
    {
        var data = new QueryData();
        data["a"] = 5L;
        data["b"] = "7";
        data["c"] = "not a number";

        Assert.Equal(5, data.GetInt("a", 0));
        Assert.Equal(7, data.GetInt("b", 0));
        Assert.Equal(99, data.GetInt("c", 99));
    }
}
