﻿/*
    Copyright (C) 2006-2024. Aardvark Platform Team. http://github.com/aardvark-platform.
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
using Aardvark.Base.Sorting;
using Aardvark.Data;
using Aardvark.Data.Points;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace Aardvark.Geometry.Points;

/// <summary>
/// </summary>
public static class MergeExtensions
{
    private static bool DidMergeWorkObject(object? x, int xCount, object? y, int yCount, object? m)
    {
        var xqs = PartIndexUtils.ConcatIndices(x, xCount, y, yCount);
        var ass = ((int[])xqs!);
        ass.QuickSortAscending();
        var mss = ((int[])m!);
        mss.QuickSortAscending();

        var assHist =
            ass.GroupBy(i => i)
                .Select(group => (group.Key, group.Count()))
                .OrderBy(x => x.Key)
                .ToArray();
        var mssHist =
            mss.GroupBy(i => i)
                .Select(group => (group.Key, group.Count()))
                .OrderBy(x => x.Key)
                .ToArray();
        var eq = Enumerable.SequenceEqual(ass, mss);
        if (!eq)
        {
            Report.Begin("Ass");
            foreach (var (k, c) in assHist)
            {
                Report.Line("{0}:{1}", k, c);
            }
            Report.End();
            Report.Begin("Mss");
            foreach (var (k, c) in mssHist)
            {
                Report.Line("{0}:{1}", k, c);
            }
            Report.End();
        }
        return eq;
    }
    private static bool DidMergeWork(IPointCloudNode x, IPointCloudNode y,IPointCloudNode m)
    {
        object? xqs = null;
        object? mqs = null;
        CollectEverything(x, [], null, null, null, null, ref xqs);
        CollectEverything(y, [], null, null, null, null, ref xqs);
        CollectEverything(m, [], null, null, null, null, ref mqs);
        var ass = ((int[])xqs!);
        ass.QuickSortAscending();
        var mss = ((int[])mqs!);
        mss.QuickSortAscending();

        var assHist =
            ass.GroupBy(i => i)
                .Select(group => (group.Key, group.Count()))
                .OrderBy(x => x.Key)
                .ToArray();
        var mssHist =
            mss.GroupBy(i => i)
                .Select(group => (group.Key, group.Count()))
                .OrderBy(x => x.Key)
                .ToArray();
        var eq = Enumerable.SequenceEqual(ass, mss);
        if(!eq)
        {
            Report.Begin("Ass");
            foreach(var (k,c) in assHist)
            {
                Report.Line("{0}:{1}", k, c);
            }
            Report.End();
            Report.Begin("Mss");
            foreach (var (k, c) in mssHist)
            {
                Report.Line("{0}:{1}", k, c);
            }
            Report.End();
        }
        return eq;
    }

    /// <summary>
    /// Collects all leaf per-point properties into given lists.
    /// Returns number of leaves that have been collected.
    /// </summary>
    internal static int CollectEverything(IPointCloudNode self, List<V3d> ps, List<C4b>? cs, List<V3f>? ns, List<int>? js, List<byte>? ks, ref object? qs)
    {
        if (self == null) return 0;

        if (self.IsLeaf)
        {
            var initialCount = ps.Count;

            var off = self.Center;
            ps.AddRange(self.Positions.Value.Map(p => off + (V3d)p));

            if (self.HasColors          && cs != null) cs.AddRange(self.Colors.Value         );
            if (self.HasNormals         && ns != null) ns.AddRange(self.Normals.Value        );
            if (self.HasIntensities     && js != null) js.AddRange(self.Intensities.Value    );
            if (self.HasClassifications && ks != null) ks.AddRange(self.Classifications.Value);
            qs = PartIndexUtils.ConcatIndices(qs, initialCount, self.PartIndices, self.PointCountCell);

            return 1;
        }
        else
        {
            var leaves = 0;
            foreach (var x in self.Subnodes)
            {
                if (x != null)
                {
                    leaves += CollectEverything(x.Value, ps, cs, ns, js, ks, ref qs);
                }
            }

            if (leaves == 0) throw new Exception($"Expected at least 1 leaf. Error 5c37764f-0c38-4da2-b2cd-2840af83c687.");

            return leaves;
        }
    }

    internal static (IPointCloudNode, bool) CollapseLeafNodes(this IPointCloudNode self, ImportConfig config)
    {
        if (!self.IsTemporaryImportNode) throw new InvalidOperationException(
            "CollapseLeafNodes is only valid for temporary import nodes. Invariant 4aa0809d-4cb0-422b-97ee-fa5b6dc4785e."
            );

        if (self.PointCountTree <= config.OctreeSplitLimit)
        {
            if (self.IsLeaf)
            {
                // leaf node ...
                return (self.WriteToStore(), true);
            }
            else
            {
                // inner node ...

                var psla = new List<V3d>();
                var csla = new List<C4b>();
                var nsla = new List<V3f>();
                var jsla = new List<int>();
                var ksla = new List<byte>();
                var qsla = (object?)null;

                var leafCount = CollectEverything(self, psla, csla, nsla, jsla, ksla, ref qsla);

                var hasCollectedPartIndices = qsla != null;

                // positions might be slightly (~eps) outside this node's bounds,
                // due to floating point conversion from local sub-node space to global space
                var bb = self.BoundingBoxExactGlobal;
                var eps = bb.Size * 1e-5;
                for (var i = 0; i < psla.Count; i++)
                {
                    var p = psla[i];
                    if (p.X <= bb.Min.X)
                    {
                        if (!p.X.ApproximateEquals(bb.Min.X, eps.X)) Report.Warn(
                            $"[WARNING] Invariant p.X <= bb.Min.X ({p.X} <= {bb.Min.X}, eps.X = {bb.Min.X + eps.X}). " +
                            $"Bounding box is {bb}. " +
                            $"Shifting p={p} to p.X={bb.Min.X}. " +
                            $"Warning 4840fe92-02df-4b9a-8233-18edb12656f9."
                            );
                        p.X = bb.Min.X + eps.X;
                    }
                    if (p.Y <= bb.Min.Y)
                    {
                        if (!p.Y.ApproximateEquals(bb.Min.Y, eps.Y)) Report.Warn(
                            $"[WARNING] Invariant p.Y <= bb.Min.Y ({p.Y} <= {bb.Min.Y}, eps.Y = {bb.Min.Y + eps.Y}). " +
                            $"Bounding box is {bb} ." +
                            $"Shifting p={p} to p.Y={bb.Min.Y}. " +
                            $"Warning 942019a9-cb0d-476c-bfb8-69a2bde8debf."
                            );
                        p.Y = bb.Min.Y + eps.Y;
                    }
                    if (p.Z <= bb.Min.Z)
                    {
                        if (!p.Z.ApproximateEquals(bb.Min.Z, eps.Z)) Report.Warn(
                            $"[WARNING] Invariant p.Z <= bb.Min.Z ({p.Z} <= {bb.Min.Z}, eps.Z = {bb.Min.Z + eps.Z}). " +
                            $"Bounding box is {bb}. " +
                            $"Shifting p={p} to p.Z={bb.Min.Z}. " +
                            $"Warning 68fd4c9e-6de1-4a43-91ae-fec4a9fb28df."
                            );
                        p.Z = bb.Min.Z + eps.Z;
                    }
                    if (p.X >= bb.Max.X)
                    {
                        if (!p.X.ApproximateEquals(bb.Max.X, eps.X)) Report.Warn(
                            $"[WARNING] Invariant p.X >= bb.Max.X ({p.X} >= {bb.Max.X}, eps.X = {bb.Max.X - eps.X}). " +
                            $"Bounding box is {bb}. " +
                            $"Shifting p={p} to p.X={bb.Max.X}. " +
                            $"Warning a24f717c-19d9-46eb-9cf5-b1f6d928963a."
                            );
                        p.X = bb.Max.X - eps.X;
                    }
                    if (p.Y >= bb.Max.Y)
                    {
                        if (!p.Y.ApproximateEquals(bb.Max.Y, eps.Y)) Report.Warn(
                            $"[WARNING] Invariant p.Y >= bb.Max.Y ({p.Y} >= {bb.Max.Y}, eps.Y = {bb.Max.Y - eps.Y}). " +
                            $"Bounding box is {bb}. " +
                            $"Shifting p={p} to p.Y={bb.Max.Y}. " +
                            $"Warning fd8aaa89-43d3-428c-9d95-a62bf5a41b07."
                            );
                        p.Y = bb.Max.Y - eps.Y;
                    }
                    if (p.Z >= bb.Max.Z)
                    {
                        if (!p.Z.ApproximateEquals(bb.Max.Z, eps.Z)) Report.Warn(
                            $"[WARNING] Invariant p.Z >= bb.Max.Z ({p.Z} >= {bb.Max.Z}, eps.Z = {bb.Max.Z - eps.Z}). " +
                            $"Bounding box is {bb}. " +
                            $"Shifting p={p} to p.Z={bb.Max.Z}. " +
                            $"Warning 9905f569-16d0-4e46-8ae2-147aeb6e7acc."
                            );
                        p.Z = bb.Max.Z - eps.Z;
                    }

                    psla[i] = p;
                }
                var bbNew = new Box3d(psla);

#if DEBUG
                {
                    // Invariant: bounding box of collected positions MUST be contained in original trees bounding box
                    if (!self.BoundingBoxExactGlobal.Contains(new Box3d(psla))) throw new Exception($"Invariant 0fdad697-b315-45b2-a581-49db8c46e20e.");
                }
#endif

                if (leafCount <= 1)
                {
                    return (self.WriteToStore(), true);
                }
                else
                {
                    var chunk = new Chunk(
                        psla.Count > 0 ? psla : null,
                        csla.Count > 0 ? csla : null,
                        nsla.Count > 0 ? nsla : null,
                        jsla.Count > 0 ? jsla : null,
                        ksla.Count > 0 ? ksla : null,
                        qsla, partIndexRange: null,
                        bbox: null
                        );

                    if (config.NormalizePointDensityGlobal)
                    {
                        chunk = chunk.ImmutableFilterMinDistByCell(self.Cell, config.ParseConfig);
                    }

#if DEBUG
                    {
                        // Invariant: collected and filtered subtree data MUST still have no more points than split limit
                        if (chunk.Count > config.OctreeSplitLimit) throw new Exception($"Invariant 8d48f48c-9f35-4d14-a9fc-80d33bf94615.");
                    }
#endif

                    var inMemory = InMemoryPointSet.Build(chunk, config.OctreeSplitLimit);
                    var collapsedNode = inMemory.ToPointSetNode(self.Storage, isTemporaryImportNode: true);

#if DEBUG
                    {
                        // Invariant: collapsed node's bounding box MUST be contained in original tree's bounding box
                        if (!self.BoundingBoxExactGlobal/*.EnlargedByRelativeEps(1e-6)*/.Contains(collapsedNode.BoundingBoxExactGlobal))
                            throw new Exception($"Invariant 0936ab9d-7c4a-4873-86ab-d36deb163716.");

                        // Invariant: original tree's root node cell MUST contain collapsed node's cell 
                        if (self.Cell != collapsedNode.Cell) throw new Exception($"Invariant 8370ea8c-ba61-42ba-823e-94d596cb5f3f.");

                        // Invariant: collapsed node MUST still be a leaf node
                        if (collapsedNode.IsNotLeaf) throw new Exception($"Invariant c4f7e0e3-9ad5-4cba-80ee-4b0849a995e6.");
                    }
