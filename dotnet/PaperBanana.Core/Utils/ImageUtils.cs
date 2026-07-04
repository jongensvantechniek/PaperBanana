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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PaperBanana.Core.Utils;

/// <summary>
/// Image helpers. Port of <c>utils/image_utils.py</c>, using ImageSharp instead
/// of Pillow.
/// </summary>
public static class ImageUtils
{
    /// <summary>
    /// Convert a base64-encoded PNG (or any supported format) into a
    /// base64-encoded JPEG. Returns <c>null</c> if conversion fails, matching
    /// the Python behaviour.
    /// </summary>
    public static string? ConvertPngB64ToJpgB64(string? pngB64)
    {
        try
        {
            if (string.IsNullOrEmpty(pngB64) || pngB64.Length < 10)
            {
                Console.WriteLine("⚠️  Invalid base64 string (too short).");
                return null;
            }

            var bytes = Convert.FromBase64String(pngB64);
            using var image = Image.Load<Rgb24>(bytes);
            using var output = new MemoryStream();
            image.Save(output, new JpegEncoder { Quality = 95 });
            return Convert.ToBase64String(output.ToArray());
        }
        catch (Exception e)
        {
            Console.WriteLine($"❌ Error converting image: {e.Message}");
            return null;
        }
    }
}
