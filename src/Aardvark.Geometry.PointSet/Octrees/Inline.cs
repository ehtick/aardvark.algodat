﻿/*
    Copyright (C) 2006-2019. Aardvark Platform Team. http://github.com/aardvark-platform.
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
using System;
using System.Collections.Generic;
using System.Linq;

namespace Aardvark.Geometry.Points
{
    public class InlineConfig
    {
        /// <summary>
        /// Collapse child nodes making each node 8 times as big.
        /// E.g. for an octree with split limit 8192, this would result in an octree with split limit 65536.
        /// </summary>
        public bool Collapse { get; }

        /// <summary>
        /// GZip inlined node.
        /// </summary>
        public bool GZipped { get; }

        /// <summary>
        /// Optionally round positions to given number of digits.
        /// </summary>
        public int? PositionsRoundedToNumberOfDigits { get; }

        public InlineConfig(bool collapse, bool gzipped, int? positionsRoundedToNumberOfDigits)
        {
            Collapse = collapse;
            GZipped = gzipped;
            PositionsRoundedToNumberOfDigits = positionsRoundedToNumberOfDigits;
        }

        public InlineConfig(bool collapse, bool gzipped) : this(collapse, gzipped, null) { }
    }

    /// <summary>
    /// </summary>
    public static class InlineExtensions
    {
        /// <summary>
        /// Experimental!
        /// Inlines and exports pointset to another store.
        /// </summary>
        public static void InlineOctree(this Storage storage, string key, Storage exportStore, InlineConfig config)
        {
            if (storage.TryGetOctree(key, out var root))
            {
                InlineOctree(storage, root, exportStore, config);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Key {key} not found in store. Invariant 0f7a7e31-f6c7-494b-a734-18c00dee3383."
                    );
            }
        }

        /// <summary>
        /// Experimental!
        /// Inlines and exports pointset to another store.
        /// </summary>
        public static void InlineOctree(this Storage storage, Guid key, Storage exportStore, InlineConfig config)
            => InlineOctree(storage, key.ToString(), exportStore, config);

        /// <summary>
        /// Experimental!
        /// Inlines and exports pointset to another store.
        /// </summary>
        public static void InlineOctree(
            this Storage storageSource, 
            IPointCloudNode root, 
            Storage storageTarget, 
            InlineConfig config
            )
        {
            Report.BeginTimed("inlining octree");

            var totalNodeCount = root.CountNodes(outOfCore: true);
            var newSplitLimit = root.PointCountCell * 8;
            Report.Line($"root              = {root.Id}");
            Report.Line($"split limit       = {root.PointCountCell,36:N0}");
            Report.Line($"split limit (new) = {newSplitLimit,36:N0}");
            Report.Line($"total node count  = {totalNodeCount,36:N0}");

            // export octree (recursively)
            var survive = new HashSet<Guid> { root.Id };
            ExportInlinedNode(root.Id);

            Report.EndTimed();

            void ExportInlinedNode(Guid key)
            {
                if (key == Guid.Empty) return;

                var node = GetNodeDataFromKey(key);
                var isLeafNode = !node.TryGetValue(Durable.Octree.SubnodesGuids, out var subnodeGuids);

                // when collapsing -> don't export leaf nodes,
                // because data has already been exported with parent node
                if (config.Collapse && isLeafNode && !survive.Contains(key))
                {
                    Report.Line($"skipping leaf {key}");
                    return;
                }

                // inline node data and export ...
                var inline = ConvertToInline(node).ToArray();
                var inlineCount = (int)inline.Single(kv => kv.Key == Durable.Octree.PointCountCell).Value;
                storageTarget.Add(key, Durable.Octree.Node, inline, config.GZipped);
                survive.Remove(key);
                Report.Line($"exported node {key} (survive = {survive.Count:N0})");

                // export children ...
                if (subnodeGuids != null)
                {
                    foreach (var x in (Guid[])subnodeGuids)
                    {
                        ExportInlinedNode(x);
                    }
                }
            }

            IEnumerable<KeyValuePair<Durable.Def, object>> ConvertToInline(
                IReadOnlyDictionary<Durable.Def, object> node
                )
            {
                var cell = (Cell)node[Durable.Octree.Cell];
                var cellCenter = cell.GetCenter();
                var bbExactGlobal = (Box3d)node[Durable.Octree.BoundingBoxExactGlobal];
                var pointCountCell = (int)node[Durable.Octree.PointCountCell];
                var pointCountTree = (long)node[Durable.Octree.PointCountTreeLeafs];
                node.TryGetValue(Durable.Octree.SubnodesGuids, out var subnodeGuids);

                var ps = default(V3f[]);
                var cs = default(C4b[]);

                if (config.Collapse && subnodeGuids != null)
                {
                    var guids = ((Guid[])subnodeGuids);

                    ps = guids
                        .Where(g => g != Guid.Empty)
                        .SelectMany(k =>
                        {
                            var n = GetNodeDataFromKey(k);
                            var nCell = ((Cell)n[Durable.Octree.Cell]);
                            var nCenter = nCell.GetCenter();
                            var delta = nCenter - cellCenter;
                            var xs = GetNodePositions(n).Map(x => (V3d)x + nCenter);
                            Report.Line($"    {k} -> {xs.Length}");
                            return xs;
                        })
                        .ToArray()
                        .Map(p => (V3f)(p - cellCenter));

                    cs = guids
                        .Where(g => g != Guid.Empty)
                        .Select(GetNodeDataFromKey)
                        .SelectMany(GetNodeColors)
                        .ToArray();

                    var guids2 = guids
                        .Map(k =>
                        {
                            if (k == Guid.Empty) return Guid.Empty;
                            var n = GetNodeDataFromKey(k);
                            return n.ContainsKey(Durable.Octree.SubnodesGuids) ? k : Guid.Empty;
                        });
                    var isNewLeaf = guids2.All(k => k == Guid.Empty);
                    if (isNewLeaf)
                    {
                        subnodeGuids = null;
                    }
                    else
                    {
                        foreach (var g in guids) if (g != Guid.Empty) survive.Add(g);
                    }

                    if (ps.Length != cs.Length) throw new InvalidOperationException(
                        $"Different number of positions ({ps.Length}) and colors ({cs.Length}). " +
                        "Invariant ac1cdac5-b7a2-4557-9383-ae80929af999."
                        );
                }
                else
                {
                    var psRef = node[Durable.Octree.PositionsLocal3fReference];
                    ps = storageSource.GetV3fArray(((Guid)psRef).ToString());

                    var csRef = node[Durable.Octree.Colors4bReference];
                    cs = storageSource.GetC4bArray(((Guid)csRef).ToString());
                }

                // optionally round positions
                if (config.PositionsRoundedToNumberOfDigits.HasValue)
                {
                    ps = ps.Map(x => x.Round(config.PositionsRoundedToNumberOfDigits.Value));
                }

                // result
                Report.Line($"    PointCountCell = {ps.Length}");
                pointCountCell = ps.Length;

                KeyValuePair<Durable.Def, object> Entry(Durable.Def def, object o) =>
                    new KeyValuePair<Durable.Def, object>(def, o);

                yield return Entry(Durable.Octree.Cell, cell);
                yield return Entry(Durable.Octree.BoundingBoxExactGlobal, bbExactGlobal);

                if (subnodeGuids != null)
                {
                    //Report.Line($"    {string.Join(",", ((Guid[])subnodeGuids).Select(x => x != Guid.Empty ? x.ToString() : "-"))}");
                    yield return Entry(Durable.Octree.SubnodesGuids, subnodeGuids);
                }

                yield return Entry(Durable.Octree.PointCountCell, pointCountCell);
                yield return Entry(Durable.Octree.PointCountTreeLeafs, pointCountTree);
                yield return Entry(Durable.Octree.PointCountTreeLeafsFloat64, (double)pointCountTree);
                yield return Entry(Durable.Octree.PositionsLocal3f, ps);

                //if (node.TryGetValue(Durable.Octree.Normals3fReference, out var nsRef))
                //{
                //    var ns = storage.GetV3fArray(((Guid)nsRef).ToString());
                //    yield return Entry(Durable.Octree.Normals3f, ns);
                //}

                yield return Entry(Durable.Octree.Colors3b, cs.Map(x => new C3b(x)));
            }

            IReadOnlyDictionary<Durable.Def, object> GetNodeDataFromKey(Guid key)
            {
                var (_, raw) = storageSource.GetDurable(key);
                if (raw != null) return (raw as IReadOnlyDictionary<Durable.Def, object>);

                var n = storageSource.GetPointCloudNode(key);
                if (n != null) return (n.Properties);

                return null;
            }

            V3f[] GetNodePositions(IReadOnlyDictionary<Durable.Def, object> n)
            {
                if (n.TryGetValue(Durable.Octree.PositionsLocal3fReference, out var r))
                {
                    return storageSource.GetV3fArray(((Guid)r).ToString());
                }
                else if (n.TryGetValue(Durable.Octree.PositionsLocal3f, out var ps))
                {
                    return (V3f[])ps;
                }
                else
                {
                    throw new InvalidOperationException("No positions. Invariant 167b491b-8e58-4e28-88ed-f0a69590465e.");
                }
            }

            C4b[] GetNodeColors(IReadOnlyDictionary<Durable.Def, object> n)
            {
                if (n.TryGetValue(Durable.Octree.Colors4bReference, out var r))
                {
                    return storageSource.GetC4bArray(((Guid)r).ToString());
                }
                else if (n.TryGetValue(Durable.Octree.Colors3b, out var cs))
                {
                    return ((C3b[])cs).Map(c => new C4b(c));
                }
                else
                {
                    throw new InvalidOperationException("No colors. Invariant 8516dbaf-9765-44ab-949c-79986514f1d1.");
                }
            }
        }

    }
}