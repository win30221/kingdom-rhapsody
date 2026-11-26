using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace KingdomRhapsody.Editor
{
    [ScriptedImporter(1, "lobbyportrait")]
    public sealed class LobbyPortraitImporter : ScriptedImporter
    {
        [Serializable]
        private sealed class Crop
        {
            public string path;
            public int y;
            public int width;
            public int height;
            public float fraction;
        }

        public override void OnImportAsset(AssetImportContext context)
        {
            var crop = JsonUtility.FromJson<Crop>(File.ReadAllText(context.assetPath));
            string art = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art"));
            string sourcePath = Path.GetFullPath(Path.Combine(art, crop.path));
            if (!sourcePath.StartsWith(art + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(sourcePath))
            {
                context.LogImportError("Could not find source art. Open the Unity project in the full repository checkout.");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath), false))
                {
                    context.LogImportError("Could not read " + crop.path);
                    return;
                }

                int width = crop.width > 0 ? crop.width : source.width;
                int height = crop.height > 0 ? crop.height : Mathf.RoundToInt(source.height * crop.fraction);
                int bottom = source.height - crop.y - height;
                if (width > source.width || height <= 0 || bottom < 0 || crop.y < 0)
                {
                    context.LogImportError("Portrait is outside " + crop.path);
                    return;
                }

                Texture2D texture = Trim(source, width, height, bottom);
                string name = Path.GetFileNameWithoutExtension(context.assetPath);
                texture.name = name + " Texture";
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                context.AddObjectToAsset("texture", texture);
                context.AddObjectToAsset("sprite", sprite);
                context.SetMainObject(sprite);
            }
            finally { DestroyImmediate(source); }
        }

        private static Texture2D Trim(Texture2D source, int width, int height, int bottom)
        {
            const int padding = 4;
            Color32[] pixels = source.GetPixels32();
            int left = width, right = -1, low = height, high = -1;
            for (int y = 0; y < height; y++)
            {
                int row = (bottom + y) * source.width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x].a < 4)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    low = Math.Min(low, y);
                    high = Math.Max(high, y);
                }
            }
            if (right < left)
                throw new InvalidDataException("Empty portrait crop.");

            int w = right - left + 1, h = high - low + 1;
            int stride = w + padding * 2;
            var trimmed = new Color32[stride * (h + padding * 2)];
            for (int y = 0; y < h; y++)
                Array.Copy(pixels, (bottom + low + y) * source.width + left,
                    trimmed, (y + padding) * stride + padding, w);

            var texture = new Texture2D(stride, h + padding * 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(trimmed);
            texture.Apply(false, true);
            return texture;
        }
    }
}
