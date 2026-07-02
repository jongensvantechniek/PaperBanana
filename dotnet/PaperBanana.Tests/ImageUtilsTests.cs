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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PaperBanana.Tests;

public class ImageUtilsTests
{
    [Fact]
    public void ConvertPngB64ToJpgB64_ProducesValidJpeg()
    {
        var pngB64 = MakePngBase64(16, 16);

        var jpgB64 = ImageUtils.ConvertPngB64ToJpgB64(pngB64);

        Assert.NotNull(jpgB64);
        var bytes = Convert.FromBase64String(jpgB64!);
        // JPEG files start with the FF D8 FF magic bytes.
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        Assert.Equal(0xFF, bytes[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ConvertPngB64ToJpgB64_ReturnsNullForInvalidInput(string? input)
    {
        Assert.Null(ImageUtils.ConvertPngB64ToJpgB64(input));
    }

    private static string MakePngBase64(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return Convert.ToBase64String(ms.ToArray());
    }
}