#endif

                    // Invariant: collapsed node must retain original part indices and part index range (if any)
                    if (hasCollectedPartIndices)
                    {
                        if (!collapsedNode.HasPartIndices) throw new Exception($"Invariant 58389bf7-be01-46f1-b0df-f75942eea2b7.");
                        if (!collapsedNode.HasPartIndexRange) throw new Exception($"Invariant 5adce094-281b-4617-b45a-1805301e34af.");
                    }

                    if (self.Cell != collapsedNode.Cell)
                    {
                        return (JoinTreeToRootCell(self.Cell, collapsedNode, config, collapse: false), true);
                    }
                    else
                    {
                        return (collapsedNode, true);
                    }
                }
            }
        }
        else
        {
            return (self.WriteToStore(), true);
        }
    }

    /// <summary>
    /// If node is a leaf, it will be split once (non-recursive, without taking into account any split limit).
    /// If node is not a leaf, this is an invalid operation.
    /// </summary>
    internal static IPointCloudNode ForceSplitLeaf(this IPointCloudNode self, ImportConfig config)
    {
        if (!self.IsTemporaryImportNode) throw new InvalidOperationException(
            "ForceSplitLeaf is only valid for temporary import nodes. Invariant 3bfca971-be98-45b7-86e7-de436b78cefb."
            );

        if (self == null) throw new ArgumentNullException(nameof(self));
        if (self.IsLeaf == false) throw new InvalidOperationException();
        if (self.PointCountCell == 0) throw new InvalidOperationException();
        if (self.PointCountTree != self.PointCountCell) throw new InvalidOperationException();
        if (self.HasPartIndices && !self.HasPartIndexRange) throw new InvalidOperationException();

        var ps = self.PositionsAbsolute;
        var cs = self.Colors?.Value;
        var ns = self.Normals?.Value;
        var js = self.Intensities?.Value;
        var ks = self.Classifications?.Value;
        var qs = self.PartIndices;

        var pss = new V3d[]?[8];
        var css = self.HasColors ? new C4b[]?[8] : null;
        var nss = self.HasNormals ? new V3f[]?[8] : null;
        var jss = self.HasIntensities ? new int[]?[8] : null;
        var kss = self.HasClassifications ? new byte[]?[8] : null;
        var qss = self.HasPartIndices ? new object?[8] : null;

        var imax = self.PointCountCell;
        if (ps.Length != imax) throw new InvalidOperationException();

        var ias = new List<int>[8];
        for (var i = 0; i < 8; i++) ias[i] = [];
        for (var i = 0; i < imax; i++) ias[self.GetSubIndex(ps[i])].Add(i);

        for (var i = 0; i < 8; i++)
        {
            var ia = ias[i];
            if (ia.Count == 0) continue;

            pss[i] = ps.Subset(ia);
            if (css != null) css[i] = cs?.Subset(ia);
            if (nss != null) nss[i] = ns?.Subset(ia);
            if (jss != null) jss[i] = js?.Subset(ia);
            if (kss != null) kss[i] = ks?.Subset(ia);
            if (qss != null) qss[i] = PartIndexUtils.Subset(qs, ia);
        }

        var subnodes = new PointSetNode[8];
        for (var i = 0; i < 8; i++)
        {
            var subPs = pss[i];
            if (subPs == null) continue;

            var subCell = self.Cell.GetOctant(i);
            if (!self.Cell.Contains(subCell)) throw new InvalidOperationException();
            if (self.Cell.Exponent != subCell.Exponent + 1) throw new InvalidOperationException();

            var chunk = new Chunk(subPs, css?[i], nss?[i], jss?[i], kss?[i], qss?[i], partIndexRange: null, subCell.BoundingBox);

            if (config.NormalizePointDensityGlobal)
            {
                chunk = chunk.ImmutableFilterMinDistByCell(subCell, config.ParseConfig);
            }

            var builder = InMemoryPointSet.Build(subPs, css?[i], nss?[i], jss?[i], kss?[i], qss?[i], subCell, int.MaxValue);

            var subnode = builder.ToPointSetNode(config.Storage, isTemporaryImportNode: true);
            if (subnode.PointCountTree > subPs.Length) throw new InvalidOperationException();
            if (!self.Cell.Contains(subnode.Cell)) throw new InvalidOperationException();
            if (self.Cell.Exponent != subnode.Cell.Exponent + 1) throw new InvalidOperationException();
            
            subnodes[i] = subnode;
        }

        var bbExactGlobal = new Box3d(subnodes.Where(x => x != null).Select(x => x.BoundingBoxExactGlobal));

        var data = ImmutableDictionary<Durable.Def, object>.Empty
            .Add(PointSetNode.TemporaryImportNode, 0)
            .Add(Durable.Octree.NodeId, Guid.NewGuid())
            .Add(Durable.Octree.Cell, self.Cell)
            .Add(Durable.Octree.BoundingBoxExactGlobal, bbExactGlobal)
            .Add(Durable.Octree.PointCountTreeLeafs, self.PointCountTree)
            .Add(Durable.Octree.SubnodesGuids, subnodes.Map(x => x?.Id ?? Guid.Empty))
            ;
        var result = new PointSetNode(data, self.Storage, true);

        // POST
        if (result.IsLeaf) throw new InvalidOperationException();
        if (result.PointCountTree != self.PointCountTree) throw new InvalidOperationException();
        if (result.PointCountCell != 0) throw new InvalidOperationException();
        if (result.Subnodes.Sum(x => x?.Value?.PointCountTree) > self.PointCountTree) throw new InvalidOperationException();
        if (self.HasPartIndexRange && !result.HasPartIndexRange) throw new InvalidOperationException();

        return result;
    }

    /// <summary>
    /// Returns union of trees as new tree (immutable operation).
    /// </summary>
    public static (IPointCloudNode, bool) Merge(this IPointCloudNode a, IPointCloudNode b, Action<long> pointsMergedCallback, ImportConfig config)
    {
        if (!a.IsTemporaryImportNode || !b.IsTemporaryImportNode) throw new InvalidOperationException(
            "Merge is only allowed on temporary import nodes. Invariant d53042e7-a032-47a9-98dc-034c0749a649."
            );

        if (a == null || a.PointCountTree == 0) { pointsMergedCallback.Invoke(b.PointCountTree); return (b, true); }
        if (b == null || b.PointCountTree == 0) { pointsMergedCallback.Invoke(a.PointCountTree); return (a, true); }

#if DEBUG && NEVERMORE
        if (config.Verbose) Report.Line($"[Merge] a = {a.Cell}, b = {b.Cell}");
#endif

        // expect
        if (a.HasPartIndexRange != b.HasPartIndexRange) throw new Exception("Invariant ff2ea514-a7b7-46dd-b147-ff7bd58ed1db.");

        if (a.PointCountTree + b.PointCountTree <= config.OctreeSplitLimit)
        {
            var psla = new List<V3d>();
            var csla = new List<C4b>();
            var nsla = new List<V3f>();
            var jsla = new List<int>();
            var ksla = new List<byte>();
            var qsla = (object?)null;

            CollectEverything(a, psla, csla, nsla, jsla, ksla, ref qsla);
            CollectEverything(b, psla, csla, nsla, jsla, ksla, ref qsla);


            var range = PartIndexUtils.MergeRanges(a.PartIndexRange, b.PartIndexRange);

            //var checkRange = PartIndexUtils.GetRange(qsla);
            //if (range != checkRange) throw new Exception("!aasdfasdasd");


            var cell = new Cell(a.Cell, b.Cell);
            var chunk = new Chunk(
                psla.Count > 0 ? psla : null,
                csla.Count > 0 ? csla : null,
                nsla.Count > 0 ? nsla : null,
                jsla.Count > 0 ? jsla : null,
                ksla.Count > 0 ? ksla : null,
                partIndices: qsla,
                partIndexRange: range,
                bbox: null
                ); 

            if (config.NormalizePointDensityGlobal)
            {
                chunk = chunk.ImmutableFilterMinDistByCell(cell, config.ParseConfig);
            }

            var storage = config.Storage;
            var ac = a.Center;
            var bc = b.Center;

            var psAbs = chunk.Positions.ToArray();
            var ns = chunk.Normals?.ToArray();
            var cs = chunk.Colors?.ToArray();
            var js = chunk.Intensities?.ToArray();
            var ks = chunk.Classifications?.ToArray();
            var qs = chunk.PartIndices;
            var qsRange = chunk.PartIndexRange;

            var bbExactGlobal = chunk.BoundingBox;

            Guid psId = Guid.NewGuid();
            //Guid? kdId = psAbs != null ? Guid.NewGuid() : (Guid?)null;
            Guid? nsId = ns != null ? Guid.NewGuid() : null;
            Guid? csId = cs != null ? Guid.NewGuid() : null;
            Guid? jsId = js != null ? Guid.NewGuid() : null;
            Guid? ksId = ks != null ? Guid.NewGuid() : null;


            var center = cell.BoundingBox.Center;

            var ps = psAbs.Map(p => (V3f)(p - center));

            var data = ImmutableDictionary<Durable.Def, object>.Empty
                .Add(PointSetNode.TemporaryImportNode, 0)
                .Add(Durable.Octree.NodeId, Guid.NewGuid())
                .Add(Durable.Octree.Cell, cell)
                .Add(Durable.Octree.BoundingBoxExactGlobal, bbExactGlobal)
                .Add(Durable.Octree.PointCountTreeLeafs, ps.LongLength)
                ;

            storage.Add(psId, ps ); data = data.Add(Durable.Octree.PositionsLocal3fReference, psId);
            //if (kdId.HasValue) { storage.Add(kdId.Value, kd.Data); data = data.Add(Durable.Octree.PointRkdTreeFDataReference, kdId.Value); }
            if (nsId.HasValue) { storage.Add(nsId.Value, ns!); data = data.Add(Durable.Octree.Normals3fReference        , nsId.Value); }
            if (csId.HasValue) { storage.Add(csId.Value, cs!); data = data.Add(Durable.Octree.Colors4bReference         , csId.Value); }
            if (jsId.HasValue) { storage.Add(jsId.Value, js!); data = data.Add(Durable.Octree.Intensities1iReference    , jsId.Value); }
            if (ksId.HasValue) { storage.Add(ksId.Value, ks!); data = data.Add(Durable.Octree.Classifications1bReference, ksId.Value); }
            if (qs != null) 
            {
                if (qsRange == null) throw new Exception("Invariant 7c01f554-d833-42cc-ab1d-9dbaa61bef45.");
                data = data.Add(Durable.Octree.PartIndexRange, qsRange);

                if (qs is Array qsArray)
                {
                    // store separately and reference by id ...
                    var id = Guid.NewGuid();
                    storage.Add(id, qsArray);
                    data = qs switch
                    {
                        byte[]  => data.Add(Durable.Octree.PerPointPartIndex1bReference, id),
                        short[] => data.Add(Durable.Octree.PerPointPartIndex1sReference, id),
                        int[]   => data.Add(Durable.Octree.PerPointPartIndex1iReference, id),
                        _       => throw new Exception($"Unexpected type {qs.GetType()}. Invariant cc05e74c-8cac-4d32-9972-0ea53e6e0911."),
                    };
                }
                else
                {
                    var def = PartIndexUtils.GetDurableDefForPartIndices(qs);
                    data = data.Add(def, qs);
                }
                
            }

            var result = new PointSetNode(data, config.Storage, writeToStore: true);
            if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant 28925464-2ff0-49e8-bf77-c97cbb2dcb47.");
            return (result, true);
        }


        // if A and B have identical root cells, then merge ...
        if (a.Cell == b.Cell)
        {
            var result = a.IsLeaf
                ? (b.IsLeaf ? MergeLeafAndLeafWithIdenticalRootCell(a, b, config)
                            : MergeLeafAndTreeWithIdenticalRootCell(a, b, config))
                : (b.IsLeaf ? MergeLeafAndTreeWithIdenticalRootCell(b, a, config)
                            : MergeTreeAndTreeWithIdenticalRootCell(a, b, pointsMergedCallback, config))
                ;

            //DidMergeWork(a, b, result);
            pointsMergedCallback?.Invoke(a.PointCountTree + b.PointCountTree);
            if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant 6b05a096-bab5-4a51-8b4c-1eff19e6806d.");
            return result.CollapseLeafNodes(config);
        }

        // if A and B do not intersect ...
        if (!a.Cell.Intersects(b.Cell))
        {
            var rootCell = new Cell(a.Cell, b.Cell);
            var result = JoinNonOverlappingTrees(rootCell, a, b, pointsMergedCallback, config);
//#if DEBUG
//                if (!config.NormalizePointDensityGlobal && result.PointCountTree != totalPointCountTree) throw new InvalidOperationException();
//#endif
            pointsMergedCallback?.Invoke(a.PointCountTree + b.PointCountTree);
            if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant b4f38aff-6499-44cf-9f0f-f8c3e78f0538.");
            return result.CollapseLeafNodes(config);
        }

        if (a.Cell.IsCenteredAtOrigin || b.Cell.IsCenteredAtOrigin)
        {
            // enumerate all non-IsCenteredAtOrigin (sub)cells of A and B
            var parts = new List<IPointCloudNode?>();
            if (a.Cell.IsCenteredAtOrigin)
            {
                if (a.IsLeaf)
                {
                    // split A into 8 subcells to get rid of centered cell
                    var aSplit = a.ForceSplitLeaf(config); 
                    if (a.HasPartIndexRange != aSplit.HasPartIndexRange) throw new Exception("Invariant 018b94f4-50fe-4d15-a2fe-6e5d93cbf9a0.");
                    return Merge(aSplit, b, pointsMergedCallback, config);
                }
                else
                {
                    parts.AddRange(a.Subnodes.Select(x => x?.Value));
                }
            }
            else
            {
                parts.Add(a);
            }

            if (b.Cell.IsCenteredAtOrigin)
            {
                if (b.IsLeaf)
                {
                    // split B into 8 subcells to get rid of centered cell
                    var bSplit = b.ForceSplitLeaf(config);
                    if (a.HasPartIndexRange != bSplit.HasPartIndexRange) throw new Exception("Invariant 59650027-2b6e-4ddd-8c3b-2bf6b3d3e08b.");
                    return Merge(a, bSplit, pointsMergedCallback, config);
                }
                else
                {
                    parts.AddRange(b.Subnodes.Select(x => x?.Value));
                }
            }
            else
            {
                parts.Add(b);
            }

            // special case: there is only 1 part -> finished
            List<IPointCloudNode> partsNonNull = parts.Where(x => x != null).ToList()!;
            if (partsNonNull.Count == 0) throw new InvalidOperationException();
            if (partsNonNull.Count == 1)
            {
                var r = partsNonNull.Single();
                pointsMergedCallback?.Invoke(r.PointCountTree);
                if (a.HasPartIndexRange != r.HasPartIndexRange) throw new Exception("Invariant 2d6be9c1-0f65-48d3-985a-856a82085dca.");
                return r.CollapseLeafNodes(config);
            }

            // common case: multiple parts
            var rootCell = new Cell(a.Cell, b.Cell);
            var roots = new IPointCloudNode[8];

            static int octant(Cell x)
            {
                if (x.IsCenteredAtOrigin) throw new InvalidOperationException();
                return (x.X >= 0 ? 1 : 0) + (x.Y >= 0 ? 2 : 0) + (x.Z >= 0 ? 4 : 0);
            }

            var qsRange = (Range1i?)null;
            foreach (var x in partsNonNull)
            {
                var oi = octant(x.Cell);
                var oct = rootCell.GetOctant(oi);
                IPointCloudNode r;
                if (roots[oi] == null)
                {
                    if (x.Cell != oct)
                    {
                        if (!oct.Contains(x.Cell)) throw new InvalidOperationException();
                        r = JoinTreeToRootCell(oct, x, config);
                        if (oct != r.Cell) throw new InvalidOperationException();
                    }
                    else
                    {
                        r = x;
                        if (oct != r.Cell) throw new InvalidOperationException();
                    }
                }
                else
                {
                    r = Merge(roots[oi], x, pointsMergedCallback, config).Item1;
                    if (oct != r.Cell) throw new InvalidOperationException();
                }

                if (oct != r.Cell) throw new InvalidOperationException();
                roots[oi] = r;

                qsRange = PartIndexUtils.MergeRanges(qsRange, r.PartIndexRange);
            }

            var pointCountTreeLeafs = roots.Where(x => x != null).Sum(x => x.PointCountTree);
            var bbExactGlobal = new Box3d(roots.Where(x => x != null).Select(x => x.BoundingBoxExactGlobal));
            pointsMergedCallback?.Invoke(pointCountTreeLeafs);

            var data = ImmutableDictionary<Durable.Def, object>.Empty
                .Add(PointSetNode.TemporaryImportNode, 0)
                .Add(Durable.Octree.NodeId, Guid.NewGuid())
                .Add(Durable.Octree.Cell, rootCell)
                .Add(Durable.Octree.BoundingBoxExactGlobal, bbExactGlobal)
                .Add(Durable.Octree.PointCountTreeLeafs, pointCountTreeLeafs)
                .Add(Durable.Octree.SubnodesGuids, roots.Map(n => n?.Id ?? Guid.Empty))
                ;

            if (qsRange != null) data = data.Add(Durable.Octree.PartIndexRange, qsRange);

            var result = new PointSetNode(data, config.Storage, writeToStore: true);
            if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant 71148683-b67c-41e5-aeff-2d63643fd440.");
            return result.CollapseLeafNodes(config);
        }
#if DEBUG
        if (a.Cell.Exponent == b.Cell.Exponent)
        {
            if (!a.Cell.IsCenteredAtOrigin && !b.Cell.IsCenteredAtOrigin) throw new InvalidOperationException(
                $"merge {a.Cell} with {b.Cell}")
                ;
        }
#endif

        // ... otherwise ensure that A's root cell is bigger than B's to reduce number of cases to handle ...
        if (a.Cell.Exponent < b.Cell.Exponent)
        {
            var result = Merge(b, a, pointsMergedCallback, config);
            if (a.HasPartIndexRange != result.Item1.HasPartIndexRange) throw new Exception("Invariant ce60ef6a-c752-4ada-a540-6eb8bf8b0aec.");
            return result;
        }

        // ... B must now be contained in exactly one of A's subcells
#if DEBUG
        var isExactlyOne = false;
#endif
        var processedPointCount = 0L;
        IPointCloudNode?[] subcells = a.Subnodes?.Map(x => x?.Value) ?? new IPointCloudNode?[8];
        for (var i = 0; i < 8; i++)
        {
            var subcellIndex = a.Cell.GetOctant(i);
            if (subcellIndex.Contains(b.Cell))
            {
#if DEBUG
                if (isExactlyOne) throw new InvalidOperationException();
                isExactlyOne = true;
#endif
                if (subcells[i] == null)
                {
                    subcells[i] = JoinTreeToRootCell(subcellIndex, b, config);
                }
                else
                {
                    subcells[i] = Merge(subcells[i]!, b, 
                        n => pointsMergedCallback?.Invoke(processedPointCount + n),
                        config).Item1;
                }

                processedPointCount += subcells[i]?.PointCountTree ?? 0L;
                pointsMergedCallback?.Invoke(processedPointCount);
            }
        }
#if DEBUG
        if (!isExactlyOne) throw new InvalidOperationException();
#endif
        IPointCloudNode result2;
        if (a.IsLeaf)
        {
            result2 = a.WithSubNodes(subcells!);
            result2 = InjectPointsIntoTree(
                a.PositionsAbsolute, a.Colors?.Value, a.Normals?.Value, a.Intensities?.Value, a.Classifications?.Value, a.PartIndices,
                result2, result2.Cell, config);
        }
        else
        {
            result2 = a.WithSubNodes(subcells);
        }

        pointsMergedCallback?.Invoke(result2.PointCountTree);
        if (a.HasPartIndexRange != result2.HasPartIndexRange) throw new Exception("Invariant 18852eb2-1be0-4f22-9c9c-1f824c657685.");
        return result2.CollapseLeafNodes(config);
    }
    
    private static T[]? Concat<T>(T[]? xs, T[]? ys)
    {
        if (xs == null && ys == null) return null;
        if ((xs == null) != (ys == null)) throw new InvalidOperationException();
        var rs = new T[xs!.Length + ys!.Length];
        Array.Copy(xs, 0, rs, 0, xs.Length);
        Array.Copy(ys, 0, rs, xs.Length, ys.Length);
        return rs;
    }
    
    private static IPointCloudNode JoinNonOverlappingTrees(Cell rootCell, IPointCloudNode a, IPointCloudNode b,
        Action<long> pointsMergedCallback, ImportConfig config
        )
    {
        #region Preconditions

        // PRE: ensure that trees 'a' and 'b' do not intersect,
        // because we are joining non-overlapping trees here
        if (a.Cell == b.Cell || a.Cell.Intersects(b.Cell)) throw new InvalidOperationException();

        // PRE: we further assume, that both trees are non-empty
        if (a.PointCountTree == 0 && b.PointCountTree == 0) throw new InvalidOperationException();

        // PRE: assume that part indices are available in both trees or in no tree (but not in one or the other)
        if (a.HasPartIndexRange != b.HasPartIndexRange) throw new Exception("Invariant b3feedfd-927d-4436-9eb9-350d377ab852.");
        var hasPartIndices = a.HasPartIndexRange && b.HasPartIndexRange;
        #endregion

        #region Case reduction

        // REDUCE CASES:
        // if one tree ('a' or 'b') is centered at origin, then ensure that 'a' is centered
        // (by swapping 'a' and 'b' if necessary)
        if (b.Cell.IsCenteredAtOrigin)
        {
#if DEBUG
            // PRE: if 'b' is centered, than 'a' cannot be centered
            // (because then 'a' and 'b' would overlap, and we join non-overlapping trees here)
            if (a.Cell.IsCenteredAtOrigin) throw new InvalidOperationException();
#endif
            Fun.Swap(ref a, ref b);
#if DEBUG
            // POST: 'a' is centered, 'b' is not centered
            if (!a.Cell.IsCenteredAtOrigin) throw new InvalidOperationException();
            if (b.Cell.IsCenteredAtOrigin) throw new InvalidOperationException();
#endif
        }
#endregion
        
#region CASE 1 of 2: one tree is centered (must be 'a', since if it originally was 'b' we would have swapped)

        if (rootCell.IsCenteredAtOrigin && a.Cell.IsCenteredAtOrigin)
        {
#region special case: split 'a' into subcells to get rid of centered cell containing points
            if (a.IsLeaf)
            {
                var aForceSplitLeaf = a.ForceSplitLeaf(config);
                if (aForceSplitLeaf.HasPartIndexRange != b.HasPartIndexRange) throw new Exception("Invariant f4b031c0-c81f-4e81-87d9-35ea15b2b9ea.");
                var r = JoinNonOverlappingTrees(rootCell, aForceSplitLeaf, b, pointsMergedCallback, config);
                if (hasPartIndices && !r.HasPartIndexRange) throw new Exception("Invariant b3feedfd-927d-4436-9eb9-350d377ab852.");
                return r;
            }
#endregion
#if DEBUG
            if (a.PointCountCell != 0) throw new InvalidOperationException();
#endif

            var subcells = new IPointCloudNode?[8];
            for (var i = 0; i < 8; i++)
            {
                var rootCellOctant = rootCell.GetOctant(i);

                var aSub = a.Subnodes![i]?.Value;
                var bIsContained = rootCellOctant.Contains(b.Cell);
#if DEBUG
                if (!bIsContained && rootCellOctant.Intersects(b.Cell)) throw new InvalidOperationException();
#endif

                if (aSub != null)
                {
                    if (bIsContained)
                    {
                        // CASE: both contained
                        if (aSub.HasPartIndexRange != b.HasPartIndexRange) throw new Exception("Invariant 7bbc36bb-8dba-4224-b6fb-660bc40b0829.");
                        var merged = Merge(aSub, b, pointsMergedCallback, config).Item1;
                        if (hasPartIndices && !merged.HasPartIndexRange) throw new Exception("Invariant 9c41c146-4cce-4313-b16a-38c20d07266f.");
                        subcells[i] = JoinTreeToRootCell(rootCellOctant, merged, config);
                        if (hasPartIndices && !subcells[i]!.HasPartIndexRange) throw new Exception("Invariant 45548cc0-f069-4812-9a36-fb3e2cbe9adf.");
                    }
                    else
                    {
                        // CASE: aSub contained
                        if (hasPartIndices && !aSub.HasPartIndexRange) throw new Exception("Invariant cd7ea7d5-ad25-4c6d-9e04-457c4fcb8710.");
                        subcells[i] = JoinTreeToRootCell(rootCellOctant, aSub, config);
                        if (hasPartIndices && !subcells[i]!.HasPartIndexRange) throw new Exception("Invariant 896cbd9e-5891-4af3-b990-2873fe6568e5.");
                    }
                }
                else
                {
                    if (bIsContained)
                    {
                        // CASE: b contained
                        if (hasPartIndices && !b.HasPartIndexRange) throw new Exception("Invariant 12803bc1-e910-4a2c-95da-aabe26c1355f.");
                        subcells[i] = JoinTreeToRootCell(rootCellOctant, b, config);
                        if (hasPartIndices && !subcells[i]!.HasPartIndexRange) throw new Exception("Invariant 424b1b8f-cdad-4ad9-bd22-f6d66e2e67b5.");
                    }
                    else
                    {
                        // CASE: none contained -> empty subcell
                        subcells[i] = null;
                    }
                }
            }

            var data = ImmutableDictionary<Durable.Def, object>.Empty
                .Add(PointSetNode.TemporaryImportNode, 0)
                .Add(Durable.Octree.NodeId, Guid.NewGuid())
                .Add(Durable.Octree.Cell, rootCell)
                .Add(Durable.Octree.PointCountTreeLeafs, a.PointCountTree + b.PointCountTree)
                .Add(Durable.Octree.SubnodesGuids, subcells.Map(x => x?.Id ?? Guid.Empty))
                ;

            if (a.HasPartIndexRange)
            {
                var mergedPartIndexRange = PartIndexUtils.MergeRanges(a.PartIndexRange, b.PartIndexRange) ?? throw new Exception("Invariant 2cac0791-b636-458c-b5bd-c3ef66477511.");
                data = data.Add(Durable.Octree.PartIndexRange, mergedPartIndexRange);
            }

            var result = new PointSetNode(data, config.Storage, writeToStore: false).CollapseLeafNodes(config).Item1;
#if DEBUG
            if (result.PointCountTree != result.Subnodes.Sum(x => x?.Value?.PointCountTree)) throw new InvalidOperationException();
#endif
            //pointsMergedCallback?.Invoke(result.PointCountTree);
            if (hasPartIndices && !result.HasPartIndexRange) throw new Exception("Invariant 595235c3-7541-41e4-9bca-77d38daff7fd.");
            return result;
        }

#endregion

#region CASE 2 of 2: no tree is centered

        else
        {
#if DEBUG
            // PRE: no tree is centered
            if (a.Cell.IsCenteredAtOrigin) throw new InvalidOperationException();
            if (b.Cell.IsCenteredAtOrigin) throw new InvalidOperationException();
#endif

            var subcells = new IPointCloudNode[8];
            var doneA = false;
            var doneB = false;
            for (var i = 0; i < 8; i++)
            {
                var subcell = rootCell.GetOctant(i);
                if (subcell.Contains(a.Cell))
                {
#if DEBUG
                    if (subcell.Intersects(b.Cell)) throw new InvalidOperationException();
#endif
                    subcells[i] = JoinTreeToRootCell(subcell, a, config);
                    if (doneB) break;
                    doneA = true;
                }
                if (subcell.Intersects(b.Cell))
                {
#if DEBUG
                    if (subcell.Intersects(a.Cell)) throw new InvalidOperationException();
#endif
                    subcells[i] = JoinTreeToRootCell(subcell, b, config);
                    if (doneA == true) break;
                    doneB = true;
                }
            }

            var pointCountTree = subcells.Sum(x => x?.PointCountTree);
            var bbExactGlobal = new Box3d(subcells.Where(x => x != null).Select(x => x.BoundingBoxExactGlobal));

            var data = ImmutableDictionary<Durable.Def, object>.Empty
                .Add(PointSetNode.TemporaryImportNode, 0)
                .Add(Durable.Octree.NodeId, Guid.NewGuid())
                .Add(Durable.Octree.Cell, rootCell)
                .Add(Durable.Octree.BoundingBoxExactGlobal, bbExactGlobal)
                .Add(Durable.Octree.PointCountTreeLeafs, a.PointCountTree + b.PointCountTree)
                .Add(Durable.Octree.SubnodesGuids, subcells.Map(x => x?.Id ?? Guid.Empty))
                ;

            if (a.HasPartIndexRange)
            {
                var mergedPartIndexRange = PartIndexUtils.MergeRanges(a.PartIndexRange, b.PartIndexRange) ?? throw new Exception("Invariant d4ed616f-a348-4303-8e64-651d669cb7bc.");
                data = data.Add(Durable.Octree.PartIndexRange, mergedPartIndexRange);
            }

            var result = new PointSetNode(data, config.Storage, writeToStore: false).CollapseLeafNodes(config).Item1;

#if DEBUG
            if (result.PointCountTree != a.PointCountTree + b.PointCountTree) throw new InvalidOperationException();
            if (result.PointCountTree != pointCountTree) throw new InvalidOperationException(
                $"Invariant d2957ed7-d12c-461c-ae79-5181a4197654. {result.PointCountTree} != {pointCountTree}."
                );
#endif
            //pointsMergedCallback?.Invoke(result.PointCountTree);/pointsMergedCallback?.Invoke(result.PointCountTree);
            if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant 2a5e4624-1597-4933-a6ba-50d41097af6a.");
            return result;
        }

#endregion
    }

    internal static IPointCloudNode JoinTreeToRootCell(Cell rootCell, IPointCloudNode a, ImportConfig config, bool collapse = true)
    {
        if (!rootCell.Contains(a.Cell)) throw new InvalidOperationException();

        if (a.Cell.IsCenteredAtOrigin)
        {
            throw new InvalidOperationException();
        }
        if (rootCell == a.Cell) return a;

        var subcells = new IPointCloudNode[8];
        for (var i = 0; i < 8; i++)
        {
            var subcell = rootCell.GetOctant(i);
            if (subcell == a.Cell) { subcells[i] = a; break; }
            if (subcell.Contains(a.Cell)) { subcells[i] = JoinTreeToRootCell(subcell, a, config, collapse); break; }
        }

        var data = ImmutableDictionary<Durable.Def, object>.Empty
            .Add(PointSetNode.TemporaryImportNode, 0)
            .Add(Durable.Octree.NodeId, Guid.NewGuid())
            .Add(Durable.Octree.Cell, rootCell)
            .Add(Durable.Octree.BoundingBoxExactGlobal, a.BoundingBoxExactGlobal)
            .Add(Durable.Octree.PointCountTreeLeafs, a.PointCountTree)
            .Add(Durable.Octree.SubnodesGuids, subcells.Map(x => x?.Id ?? Guid.Empty))
            ;

        if (a.HasPartIndexRange) data = data.Add(Durable.Octree.PartIndexRange, a.PartIndexRange);

        var result = (IPointCloudNode)new PointSetNode(data, config.Storage, writeToStore: false);
        if (collapse) result = result.CollapseLeafNodes(config).Item1;
        else result = result.WriteToStore();

        if (a.PartIndexRange != result.PartIndexRange) throw new Exception("Invariant 8386bc52-2f58-4bbb-8260-25300d0b4e0f.");
        return result;
    }

    private static IPointCloudNode MergeLeafAndLeafWithIdenticalRootCell(IPointCloudNode a, IPointCloudNode b, ImportConfig config)
    {
        if (!a.IsTemporaryImportNode || !b.IsTemporaryImportNode) throw new InvalidOperationException(
            "MergeLeafAndLeafWithIdenticalRootCell is only valid for temporary import nodes. Invariant 2d68b9d2-a001-47a8-b481-87488f33b85d."
            );

        if (a.IsLeaf == false || b.IsLeaf == false) throw new InvalidOperationException();
        if (a.Cell != b.Cell) throw new InvalidOperationException();
        if (b.PositionsAbsolute == null) throw new InvalidOperationException();
        if (a.HasColors != b.HasColors) throw new InvalidOperationException();
        if (a.HasNormals != b.HasNormals) throw new InvalidOperationException();
        if (a.HasIntensities != b.HasIntensities) throw new InvalidOperationException();
        if (a.HasClassifications != b.HasClassifications) throw new InvalidOperationException();
        if (a.HasPartIndexRange != b.HasPartIndexRange) throw new Exception("Invariant 653807fb-16d5-42d5-bf24-d48635195c92.");

        var cell = a.Cell;

        var ps = Concat(a.PositionsAbsolute, b.PositionsAbsolute);
        var cs = Concat(a.Colors?.Value, b.Colors?.Value);
        var ns = Concat(a.Normals?.Value, b.Normals?.Value);
        var js = Concat(a.Intensities?.Value, b.Intensities?.Value);
        var ks = Concat(a.Classifications?.Value, b.Classifications?.Value);
        var qs = PartIndexUtils.ConcatIndices(a.PartIndices, a.PointCountCell, b.PartIndices, b.PointCountCell);

        var chunk = new Chunk(ps, cs, ns, js, ks, partIndices: qs, partIndexRange: null, cell.BoundingBox);
        if (config.NormalizePointDensityGlobal)
        {
            chunk = chunk.ImmutableFilterMinDistByCell(cell, config.ParseConfig);
        }
        var result = InMemoryPointSet.Build(chunk, cell, config.OctreeSplitLimit).ToPointSetNode(config.Storage, isTemporaryImportNode: true);
        if (a.Cell != result.Cell) throw new InvalidOperationException("Invariant 771d781a-6d37-4017-a890-4f72a96a01a8."); 
        if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant 583675f5-5fb2-4b2d-9b55-3c559bac8bd4.");
        return result;
    }

    private static IPointCloudNode MergeLeafAndTreeWithIdenticalRootCell(IPointCloudNode a, IPointCloudNode b, ImportConfig config)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        if (a.IsLeaf == false || b.IsLeaf == true) throw new InvalidOperationException();
        if (a.Cell != b.Cell) throw new InvalidOperationException();
        if (a.HasPartIndexRange != b.HasPartIndexRange) throw new Exception("Invariant 7fe9ebc7-b51e-4a5a-8aa8-0e08b63b6240.");

        var result = InjectPointsIntoTree(a.PositionsAbsolute, a.Colors?.Value, a.Normals?.Value, a.Intensities?.Value, a.Classifications?.Value, a.PartIndices, b, a.Cell, config);
        if (a.Cell != result.Cell) throw new InvalidOperationException("Invariant 55551919-1a11-4ea9-bb4e-6f1a6b15e3d5.");
        if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant bf22e8ce-b517-4f42-90d8-947db603c244.");
        return result;
    }

    private static IPointCloudNode MergeTreeAndTreeWithIdenticalRootCell(IPointCloudNode a, IPointCloudNode b,
        Action<long> pointsMergedCallback,
        ImportConfig config
        )
    {
        if (a.IsLeaf || b.IsLeaf) throw new InvalidOperationException();
        if (a.Cell != b.Cell) throw new InvalidOperationException();
        if (a.PointCountCell > 0) throw new InvalidOperationException();
        if (b.PointCountCell > 0) throw new InvalidOperationException(); 
        if (a.HasPartIndexRange != b.HasPartIndexRange) throw new Exception("Invariant 0103f130-f022-42e5-8efc-da4e5747177a.");

        var pointCountTree = 0L;
        var subcells = new IPointCloudNode?[8];
        var subcellsDebug = new int[8];
        Range1i? qsRange = null;
        for (var i = 0; i < 8; i++)
        {
            var octant = a.Cell.GetOctant(i);
            var x = a.Subnodes![i]?.Value;
            var y = b.Subnodes![i]?.Value;

            if (a.Subnodes[i] != null && a.Subnodes[i]?.Value == null) throw new InvalidOperationException("Invariant 5571b3ac-a807-4318-9d07-d0843664b142.");
            if (b.Subnodes[i] != null && b.Subnodes[i]?.Value == null) throw new InvalidOperationException("Invariant 5eecb345-3460-4f9a-948c-efa29dea26b9.");

            if (x != null)
            {
                if (y != null)
                {
                    var m = Merge(x, y, pointsMergedCallback, config).Item1;
                    subcells[i] = m;
                    //if (x.PointCountTree + y.PointCountTree != subcells[i].PointCountTree) throw new InvalidOperationException("Invariant 82072553-7271-4448-b74d-735d44eb03b0.");
                    pointCountTree += m.PointCountTree;
                    qsRange = PartIndexUtils.MergeRanges(qsRange, m.PartIndexRange);
                    subcellsDebug[i] = 0;
                }
                else
                {
                    subcells[i] = x;
                    pointCountTree += x.PointCountTree; 
                    qsRange = PartIndexUtils.MergeRanges(qsRange, x.PartIndexRange);
                    //if (subcells[i].PointCountTree != x.PointCountTree) throw new InvalidOperationException();
                    subcellsDebug[i] = 1;
                }
            }
            else
            {
                if (y != null)
                {
                    subcells[i] = y;
                    pointCountTree += y.PointCountTree;
                    qsRange = PartIndexUtils.MergeRanges(qsRange, y.PartIndexRange);

                    //if (subcells[i].PointCountTree != y.PointCountTree) throw new InvalidOperationException();
                    subcellsDebug[i] = 2;
                }
                else
                {
                    subcells[i] = null;
                    subcellsDebug[i] = 3;
                }
            }
        }

        var replacements = ImmutableDictionary<Durable.Def, object>.Empty
            .Add(Durable.Octree.PointCountTreeLeafs, pointCountTree)
            .Add(Durable.Octree.SubnodesGuids, subcells.Map(x => x?.Id ?? Guid.Empty))
            .Add(Durable.Octree.BoundingBoxExactGlobal, new Box3d(subcells.Where(n => n != null).Select(n => n!.BoundingBoxExactGlobal)))
            ;

        if (qsRange != null) replacements = replacements.Add(Durable.Octree.PartIndexRange, qsRange);

        var result = a.With(replacements).CollapseLeafNodes(config).Item1;
        if (a.Cell != result.Cell) throw new InvalidOperationException("Invariant 97239777-8a0c-4158-853b-e9ebef63fda8.");
        if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant 95228e78-b71f-4778-bf8a-926bd91e3560.");
        return result;
    }
    
    private static PointSetNode CreateTmpNode(
        ImportConfig config,
        Cell cell,
        IList<V3d>? positions,
        IList<C4b>? colors,
        IList<V3f>? normals,
        IList<int>? intensities,
        IList<byte>? classifications,
        object? partIndices,
        Range1i? partIndexRange
        )
    {
        var chunk = new Chunk(positions, colors, normals, intensities, classifications, partIndices, partIndexRange, bbox: null);
        if (config.NormalizePointDensityGlobal) chunk = chunk.ImmutableFilterMinDistByCell(cell, config.ParseConfig);
        var node = InMemoryPointSet.Build(chunk, cell, config.OctreeSplitLimit).ToPointSetNode(config.Storage, isTemporaryImportNode: true);
        if (node.Cell != cell) throw new InvalidOperationException("Invariant a9d952d5-5e01-4f59-9b6b-8a4e6a3d4cd9.");
        if (partIndices != null && !node.HasPartIndexRange) throw new Exception("Invariant b9fccc91-cb8e-4efe-b4f1-25f39c90a8f7.");
        return node;
    }

    private static IPointCloudNode InjectPointsIntoTree(
        IList<V3d> psAbsolute, IList<C4b>? cs, IList<V3f>? ns, IList<int>? js, IList<byte>? ks, object? qs,
        IPointCloudNode a, Cell cell, ImportConfig config
        )
    {
        if (a != null && !a.IsTemporaryImportNode) throw new InvalidOperationException(
            "InjectPointsIntoTree is only valid for temporary import nodes. Invariant 0b0c48dc-8500-4ad6-a3dd-9c00f6d0b1d9."
            );

        if (a == null)
        {
            var result0 = CreateTmpNode(config, cell, psAbsolute, cs, ns, js, ks, qs, partIndexRange: null);
            if (qs != null && !result0.HasPartIndices) throw new NotImplementedException("PARTINDICES");
            //DidMergeWorkObject(null, 0, qs, psAbsolute.Count, result0.PartIndices);
            return result0;
        }

        if (a.Cell != cell) throw new InvalidOperationException("Invariant f447b6e5-52ef-4535-b8e4-e2aabedaef9e.");

        if (a.IsLeaf)
        {
            if (cs != null && !a.HasColors) throw new InvalidOperationException("Invariant 64d98ee7-5b08-4de7-9086-a38e707eb354.");
            if (cs == null && a.HasColors) throw new InvalidOperationException("Invariant 7c1cb6cb-16fe-40aa-83f4-61c0c2e50ec9.");
            if (ns != null && !a.HasNormals) throw new InvalidOperationException("Invariant 12263f36-1d5d-4c2b-aa1e-9f96f80047f2.");
            if (ns == null && a.HasNormals) throw new InvalidOperationException("Invariant 1e35a025-9a10-4bee-993b-109090c85b50.");

            var newPs = new List<V3d>(psAbsolute); newPs.AddRange(a.PositionsAbsolute);
            var newCs = cs != null ? new List<C4b>(cs) : null; newCs?.AddRange(a.Colors!.Value);
            var newNs = ns != null ? new List<V3f>(ns) : null; newNs?.AddRange(a.Normals!.Value);
            var newJs = js != null ? new List<int>(js) : null; newJs?.AddRange(a.Intensities!.Value);
            var newKs = ks != null ? new List<byte>(ks) : null; newKs?.AddRange(a.Classifications!.Value);
            var newQs = PartIndexUtils.ConcatIndices(qs, psAbsolute.Count, a.PartIndices, a.PointCountCell);

            var result0 = CreateTmpNode(config, cell, newPs, newCs, newNs, newJs, newKs, newQs, partIndexRange: null);
            if (a.HasPartIndexRange != result0.HasPartIndexRange) throw new Exception("Invariant c338fe24-7377-450c-af16-26be47861137.");
            //doesnt check if result0 is innernode 
            //DidMergeWorkObject(a.PartIndices, a.PointCountCell, qs, psAbsolute.Count, result0.PartIndices);
            return result0;
        }

        var pss = new List<V3d>[8];
        var css = cs != null ? new List<C4b>[8] : null;
        var nss = ns != null ? new List<V3f>[8] : null;
        var iss = js != null ? new List<int>[8] : null;
        var kss = ks != null ? new List<byte>[8] : null;
        var qss = qs != null ? new List<int>[8] : null;

#if DEBUG
        var bb = cell.BoundingBox;
        if (!psAbsolute.All(bb.Contains)) Report.Warn(
            $"Not all points contained in cell bounds {cell}. " +
            $"Warning b2749dac-e8d4-4f95-a0ac-b97cad9c0b37."
            );
#endif

        for (var i = 0; i < psAbsolute.Count; i++)
        {
            var j = a.GetSubIndex(psAbsolute[i]);
            if (pss[j] == null)
            {
                pss[j] = [];
                if (cs != null) css![j] = [];
                if (ns != null) nss![j] = [];
                if (js != null) iss![j] = [];
                if (ks != null) kss![j] = [];
                if (qs != null) qss![j] = [];
            }
            pss[j].Add(psAbsolute[i]);
            if (cs != null) css![j].Add(cs[i]);
            if (ns != null) nss![j].Add(ns[i]);
            if (js != null) iss![j].Add(js[i]);
            if (ks != null) kss![j].Add(ks[i]);
            if (qs != null) qss![j].Add(PartIndexUtils.Get(qs, i)!.Value);
        }

        if (pss.Sum(x => x?.Count) != psAbsolute.Count) throw new InvalidOperationException();

        var subcells = new IPointCloudNode?[8];
        for (var j = 0; j < 8; j++)
        {
            var subCell = cell.GetOctant(j);
            var x = a.Subnodes![j]?.Value;
            var qsss = (qss != null && qss[j] != null) ? PartIndexUtils.Compact(qss![j].ToArray()) : null;
            if (pss[j] != null)
            {
                if (x == null)
                {
                    // injecting points into non-existing subtree
                    subcells[j] = CreateTmpNode(config, subCell, pss[j], css?[j], nss?[j], iss?[j], kss?[j], qsss, partIndexRange: null);
                }
                else
                {
                    subcells[j] = InjectPointsIntoTree(pss[j], css?[j], nss?[j], iss?[j], kss?[j], qsss, x, subCell, config);
                }
            }
            else
            {
                subcells[j] = x;
            }
        }

        var result = a.WithSubNodes(subcells).CollapseLeafNodes(config).Item1;
        if (result.Cell != cell) throw new InvalidOperationException("Invariant 04aa0996-2942-41e5-bfdb-0c6841e2f12f."); 
        if (a.HasPartIndexRange != result.HasPartIndexRange) throw new Exception("Invariant 9a734621-00d6-4ea9-b915-20faf1eaaef5.");
        return result;
    }
}
