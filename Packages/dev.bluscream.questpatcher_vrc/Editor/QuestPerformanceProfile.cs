using System;
using UnityEngine;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Performance Rank levels for VRChat Quest avatars
    /// </summary>
    public enum QuestPerformanceRank
    {
        Excellent,
        Good,
        Medium,
        Poor,
        VeryPoor
    }

    /// <summary>
    /// Asset placement locations for generated Quest assets
    /// </summary>
    public enum AssetPlacementLocation
    {
        SeparateFolder,
        SameFolderAsOriginal
    }

    /// <summary>
    /// Pruning strategies for excess PhysBones
    /// </summary>
    public enum PhysBonePruningStrategy
    {
        Disabled,
        DeepestFirst,
        ShallowestFirst,
        InteractiveChecklist
    }

    /// <summary>
    /// Configuration profile defining resource limits for a target Quest Performance Rank
    /// </summary>
    [Serializable]
    public class QuestPerformanceProfile
    {
        public QuestPerformanceRank Rank = QuestPerformanceRank.Medium;
        public AssetPlacementLocation Placement = AssetPlacementLocation.SeparateFolder;
        public PhysBonePruningStrategy PruningStrategy = PhysBonePruningStrategy.DeepestFirst;
        public int MaxTriangles = 32000;
        public int MaxSkinnedMeshes = 4;
        public int MaxMeshRenderers = 4;
        public int MaxMaterialSlots = 4;
        public long MaxTextureMemoryBytes = 2048 * 1024 * 1024L; // 2048 MB limit
        public int MaxPhysBoneComponents = 16;
        public int MaxPhysBoneTransforms = 32;
        public int MaxPhysBoneColliders = 16;
        public int MaxPhysBoneCollisionChecks = 32;
        public bool RemoveIncompatibleComponents = true;
        public bool SaveAssetsInSameFolder = true;

        public static QuestPerformanceProfile GetProfile(QuestPerformanceRank rank)
        {
            switch (rank)
            {
                case QuestPerformanceRank.Excellent:
                    return new QuestPerformanceProfile
                    {
                        Rank = QuestPerformanceRank.Excellent,
                        MaxTriangles = 7500,
                        MaxSkinnedMeshes = 1,
                        MaxMeshRenderers = 1,
                        MaxMaterialSlots = 1,
                        MaxTextureMemoryBytes = 10 * 1024 * 1024L, // 10 MB
                        MaxPhysBoneComponents = 0,
                        MaxPhysBoneTransforms = 0,
                        MaxPhysBoneColliders = 0,
                        MaxPhysBoneCollisionChecks = 0
                    };

                case QuestPerformanceRank.Good:
                    return new QuestPerformanceProfile
                    {
                        Rank = QuestPerformanceRank.Good,
                        MaxTriangles = 15000,
                        MaxSkinnedMeshes = 2,
                        MaxMeshRenderers = 2,
                        MaxMaterialSlots = 2,
                        MaxTextureMemoryBytes = 10 * 1024 * 1024L, // 10 MB
                        MaxPhysBoneComponents = 8,
                        MaxPhysBoneTransforms = 16,
                        MaxPhysBoneColliders = 8,
                        MaxPhysBoneCollisionChecks = 16
                    };

                case QuestPerformanceRank.Medium:
                    return new QuestPerformanceProfile
                    {
                        Rank = QuestPerformanceRank.Medium,
                        MaxTriangles = 20000,
                        MaxSkinnedMeshes = 2,
                        MaxMeshRenderers = 2,
                        MaxMaterialSlots = 4,
                        MaxTextureMemoryBytes = 20 * 1024 * 1024L, // 20 MB
                        MaxPhysBoneComponents = 8,
                        MaxPhysBoneTransforms = 32,
                        MaxPhysBoneColliders = 16,
                        MaxPhysBoneCollisionChecks = 32
                    };

                case QuestPerformanceRank.Poor:
                    return new QuestPerformanceProfile
                    {
                        Rank = QuestPerformanceRank.Poor,
                        MaxTriangles = 20000,
                        MaxSkinnedMeshes = 2,
                        MaxMeshRenderers = 2,
                        MaxMaterialSlots = 4,
                        MaxTextureMemoryBytes = 40 * 1024 * 1024L, // 40 MB (VRChat Quest hard limit)
                        MaxPhysBoneComponents = 8,
                        MaxPhysBoneTransforms = 64,
                        MaxPhysBoneColliders = 16,
                        MaxPhysBoneCollisionChecks = 64
                    };

                case QuestPerformanceRank.VeryPoor:
                default:
                    return new QuestPerformanceProfile
                    {
                        Rank = QuestPerformanceRank.VeryPoor,
                        MaxTriangles = int.MaxValue,
                        MaxSkinnedMeshes = int.MaxValue,
                        MaxMeshRenderers = int.MaxValue,
                        MaxMaterialSlots = int.MaxValue,
                        MaxTextureMemoryBytes = 40 * 1024 * 1024L, // 40 MB (VRChat Quest hard limit)
                        MaxPhysBoneComponents = 8,
                        MaxPhysBoneTransforms = 64,
                        MaxPhysBoneColliders = 16,
                        MaxPhysBoneCollisionChecks = 64
                    };
            }
        }
    }
}
