﻿/*
    Copyright (C) 2006-2018. Aardvark Platform Team. http://github.com/aardvark-platform.
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.
    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using Aardvark.Base;
using Aardvark.Data;
using Aardvark.Data.Points;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Aardvark.Geometry.Points
{
    /// <summary>
    /// </summary>
    public static class IPointCloudNodeExtensions
    {
        #region ForEach (optionally traversing out-of-core nodes) 

        /// <summary>
        /// Calls action for each node in this tree.
        /// </summary>
        public static void ForEachNode(
            this IPointCloudNode self, bool outOfCore, Action<IPointCloudNode> action)
        {
            action(self);

            if (self.Subnodes == null) return;

            if (outOfCore)
            {
                for (var i = 0; i < 8; i++)
                {
                    self.Subnodes[i]?.Value.ForEachNode(outOfCore, action);
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        if (n.TryGetValue(out IPointCloudNode node)) node.ForEachNode(outOfCore, action);
                    }
                }
            }
        }

        /// <summary>
        /// </summary>
        public static IEnumerable<IPointCloudNode> ForEachNode(
           this IPointCloudNode self, int minCellExponent = int.MinValue
           )
        {
            if (self == null) yield break;

            if (self.Cell.Exponent < minCellExponent) yield break;

            yield return self;

            if (self.Subnodes != null)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n == null) continue;
                    foreach (var x in ForEachNode(n.Value, minCellExponent)) yield return x;
                }
            }
        }

        /// <summary>
        /// Calls action for each (node, fullyInside) in this pointset, that is intersecting the given hull.
        /// </summary>
        public static void ForEachIntersectingNode(
            this IPointCloudNode self, bool outOfCore, Hull3d hull, bool doNotTraverseSubnodesWhenFullyInside,
            Action<IPointCloudNode, bool> action, CancellationToken ct = default
            )
        {
            ct.ThrowIfCancellationRequested();

            for (var i = 0; i < hull.PlaneCount; i++)
            {
                if (!self.IntersectsNegativeHalfSpace(hull.PlaneArray[i])) return;
            }

            bool fullyInside = true;
            for (var i = 0; i < hull.PlaneCount; i++)
            {
                if (!self.InsideNegativeHalfSpace(hull.PlaneArray[i]))
                {
                    fullyInside = false;
                    break;
                }
            }

            action(self, fullyInside);

            if (fullyInside && doNotTraverseSubnodesWhenFullyInside) return;

            if (self.Subnodes == null) return;

            if (outOfCore)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        n.Value.ForEachIntersectingNode(outOfCore, hull, doNotTraverseSubnodesWhenFullyInside, action, ct);
                    }
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        if (n.TryGetValue(out IPointCloudNode node))
                        {
                            node.ForEachIntersectingNode(outOfCore, hull, doNotTraverseSubnodesWhenFullyInside, action, ct);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calls action for each (node, fullyInside) in this tree that is intersecting the given hull.
        /// </summary>
        public static IEnumerable<CellQueryResult> ForEachNodeIntersecting(
            this IPointCloudNode self,
            Hull3d hull, bool doNotTraverseSubnodesWhenFullyInside, int minCellExponent = int.MinValue
            )
        {
            if (self == null) yield break;

            if (self.Cell.Exponent < minCellExponent) yield break;

            for (var i = 0; i < hull.PlaneCount; i++)
            {
                if (!self.IntersectsNegativeHalfSpace(hull.PlaneArray[i])) yield break;
            }

            bool fullyInside = true;
            for (var i = 0; i < hull.PlaneCount; i++)
            {
                if (!self.InsideNegativeHalfSpace(hull.PlaneArray[i]))
                {
                    fullyInside = false;
                    break;
                }
            }

            yield return new CellQueryResult(self, fullyInside);

            if (fullyInside && doNotTraverseSubnodesWhenFullyInside) yield break;

            if (self.Subnodes == null) yield break;
            for (var i = 0; i < 8; i++)
            {
                var n = self.Subnodes[i];
                if (n == null) continue;
                var xs = ForEachNodeIntersecting(n.Value, hull, doNotTraverseSubnodesWhenFullyInside, minCellExponent);
                foreach (var x in xs) yield return x;
            }
        }

        #endregion

        #region Intersections, inside/outside, ...

        /// <summary>
        /// Index of subnode for given point.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSubIndex(this IPointCloudNode self, in V3d p)
        {
            var i = 0;
            var c = self.Center;
            if (p.X > c.X) i = 1;
            if (p.Y > c.Y) i += 2;
            if (p.Z > c.Z) i += 4;
            return i;
        }

        /// <summary>
        /// Returns true if this node intersects the space within a given distance to a plane.
        /// </summary>
        public static bool Intersects(this IPointCloudNode self, Plane3d plane, double distance)
            => self.BoundingBoxExactGlobal.Intersects(plane, distance);

        /// <summary>
        /// Returns true if this node intersects the positive halfspace defined by given plane.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectsPositiveHalfSpace(this IPointCloudNode self, in Plane3d plane)
        {
            var corners = self.BoundingBoxExactGlobal.ComputeCorners();
            for (var i = 0; i < 8; i++)
            {
                if (plane.Height(corners[i]) > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if this node intersects the negative halfspace defined by given plane.
        /// </summary>
        public static bool IntersectsNegativeHalfSpace(this IPointCloudNode self, in Plane3d plane)
        {
            var corners = self.BoundingBoxExactGlobal.ComputeCorners();
            for (var i = 0; i < 8; i++)
            {
                if (plane.Height(corners[i]) < 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if this node is fully inside the positive halfspace defined by given plane.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InsidePositiveHalfSpace(this IPointCloudNode self, in Plane3d plane)
        {
            self.BoundingBoxExactGlobal.GetMinMaxInDirection(plane.Normal, out V3d min, out V3d max);
            return plane.Height(min) > 0;
        }

        /// <summary>
        /// Returns true if this node is fully inside the negative halfspace defined by given plane.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InsideNegativeHalfSpace(this IPointCloudNode self, in Plane3d plane)
        {
            self.BoundingBoxExactGlobal.GetMinMaxInDirection(-plane.Normal, out V3d min, out V3d max);
            return plane.Height(min) < 0;
        }

        #endregion

        #region Counts (optionally traversing out-of-core nodes)

        /// <summary>
        /// Eagerly counts all points in octree (without using PointCountTree property).
        /// </summary>
        public static long CountPoints(this IPointCloudNode self)
        {
            if (self == null) return 0;

            if (self.IsLeaf)
            {
                return self.Positions.Value.Length;
            }
            else
            {
                var count = 0L;
                if (self.Subnodes != null)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var n = self.Subnodes[i];
                        if (n != null) count += CountPoints(n.Value);
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Gets minimum point count of all tree nodes (eager).
        /// </summary>
        public static long GetMinimumNodePointCount(this IPointCloudNode self)
        {
            if (self == null) return 0;

            long min = self.PointCountCell;
            if (self.Subnodes != null)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        var x = GetMinimumNodePointCount(n.Value);
                        if (x < min) min = x;
                    }
                }
            }
            return min;
        }

        /// <summary>
        /// Gets maximum point count of all tree nodes (eager).
        /// </summary>
        public static long GetMaximumNodePointCount(this IPointCloudNode self)
        {
            if (self == null) return 0;

            long max = self.PointCountCell;
            if (self.Subnodes != null)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        var x = GetMaximumNodePointCount(n.Value);
                        if (x > max) max = x;
                    }
                }
            }
            return max;
        }

        /// <summary>
        /// Gets average point count of all tree nodes (eager).
        /// </summary>
        public static double GetAverageNodePointCount(this IPointCloudNode self)
        {
            if (self == null) return 0;
            long sum = 0, count = 0;
            _GetAverageNodePointCount(self, ref sum, ref count);
            return sum / (double)count;
        }
        private static void _GetAverageNodePointCount(this IPointCloudNode self, ref long sum, ref long count)
        {
            sum += self.PointCountCell;
            count++;

            if (self.Subnodes == null) return;

            for (var i = 0; i < 8; i++)
            {
                var n = self.Subnodes[i];
                if (n != null) _GetAverageNodePointCount(n.Value, ref sum, ref count);
            }
        }

        /// <summary>
        /// Total number of nodes.
        /// </summary>
        public static long CountNodes(this IPointCloudNode self, bool outOfCore)
        {
            var count = 1L;
            if (self.Subnodes != null)
            {
                if (outOfCore)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var n = self.Subnodes[i];
                        if (n != null) count += n.Value.CountNodes(outOfCore);
                    }
                }
                else
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var n = self.Subnodes[i];
                        if (n != null)
                        {
                            if (n.TryGetValue(out IPointCloudNode node)) count += node.CountNodes(outOfCore);
                        }
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Number of leaf nodes.
        /// </summary>
        public static long CountLeafNodes(this IPointCloudNode self, bool outOfCore)
        {
            if (self.Subnodes == null) return 1;

            var count = 0L;
            if (outOfCore)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null) count += n.Value.CountLeafNodes(outOfCore);
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        if (n.TryGetValue(out IPointCloudNode node)) count += node.CountLeafNodes(outOfCore);
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Gets minimum point count of leaf nodes.
        /// </summary>
        public static long GetMinimumLeafPointCount(this IPointCloudNode self, bool outOfCore)
        {
            var min = long.MaxValue;
            if (self.Subnodes != null)
            {
                if (outOfCore)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var n = self.Subnodes[i];
                        if (n != null)
                        {
                            var x = n.Value.GetMinimumLeafPointCount(outOfCore);
                            if (x < min) min = x;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var n = self.Subnodes[i];
                        if (n != null)
                        {
                            if (n.TryGetValue(out IPointCloudNode node))
                            {
                                var x = node.GetMinimumLeafPointCount(outOfCore);
                                if (x < min) min = x;
                            }
                        }
                    }
                }
            }
            return min;
        }

        /// <summary>
        /// Gets maximum point count of leaf nodes.
        /// </summary>
        public static long GetMaximumLeafPointCount(this IPointCloudNode self, bool outOfCore)
        {
            var max = long.MinValue;
            if (self.Subnodes != null)
            {
                if (outOfCore)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var n = self.Subnodes[i];
                        if (n != null)
                        {
                            var x = n.Value.GetMinimumLeafPointCount(outOfCore);
                            if (x > max) max = x;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var n = self.Subnodes[i];
                        if (n != null)
                        {
                            if (n.TryGetValue(out IPointCloudNode node))
                            {
                                var x = node.GetMinimumLeafPointCount(outOfCore);
                                if (x > max) max = x;
                            }
                        }
                    }
                }
            }
            return max;
        }

        /// <summary>
        /// Gets average point count of leaf nodes.
        /// </summary>
        public static double GetAverageLeafPointCount(this IPointCloudNode self, bool outOfCore)
        {
            return self.PointCountTree / (double)self.CountNodes(outOfCore);
        }

        /// <summary>
        /// Depth of tree (minimum).
        /// </summary>
        public static int GetMinimumTreeDepth(this IPointCloudNode self, bool outOfCore)
        {
            if (self.Subnodes == null) return 1;

            var min = int.MaxValue;

            if (outOfCore)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        var x = n.Value.GetMinimumTreeDepth(outOfCore);
                        if (x < min) min = x;
                    }
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        if (n.TryGetValue(out IPointCloudNode node))
                        {
                            var x = node.GetMinimumTreeDepth(outOfCore);
                            if (x < min) min = x;
                        }
                    }
                }
            }
            return 1 + (min != int.MaxValue ? min : 0);
        }

        /// <summary>
        /// Depth of tree (maximum).
        /// </summary>
        public static int GetMaximiumTreeDepth(this IPointCloudNode self, bool outOfCore)
        {
            if (self.Subnodes == null) return 1;

            var max = 0;

            if (outOfCore)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        var x = n.Value.GetMaximiumTreeDepth(outOfCore);
                        if (x > max) max = x;
                    }
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        if (n.TryGetValue(out IPointCloudNode node))
                        {
                            var x = node.GetMaximiumTreeDepth(outOfCore);
                            if (x > max) max = x;
                        }
                    }
                }
            }
            return 1 + max;
        }

        /// <summary>
        /// Depth of tree (average).
        /// </summary>
        public static double GetAverageTreeDepth(this IPointCloudNode self, bool outOfCore)
        {
            long sum = 0, count = 0;
            self.GetAverageTreeDepth(outOfCore, 1, ref sum, ref count);
            return sum / (double)count;
        }
        private static void GetAverageTreeDepth(this IPointCloudNode self, bool outOfCore, int depth, ref long sum, ref long count)
        {
            if (self.Subnodes == null)
            {
                sum += depth; count++;
                return;
            }

            ++depth;

            if (outOfCore)
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null) n.Value.GetAverageTreeDepth(outOfCore, depth, ref sum, ref count);
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    var n = self.Subnodes[i];
                    if (n != null)
                    {
                        if (n.TryGetValue(out IPointCloudNode node)) node.GetAverageTreeDepth(outOfCore, depth, ref sum, ref count);
                    }
                }
            }
        }

        #endregion

        #region TryGet*

//        private static PersistentRef<T> GetValue<T>(IPointCloudNode self, 
//            Durable.Def keyData, Durable.Def keyRef,
//            Func<string, T> get, Func<string, (bool, T)> tryGet
//            ) where T : class
//        {
//            if (self.TryGetValue(keyData, out var value))
//            {
//                if (value is T x) return PersistentRef<T>.FromValue(x);
//                if (value is PersistentRef<T> pref) return pref;
//            }

//            if (self.TryGetValue(keyRef, out var o) && o is Guid id)
//                return new PersistentRef<T>(id.ToString(), get, tryGet);

////#if DEBUG
////            Console.WriteLine($"keyData: {keyData}");
////            Console.WriteLine($"keyRef : {keyRef}");
////            foreach (var kv in self.Data) Console.WriteLine($"{kv.Key} - {kv.Value}");
////#endif
//            throw new InvalidOperationException($"Invariant 0725615a-a9a3-4989-86bd-a0b5708b2283. {keyData}. {keyRef}.");
//        }

        /// <summary>Returns null if node has no colors.</summary>
        public static PersistentRef<C4b[]> TryGetColors4b(this IPointCloudNode self)
            => self.HasColors ? self.Colors : null;

        /// <summary>Returns null if node has no normals.</summary>
        public static PersistentRef<V3f[]> TryGetNormals3f(this IPointCloudNode self)
            => self.HasNormals ? self.Normals : null;

        /// <summary>Returns null if node has no intensities.</summary>
        public static PersistentRef<int[]> TryGetIntensities(this IPointCloudNode self)
            => self.HasIntensities ? self.Intensities : null;

        /// <summary>Returns null if node has no classifications.</summary>
        public static PersistentRef<byte[]> TryGetClassifications(this IPointCloudNode self)
            => self.HasClassifications ? self.Classifications : null;

        #endregion

        /// <summary></summary>
        public static bool IsLeaf(this IPointCloudNode self) => self.Subnodes == null;

        /// <summary></summary>
        public static bool IsNotLeaf(this IPointCloudNode self) => self.Subnodes != null;

        /// <summary>
        /// Computes centroid of absolute (LoD) positions in this node, or null if no (LoD) positions.
        /// </summary>
        public static V3d? ComputeCentroid(this IPointCloudNode self) => self.HasPositions ? self.PositionsAbsolute.Average() : (V3d?)null;

    }
}
