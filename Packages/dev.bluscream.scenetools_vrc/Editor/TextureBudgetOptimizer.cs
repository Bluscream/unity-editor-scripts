using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.TextureCompressor
{
    public enum TexturePlatform { Android, iOS, Standalone }

    /// <summary>
    /// Inputs for a texture budget optimization pass.
    /// VRAM and disk are SEPARATE budgets driven by different levers:
    ///   • VRAM  = resolution × format bits-per-pixel (+ mips). Crunch does NOT reduce it.
    ///   • Disk  = bytes stored in the AssetBundle. Crunch reduces it a lot; block size also matters.
    /// </summary>
    public class TextureBudgetRequest
    {
        /// <summary>Hard uncompressed texture memory budget (VRChat mobile hard cap is 40 MB).</summary>
        public long VramBudgetBytes = 40L * 1024 * 1024;
        /// <summary>Budget for the TEXTURE portion of the AssetBundle (total cap minus non-texture payload).</summary>
        public long DiskBudgetBytes = 10L * 1024 * 1024;
        /// <summary>Optional ceiling. 0 (default) = start each texture at its own native resolution.</summary>
        public int MaxResolution = 0;
        /// <summary>
        /// Preferred resolution floor. This only affects ORDERING, never reachability: every
        /// format/crunch combination at or above it is tried before anything below it, but the ladder
        /// always continues down to AbsoluteMinResolution if the budget demands it.
        /// </summary>
        public int MinResolution = 512;
        /// <summary>Hard floor. 32px keeps textures from ever being the reason a budget can't be met.</summary>
        public int AbsoluteMinResolution = 32;
        /// <summary>
        /// How expensive losing resolution is, relative to losing format detail.
        /// 1.0 = proportional. Higher values (2-3) make downscaling costly, so a big body atlas keeps its
        /// pixels and absorbs the budget through a larger ASTC block instead — usually the better look.
        /// </summary>
        public float ResolutionPriority = 2.0f;
        /// <summary>Allow crunched formats. Crunch trades VRAM (fixed 8bpp) and quality for much smaller disk size.</summary>
        public bool AllowCrunch = true;
        /// <summary>Unity crunch quality 0-100 (higher = better looking, larger on disk).</summary>
        public int CrunchQuality = 50;
        /// <summary>Target platform. Null = derive from the active build target.</summary>
        public TexturePlatform? Platform = null;
    }

    public class TextureBudgetResult
    {
        public int TexturesProcessed;
        public long EstimatedVramBytes;
        public long EstimatedDiskBytes;
        public long VramBudgetBytes;
        public long DiskBudgetBytes;
        public bool HitFloor;
        /// <summary>True when the budget forced textures below the preferred resolution floor.</summary>
        public bool WentBelowPreferredResolution;
        public int TexturesBelowPreferredResolution;
        public Dictionary<string, int> TierHistogram = new Dictionary<string, int>();

        public bool VramBudgetMet => EstimatedVramBytes <= VramBudgetBytes;
        public bool DiskBudgetMet => EstimatedDiskBytes <= DiskBudgetBytes;

        public string Describe()
        {
            string tiers = string.Join(", ", TierHistogram.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value}× {kv.Key}"));
            return $"VRAM {EstimatedVramBytes / (1024.0 * 1024.0):F1}/{VramBudgetBytes / (1024.0 * 1024.0):F1} MB " +
                   $"({(VramBudgetMet ? "OK" : "OVER")}), Texture disk ~{EstimatedDiskBytes / (1024.0 * 1024.0):F2}/{DiskBudgetBytes / (1024.0 * 1024.0):F2} MB " +
                   $"({(DiskBudgetMet ? "OK" : "OVER")}){(string.IsNullOrEmpty(tiers) ? "" : $" — {tiers}")}";
        }
    }

    /// <summary>
    /// Allocates a per-texture resolution/format so an avatar fits BOTH the uncompressed VRAM budget
    /// and the compressed AssetBundle budget, degrading the largest contributors first instead of
    /// applying one blunt setting to every texture.
    /// </summary>
    public static class TextureBudgetOptimizer
    {
        private class Tier
        {
            public string Name;
            public TextureImporterFormat Format;
            public float Bpp;          // VRAM bits per pixel
            public float DiskFactor;   // stored bytes ≈ VRAM bytes × this (block data compresses ~2x under the bundle's LZ4)
            public float Quality;      // perceptual rank, 0-100
            public bool IsCrunched;
            public int CrunchQuality;
            public bool RequiresNoAlpha;
            public bool SafeForNormalMaps = true;
        }

        private class Level
        {
            public int Resolution;
            public Tier Tier;
            public float Score;        // higher = better looking
        }

        private class TexEntry
        {
            public TextureImporter Importer;
            public int NativeW, NativeH;
            public bool Mipmaps;
            public List<Level> Ladder;
            public int LevelIndex;
            public long Vram, Disk;
            /// <summary>Multiplier on the cost of degrading this texture. >1 protects, &lt;1 sacrifices first.</summary>
            public float Importance = 1f;
            public string Role = "unknown";
        }

        /// <summary>
        /// How much visual weight a texture carries, inferred from the shader properties it is bound to.
        /// Without this the allocator cannot tell a body albedo from a wristwatch roughness map and will
        /// happily crunch the former while preserving the latter at 2048px.
        /// </summary>
        private static readonly (string[] props, string role, float importance)[] RoleRules =
        {
            // Albedo / base colour — what the eye actually reads. Protect hardest.
            (new[] { "_maintex", "_basemap", "_basecolormap", "_maintexture", "_albedomap", "_color" }, "albedo", 1.8f),
            // Normal maps — visible as shading detail, and they band badly at large block sizes.
            (new[] { "_bumpmap", "_normalmap", "_detailnormalmap", "_normalmap2", "_bumpmap2" }, "normal", 1.2f),
            // Emission / matcaps — noticeable but usually low frequency.
            (new[] { "_emissionmap", "_emissivemap", "_matcap", "_spheretex", "_sphereadd" }, "emission", 0.8f),
            // Data / mask maps — low frequency, survive aggressive compression almost invisibly.
            (new[] { "_metallicglossmap", "_specglossmap", "_occlusionmap", "_detailmask", "_masktex",
                     "_aomap", "_roughnessmap", "_smoothnessmap", "_metallicmap", "_mask", "_shadowmask" }, "data", 0.5f),
        };

        /// <summary>
        /// Name fragments that reliably mark decoration rather than content. Only NEGATIVE keywords are
        /// used: positive ones like "body" or "head" are ambiguous — this very avatar carries
        /// "VRCOSC_Watch_Body_BaseColor" (a wristwatch) alongside "Bee Mayu/Body_Base_color" (the real
        /// skin), so a positive rule would promote exactly the wrong textures. Being wrong about a
        /// negative keyword is also cheap: a credits texture at low resolution is the correct outcome.
        /// </summary>
        private static readonly (string[] words, float penalty)[] NegativeNameRules =
        {
            (new[] { "credit", "watermark", "logo", "thumbnail", "preview", "banner", "splash",
                     "unused", "backup", "placeholder", "sample", "_ref", "readme", "donotuse" }, 0.25f),
            (new[] { "icon", "hud", "menu", "button", "cursor", "arrow", "ping", "crosshair", "toggle" }, 0.5f),
        };

        private static float NamePenalty(params string[] names)
        {
            float penalty = 1f;
            foreach (string n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                string lower = n.ToLowerInvariant();
                foreach (var (words, p) in NegativeNameRules)
                    if (words.Any(w => lower.Contains(w))) penalty = Math.Min(penalty, p);
            }
            return penalty;
        }

        /// <summary>Rough world-space visual footprint of a renderer, used as a prominence proxy.</summary>
        private static float RendererFootprint(Renderer r)
        {
            try
            {
                Vector3 s = r.bounds.size;
                if (s == Vector3.zero) return 0f;
                // Surface-area-ish measure: a body mesh dwarfs a wristwatch, without assuming any naming
                return Mathf.Abs(s.x * s.y) + Mathf.Abs(s.y * s.z) + Mathf.Abs(s.x * s.z);
            }
            catch { return 0f; }
        }

        /// <summary>
        /// Maps every texture on the avatar to the shader properties it is bound to, so each one can be
        /// classified. A texture used in several roles keeps the most protective classification, and the
        /// role weight is then modulated by how visually prominent the largest renderer using it is, plus
        /// a penalty for decoration-style names.
        /// </summary>
        private static Dictionary<string, (string role, float importance)> ClassifyTextures(GameObject avatarRoot)
        {
            var roleOf = new Dictionary<string, (string role, float weight)>();
            var footprintOf = new Dictionary<string, float>();
            var nameOf = new Dictionary<string, string>();
            var result = new Dictionary<string, (string, float)>();
            if (avatarRoot == null) return result;

            float maxFootprint = 0f;

            foreach (Renderer r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                float footprint = RendererFootprint(r);
                maxFootprint = Math.Max(maxFootprint, footprint);

                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    int count = ShaderUtil.GetPropertyCount(m.shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                        string prop = ShaderUtil.GetPropertyName(m.shader, i);
                        Texture tex = m.GetTexture(prop);
                        if (tex == null) continue;
                        string path = AssetDatabase.GetAssetPath(tex);
                        if (string.IsNullOrEmpty(path)) continue;

                        // Largest renderer wins: a texture shared by the body and a trinket counts as body
                        footprintOf[path] = Math.Max(footprintOf.TryGetValue(path, out float f) ? f : 0f, footprint);
                        // Remember the context names for the keyword check
                        if (!nameOf.ContainsKey(path)) nameOf[path] = $"{path} {m.name} {r.gameObject.name}";

                        string propLower = prop.ToLowerInvariant();
                        foreach (var (props, role, weight) in RoleRules)
                        {
                            if (!props.Any(p => propLower == p || propLower.EndsWith(p))) continue;
                            if (!roleOf.TryGetValue(path, out var existing) || weight > existing.weight)
                                roleOf[path] = (role, weight);
                            break;
                        }
                    }
                }
            }

            foreach (var kv in footprintOf)
            {
                string path = kv.Key;
                var role = roleOf.TryGetValue(path, out var rr) ? rr : ("unknown", 1.0f);

                // Prominence: 0.6 for a barely-visible trinket up to 1.4 for the largest mesh on the
                // avatar. sqrt keeps mid-sized meshes from collapsing towards the floor.
                float prominence = 1f;
                if (maxFootprint > 0f)
                    prominence = 0.6f + 0.8f * Mathf.Sqrt(Mathf.Clamp01(kv.Value / maxFootprint));

                float penalty = NamePenalty(nameOf.TryGetValue(path, out string n) ? n : path);
                float importance = Mathf.Clamp(role.Item2 * prominence * penalty, 0.15f, 3.0f);
                result[path] = (role.Item1, importance);
            }

            return result;
        }

        // ── Mobile (Quest / iOS). ASTC is the only sane choice for VRAM; crunch is ETC2-only in Unity,
        //    so an "ASTC + crunch" combination does not exist — that mistake is why tightening a budget
        //    used to change nothing.
        private static List<Tier> MobileTiers(bool allowCrunch, int crunchQuality)
        {
            var tiers = new List<Tier>
            {
                new Tier { Name = "ASTC 4x4",   Format = TextureImporterFormat.ASTC_4x4,   Bpp = 8.00f, DiskFactor = 0.50f, Quality = 100f },
                new Tier { Name = "ASTC 5x5",   Format = TextureImporterFormat.ASTC_5x5,   Bpp = 5.12f, DiskFactor = 0.50f, Quality = 92f  },
                new Tier { Name = "ASTC 6x6",   Format = TextureImporterFormat.ASTC_6x6,   Bpp = 3.55f, DiskFactor = 0.50f, Quality = 84f  },
                new Tier { Name = "ASTC 8x8",   Format = TextureImporterFormat.ASTC_8x8,   Bpp = 2.00f, DiskFactor = 0.50f, Quality = 70f  },
                new Tier { Name = "ASTC 10x10", Format = TextureImporterFormat.ASTC_10x10, Bpp = 1.28f, DiskFactor = 0.50f, Quality = 58f  },
                new Tier { Name = "ASTC 12x12", Format = TextureImporterFormat.ASTC_12x12, Bpp = 1.00f, DiskFactor = 0.50f, Quality = 50f  },
            };

            if (allowCrunch)
            {
                // Crunched ETC2 keeps 8bpp in VRAM but stores far fewer bytes — only worth it when the
                // DISK budget is the binding constraint and VRAM has room to spare.
                // Crunch is a genuinely different trade than ASTC block growth: it keeps full resolution
                // and pays with VRAM (fixed 8bpp) plus DCT-style artifacts, but its stored size is tiny.
                // Offered at several qualities so the allocator can pick the point it needs rather than
                // being forced onto one setting.
                foreach (int q in new[] { Mathf.Clamp(crunchQuality, 0, 100), 25, 0 }.Distinct().OrderByDescending(v => v))
                {
                    tiers.Add(new Tier
                    {
                        Name = $"ETC2 Crunched {q}%",
                        Format = TextureImporterFormat.ETC2_RGBA8Crunched,
                        Bpp = 8.00f,
                        // Crunch stores its own compressed stream; measured Unity output lands well under
                        // block formats. The bundle's LZ4 cannot compress it further.
                        DiskFactor = 0.05f + 0.15f * (q / 100f),
                        Quality = 44f + 0.32f * q,
                        IsCrunched = true,
                        CrunchQuality = q,
                        SafeForNormalMaps = false     // crunch mangles normal maps
                    });
                }
            }
            return tiers;
        }

        // ── PC / Standalone
        private static List<Tier> StandaloneTiers(bool allowCrunch, int crunchQuality)
        {
            var tiers = new List<Tier>
            {
                new Tier { Name = "BC7",  Format = TextureImporterFormat.BC7,   Bpp = 8.00f, DiskFactor = 0.50f, Quality = 100f },
                new Tier { Name = "DXT5", Format = TextureImporterFormat.DXT5,  Bpp = 8.00f, DiskFactor = 0.50f, Quality = 82f  },
                new Tier { Name = "DXT1", Format = TextureImporterFormat.DXT1,  Bpp = 4.00f, DiskFactor = 0.50f, Quality = 64f, RequiresNoAlpha = true },
            };
            if (allowCrunch)
            {
                int q = Mathf.Clamp(crunchQuality, 0, 100);
                tiers.Add(new Tier
                {
                    Name = $"DXT5 Crunched {q}%",
                    Format = TextureImporterFormat.DXT5Crunched,
                    Bpp = 8.00f,
                    DiskFactor = 0.10f + 0.30f * (q / 100f),
                    Quality = 40f + 0.30f * q,
                    IsCrunched = true,
                    CrunchQuality = q,
                    SafeForNormalMaps = false
                });
            }
            return tiers;
        }

        public static string PlatformName(TexturePlatform p)
            => p == TexturePlatform.Android ? "Android" : (p == TexturePlatform.iOS ? "iPhone" : "Standalone");

        private static TexturePlatform ActivePlatform()
        {
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android: return TexturePlatform.Android;
                case BuildTarget.iOS: return TexturePlatform.iOS;
                default: return TexturePlatform.Standalone;
            }
        }

        public static TextureBudgetResult Optimize(GameObject avatarRoot, TextureBudgetRequest request, Action<string> progressCallback = null)
        {
            var result = new TextureBudgetResult
            {
                VramBudgetBytes = Math.Max(1024 * 1024L, request.VramBudgetBytes),
                DiskBudgetBytes = Math.Max(256 * 1024L, request.DiskBudgetBytes)
            };
            if (avatarRoot == null) return result;

            TexturePlatform platform = request.Platform ?? ActivePlatform();
            string platformName = PlatformName(platform);

            HashSet<TextureImporter> importers = TextureCompressionEditor.GetUniqueTextureImporters(avatarRoot);
            if (importers.Count == 0) return result;

            List<Tier> tiers = platform == TexturePlatform.Standalone
                ? StandaloneTiers(request.AllowCrunch, request.CrunchQuality)
                : MobileTiers(request.AllowCrunch, request.CrunchQuality);

            // Resolution ladder, capped by the user's max and floored by MinResolution
            // No global resolution ceiling: each texture starts at its own native size (optionally capped)
            // and the allocator decides how far down it needs to go. A hard 32px floor guarantees textures
            // can always be shrunk far enough that they are never the reason a budget cannot be met.
            int hardMinRes = Mathf.Clamp(request.AbsoluteMinResolution, 32, 4096);
            int preferredMinRes = Math.Max(hardMinRes, request.MinResolution);
            int ceiling = request.MaxResolution > 0 ? request.MaxResolution : int.MaxValue;
            var standardResolutions = new[] { 8192, 4096, 2048, 1024, 512, 256, 128, 64, 32 };

            // Global level ladder ordered best → worst.
            //   score = formatQuality × (resolution / topResolution) ^ ResolutionPriority
            // ResolutionPriority controls the trade the user cares about: at 1.0 losing half the
            // resolution costs the same as halving format quality, so the optimizer happily downscales.
            // At 2-3 downscaling becomes expensive and big atlases keep their pixels, absorbing the
            // budget through larger ASTC blocks (blurrier, but far better than a 512px body texture).
            float resPriority = Mathf.Clamp(request.ResolutionPriority, 0.25f, 4f);

            // Builds the degradation ladder for one texture, anchored at its own native resolution.
            // Two hard-ordered segments: every format/crunch combination at or above the preferred
            // resolution floor is exhausted before any sub-floor level is offered — but sub-floor levels
            // always exist, all the way down to the hard floor.
            Func<int, List<Level>> buildLadder = nativeLongest =>
            {
                int top = Math.Min(nativeLongest, ceiling);
                var usable = standardResolutions.Where(r => r <= top && r >= hardMinRes).ToList();
                if (usable.Count == 0) usable.Add(Mathf.Clamp(top, hardMinRes, 8192));
                int topRes = usable.Max();

                var built = new List<Level>();
                foreach (int res in usable)
                    foreach (Tier t in tiers)
                        built.Add(new Level
                        {
                            Resolution = res,
                            Tier = t,
                            Score = t.Quality * Mathf.Pow(res / (float)topRes, resPriority)
                        });

                return built.Where(l => l.Resolution >= preferredMinRes).OrderByDescending(l => l.Score)
                    .Concat(built.Where(l => l.Resolution < preferredMinRes).OrderByDescending(l => l.Score))
                    .ToList();
            };

            // Build per-texture entries with a ladder filtered to what each texture supports
            var roles = ClassifyTextures(avatarRoot);
            var entries = new List<TexEntry>();
            foreach (TextureImporter imp in importers)
            {
                if (imp == null) continue;
                if (!Bluscream.Utils.GetSourceTextureWidthAndHeight(imp, out int nw, out int nh)) { nw = nh = 2048; }

                bool hasAlpha = imp.DoesSourceTextureHaveAlpha();
                bool isNormal = imp.textureType == TextureImporterType.NormalMap;

                var ladder = buildLadder(Math.Max(1, Math.Max(nw, nh)))
                    .Where(l => !(l.Tier.RequiresNoAlpha && hasAlpha))
                    .Where(l => !(isNormal && !l.Tier.SafeForNormalMaps))
                    .ToList();
                if (ladder.Count == 0) continue;

                // Normal maps flagged on the importer count as normals even if bound to an odd property
                var role = roles.TryGetValue(imp.assetPath, out var cls) ? cls : (isNormal ? ("normal", 1.2f) : ("unknown", 1.0f));

                var e = new TexEntry
                {
                    Importer = imp,
                    NativeW = Math.Max(1, nw),
                    NativeH = Math.Max(1, nh),
                    Mipmaps = imp.mipmapEnabled,
                    Ladder = ladder,
                    LevelIndex = 0,
                    Role = role.Item1,
                    Importance = role.Item2
                };
                Measure(e);
                entries.Add(e);
            }

            if (entries.Count == 0) return result;

            long totalVram = entries.Sum(e => e.Vram);
            long totalDisk = entries.Sum(e => e.Disk);

            Debug.Log($"[TextureBudget] Texture roles: " + string.Join(", ",
                entries.GroupBy(e => e.Role).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()}× {g.Key}")));
            Debug.Log($"[TextureBudget] {entries.Count} texture(s) on {platformName}. Starting at best tier: " +
                      $"VRAM {totalVram / (1024.0 * 1024.0):F1} MB (budget {result.VramBudgetBytes / (1024.0 * 1024.0):F1} MB), " +
                      $"disk ~{totalDisk / (1024.0 * 1024.0):F2} MB (budget {result.DiskBudgetBytes / (1024.0 * 1024.0):F2} MB).");

            // ── Greedy degradation: repeatedly downgrade whichever texture gives the most budget
            //    relief per unit of quality lost, until both budgets are satisfied.
            int guard = entries.Sum(e => e.Ladder.Count) + 16;
            while ((totalVram > result.VramBudgetBytes || totalDisk > result.DiskBudgetBytes) && guard-- > 0)
            {
                bool vramOver = totalVram > result.VramBudgetBytes;
                bool diskOver = totalDisk > result.DiskBudgetBytes;

                TexEntry best = null;
                double bestScore = double.NegativeInfinity;
                long bestVram = 0, bestDisk = 0;

                int bestLevelIndex = -1;

                foreach (TexEntry e in entries)
                {
                    // The ladder is ordered by QUALITY, which is not monotonic in EITHER cost: with a
                    // high ResolutionPriority "2048 ASTC 12x12" outranks "1024 ASTC 4x4" (larger), and
                    // a crunch tier can undercut an ASTC tier on disk while costing 4x the VRAM.
                    // So a candidate must be a Pareto improvement: it may never worsen a budget that is
                    // already over, nor push a satisfied budget over, and must strictly improve at
                    // least one budget that is over. Scanning forward until such a level is found —
                    // rather than stopping at the first level that helps *anything* — is what keeps
                    // textures from getting trapped one step below a tier they can never leave.
                    int candidate = -1;
                    long candVram = 0, candDisk = 0;
                    for (int k = e.LevelIndex + 1; k < e.Ladder.Count; k++)
                    {
                        Measure(e, k, out long kv, out long kd);

                        bool vramAcceptable = vramOver
                            ? kv <= e.Vram
                            : totalVram - e.Vram + kv <= result.VramBudgetBytes;
                        bool diskAcceptable = diskOver
                            ? kd <= e.Disk
                            : totalDisk - e.Disk + kd <= result.DiskBudgetBytes;
                        bool improvesBinding = (vramOver && kv < e.Vram) || (diskOver && kd < e.Disk);

                        if (vramAcceptable && diskAcceptable && improvesBinding)
                        {
                            candidate = k; candVram = kv; candDisk = kd; break;
                        }
                    }
                    if (candidate < 0) continue;

                    long vramSaved = e.Vram - candVram;
                    long diskSaved = e.Disk - candDisk;

                    // Only count savings against budgets that are actually exceeded
                    double relief = 0;
                    if (vramOver) relief += (double)vramSaved / result.VramBudgetBytes;
                    if (diskOver) relief += (double)diskSaved / result.DiskBudgetBytes;
                    if (relief <= 0) continue;

                    // Weight the cost by how much the texture matters: a body albedo "costs" far more to
                    // degrade than a roughness mask, so the greedy spends the budget on masks first.
                    float qualityLost = Math.Max(0.01f, e.Ladder[e.LevelIndex].Score - e.Ladder[candidate].Score) * e.Importance;
                    double score = relief / qualityLost;

                    if (score > bestScore)
                    {
                        bestScore = score; best = e; bestVram = candVram; bestDisk = candDisk; bestLevelIndex = candidate;
                    }
                }

                if (best == null)
                {
                    result.HitFloor = true;
                    int atLastLevel = entries.Count(e => e.LevelIndex >= e.Ladder.Count - 1);
                    Debug.LogWarning($"[TextureBudget] No further texture reduction is possible ({atLastLevel}/{entries.Count} at their final ladder level, floor {hardMinRes}px). " +
                                     $"VRAM {totalVram / (1024.0 * 1024.0):F1} MB, disk ~{totalDisk / (1024.0 * 1024.0):F2} MB.");
                    break;
                }

                totalVram += bestVram - best.Vram;
                totalDisk += bestDisk - best.Disk;
                best.LevelIndex = bestLevelIndex;
                best.Vram = bestVram;
                best.Disk = bestDisk;
            }

            // ── Ascent: the descent stops the moment both budgets fit, which can leave headroom unused
            //    (especially VRAM, since disk usually binds first). Spend whatever is genuinely left by
            //    promoting textures back up their ladder — most valuable and cheapest first — as long as
            //    both budgets stay satisfied. Never runs when either budget is still exceeded.
            if (totalVram <= result.VramBudgetBytes && totalDisk <= result.DiskBudgetBytes)
            {
                int upgrades = 0;
                int ascentGuard = entries.Sum(e => e.Ladder.Count) + 16;
                while (ascentGuard-- > 0)
                {
                    TexEntry bestUp = null;
                    int bestUpIndex = -1;
                    double bestUpScore = 0;
                    long upVram = 0, upDisk = 0;

                    foreach (TexEntry e in entries)
                    {
                        // Walk towards higher quality (lower index), nearest first
                        for (int k = e.LevelIndex - 1; k >= 0; k--)
                        {
                            Measure(e, k, out long kv, out long kd);
                            if (totalVram - e.Vram + kv > result.VramBudgetBytes) continue;
                            if (totalDisk - e.Disk + kd > result.DiskBudgetBytes) continue;

                            float gain = (e.Ladder[k].Score - e.Ladder[e.LevelIndex].Score) * e.Importance;
                            if (gain <= 0) continue;

                            // Cost in the scarcer currency, normalised against each budget
                            double cost = Math.Max(1e-9,
                                (double)(kv - e.Vram) / result.VramBudgetBytes +
                                (double)(kd - e.Disk) / result.DiskBudgetBytes);
                            double s = gain / cost;
                            if (s > bestUpScore)
                            {
                                bestUpScore = s; bestUp = e; bestUpIndex = k; upVram = kv; upDisk = kd;
                            }
                            break; // nearest affordable upgrade for this texture only
                        }
                    }

                    if (bestUp == null) break;

                    totalVram += upVram - bestUp.Vram;
                    totalDisk += upDisk - bestUp.Disk;
                    bestUp.LevelIndex = bestUpIndex;
                    bestUp.Vram = upVram;
                    bestUp.Disk = upDisk;
                    upgrades++;
                }

                if (upgrades > 0)
                    Debug.Log($"[TextureBudget] Reclaimed leftover budget with {upgrades} quality upgrade(s) → VRAM {totalVram / (1024.0 * 1024.0):F1} MB, disk ~{totalDisk / (1024.0 * 1024.0):F2} MB.");
            }

            // ── Apply
            int index = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TexEntry e in entries)
                {
                    index++;
                    Level lvl = e.Ladder[e.LevelIndex];
                    progressCallback?.Invoke($"Applying texture settings ({index}/{entries.Count}): {System.IO.Path.GetFileName(e.Importer.assetPath)} → {lvl.Resolution}px {lvl.Tier.Name}");
                    // Per-texture decisions are only visible here; log the important ones so the
                    // allocation can be audited without digging through .meta files.
                    if (e.Importance >= 1.3f || e.Importance <= 0.5f)
                        Debug.Log($"[TextureBudget]   [{e.Role} ×{e.Importance:F2}] {System.IO.Path.GetFileName(e.Importer.assetPath)} → {lvl.Resolution}px {lvl.Tier.Name}");

                    TextureImporterPlatformSettings s = e.Importer.GetPlatformTextureSettings(platformName);
                    s.overridden = true;
                    s.name = platformName;
                    s.maxTextureSize = lvl.Resolution;
                    s.format = lvl.Tier.Format;
                    s.textureCompression = TextureImporterCompression.Compressed;
                    s.crunchedCompression = lvl.Tier.IsCrunched;
                    // For crunched formats this is the crunch ratio; for block formats it is encoder effort.
                    s.compressionQuality = lvl.Tier.IsCrunched ? lvl.Tier.CrunchQuality : 100;

                    Undo.RecordObject(e.Importer, "Optimize Texture Budget");
                    e.Importer.SetPlatformTextureSettings(s);
                    e.Importer.SaveAndReimport();

                    string key = $"{lvl.Resolution}px {lvl.Tier.Name}";
                    result.TierHistogram[key] = result.TierHistogram.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            result.TexturesProcessed = entries.Count;
            result.EstimatedVramBytes = totalVram;
            result.EstimatedDiskBytes = totalDisk;
            result.TexturesBelowPreferredResolution = entries.Count(e => e.Ladder[e.LevelIndex].Resolution < preferredMinRes);
            result.WentBelowPreferredResolution = result.TexturesBelowPreferredResolution > 0;

            Debug.Log($"[TextureBudget] Done — {result.Describe()}");
            if (result.WentBelowPreferredResolution)
            {
                Debug.LogWarning($"[TextureBudget] {result.TexturesBelowPreferredResolution} texture(s) had to go below the preferred {preferredMinRes}px floor — every format/crunch combination above it was exhausted before downscaling further.");
            }
            return result;
        }

        private static void Measure(TexEntry e) => Measure(e, e.LevelIndex, out e.Vram, out e.Disk);

        /// <summary>
        /// VRAM is summed over the real mip chain to match how AvatarSDKEvaluator reports texture memory,
        /// so the optimizer's numbers and the SDK report agree.
        /// </summary>
        private static void Measure(TexEntry e, int levelIndex, out long vram, out long disk)
        {
            Level lvl = e.Ladder[levelIndex];
            int longest = Math.Max(e.NativeW, e.NativeH);
            double scale = Math.Min(1.0, (double)lvl.Resolution / Math.Max(1, longest));
            int w = Math.Max(1, (int)Math.Round(e.NativeW * scale));
            int h = Math.Max(1, (int)Math.Round(e.NativeH * scale));

            long bytes = 0;
            if (e.Mipmaps)
            {
                int mw = w, mh = h;
                while (true)
                {
                    bytes += (long)Math.Max(1, (mw * (long)mh * lvl.Tier.Bpp) / 8.0);
                    if (mw == 1 && mh == 1) break;
                    mw = Math.Max(1, mw >> 1);
                    mh = Math.Max(1, mh >> 1);
                }
            }
            else
            {
                bytes = (long)Math.Max(1, (w * (long)h * lvl.Tier.Bpp) / 8.0);
            }

            vram = bytes;
            disk = (long)(bytes * lvl.Tier.DiskFactor);
        }
    }
}
