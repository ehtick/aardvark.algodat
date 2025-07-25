﻿/*
   Aardvark Platform
   Copyright (C) 2006-2025  Aardvark Platform Team
   https://aardvark.graphics

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

#pragma warning disable CS1591

namespace Aardvark.Data.Points
{
    /// <summary>
    /// Parsers emit a sequence of chunks of points with optional colors, normals, and intensities.
    /// </summary>
    public class Chunk
    {
        /// <summary>
        /// Appends two lists. Also works for null args: a + null -> a, null + b -> b, null + null -> null.
        /// </summary>
        private static IList<T>? Append<T>(IList<T>? l, IList<T>? r)
        {
            if (l == null) return r;
            if (r == null) return l;

            var ll = new List<T>(l);
            ll.AddRange(r);
            return ll;
        }

        public static readonly Chunk Empty = new(Array.Empty<V3d>(), null, null, null, null, null, null, Box3d.Invalid);

        public readonly IList<V3d> Positions;
        public readonly IList<C4b>? Colors;
        public readonly IList<V3f>? Normals;
        public readonly IList<int>? Intensities;
        public readonly IList<byte>? Classifications;
        public readonly object? PartIndices;
        public readonly Range1i? PartIndexRange;

        public GenericChunk ToGenericChunk()
        {
            var data = ImmutableDictionary<Durable.Def, object>.Empty
                .Add(GenericChunk.Defs.Positions3d, Positions.ToArray())
                ;
            if (Colors != null) data = data.Add(GenericChunk.Defs.Colors4b, Colors.ToArray());
            if (Normals != null) data = data.Add(GenericChunk.Defs.Normals3f, Normals.ToArray());
            if (Intensities != null) data = data.Add(GenericChunk.Defs.Intensities1i, Intensities.ToArray());
            if (Classifications != null) data = data.Add(GenericChunk.Defs.Classifications1b, Classifications.ToArray());
            if (PartIndices != null) data = data.Add(PartIndexUtils.GetDurableDefForPartIndices(PartIndices), PartIndices);
            if (PartIndexRange != null) data = data.Add(Durable.Octree.PartIndexRange, PartIndexRange);
            return new GenericChunk(data, BoundingBox);
        }

        public readonly Box3d BoundingBox;

        public int Count => Positions.Count;

        public bool IsEmpty => Count == 0;

        public bool HasPositions => true;

        [MemberNotNullWhen(true, nameof(Colors))]
        public bool HasColors => Colors != null;

        [MemberNotNullWhen(true, nameof(Normals))]
        public bool HasNormals => Normals != null;

        [MemberNotNullWhen(true, nameof(Intensities))]
        public bool HasIntensities => Intensities != null;

        [MemberNotNullWhen(true, nameof(Classifications))]
        public bool HasClassifications => Classifications != null;

        [MemberNotNullWhen(true, nameof(PartIndices))]
        public bool HasPartIndices => PartIndices != null;

        [MemberNotNullWhen(true, nameof(PartIndexRange))]
        public bool HasPartIndexRange => PartIndexRange != null;

        public IList<int>? TryGetPartIndices()
        {
            return PartIndexUtils.Expand(PartIndices, Count);
        }

        public static Chunk ImmutableMerge(Chunk a, Chunk b)
        {
            if (a is null || a.IsEmpty) return b;
            if (b is null || b.IsEmpty) return a;

            ImmutableList<V3d> ps;
            {
                var ps0 = (a.Positions is ImmutableList<V3d> x0) ? x0 : [.. a.Positions];
                var ps1 = (b.Positions is ImmutableList<V3d> x1) ? x1 : [.. b.Positions];
                ps = ps0.AddRange(ps1);
            }

            ImmutableList<C4b>? cs = null;
            if (a.HasColors)
            {
                var cs0 = (a.Colors is ImmutableList<C4b> x2) ? x2 : [.. a.Colors];
                if (b.HasColors)
                {
                    var cs1 = (b.Colors is ImmutableList<C4b> x3) ? x3 : [.. b.Colors];
                    cs = cs0.AddRange(cs1);
                }
                else
                {
                    cs = cs0;
                }
            }

            ImmutableList<V3f>? ns = null;
            if (a.HasNormals)
            {
                var ns0 = (a.Normals is ImmutableList<V3f> x4) ? x4 : [.. a.Normals];
                if (b.HasNormals)
                {
                    var ns1 = (b.Normals is ImmutableList<V3f> x5) ? x5 : [.. b.Normals];
                    ns = ns0.AddRange(ns1);
                }
                else
                {
                    ns = ns0;
                }
            }

            ImmutableList<int>? js = null;
            if (a.HasIntensities)
            {
                var js0 = (a.Intensities is ImmutableList<int> x6) ? x6 : [.. a.Intensities];
                if (b.HasIntensities)
                {
                    var js1 = (b.Intensities is ImmutableList<int> x7) ? x7 : [.. b.Intensities];
                    js = js0.AddRange(js1);
                }
                else
                {
                    js = js0;
                }
            }

            ImmutableList<byte>? ks = null;
            if (a.HasClassifications)
            {
                var ks0 = (a.Classifications is ImmutableList<byte> x8) ? x8 : [.. a.Classifications];
                if (b.HasClassifications)
                {
                    var ks1 = (b.Classifications is ImmutableList<byte> x9) ? x9 : [.. b.Classifications];
                    ks = ks0.AddRange(ks1);
                }
                else
                {
                    ks = ks0;
                }
            }

            var qs = PartIndexUtils.ConcatIndices(a.PartIndices, a.Count, b.PartIndices, b.Count);
            var qsRange = PartIndexUtils.MergeRanges(a.PartIndexRange, b.PartIndexRange);

            return new Chunk(ps, cs, ns, js, ks, qs, qsRange, new Box3d(a.BoundingBox, b.BoundingBox));
        }

        public static Chunk ImmutableMerge(params Chunk[] chunks)
        {
            if (chunks == null || chunks.Length == 0) return Empty;
            if (chunks.Length == 1) return chunks[0];

            var head = chunks[0];
            var totalCount = chunks.Sum(c => c.Count);

            var ps = new V3d[totalCount];
            var cs = head.HasColors ? new C4b[totalCount] : null;
            var ns = head.HasNormals ? new V3f[totalCount] : null;
            var js = head.HasIntensities ? new int[totalCount] : null;
            var ks = head.HasClassifications ? new byte[totalCount] : null;

            var offset = 0;
            foreach (var chunk in chunks)
            {
                if (chunk.IsEmpty) continue;

#pragma warning disable CS8602
                if (ps != null) chunk.Positions.CopyTo(ps, offset);
                if (cs != null) chunk.Colors.CopyTo(cs, offset);
                if (ns != null) chunk.Normals.CopyTo(ns, offset);
                if (js != null) chunk.Intensities.CopyTo(js, offset);
                if (ks != null) chunk.Classifications.CopyTo(ks, offset);
#pragma warning restore CS8602

                offset += chunk.Count;
            }

            if (ps == null) throw new Exception("Invariant 4cc7d585-9a46-4ba2-892a-95fce9ed06da.");

            var qs = PartIndexUtils.ConcatIndices(chunks.Select(x => (indices: x.PartIndices, count: x.Count)));
            var qsRanges = PartIndexUtils.MergeRanges(chunks.Select(x => x.PartIndexRange));
            return new Chunk(ps, cs, ns, js, ks, qs, qsRanges, bbox: new Box3d(chunks.Select(x => x.BoundingBox)));
        }

        public static Chunk ImmutableMerge(IEnumerable<Chunk> chunks)
            => ImmutableMerge([.. chunks]);

        /// <summary>
        /// </summary>
        /// <param name="positions">Optional.</param>
        /// <param name="colors">Optional. Either null or same number of elements as positions.</param>
        /// <param name="normals">Optional. Either null or same number of elements as positions.</param>
        /// <param name="intensities">Optional. Either null or same number of elements as positions.</param>
        /// <param name="classifications">Optional. Either null or same number of elements as positions.</param>
        /// <param name="partIndices">Optional. Either (A) null, or (B) single uint value for all points in cell, or (3) array with same number of elements (byte|short|int) as positions.</param>
        /// <param name="partIndexRange">Optional. If null and partIndices not null, then it will be automatically computed from partIndices.</param>
        /// <param name="bbox">Optional. If null, then bbox will be constructed from positions.</param>
        public Chunk(
            IList<V3d>? positions,
            IList<C4b>? colors,
            IList<V3f>? normals,
            IList<int>? intensities,
            IList<byte>? classifications,
            object? partIndices,
            Range1i? partIndexRange,
            Box3d? bbox
            )
        {
            if (positions == null) throw new ArgumentNullException(
                nameof(positions), "Error ad93ff8e-d1a9-4c96-a626-43c69617371e."
                );

            var countMismatch = false;

            if (colors != null && colors.Count != positions.Count)
            {
                countMismatch = true; //throw new ArgumentException(nameof(colors));
                Report.Warn($"Expected {positions.Count} color values, but found {colors.Count}. " +
                    $"Warning 3c3ac3d4-c4de-4ad6-a925-d2fbaece1634."
                    );
            }
            if (normals != null && normals.Count != positions.Count)
            {
                countMismatch = true; //throw new ArgumentException(nameof(normals));
                Report.Warn($"Expected {positions.Count} normal values, but found {normals.Count}. " +
                    $"Warning 0e6b1e22-25a1-4814-8dc7-add137edcd45."
                    );
            }
            if (intensities != null && intensities.Count != positions.Count)
            {
                countMismatch = true; //throw new ArgumentException(nameof(intensities));
                Report.Warn($"Expected {positions.Count} intensity values, but found {intensities.Count}. " +
                    $"Warning ea548345-6f32-4504-8189-4dd4a4531708."
                    );
            }
            if (classifications != null && classifications.Count != positions.Count)
            {
                countMismatch = true; //throw new ArgumentException(nameof(classifications));
                Report.Warn(
                    $"Expected {positions.Count} classification values, but found {classifications.Count}. " +
                    $"Warning 0c82c851-986f-4690-980b-7a3a240bf499."
                    );
            }

            #region part indices

            switch (partIndices)
            {
                case null        : break;
                case int         : break;
                case uint        : break;
                case IList<byte> : break;
                case IList<short>: break;
                case IList<int>  : break;
                default: throw new Exception($"Unexpected part indices type {partIndices.GetType().FullName}. Error fc9d196d-508e-4977-8f04-2167c71e38b0.");
            }
            IList<byte>? qs1b = null;
            if (partIndices is IList<byte> _qs1b && _qs1b.Count != positions.Count)
            {
                qs1b = _qs1b;
                countMismatch = true;
                Report.Warn(
                    $"Expected {positions.Count} part indices, but found {_qs1b.Count}. " +
                    $"Warning a03f843f-d8cd-4a3d-a1c8-d48eb64c5b39."
                    );
            }
            IList<short>? qs1s = null;
            if (partIndices is IList<short> _qs1s && _qs1s.Count != positions.Count)
            {
                qs1s = _qs1s;
                countMismatch = true;
                Report.Warn(
                    $"Expected {positions.Count} part indices, but found {_qs1s.Count}. " +
                    $"Warning 93c9f432-3e59-45a3-b350-9d5ba7bf9ee3."
                    );
            }
            IList<int>? qs1i = null;
            if (partIndices is IList<int> _qs1i && _qs1i.Count != positions.Count)
            {
                qs1i = _qs1i;
                countMismatch = true;
                Report.Warn(
                    $"Expected {positions.Count} part indices, but found {_qs1i.Count}. " +
                    $"Warning b94ca9d5-1322-4f57-9ebc-85ea8d3fa8bf."
                    );
            }

            #endregion

            if (countMismatch)
            {
                var minCount = positions.Count;
                if (colors          != null && colors.Count          != positions.Count) minCount = Math.Min(minCount, colors.Count         );
                if (normals         != null && normals.Count         != positions.Count) minCount = Math.Min(minCount, normals.Count        );
                if (intensities     != null && intensities.Count     != positions.Count) minCount = Math.Min(minCount, intensities.Count    );
                if (classifications != null && classifications.Count != positions.Count) minCount = Math.Min(minCount, classifications.Count);
                if (qs1b            != null && qs1b.Count            != positions.Count) minCount = Math.Min(minCount, qs1b.Count           );
                if (qs1s            != null && qs1s.Count            != positions.Count) minCount = Math.Min(minCount, qs1s.Count           );
                if (qs1i            != null && qs1i.Count            != positions.Count) minCount = Math.Min(minCount, qs1i.Count           );

                if (positions.Count != minCount                                 ) positions       = [.. positions      .Take(minCount)];
                if (colors          != null && colors         .Count != minCount) colors          = [.. colors         .Take(minCount)];
                if (normals         != null && normals        .Count != minCount) normals         = [.. normals        .Take(minCount)];
                if (intensities     != null && intensities    .Count != minCount) intensities     = [.. intensities    .Take(minCount)];
                if (classifications != null && classifications.Count != minCount) classifications = [.. classifications.Take(minCount)];
                if (qs1b            != null && qs1b           .Count != minCount) partIndices     = qs1b               .Take(minCount).ToArray();
                if (qs1s            != null && qs1s           .Count != minCount) partIndices     = qs1s               .Take(minCount).ToArray();
                if (qs1i            != null && qs1i           .Count != minCount) partIndices     = qs1i               .Take(minCount).ToArray();
            }

            Positions       = positions;
            Colors          = colors;
            Normals         = normals;
            Intensities     = intensities;
            Classifications = classifications;
            PartIndices     = partIndices;
            PartIndexRange  = partIndexRange ?? PartIndexUtils.GetRange(partIndices);
            BoundingBox     = bbox ?? (positions.Count > 0 ? new Box3d(positions) : Box3d.Invalid);
        }

        public Chunk(IList<V3d>? positions) : this(positions, null, null, null, null, null, null, null) { }

        public IEnumerable<Chunk> Split(int chunksize)
        {
            if (chunksize < 1) throw new Exception();
            if (chunksize >= Count)
            {
                yield return this;
            }
            else
            {
                var i = 0;
                while (i < Count)
                {
                    var qs = PartIndexUtils.Take(PartIndexUtils.Skip(PartIndices, i), chunksize);
                    yield return new Chunk(
                        [.. Positions.Skip(i).Take(chunksize)],
                        colors: HasColors ? Colors.Skip(i).Take(chunksize).ToArray() : null,
                        normals: HasNormals ? Normals.Skip(i).Take(chunksize).ToArray() : null,
                        intensities: HasIntensities ? Intensities.Skip(i).Take(chunksize).ToArray() : null,
                        classifications: HasClassifications ? Classifications.Skip(i).Take(chunksize).ToArray() : null,
                        partIndices: qs,
                        partIndexRange: PartIndexUtils.GetRange(qs),
                        bbox: null
                        );

                    i += chunksize;
                }
            }
        }

        /// <summary>
        /// Creates new chunk which is union of this chunk and other. 
        /// </summary>
        public Chunk Union(Chunk other)
        {
            if (other.IsEmpty) return this;

            var ps = Append(Positions, other.Positions);
            return ps == null
                ? throw new Exception("Invariant c799ccc6-4edf-4530-b1eb-59d93ef69727.")
                : new Chunk(
                    ps,
                    Append(Colors, other.Colors),
                    Append(Normals, other.Normals),
                    Append(Intensities, other.Intensities),
                    Append(Classifications, other.Classifications),
                    partIndices: PartIndexUtils.ConcatIndices(PartIndices, Count, other.PartIndices, other.Count),
                    partIndexRange: PartIndexUtils.MergeRanges(PartIndexRange, other.PartIndexRange),
                    Box.Union(BoundingBox, other.BoundingBox)
                );
        }

        public Chunk WithNormalsTransformed(Rot3d r)
        {
            if (r == Rot3d.Identity) return this;
            var ns = Normals.Map(n => (V3f)r.Transform((V3d)n));
            return WithNormals(ns);
        }

        /// <summary>
        /// Immutable update of positions.
        /// </summary>
        public Chunk WithPositions(IList<V3d> newPositions) => new(newPositions, Colors, Normals, Intensities, Classifications, PartIndices, PartIndexRange, BoundingBox);

        /// <summary>
        /// Immutable update of colors.
        /// </summary>
        public Chunk WithColors(IList<C4b> newColors) => new(Positions, newColors, Normals, Intensities, Classifications, PartIndices, PartIndexRange, BoundingBox);

        /// <summary>
        /// Immutable update of normals.
        /// </summary>
        public Chunk WithNormals(IList<V3f> newNormals) => new(Positions, Colors, newNormals, Intensities, Classifications, PartIndices, PartIndexRange, BoundingBox);

        /// <summary>
        /// Immutable update of normals.
        /// </summary>
        public Chunk WithIntensities(IList<int> newIntensities) => new(Positions, Colors, Normals, newIntensities, Classifications, PartIndices, PartIndexRange, BoundingBox);

        /// <summary>
        /// Immutable update of classifications.
        /// </summary>
        public Chunk WithClassifications(IList<byte> newClassifications) => new(Positions, Colors, Normals, Intensities, newClassifications, PartIndices, PartIndexRange, BoundingBox);

        /// <summary>
        /// Immutable update of part indices.
        /// </summary>
        public Chunk WithPartIndices(object? newPartIndices, Range1i? newPartIndexRange) => new(Positions, Colors, Normals, Intensities, Classifications, newPartIndices, newPartIndexRange, BoundingBox);

        /// <summary>
        /// Returns chunk with duplicate point positions removed.
        /// </summary>
        public Chunk ImmutableDeduplicate(bool verbose)
        {
            if (IsEmpty) return Empty;
            if (!HasPositions || Positions == null) return this;

            var dedup = new HashSet<V3d>();
            var ia = new List<int>();
            for (var i = 0; i < Count; i++)
            {
                if (dedup.Add(Positions[i])) ia.Add(i);
            }
            var hasDuplicates = ia.Count < Count;

            if (hasDuplicates)
            {
                T[]? f<T>(bool has, IList<T>? xs) => (has && xs != null) ? ia.MapToArray(i => xs[i]) : null;
                var ps = f(HasPositions, Positions) ?? throw new InvalidOperationException();
                var cs = f(HasColors, Colors);
                var ns = f(HasNormals, Normals);
                var js = f(HasIntensities, Intensities);
                var ks = f(HasClassifications, Classifications);
                var qs = PartIndexUtils.Subset(PartIndices, ia);

                if (verbose)
                {
                    var removedCount = Positions.Count - ps.Length;
                    var removedPercent = (removedCount / (double)Positions.Count) * 100.0;
                    Report.Line($"removed {removedCount:N0} duplicate points ({removedPercent:0.00}% of {Positions.Count:N0})");
#if DEBUG
                    if (ps.Length == 1) Report.Warn($"Bam! Complete chunk collapsed to a single point.");
#endif
                }

                return new Chunk(ps, cs, ns, js, ks, partIndices: qs, PartIndexRange, bbox: null);
            }
            else
            {
                return this;
            }
        }

        public Chunk ImmutableMapPositions(Func<V3d, V3d> mapping)
            => IsEmpty ? Empty : new(Positions.Map(mapping), Colors, Normals, Intensities, Classifications, PartIndices, PartIndexRange, BoundingBox);

        public Chunk ImmutableMergeWith(IEnumerable<Chunk> others)
            => ImmutableMerge(this, ImmutableMerge(others));

        public Chunk ImmutableMergeWith(params Chunk[] others)
            => ImmutableMerge(this, ImmutableMerge(others));

        /// <summary>
        /// Splits this chunk into multiple chunks according to key of i-th point in chunk.
        /// </summary>
        public Dictionary<TKey, Chunk> GroupBy<TKey>(Func<Chunk, int, TKey> keySelector)
        {
            if (IsEmpty) return [];

            var dict = new Dictionary<TKey, List<int>>();
            for (var i = 0; i < Count; i++)
            {
                var k = keySelector(this, i);
                if (!dict.TryGetValue(k, out var ia)) dict[k] = ia = [];
                ia.Add(i);
            }

            var result = new Dictionary<TKey, Chunk>();
            foreach (var kv in dict)
            {
                var ia = kv.Value;
                var qs = PartIndexUtils.Subset(PartIndices, ia);
                result[kv.Key] = new Chunk(
                    ia.Map(i => Positions[i]),
                    Colors          != null ? ia.Map(i => Colors[i])          : null,
                    Normals         != null ? ia.Map(i => Normals[i])         : null,
                    Intensities     != null ? ia.Map(i => Intensities[i])     : null,
                    Classifications != null ? ia.Map(i => Classifications[i]) : null,
                    partIndices: qs,
                    partIndexRange: PartIndexUtils.GetRange(qs),
                    bbox: null
                    );
            }
            return result;
        }

        #region ImmutableFilter...

        /// <summary>
        /// Returns chunk with points for which predicate is true.
        /// </summary>
        public Chunk ImmutableFilter(Func<Chunk, int, bool> predicate)
        {
            if (IsEmpty) return Empty;

            var ia = new List<int>();
            for (var i = 0; i < Count; i++) if (predicate(this, i)) ia.Add(i);

            var ps = Positions.Subset(ia);
            var cs = Colors?.Subset(ia);
            var ns = Normals?.Subset(ia);
            var js = Intensities?.Subset(ia);
            var ks = Classifications?.Subset(ia);
            var qs = PartIndexUtils.Subset(PartIndices, ia);


            return new Chunk(ps, cs, ns, js, ks, qs, PartIndexUtils.GetRange(qs), bbox: null);
        }

        /// <summary>
        /// Removes points which are less than minDist from previous point (L2, Euclidean).
        /// </summary>
        public Chunk ImmutableFilterSequentialMinDistL2(double minDist)
        {
            if (IsEmpty) return Empty;
            if (minDist <= 0.0 || !HasPositions) return this;
            var minDistSquared = minDist * minDist;

            var ia = new List<int>();
            var prev = V3d.MinValue;
            for (var i = 0; i < Positions.Count; i++)
            {
                var p = Positions[i];
                if (Utils.DistLessThanL2(ref p, ref prev, minDistSquared)) continue;
                prev = p;
                ia.Add(i);
            }

            var qs = PartIndexUtils.Subset(PartIndices, ia);
            return new Chunk(
                Positions.Subset(ia),
                Colors?.Subset(ia),
                Normals?.Subset(ia),
                Intensities?.Subset(ia),
                Classifications?.Subset(ia),
                partIndices: qs,
                partIndexRange: PartIndexUtils.GetRange(qs),
                bbox: null
                );
        }

        /// <summary>
        /// Removes points which are less than minDist from previous point (L1, Manhattan).
        /// </summary>
        public Chunk ImmutableFilterSequentialMinDistL1(double minDist)
        {
            if (IsEmpty) return Empty;
            if (minDist <= 0.0 || !HasPositions) return this;

            var ia = new List<int>();
            var prev = V3d.MinValue;
            for (var i = 0; i < Positions.Count; i++)
            {
                var p = Positions[i];
                if (Utils.DistLessThanL1(ref p, ref prev, minDist)) continue;
                prev = p;
                ia.Add(i);
            }

            var qs = PartIndexUtils.Subset(PartIndices, ia);
            return new Chunk(
                Positions.Subset(ia),
                Colors?.Subset(ia),
                Normals?.Subset(ia),
                Intensities?.Subset(ia),
                Classifications?.Subset(ia),
                partIndices: qs,
                partIndexRange: PartIndexUtils.GetRange(qs),
                bbox: null
                );
        }

        /// <summary>
        /// Returns chunk with duplicate point positions removed.
        /// </summary>
        public Chunk ImmutableFilterMinDistByCell(Cell bounds, ParseConfig config)
        {
            if (IsEmpty) return Empty;
            if (config.MinDist <= 0.0 || !HasPositions) return this;
            if (Positions == null) throw new InvalidOperationException("Invariant 47ef0ae0-e76f-4f86-bc44-67cc8c939b1c.");

            var smallestCellExponent = Fun.Log2(config.MinDist).Ceiling();
            var positions = Positions;
            var take = new bool[Count];
            var foo = new List<int>(positions.Count); for (var i = 0; i < positions.Count; i++) foo.Add(i);
            Filter(bounds, foo).Wait();

            async Task Filter(Cell c, List<int> ia)
            {
#if DEBUG
                if (ia == null || ia.Count == 0) throw new InvalidOperationException();
                if (c.Exponent < smallestCellExponent) throw new InvalidOperationException();
#endif
                if (c.Exponent == smallestCellExponent)
                {
                    take[ia[0]] = true;
                    return;
                }

                var center = c.GetCenter();
                var subias = new List<int>[8].SetByIndex(_ => []);
                for (var i = 0; i < ia.Count; i++)
                {
                    var p = positions[ia[i]];
                    var o = 0;
                    if (p.X >= center.X) o = 1;
                    if (p.Y >= center.Y) o |= 2;
                    if (p.Z >= center.Z) o |= 4;
                    subias[o].Add(ia[i]);
                }

                var ts = new List<Task>();
                for (var i = 0; i < 8; i++)
                {
                    if (subias[i].Count == 0) continue;
                    if (subias[i].Count == 1) { take[subias[i][0]] = true; continue; }
                    var _i = i;
                    var t = (subias[i].Count < 16384)
                        ? Filter(c.GetOctant(i), subias[i])
                        : Task.Run(() => Filter(c.GetOctant(_i), subias[_i]))
                        ;
                    ts.Add(t);
                }
                await Task.WhenAll(ts);
            }

            List<T>? f<T>(bool has, IList<T>? xs) => has ? xs.Where((_, i) => take[i]).ToList() : null;
            var ps = f(HasPositions, Positions);
            var cs = f(HasColors, Colors);
            var ns = f(HasNormals, Normals);
            var js = f(HasIntensities, Intensities);
            var ks = f(HasClassifications, Classifications);

            var qs = (object?)null;
            if (HasPartIndices)
            {
                var ia = new List<int>();
                for (var i = 0; i < take.Length; i++) if (take[i]) ia.Add(i);
                qs = PartIndexUtils.Subset(PartIndices, ia);
            }

            if (config.Verbose)
            {
                var removedCount = Count - ps?.Count;
                if (removedCount > 0)
                {
                    //Report.Line($"[ImmutableFilterMinDistByCell] {this.Count:N0} - {removedCount:N0} -> {ps.Count:N0}");
                }
            }

            if (ps == null) throw new Exception("Invariant 84440204-5496-479f-ad3e-9e45e5dd16c1.");
            return new Chunk(ps, cs, ns, js, ks, qs, PartIndexUtils.GetRange(qs), bbox: null);
        }

#pragma warning disable CS8602

        /// <summary>
        /// Returns chunk with points for which given predicate is true.
        /// </summary>
        public Chunk ImmutableFilterByPosition(Func<V3d, bool> predicate)
            => Positions != null ? ImmutableFilter((c, i) => predicate(c.Positions[i])) : this;

        /// <summary>
        /// Returns chunk with points which are inside given box.
        /// </summary>
        public Chunk ImmutableFilterByBox3d(Box3d filter)
            => Positions != null ? ImmutableFilter((c, i) => filter.Contains(c.Positions[i])) : this;

        /// <summary>
        /// Returns chunk with points p.XY which are inside given box.
        /// </summary>
        public Chunk ImmutableFilterByBoxXY(Box2d filter)
            => Positions != null ? ImmutableFilter((c, i) => { var p = c.Positions[i]; return p.X >= filter.Min.X && p.X < filter.Max.X && p.Y >= filter.Min.Y && p.Y < filter.Max.Y; }) : this;

        /// <summary>
        /// Returns chunk with points which are inside given cell.
        /// </summary>
        public Chunk ImmutableFilterByCell(Cell filter)
        {
            var bb = filter.BoundingBox;
            return Positions != null ? ImmutableFilter((c, i) => bb.Contains(c.Positions[i])) : this;
        }

        /// <summary>
        /// Returns chunk with points for which given predicate is true.
        /// </summary>
        public Chunk ImmutableFilterByColor(Func<C4b, bool> predicate)
            => Colors != null ? ImmutableFilter((c, i) => predicate(c.Colors[i])) : this;

        /// <summary>
        /// Returns chunk with points for which given predicate is true.
        /// </summary>
        public Chunk ImmutableFilterByNormal(Func<V3f, bool> predicate)
            => Normals != null ? ImmutableFilter((c, i) => predicate(c.Normals[i])) : this;

        /// <summary>
        /// Returns chunk with points for which given predicate is true.
        /// </summary>
        public Chunk ImmutableFilterByIntensity(Func<int, bool> predicate)
            => Intensities != null ? ImmutableFilter((c, i) => predicate(c.Intensities[i])) : this;

        /// <summary>
        /// Returns chunk with points for which given predicate is true.
        /// </summary>
        public Chunk ImmutableFilterByClassification(Func<byte, bool> predicate)
            => Classifications != null ? ImmutableFilter((c, i) => predicate(c.Classifications[i])) : this;

#pragma warning restore CS8602

        #endregion
    }
}