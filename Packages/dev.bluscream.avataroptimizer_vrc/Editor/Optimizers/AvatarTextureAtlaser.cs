using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Merges several materials into one by packing their textures into a shared atlas and rewriting the
    /// meshes' UV0 into the packed cells — the Unity-side equivalent of the Blender material-combiner step
    /// in the tutorial series, and the only way to get below a material slot limit that deduplication alone
    /// cannot reach (see <see cref="AvatarMaterialSlotOptimizer"/>).
    ///
    /// Atlasing is visually destructive and cannot be undone by re-running the optimizer, so every stage is
    /// gated conservatively: a group is only atlased when it is provably safe, and anything questionable is
    /// skipped with a logged reason rather than approximated.
    ///
    /// Safety gates:
    ///  • UV0 of every affected submesh must lie inside [0,1] — tiling or offset UVs cannot be packed.
    ///  • Materials must share a shader, render queue and keyword set, so one atlas material can stand in
    ///    for all of them. This is what keeps a transparent material out of an opaque atlas.
    ///  • All non-texture shader properties must be identical, since the merged material can only carry one
    ///    value for each.
    ///  • A mesh that shares vertices between submeshes is skipped, because one vertex cannot hold two
    ///    different atlas UVs.
    /// </summary>
    public static class AvatarTextureAtlaser
    {
        /// <summary>Transparent padding around each cell, to stop neighbours bleeding in at low mip levels.</summary>
        private const int CellPadding = 8;

        private const int MaxAtlasDimension = 4096;

        /// <summary>UV tolerance for the [0,1] containment test — catches float error without allowing real tiling.</summary>
        private const float UvEpsilon = 0.001f;

        /// <summary>Texture properties that hold non-color data and must be sampled in linear space.</summary>
        private static readonly HashSet<string> LinearTextureProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "_BumpMap", "_DetailNormalMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_ParallaxMap"
        };

        /// <summary>Neutral fill for a member that lacks a texture the rest of its group has.</summary>
        private static readonly Dictionary<string, Color> NeutralFill = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "_BumpMap", new Color(0.5f, 0.5f, 1f, 1f) },
            { "_DetailNormalMap", new Color(0.5f, 0.5f, 1f, 1f) },
            { "_EmissionMap", Color.black },
            { "_MetallicGlossMap", Color.black },
            { "_SpecGlossMap", Color.black },
            { "_OcclusionMap", Color.white }
        };

        /// <summary>
        /// Atlases material groups until the avatar fits <paramref name="maxMaterialSlots"/>.
        /// </summary>
        /// <returns>Number of material slots eliminated.</returns>
        public static int AtlasMaterials(
            GameObject avatarRoot,
            int maxMaterialSlots,
            string assetOutputDirectory,
            Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return 0;

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true)
                .Where(r => r != null && r.sharedMaterials != null)
                .ToArray();

            int currentSlots = renderers.Sum(r => r.sharedMaterials.Length);
            if (maxMaterialSlots == int.MaxValue || currentSlots <= maxMaterialSlots)
            {
                Debug.Log($"[AvatarTextureAtlaser] Material slots {currentSlots} / {maxMaterialSlots} already within budget — skipping atlasing.");
                return 0;
            }

            Debug.Log($"[AvatarTextureAtlaser] Material slots {currentSlots} > limit {maxMaterialSlots} — attempting to atlas.");

            // A mesh whose submeshes share vertices cannot carry per-submesh atlas UVs.
            var eligibleRenderers = new List<Renderer>();
            foreach (Renderer r in renderers)
            {
                Mesh mesh = GetMesh(r);
                if (mesh == null) continue;
                if (SharesVerticesBetweenSubMeshes(mesh))
                {
                    Debug.Log($"[AvatarTextureAtlaser] Skipping '{r.name}': its submeshes share vertices, which cannot hold two different atlas UVs.");
                    continue;
                }
                eligibleRenderers.Add(r);
            }

            if (eligibleRenderers.Count == 0)
            {
                Debug.LogWarning("[AvatarTextureAtlaser] No renderer is eligible for atlasing.");
                return 0;
            }

            // Only materials whose every usage has in-range UVs can be packed.
            Dictionary<Material, List<SubMeshUsage>> usage = CollectUsages(eligibleRenderers);
            List<Material> atlasable = usage.Keys.Where(m => IsAtlasable(m, usage[m])).ToList();

            List<List<Material>> groups = GroupCompatibleMaterials(atlasable);
            // Biggest groups collapse the most slots per atlas built.
            groups = groups.Where(g => g.Count >= 2).OrderByDescending(g => g.Count).ToList();

            if (groups.Count == 0)
            {
                Debug.LogWarning($"[AvatarTextureAtlaser] Material slots are over budget ({currentSlots} / {maxMaterialSlots}) but no two materials are compatible enough to atlas together.");
                return 0;
            }

            int eliminated = 0;
            foreach (List<Material> group in groups)
            {
                if (currentSlots - eliminated <= maxMaterialSlots)
                {
                    Debug.Log($"[AvatarTextureAtlaser] Slot budget reached ({currentSlots - eliminated} / {maxMaterialSlots}) — stopping before over-atlasing.");
                    break;
                }

                progressCallback?.Invoke($"Atlasing {group.Count} materials...");
                int saved = AtlasGroup(group, usage, avatarRoot, assetOutputDirectory);
                eliminated += saved;
            }

            Debug.Log($"[AvatarTextureAtlaser] Complete: eliminated {eliminated} material slot(s).");
            return eliminated;
        }

        /// <summary>One submesh of one renderer drawn with a particular material.</summary>
        private sealed class SubMeshUsage
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public int SubMeshIndex;
        }

        private static Dictionary<Material, List<SubMeshUsage>> CollectUsages(List<Renderer> renderers)
        {
            var usage = new Dictionary<Material, List<SubMeshUsage>>();

            foreach (Renderer r in renderers)
            {
                Mesh mesh = GetMesh(r);
                if (mesh == null) continue;

                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;

                    if (!usage.TryGetValue(m, out List<SubMeshUsage> list))
                        usage[m] = list = new List<SubMeshUsage>();

                    list.Add(new SubMeshUsage { Renderer = r, Mesh = mesh, SubMeshIndex = i });
                }
            }

            return usage;
        }

        /// <summary>A material is atlasable only if every submesh drawn with it keeps UV0 inside [0,1].</summary>
        private static bool IsAtlasable(Material material, List<SubMeshUsage> usages)
        {
            if (material == null || material.shader == null) return false;

            foreach (SubMeshUsage u in usages)
            {
                if (!SubMeshUvsInUnitRange(u.Mesh, u.SubMeshIndex))
                {
                    Debug.Log($"[AvatarTextureAtlaser] Material '{material.name}' cannot be atlased: submesh {u.SubMeshIndex} of '{u.Mesh.name}' has UVs outside [0,1] (tiling or offset).");
                    return false;
                }
            }
            return true;
        }

        private static bool SubMeshUvsInUnitRange(Mesh mesh, int subMeshIndex)
        {
            Vector2[] uvs = mesh.uv;
            if (uvs == null || uvs.Length == 0) return false;

            int[] tris = mesh.GetTriangles(subMeshIndex);
            foreach (int idx in tris)
            {
                if (idx < 0 || idx >= uvs.Length) return false;
                Vector2 uv = uvs[idx];
                if (float.IsNaN(uv.x) || float.IsNaN(uv.y)) return false;
                if (uv.x < -UvEpsilon || uv.x > 1f + UvEpsilon) return false;
                if (uv.y < -UvEpsilon || uv.y > 1f + UvEpsilon) return false;
            }
            return true;
        }

        /// <summary>
        /// True if any vertex is referenced by more than one submesh — such a vertex would need two
        /// different atlas UVs at once.
        /// </summary>
        private static bool SharesVerticesBetweenSubMeshes(Mesh mesh)
        {
            if (mesh.subMeshCount <= 1) return false;

            var seen = new HashSet<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var thisSub = new HashSet<int>(mesh.GetTriangles(sub));
                if (thisSub.Overlaps(seen)) return true;
                seen.UnionWith(thisSub);
            }
            return false;
        }

        /// <summary>
        /// Buckets materials that one atlas material could stand in for: same shader, render queue, keywords,
        /// and identical values for every non-texture property.
        /// </summary>
        private static List<List<Material>> GroupCompatibleMaterials(List<Material> materials)
        {
            var groups = new List<List<Material>>();

            foreach (Material m in materials)
            {
                List<Material> match = groups.FirstOrDefault(g => AreCompatible(g[0], m));
                if (match != null) match.Add(m);
                else groups.Add(new List<Material> { m });
            }

            return groups;
        }

        private static bool AreCompatible(Material a, Material b)
        {
            if (a == null || b == null) return false;
            if (a == b) return false;
            if (a.shader != b.shader) return false;
            if (a.renderQueue != b.renderQueue) return false;
            if (!a.shaderKeywords.OrderBy(k => k).SequenceEqual(b.shaderKeywords.OrderBy(k => k))) return false;

            Shader shader = a.shader;
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                string prop = ShaderUtil.GetPropertyName(shader, i);
                switch (ShaderUtil.GetPropertyType(shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        // Textures are what the atlas merges, but a non-default tiling/offset would
                        // invalidate the packed UVs.
                        if (a.GetTextureScale(prop) != Vector2.one || b.GetTextureScale(prop) != Vector2.one) return false;
                        if (a.GetTextureOffset(prop) != Vector2.zero || b.GetTextureOffset(prop) != Vector2.zero) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        if (a.GetColor(prop) != b.GetColor(prop)) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        if (a.GetVector(prop) != b.GetVector(prop)) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        if (!Mathf.Approximately(a.GetFloat(prop), b.GetFloat(prop))) return false;
                        break;
                }
            }
            return true;
        }

        /// <summary>
        /// Packs one compatible group into a shared atlas, rewrites the affected UVs, and collapses the
        /// group's submeshes onto a single new material.
        /// </summary>
        /// <returns>Material slots eliminated.</returns>
        private static int AtlasGroup(
            List<Material> group,
            Dictionary<Material, List<SubMeshUsage>> usage,
            GameObject avatarRoot,
            string assetOutputDirectory)
        {
            // Every texture property any member uses must appear in the atlas set, so the packed layout
            // stays identical across maps and one set of UVs addresses all of them.
            List<string> textureProperties = CollectTextureProperties(group);
            if (textureProperties.Count == 0)
            {
                Debug.Log($"[AvatarTextureAtlaser] Group of {group.Count} materials has no textures to pack — skipping.");
                return 0;
            }

            string primaryProperty = textureProperties.Contains("_MainTex") ? "_MainTex" : textureProperties[0];

            // Cell size follows the material's own primary texture, so a low-res material does not get
            // upscaled into the atlas and waste space.
            var entries = new List<TextureAtlasPacker.PackEntry>();
            foreach (Material m in group)
            {
                Vector2Int size = GetSourceSize(m, primaryProperty);
                entries.Add(new TextureAtlasPacker.PackEntry
                {
                    Key = m,
                    Width = size.x + CellPadding * 2,
                    Height = size.y + CellPadding * 2
                });
            }

            TextureAtlasPacker.PackResult pack = PackWithDownscale(entries);
            if (!pack.Success)
            {
                Debug.LogWarning($"[AvatarTextureAtlaser] Could not pack {group.Count} materials into a {MaxAtlasDimension}px atlas even after downscaling — skipping this group.");
                return 0;
            }

            Debug.Log($"[AvatarTextureAtlaser] Packed {group.Count} materials into a {pack.Width}x{pack.Height} atlas across {textureProperties.Count} texture map(s).");
            OptimizerLog.Verbose("AvatarTextureAtlaser", $"  atlas maps: {string.Join(", ", textureProperties)} (primary '{primaryProperty}')");
            foreach (TextureAtlasPacker.PackEntry e in pack.Entries)
            {
                TextureAtlasPacker.PackEntry entry = e;
                OptimizerLog.Trace("AvatarTextureAtlaser", () =>
                    $"  cell '{((Material)entry.Key).name}': {entry.Width}x{entry.Height} at ({entry.X},{entry.Y})" +
                    $"{(entry.Placed ? "" : " [UNPLACED]")}");
            }

            // Build one atlas per texture property, all sharing the packed layout.
            var atlases = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (string prop in textureProperties)
            {
                Texture2D atlas = BuildAtlas(prop, pack, avatarRoot.name, assetOutputDirectory);
                if (atlas != null) atlases[prop] = atlas;
            }

            if (!atlases.ContainsKey(primaryProperty))
            {
                Debug.LogWarning($"[AvatarTextureAtlaser] Failed to build the primary '{primaryProperty}' atlas — skipping this group.");
                return 0;
            }

            Material atlasMaterial = CreateAtlasMaterial(group[0], atlases, avatarRoot.name, assetOutputDirectory);
            if (atlasMaterial == null) return 0;

            var rects = pack.Entries
                .Where(e => e.Placed)
                .ToDictionary(e => (Material)e.Key, e => ToUvRect(e, pack.Width, pack.Height));

            return ApplyAtlasMaterial(group, usage, atlasMaterial, rects, avatarRoot.name, assetOutputDirectory);
        }

        /// <summary>
        /// Tries to pack at full resolution, halving every cell until it fits inside the atlas cap.
        /// </summary>
        private static TextureAtlasPacker.PackResult PackWithDownscale(List<TextureAtlasPacker.PackEntry> entries)
        {
            var original = entries.Select(e => new Vector2Int(e.Width, e.Height)).ToList();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                int divisor = 1 << attempt;
                for (int i = 0; i < entries.Count; i++)
                {
                    entries[i].Width = Mathf.Max(CellPadding * 2 + 4, original[i].x / divisor);
                    entries[i].Height = Mathf.Max(CellPadding * 2 + 4, original[i].y / divisor);
                    entries[i].Placed = false;
                }

                TextureAtlasPacker.PackResult result = TextureAtlasPacker.Pack(entries, MaxAtlasDimension);
                if (result.Success)
                {
                    if (attempt > 0)
                        Debug.Log($"[AvatarTextureAtlaser] Atlas cells downscaled {divisor}x to fit within {MaxAtlasDimension}px.");
                    return result;
                }
            }

            return new TextureAtlasPacker.PackResult();
        }

        /// <summary>The inner (unpadded) region of a packed cell, in normalized atlas coordinates.</summary>
        private static Rect ToUvRect(TextureAtlasPacker.PackEntry entry, int atlasWidth, int atlasHeight)
        {
            float x = (entry.X + CellPadding) / (float)atlasWidth;
            float y = (entry.Y + CellPadding) / (float)atlasHeight;
            float w = (entry.Width - CellPadding * 2) / (float)atlasWidth;
            float h = (entry.Height - CellPadding * 2) / (float)atlasHeight;
            return new Rect(x, y, w, h);
        }

        private static List<string> CollectTextureProperties(List<Material> group)
        {
            var props = new List<string>();
            Shader shader = group[0].shader;
            int count = ShaderUtil.GetPropertyCount(shader);

            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;

                string prop = ShaderUtil.GetPropertyName(shader, i);
                // Only pack properties at least one member actually uses.
                if (group.Any(m => m.HasProperty(prop) && m.GetTexture(prop) is Texture2D))
                    props.Add(prop);
            }

            return props;
        }

        private static Vector2Int GetSourceSize(Material material, string property)
        {
            if (material.HasProperty(property) && material.GetTexture(property) is Texture2D tex)
                return new Vector2Int(Mathf.Max(4, tex.width), Mathf.Max(4, tex.height));

            return new Vector2Int(256, 256);
        }

        /// <summary>
        /// Renders every member's texture for one property into the packed layout and saves it as a PNG.
        /// </summary>
        private static Texture2D BuildAtlas(
            string property,
            TextureAtlasPacker.PackResult pack,
            string avatarName,
            string assetOutputDirectory)
        {
            bool linear = LinearTextureProperties.Contains(property);
            var atlas = new Texture2D(pack.Width, pack.Height, TextureFormat.RGBA32, false, linear);

            Color fill = NeutralFill.TryGetValue(property, out Color neutral) ? neutral : Color.white;

            // Start from the neutral value so padding gutters never sample as black.
            var clear = new Color[pack.Width * pack.Height];
            for (int i = 0; i < clear.Length; i++) clear[i] = fill;
            atlas.SetPixels(clear);

            foreach (TextureAtlasPacker.PackEntry entry in pack.Entries)
            {
                if (!entry.Placed) continue;
                var material = (Material)entry.Key;

                int innerW = entry.Width - CellPadding * 2;
                int innerH = entry.Height - CellPadding * 2;
                if (innerW <= 0 || innerH <= 0) continue;

                Color[] pixels;
                if (material.HasProperty(property) && material.GetTexture(property) is Texture2D source)
                {
                    Texture2D readable = GetReadableCopy(source, innerW, innerH, linear);
                    if (readable == null) continue;
                    pixels = readable.GetPixels();
                    UnityEngine.Object.DestroyImmediate(readable);
                }
                else
                {
                    // This member has no such map — fill its cell with the neutral value for the property.
                    pixels = Enumerable.Repeat(fill, innerW * innerH).ToArray();
                }

                atlas.SetPixels(entry.X + CellPadding, entry.Y + CellPadding, innerW, innerH, pixels);

                // Extend the cell's edge pixels into the gutter so bilinear filtering and mips do not
                // pull in whatever was packed next door.
                ApplyPadding(atlas, entry.X + CellPadding, entry.Y + CellPadding, innerW, innerH);
            }

            atlas.Apply(true);
            return SaveAtlasPng(atlas, property, avatarName, assetOutputDirectory, linear);
        }

        /// <summary>Bleeds a cell's border outward into its padding gutter.</summary>
        private static void ApplyPadding(Texture2D atlas, int x, int y, int w, int h)
        {
            for (int p = 1; p <= CellPadding; p++)
            {
                for (int i = 0; i < w; i++)
                {
                    if (y - p >= 0) atlas.SetPixel(x + i, y - p, atlas.GetPixel(x + i, y));
                    if (y + h + p - 1 < atlas.height) atlas.SetPixel(x + i, y + h + p - 1, atlas.GetPixel(x + i, y + h - 1));
                }
                for (int j = 0; j < h; j++)
                {
                    if (x - p >= 0) atlas.SetPixel(x - p, y + j, atlas.GetPixel(x, y + j));
                    if (x + w + p - 1 < atlas.width) atlas.SetPixel(x + w + p - 1, y + j, atlas.GetPixel(x + w - 1, y + j));
                }
            }
        }

        /// <summary>
        /// Produces a readable, resized copy of a texture regardless of its import settings or compression,
        /// by blitting through a RenderTexture.
        /// </summary>
        private static Texture2D GetReadableCopy(Texture2D source, int width, int height, bool linear)
        {
            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(
                    width, height, 0,
                    RenderTextureFormat.ARGB32,
                    linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();
                return readable;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarTextureAtlaser] Could not read texture '{source.name}': {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Texture2D SaveAtlasPng(
            Texture2D atlas,
            string property,
            string avatarName,
            string assetOutputDirectory,
            bool linear)
        {
            try
            {
                string dir = !string.IsNullOrEmpty(assetOutputDirectory)
                    ? assetOutputDirectory
                    : "Assets/_AVATAROPTIMIZER/" + avatarName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string safeProperty = property.TrimStart('_');
                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/Atlas_{safeProperty}.png".Replace('\\', '/'));

                File.WriteAllBytes(path, atlas.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(atlas);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = !linear;
                    if (property == "_BumpMap" || property == "_DetailNormalMap")
                        importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }

                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarTextureAtlaser] Could not save atlas for '{property}': {e.Message}");
                return null;
            }
        }

        private static Material CreateAtlasMaterial(
            Material template,
            Dictionary<string, Texture2D> atlases,
            string avatarName,
            string assetOutputDirectory)
        {
            try
            {
                var mat = new Material(template) { name = template.name + "_Atlas" };
                foreach (var kvp in atlases)
                {
                    if (mat.HasProperty(kvp.Key))
                    {
                        mat.SetTexture(kvp.Key, kvp.Value);
                        mat.SetTextureScale(kvp.Key, Vector2.one);
                        mat.SetTextureOffset(kvp.Key, Vector2.zero);
                    }
                }

                string dir = !string.IsNullOrEmpty(assetOutputDirectory)
                    ? assetOutputDirectory
                    : "Assets/_AVATAROPTIMIZER/" + avatarName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{mat.name}.mat".Replace('\\', '/'));
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarTextureAtlaser] Could not create atlas material: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clones each affected mesh, rewrites the group's UVs into their atlas cells on the clone, collapses
        /// the group's submeshes into one, and repoints the renderer. The original mesh asset is never
        /// modified — the rewrite happens only on the copy.
        /// </summary>
        private static int ApplyAtlasMaterial(
            List<Material> group,
            Dictionary<Material, List<SubMeshUsage>> usage,
            Material atlasMaterial,
            Dictionary<Material, Rect> rects,
            string avatarName,
            string assetOutputDirectory)
        {
            var groupSet = new HashSet<Material>(group);
            var affectedRenderers = group.SelectMany(m => usage[m]).Select(u => u.Renderer).Distinct().ToList();

            int eliminated = 0;

            foreach (Renderer r in affectedRenderers)
            {
                Mesh mesh = GetMesh(r);
                if (mesh == null) continue;

                Material[] mats = r.sharedMaterials;

                Mesh newMesh = UnityEngine.Object.Instantiate(mesh);
                newMesh.name = mesh.name + "_Atlased";

                Vector2[] uvs = newMesh.uv;
                if (uvs == null || uvs.Length == 0)
                {
                    UnityEngine.Object.DestroyImmediate(newMesh);
                    Debug.Log($"[AvatarTextureAtlaser] Skipping '{r.name}': mesh has no UV0 to rewrite.");
                    continue;
                }

                var keptMaterials = new List<Material>();
                var keptSubMeshTriangles = new List<List<int>>();
                var atlasTriangles = new List<int>();
                bool anyAtlased = false;

                for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                {
                    int[] tris = mesh.GetTriangles(i);
                    if (mats[i] != null && groupSet.Contains(mats[i]) && rects.TryGetValue(mats[i], out Rect rect))
                    {
                        Material remapped = mats[i];
                        OptimizerLog.Verbose("AvatarTextureAtlaser",
                            $"  '{r.name}' submesh {i} ('{remapped.name}') -> atlas rect " +
                            $"x={rect.x:F4} y={rect.y:F4} w={rect.width:F4} h={rect.height:F4} ({tris.Length / 3} tris)");

                        // Scale this submesh's UVs into the material's packed cell.
                        foreach (int idx in tris)
                        {
                            if (idx < 0 || idx >= uvs.Length) continue;
                            Vector2 uv = uvs[idx];
                            uvs[idx] = new Vector2(
                                rect.x + Mathf.Clamp01(uv.x) * rect.width,
                                rect.y + Mathf.Clamp01(uv.y) * rect.height);
                        }

                        atlasTriangles.AddRange(tris);
                        anyAtlased = true;
                    }
                    else
                    {
                        keptMaterials.Add(mats[i]);
                        keptSubMeshTriangles.Add(tris.ToList());
                    }
                }

                if (!anyAtlased)
                {
                    UnityEngine.Object.DestroyImmediate(newMesh);
                    continue;
                }

                int slotsBefore = mats.Length;

                // The whole group collapses into exactly one submesh.
                keptMaterials.Add(atlasMaterial);
                keptSubMeshTriangles.Add(atlasTriangles);

                newMesh.uv = uvs;
                newMesh.subMeshCount = keptSubMeshTriangles.Count;
                for (int sub = 0; sub < keptSubMeshTriangles.Count; sub++)
                    newMesh.SetTriangles(keptSubMeshTriangles[sub].ToArray(), sub);

                // UV rewriting and submesh collapsing both fail silently — a UV in the wrong cell just
                // renders the wrong texture. Check the result before it is persisted.
                MeshIntegrity.Validate(newMesh, $"atlased mesh for '{r.name}'");
                ValidateUvsInUnitRange(newMesh, r.name);

                SaveMeshAsset(newMesh, avatarName, assetOutputDirectory);

                Undo.RecordObject(r, "Apply Atlas Material");
                if (r is SkinnedMeshRenderer smr) smr.sharedMesh = newMesh;
                else if (r is MeshRenderer mr && mr.GetComponent<MeshFilter>() != null) mr.GetComponent<MeshFilter>().sharedMesh = newMesh;

                r.sharedMaterials = keptMaterials.ToArray();

                eliminated += slotsBefore - keptMaterials.Count;
                Debug.Log($"[AvatarTextureAtlaser] '{r.name}': {slotsBefore} slots -> {keptMaterials.Count} using atlas material '{atlasMaterial.name}'.");
            }

            return eliminated;
        }

        /// <summary>
        /// Every rewritten UV must land inside the atlas. A value outside [0,1] means a cell rect was
        /// computed wrongly and the mesh will sample a neighbouring material's pixels.
        /// </summary>
        private static void ValidateUvsInUnitRange(Mesh mesh, string rendererName)
        {
            if (!OptimizerLog.ValidateMeshes) return;

            Vector2[] uvs = mesh.uv;
            if (uvs == null) return;

            int outside = 0;
            int firstBad = -1;
            for (int i = 0; i < uvs.Length; i++)
            {
                Vector2 uv = uvs[i];
                if (float.IsNaN(uv.x) || float.IsNaN(uv.y) ||
                    uv.x < -UvEpsilon || uv.x > 1f + UvEpsilon ||
                    uv.y < -UvEpsilon || uv.y > 1f + UvEpsilon)
                {
                    if (firstBad < 0) firstBad = i;
                    outside++;
                }
            }

            if (outside > 0)
            {
                OptimizerLog.Error("AvatarTextureAtlaser",
                    $"'{rendererName}': {outside} rewritten UV(s) fall outside the atlas (first at vertex {firstBad}: {uvs[firstBad]}). " +
                    "These will sample the wrong material's pixels.");
            }
            else
            {
                OptimizerLog.Verbose("AvatarTextureAtlaser", $"'{rendererName}': all {uvs.Length:N0} UVs land inside the atlas.");
            }
        }

        private static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (r is MeshRenderer mr)
            {
                MeshFilter filter = mr.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }
            return null;
        }

        private static void SaveMeshAsset(Mesh mesh, string avatarName, string assetOutputDirectory)
        {
            try
            {
                string dir = !string.IsNullOrEmpty(assetOutputDirectory)
                    ? assetOutputDirectory
                    : "Assets/_AVATAROPTIMIZER/" + avatarName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{mesh.name}.asset".Replace('\\', '/'));
                AssetDatabase.CreateAsset(mesh, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarTextureAtlaser] Could not persist atlased mesh '{mesh.name}': {e.Message}");
            }
        }
    }
}
