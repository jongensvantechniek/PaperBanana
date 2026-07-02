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

using PaperBanana.Core.Configuration;

namespace PaperBanana.Tests;

public class ConfigTests
{
    [Fact]
    public void ModelConfig_LoadsFromYaml()
    {
        var yaml = """
        defaults:
          model_name: "gemini-3-pro-preview"
          image_model_name: "gemini-3-pro-image-preview"
        api_keys:
          google_api_key: "yaml-google-key"
        services:
          plot_service_url: "http://localhost:9999"
        """;
        var path = WriteTemp(yaml);
        try
        {
            var config = ModelConfig.Load(path);

            Assert.Equal("gemini-3-pro-preview", config.ModelName);
            Assert.Equal("gemini-3-pro-image-preview", config.ImageModelName);
            Assert.Equal("yaml-google-key", config.GoogleApiKey);
            Assert.Equal("http://localhost:9999", config.PlotServiceUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ModelConfig_EnvironmentVariableTakesPrecedenceOverYaml()
    {
        var yaml = """
        defaults:
          model_name: "yaml-model"
        """;
        var path = WriteTemp(yaml);
        Environment.SetEnvironmentVariable("MODEL_NAME", "env-model");
        try
        {
            var config = ModelConfig.Load(path);
            Assert.Equal("env-model", config.ModelName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MODEL_NAME", null);
            File.Delete(path);
        }
    }

    [Fact]
    public void ExpConfig_InitializeComputesExpNameAndResultDir()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "pb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var exp = new ExpConfig
            {
                DatasetName = "PaperBananaBench",
                TaskName = "diagram",
                SplitName = "test",
                ExpMode = "dev_full",
                RetrievalSetting = "auto",
                WorkDir = workDir,
                Timestamp = "0101_1200",
            };
            exp.Initialize(new ModelConfig { ModelName = "m", ImageModelName = "im" });

            Assert.Equal("0101_1200_autoret_dev_full_test", exp.ExpName);
            Assert.Equal("m", exp.ModelName);
            Assert.Equal("im", exp.ImageModelName);
            Assert.True(Directory.Exists(exp.ResultDir));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "pb-config-" + Guid.NewGuid().ToString("N") + ".yaml");
        File.WriteAllText(path, content);
        return path;
    }
}
