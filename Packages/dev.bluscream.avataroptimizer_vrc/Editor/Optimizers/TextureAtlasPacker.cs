using System;
using System.Collections.Generic;
using System.Linq;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Growing binary-tree rectangle packer used to lay out texture atlases.
    ///
    /// Rectangles are placed largest-first into a tree of free nodes; each placement splits the node it
    /// occupied into the strip to its right and the strip below it. When nothing fits, the root grows in
    /// whichever direction keeps the atlas closest to square, which keeps the final power-of-two rounding
    /// from wasting a whole doubling on one axis.
    ///
    /// This is a clean-room implementation of the classic growing binary tree bin-packing approach.
    /// </summary>
    public static class TextureAtlasPacker
    {
        /// <summary>A rectangle to place, identified by an arbitrary caller-supplied key.</summary>
        public sealed class PackEntry
        {
            public object Key;
            public int Width;
            public int Height;

            /// <summary>Assigned position, valid only once packing has succeeded.</summary>
            public int X, Y;
            public bool Placed;
        }

        public sealed class PackResult
        {
            public bool Success;
            public int Width;
            public int Height;
            public List<PackEntry> Entries = new List<PackEntry>();
        }

        private sealed class Node
        {
            public int X, Y, W, H;
            public bool Used;
            public Node Right;  // strip to the right of a placed rectangle
            public Node Down;   // strip below a placed rectangle
        }

        /// <summary>
        /// Packs the given rectangles, growing the atlas as needed.
        /// </summary>
        /// <param name="entries">Rectangles to place. Their X/Y/Placed fields are filled in.</param>
        /// <param name="maxDimension">Hard cap on either axis; packing fails rather than exceeding it.</param>
        /// <param name="forcePowerOfTwo">Round the final atlas up to power-of-two dimensions.</param>
        public static PackResult Pack(IEnumerable<PackEntry> entries, int maxDimension, bool forcePowerOfTwo = true)
        {
            var result = new PackResult();
            if (entries == null) return result;

            // Largest first: big rectangles placed late tend to force a growth step that wastes space.
            List<PackEntry> sorted = entries
                .Where(e => e != null && e.Width > 0 && e.Height > 0)
                .OrderByDescending(e => Math.Max(e.Width, e.Height))
                .ThenByDescending(e => e.Width * e.Height)
                .ToList();

            result.Entries = sorted;
            if (sorted.Count == 0) return result;

            // A single rectangle larger than the cap can never be placed.
            if (sorted.Any(e => e.Width > maxDimension || e.Height > maxDimension))
                return result;

            Node root = new Node { X = 0, Y = 0, W = sorted[0].Width, H = sorted[0].Height };

            foreach (PackEntry entry in sorted)
            {
                Node target = FindNode(root, entry.Width, entry.Height);
                if (target != null)
                {
                    target = SplitNode(target, entry.Width, entry.Height);
                }
                else
                {
                    target = GrowNode(ref root, entry.Width, entry.Height, maxDimension);
                    if (target == null) return result; // hit the cap — caller must shrink inputs
                }

                entry.X = target.X;
                entry.Y = target.Y;
                entry.Placed = true;
            }

            int width = root.W;
            int height = root.H;
            if (forcePowerOfTwo)
            {
                width = NextPowerOfTwo(width);
                height = NextPowerOfTwo(height);
            }

            if (width > maxDimension || height > maxDimension) return result;

            result.Success = true;
            result.Width = width;
            result.Height = height;
            return result;
        }

        /// <summary>Depth-first search for the first free node the rectangle fits into.</summary>
        private static Node FindNode(Node node, int w, int h)
        {
            if (node == null) return null;

            if (node.Used)
                return FindNode(node.Right, w, h) ?? FindNode(node.Down, w, h);

            return (w <= node.W && h <= node.H) ? node : null;
        }

        /// <summary>Marks a node used and carves the leftover space into right/down strips.</summary>
        private static Node SplitNode(Node node, int w, int h)
        {
            node.Used = true;
            node.Down = new Node { X = node.X, Y = node.Y + h, W = node.W, H = node.H - h };
            node.Right = new Node { X = node.X + w, Y = node.Y, W = node.W - w, H = h };
            return node;
        }

        /// <summary>
        /// Grows the atlas to fit a rectangle that no free node could hold, preferring the direction that
        /// keeps the atlas closest to square.
        /// </summary>
        private static Node GrowNode(ref Node root, int w, int h, int maxDimension)
        {
            bool canGrowDown = w <= root.W && root.H + h <= maxDimension;
            bool canGrowRight = h <= root.H && root.W + w <= maxDimension;

            // Prefer the growth that keeps the atlas squarer.
            bool shouldGrowRight = canGrowRight && root.H >= root.W + w;
            bool shouldGrowDown = canGrowDown && root.W >= root.H + h;

            if (shouldGrowRight) return GrowRight(ref root, w, h);
            if (shouldGrowDown) return GrowDown(ref root, w, h);
            if (canGrowRight) return GrowRight(ref root, w, h);
            if (canGrowDown) return GrowDown(ref root, w, h);

            return null;
        }

        private static Node GrowRight(ref Node root, int w, int h)
        {
            Node newRoot = new Node
            {
                Used = true,
                X = 0,
                Y = 0,
                W = root.W + w,
                H = root.H,
                Down = root,
                Right = new Node { X = root.W, Y = 0, W = w, H = root.H }
            };
            root = newRoot;

            Node target = FindNode(root, w, h);
            return target != null ? SplitNode(target, w, h) : null;
        }

        private static Node GrowDown(ref Node root, int w, int h)
        {
            Node newRoot = new Node
            {
                Used = true,
                X = 0,
                Y = 0,
                W = root.W,
                H = root.H + h,
                Down = new Node { X = 0, Y = root.H, W = root.W, H = h },
                Right = root
            };
            root = newRoot;

            Node target = FindNode(root, w, h);
            return target != null ? SplitNode(target, w, h) : null;
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value < 1) return 1;
            int result = 1;
            while (result < value) result <<= 1;
            return result;
        }
    }
}
