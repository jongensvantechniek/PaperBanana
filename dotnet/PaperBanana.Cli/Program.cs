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

// Main entry point for the PaperBanana CLI. Port of main.py.

using System.Text.Json;
using System.Text.Json.Nodes;
using PaperBanana.Core;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Models;
using PaperBanana.Core.Utils;

var options = ParseArgs(args);

var workDir = options.TryGetValue("work_dir", out var wd) && !string.IsNullOrEmpty(wd)
    ? Path.GetFullPath(wd)
    : ResolveWorkDir();

Console.WriteLine($"Work directory: {workDir}");

var modelConfig = ModelConfig.Load(Path.Combine(workDir, "configs", "model_config.yaml"));

var expConfig = new ExpConfig
{
    DatasetName = options.GetValueOrDefault("dataset_name", "PaperBananaBench"),
    TaskName = options.GetValueOrDefault("task_name", "diagram"),
    SplitName = options.GetValueOrDefault("split_name", "test"),
    ExpMode = options.GetValueOrDefault("exp_mode", "dev"),
    RetrievalSetting = options.GetValueOrDefault("retrieval_setting", "auto"),
    MaxCriticRounds = int.TryParse(options.GetValueOrDefault("max_critic_rounds", "3"), out var mcr) ? mcr : 3,
    ModelName = options.GetValueOrDefault("model_name", string.Empty),
    WorkDir = workDir,
};
expConfig.Initialize(modelConfig);

var inputFile = Path.Combine(workDir, "data", expConfig.DatasetName, expConfig.TaskName, $"{expConfig.SplitName}.json");
var outputFile = Path.Combine(expConfig.ResultDir, $"{expConfig.ExpName}.json");
Console.WriteLine($"Input file: {inputFile}  Output file: {outputFile}");

var dataList = LoadInput(inputFile);

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var generation = new GenerationClient(httpClient, modelConfig);
var plotExecutor = new PlotExecutorClient(httpClient, modelConfig);
var factory = new PaperVizFactory(generation, plotExecutor);
var processor = factory.CreateProcessor(expConfig);

const int concurrentNum = 10;
Console.WriteLine($"Using max concurrency: {concurrentNum}");

var allResults = new List<QueryData>();
int idx = 0;
await foreach (var result in processor.ProcessQueriesBatchAsync(dataList, maxConcurrent: concurrentNum))
{
    allResults.Add(result);
    idx++;
    if (idx % 10 == 0)
    {
        SaveResults(allResults, outputFile);
    }
}

SaveResults(allResults, outputFile);
Console.WriteLine("Processing completed.");
return;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
        {
            continue;
        }

        var key = args[i][2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
        {
            result[key] = args[++i];
        }
        else
        {
            result[key] = "true";
        }
    }

    return result;
}

// Walk upward from the current directory to find the repo root (the folder that
// contains the style_guides directory). Falls back to the current directory.
static string ResolveWorkDir()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "style_guides")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static List<QueryData> LoadInput(string path)
{
    var list = new List<QueryData>();
    if (!File.Exists(path))
    {
        Console.WriteLine($"⚠️  Input file not found: {path}");
        return list;
    }

    if (JsonNode.Parse(File.ReadAllText(path)) is JsonArray array)
    {
        foreach (var node in array)
        {
            if (node is JsonObject obj)
            {
                list.Add(QueryData.FromJsonObject(obj));
            }
        }
    }

    return list;
}

static void SaveResults(List<QueryData> results, string outputFile)
{
    Console.WriteLine($"Incremental saving results (count: {results.Count}) to {outputFile}");
    var array = new JsonArray();
    foreach (var result in results)
    {
        array.Add(JsonSerializer.SerializeToNode(result.ToDictionary(), JsonDefaults.Options));
    }

    File.WriteAllText(outputFile, array.ToJsonString(JsonDefaults.IndentedOptions));
}
