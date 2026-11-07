using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    internal readonly struct TalentTreeBakeCurve
    {
        public readonly int FromId;
        public readonly int ToId;
        public readonly Vector2 Start;
        public readonly Vector2 Control1;
        public readonly Vector2 Control2;
        public readonly Vector2 End;

        public TalentTreeBakeCurve(int fromId, int toId, Vector2 start, Vector2 end,
            Vector2 fromHandle, Vector2 toHandle)
        {
            FromId = fromId;
            ToId = toId;
            Start = start;
            Control1 = start + fromHandle;
            Control2 = end + toHandle;
            End = end;
        }
    }

    internal sealed class TalentTreeBakedTile
    {
        public int x;
        public int y;
        public Vector2 position;
        public Vector2 size;
        public string assetPath;
        public Sprite sprite;
    }

    internal sealed class TalentTreeBakedEdge
    {
        public int fromId;
        public int toId;
        public Vector2 position;
        public Vector2 size;
        public string atlasPath;
        public string spriteName;
        public float pixelsPerUnit;
    }

    internal sealed class TalentTreeSpriteBakeResult
    {
        public readonly List<TalentTreeBakedTile> tiles = new List<TalentTreeBakedTile>();
        public readonly List<TalentTreeBakedEdge> edges = new List<TalentTreeBakedEdge>();
        public readonly List<string> generatedAssets = new List<string>();
    }

    internal static class TalentTreeSpriteBaker
    {
        public const int TileSize = 2048;
        public const int AtlasSize = 2048;
        private const float ActivePixelsPerUnit = 2f;
        private const int Gutter = 4;
        private const int MaxTiles = 512;

        private sealed class Placement
        {
            public TalentTreeBakeCurve curve;
            public TalentTreeBakedEdge record;
            public Vector2 worldMin;
            public int width;
            public int height;
            public string atlasName;
            public int x;
            public int y;
            public float pixelsPerUnit;
        }

        public static TalentTreeSpriteBakeResult Bake(IReadOnlyList<TalentTreeBakeCurve> curves,
            TalentTreeLayout layout, string assetFolder)
        {
            if (!AssetDatabase.IsValidFolder(assetFolder))
            {
                string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
            }
            var result = new TalentTreeSpriteBakeResult();
            try
            {
                var tiles = GroupTiles(curves, layout.lineWidth);
                int tileIndex = 0;
                foreach (var pair in tiles.OrderBy(pair => pair.Key.x).ThenBy(pair => pair.Key.y))
                {
                    EditorUtility.DisplayProgressBar("烘焙天赋树线条",
                        "未激活底图 " + (tileIndex + 1) + "/" + tiles.Count,
                        0.7f * tileIndex / Mathf.Max(1, tiles.Count));
                    var pixels = new Color32[TileSize * TileSize];
                    Vector2 tileMin = new Vector2(pair.Key.x * TileSize, pair.Key.y * TileSize);
                    foreach (var curve in pair.Value)
                        Rasterize(pixels, TileSize, TileSize, tileMin, 1, 0, 0, curve,
                            layout.lineWidth, layout.lineColor);
                    string path = assetFolder + "/Inactive_" + pair.Key.x + "_" + pair.Key.y + ".png";
                    WriteTexture(path, TileSize, TileSize, pixels, false, null, 1);
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null) throw new InvalidOperationException("无法导入未激活线条图片：" + path);
                    result.tiles.Add(new TalentTreeBakedTile
                    {
                        x = pair.Key.x, y = pair.Key.y, position = tileMin + Vector2.one * (TileSize * 0.5f),
                        size = Vector2.one * TileSize, assetPath = path, sprite = sprite
                    });
                    result.generatedAssets.Add(path);
                    tileIndex++;
                }

                var placements = PackActive(curves, layout.lineWidth * layout.activeLineWidthMultiplier);
                var atlasGroups = placements.GroupBy(placement => placement.atlasName)
                    .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
                for (int atlasIndex = 0; atlasIndex < atlasGroups.Length; atlasIndex++)
                {
                    var atlasGroup = atlasGroups[atlasIndex];
                    EditorUtility.DisplayProgressBar("烘焙天赋树线条",
                        "激活线图集 " + (atlasIndex + 1) + "/" + atlasGroups.Length,
                        0.7f + 0.3f * atlasIndex / Mathf.Max(1, atlasGroups.Length));
                    int width = Mathf.NextPowerOfTwo(atlasGroup.Max(item => item.x + item.width + Gutter));
                    int height = Mathf.NextPowerOfTwo(atlasGroup.Max(item => item.y + item.height + Gutter));
                    var pixels = new Color32[width * height];
                    var metadata = new List<SpriteMetaData>();
                    string path = assetFolder + "/" + atlasGroup.Key + ".png";
                    foreach (var item in atlasGroup)
                    {
                        Rasterize(pixels, width, height, item.worldMin, item.pixelsPerUnit,
                            item.x, item.y, item.curve,
                            layout.lineWidth * layout.activeLineWidthMultiplier, layout.activeLineColor,
                            item.width, item.height);
                        metadata.Add(new SpriteMetaData
                        {
                            name = item.record.spriteName,
                            rect = new Rect(item.x, item.y, item.width, item.height),
                            alignment = (int)SpriteAlignment.Center,
                            pivot = new Vector2(0.5f, 0.5f)
                        });
                        item.record.atlasPath = path;
                        result.edges.Add(item.record);
                    }
                    WriteTexture(path, width, height, pixels, true, metadata.ToArray(), ActivePixelsPerUnit);
                    result.generatedAssets.Add(path);
                }
                result.edges.Sort((a, b) => a.fromId != b.fromId ? a.fromId.CompareTo(b.fromId) : a.toId.CompareTo(b.toId));
                result.generatedAssets.Sort(StringComparer.Ordinal);
                return result;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static Dictionary<Vector2Int, List<TalentTreeBakeCurve>> GroupTiles(
            IReadOnlyList<TalentTreeBakeCurve> curves, float lineWidth)
        {
            var tiles = new Dictionary<Vector2Int, List<TalentTreeBakeCurve>>();
            float margin = lineWidth * 0.5f + 1f;
            foreach (var curve in curves)
            {
                ControlBounds(curve, out Vector2 min, out Vector2 max);
                int minX = Mathf.FloorToInt((min.x - margin) / TileSize);
                int maxX = Mathf.FloorToInt((max.x + margin) / TileSize);
                int minY = Mathf.FloorToInt((min.y - margin) / TileSize);
                int maxY = Mathf.FloorToInt((max.y + margin) / TileSize);
                if ((long)(maxX - minX + 1) * (maxY - minY + 1) > MaxTiles)
                    throw new InvalidOperationException("一条天赋连线跨越过多图片分块，请缩短曲线或拆分连线。");
                for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                {
                    var key = new Vector2Int(x, y);
                    if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = new List<TalentTreeBakeCurve>();
                    list.Add(curve);
                }
                if (tiles.Count > MaxTiles)
                    throw new InvalidOperationException("天赋树需要超过 512 张底图，请缩小布局范围或分树导出。");
            }
            return tiles;
        }

        private static List<Placement> PackActive(IReadOnlyList<TalentTreeBakeCurve> curves, float lineWidth)
        {
            var result = new List<Placement>(curves.Count);
            var buckets = curves.GroupBy(curve =>
            {
                Vector2 center = (curve.Start + curve.End) * 0.5f;
                return new Vector2Int(Mathf.FloorToInt((center.x + TileSize * 0.5f) / TileSize),
                    Mathf.FloorToInt((center.y + TileSize * 0.5f) / TileSize));
            }).OrderBy(group => group.Key.x).ThenBy(group => group.Key.y);
            foreach (var bucket in buckets)
            {
                int x = Gutter, y = Gutter, rowHeight = 0, atlas = 0;
                foreach (var curve in bucket.OrderBy(curve => curve.FromId).ThenBy(curve => curve.ToId))
                {
                    ControlBounds(curve, out Vector2 min, out Vector2 max);
                    float pixelsPerUnit = ActivePixelsPerUnit;
                    int left = 0, bottom = 0, right = 0, top = 0, width = 0, height = 0;
                    for (int resolution = 0; resolution < 12; resolution++)
                    {
                        float margin = lineWidth * 0.5f + 2f / pixelsPerUnit;
                        left = Mathf.FloorToInt((min.x - margin) * pixelsPerUnit);
                        bottom = Mathf.FloorToInt((min.y - margin) * pixelsPerUnit);
                        right = Mathf.CeilToInt((max.x + margin) * pixelsPerUnit);
                        top = Mathf.CeilToInt((max.y + margin) * pixelsPerUnit);
                        width = Mathf.Max(1, right - left);
                        height = Mathf.Max(1, top - bottom);
                        if (width + Gutter * 2 <= AtlasSize && height + Gutter * 2 <= AtlasSize) break;
                        pixelsPerUnit *= 0.5f;
                    }
                    if (width + Gutter * 2 > AtlasSize || height + Gutter * 2 > AtlasSize)
                        throw new InvalidOperationException("激活连线 " + curve.FromId + "→" + curve.ToId +
                            " 的坐标跨度过大，无法烘焙到 2048 像素图集。");
                    if (x + width + Gutter > AtlasSize)
                    {
                        x = Gutter;
                        y += rowHeight + Gutter;
                        rowHeight = 0;
                    }
                    if (y + height + Gutter > AtlasSize)
                    {
                        atlas++;
                        x = y = Gutter;
                        rowHeight = 0;
                    }
                    var worldMin = new Vector2(left, bottom) / pixelsPerUnit;
                    var size = new Vector2(width, height) / pixelsPerUnit;
                    result.Add(new Placement
                    {
                        curve = curve, worldMin = worldMin, width = width, height = height,
                        atlasName = "ActiveAtlas_" + bucket.Key.x + "_" + bucket.Key.y + "_" +
                            atlas.ToString("D3"), x = x, y = y, pixelsPerUnit = pixelsPerUnit,
                        record = new TalentTreeBakedEdge
                        {
                            fromId = curve.FromId, toId = curve.ToId,
                            position = worldMin + size * 0.5f, size = size,
                            pixelsPerUnit = pixelsPerUnit,
                            spriteName = "Edge_" + curve.FromId + "_" + curve.ToId
                        }
                    });
                    x += width + Gutter;
                    rowHeight = Mathf.Max(rowHeight, height);
                }
            }
            return result;
        }

        private static void ControlBounds(TalentTreeBakeCurve curve, out Vector2 min, out Vector2 max)
        {
            min = Vector2.Min(Vector2.Min(curve.Start, curve.End), Vector2.Min(curve.Control1, curve.Control2));
            max = Vector2.Max(Vector2.Max(curve.Start, curve.End), Vector2.Max(curve.Control1, curve.Control2));
        }

        private static Vector2 Bezier(TalentTreeBakeCurve curve, float t)
        {
            float u = 1f - t;
            return u * u * u * curve.Start + 3f * u * u * t * curve.Control1 +
                   3f * u * t * t * curve.Control2 + t * t * t * curve.End;
        }

        private static void Rasterize(Color32[] pixels, int textureWidth, int textureHeight,
            Vector2 worldMin, float pixelsPerUnit, int pixelOffsetX, int pixelOffsetY,
            TalentTreeBakeCurve curve, float lineWidth, Color color, int clipWidth = -1, int clipHeight = -1)
        {
            int count = Mathf.Clamp(Mathf.CeilToInt((Vector2.Distance(curve.Start, curve.Control1) +
                Vector2.Distance(curve.Control1, curve.Control2) + Vector2.Distance(curve.Control2, curve.End)) *
                pixelsPerUnit / 3f), 12, 256);
            float radius = lineWidth * 0.5f;
            float feather = 0.5f / pixelsPerUnit;
            int maxX = pixelOffsetX + (clipWidth < 0 ? textureWidth : clipWidth) - 1;
            int maxY = pixelOffsetY + (clipHeight < 0 ? textureHeight : clipHeight) - 1;
            Color32 pigment = color;
            Vector2 previous = curve.Start;
            for (int step = 1; step <= count; step++)
            {
                Vector2 current = Bezier(curve, step / (float)count);
                Vector2 low = Vector2.Min(previous, current) - Vector2.one * (radius + feather);
                Vector2 high = Vector2.Max(previous, current) + Vector2.one * (radius + feather);
                int left = Mathf.Max(pixelOffsetX, Mathf.FloorToInt((low.x - worldMin.x) * pixelsPerUnit) + pixelOffsetX);
                int right = Mathf.Min(maxX, Mathf.CeilToInt((high.x - worldMin.x) * pixelsPerUnit) + pixelOffsetX);
                int bottom = Mathf.Max(pixelOffsetY, Mathf.FloorToInt((low.y - worldMin.y) * pixelsPerUnit) + pixelOffsetY);
                int top = Mathf.Min(maxY, Mathf.CeilToInt((high.y - worldMin.y) * pixelsPerUnit) + pixelOffsetY);
                Vector2 segment = current - previous;
                float lengthSquared = segment.sqrMagnitude;
                for (int py = bottom; py <= top; py++)
                for (int px = left; px <= right; px++)
                {
                    Vector2 world = worldMin + new Vector2(px - pixelOffsetX + 0.5f,
                        py - pixelOffsetY + 0.5f) / pixelsPerUnit;
                    float projection = lengthSquared > 0f
                        ? Mathf.Clamp01(Vector2.Dot(world - previous, segment) / lengthSquared) : 0f;
                    float distance = Vector2.Distance(world, previous + segment * projection);
                    float coverage = Mathf.Clamp01((radius - distance) * pixelsPerUnit + 0.5f);
                    if (coverage <= 0f) continue;
                    int index = py * textureWidth + px;
                    float sourceAlpha = color.a * coverage;
                    float destinationAlpha = pixels[index].a / 255f;
                    float combined = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
                    pixels[index] = new Color32(pigment.r, pigment.g, pigment.b,
                        (byte)Mathf.RoundToInt(combined * 255f));
                }
                previous = current;
            }
        }

        private static void WriteTexture(string assetPath, int width, int height, Color32[] pixels,
            bool multiple, SpriteMetaData[] sprites, float pixelsPerUnit)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            byte[] bytes;
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                bytes = texture.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            string fullPath = Path.GetFullPath(assetPath);
            bool changed = !File.Exists(fullPath) || !File.ReadAllBytes(fullPath).SequenceEqual(bytes);
            if (changed)
            {
                File.WriteAllBytes(fullPath, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("无法配置线条图片导入：" + assetPath);
            bool settingsChanged = importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != (multiple ? SpriteImportMode.Multiple : SpriteImportMode.Single) ||
                importer.spritePixelsPerUnit != pixelsPerUnit || importer.mipmapEnabled ||
                importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                importer.maxTextureSize != AtlasSize || importer.npotScale != TextureImporterNPOTScale.None ||
                !importer.alphaIsTransparency || importer.filterMode != FilterMode.Bilinear;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = multiple ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = AtlasSize;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.isReadable = false;
            if (multiple && !SameSprites(importer.spritesheet, sprites))
            {
                importer.spritesheet = sprites;
                settingsChanged = true;
            }
            settingsChanged |= SetPlatform(importer, "Android", TextureImporterFormat.ASTC_4x4);
            settingsChanged |= SetPlatform(importer, "iPhone", TextureImporterFormat.ASTC_4x4);
            settingsChanged |= SetPlatform(importer, "Standalone", TextureImporterFormat.DXT5);
            if (settingsChanged) importer.SaveAndReimport();
        }

        private static bool SameSprites(SpriteMetaData[] previous, SpriteMetaData[] next)
        {
            if (previous == null || next == null || previous.Length != next.Length) return false;
            for (int i = 0; i < previous.Length; i++)
                if (previous[i].name != next[i].name || previous[i].rect != next[i].rect ||
                    previous[i].pivot != next[i].pivot) return false;
            return true;
        }

        private static bool SetPlatform(TextureImporter importer, string name, TextureImporterFormat format)
        {
            var settings = importer.GetPlatformTextureSettings(name);
            bool changed = !settings.overridden || settings.format != format ||
                settings.maxTextureSize != AtlasSize || settings.compressionQuality != 100;
            if (!changed) return false;
            settings.name = name;
            settings.overridden = true;
            settings.format = format;
            settings.maxTextureSize = AtlasSize;
            settings.compressionQuality = 100;
            importer.SetPlatformTextureSettings(settings);
            return true;
        }
    }
}
