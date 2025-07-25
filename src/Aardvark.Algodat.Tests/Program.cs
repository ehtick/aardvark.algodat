﻿using Aardvark.Base;
using Aardvark.Data;
using Aardvark.Data.Points;
using Aardvark.Data.Points.Import;
using Aardvark.Geometry.Points;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Uncodium.SimpleStore;
using static Aardvark.Geometry.Points.Queries;
using static Microsoft.FSharp.Core.ByRefKinds;
using static System.Console;

#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE0059 // Unnecessary assignment of a value
#pragma warning disable IDE0130 // Namespace does not match folder structure

#if !NETCOREAPP
using System.ComponentModel;
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit { }
}
#endif

namespace Aardvark.Geometry.Tests
{
    public class Program
    {
        internal static void EnumerateOctreeNodes(string storePath, string key)
        {
            var store = new SimpleDiskStore(storePath).ToPointCloudStore();
            var root = store.GetPointCloudNode(key);

            var nodes = root.EnumerateNodes();

            var n = 0;
            foreach (var node in nodes)
            {
                WriteLine($"[{n++,8:N0}] {node.PointCountCell}");

                if (node.IsLeaf && node.HasNormals)
                {
                    var ns = node.Normals.Value;
                    var hasOnlyDefaultNormals = true;
                    for (var i = 0; i < ns.Length; i++)
                    {
                        if (ns[i] == V3f.OOO) Debugger.Break();
                        if (ns[i] != V3f.OOI) { hasOnlyDefaultNormals = false; break; }
                        if (hasOnlyDefaultNormals)
                        {
                            WriteLine("  has only default normals");
                        }
                    }
                }
            }
        }

        #region CreateStore

        internal static Task<string> CreateStore(string filename, string storeDir, double minDist, int maxDegreeOfParallelism = 0)
            => CreateStore(filename, new DirectoryInfo(storeDir), minDist, maxDegreeOfParallelism);

        internal static Task<string> CreateStore(IEnumerable<Chunk> chunks, string storeDir, double minDist, int? splitLimit = null)
            => CreateStore(chunks, new DirectoryInfo(storeDir), minDist, splitLimit);

        internal static Task<string> CreateStore(string filename, DirectoryInfo storeDir, double minDist, int maxDegreeOfParallelism = 0)
        {
            if (!storeDir.Exists) storeDir.Create();

            var key = Path.GetFileName(filename);

            using var store = new SimpleDiskStore(Path.Combine(storeDir.FullName, "data.uds")).ToPointCloudStore();

            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey(key)
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(maxDegreeOfParallelism)
                .WithMinDist(minDist)
                .WithNormalizePointDensityGlobal(true)
                //.WithMaxChunkPointCount(32 * 1024 * 1024)
                //.WithOctreeSplitLimit(8192*4)
                //.WithEnabledPartIndices(false)
                .WithDefaultNormal(V3f.OOI)
                ;

            Report.BeginTimed($"importing {filename}");
            var pcl = PointCloud.Import(filename, config);
            Report.EndTimed();

            File.WriteAllText(Path.Combine(storeDir.FullName, "key.txt"), key);

            return Task.FromResult(key);
        }

        internal static Task<string> CreateStore(IEnumerable<Chunk> chunks, DirectoryInfo storeDir, double minDist, int? splitLimit = null, int maxDegreeOfParallelism = 0)
        {
            if (!storeDir.Exists) storeDir.Create();

            var filename = storeDir.FullName;
            var key = Path.GetFileName(filename);

            using var store = new SimpleDiskStore(Path.Combine(storeDir.FullName, "data.uds")).ToPointCloudStore();

            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey(key)
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(maxDegreeOfParallelism)
                .WithMinDist(minDist)
                .WithNormalizePointDensityGlobal(true)
                //.WithMaxChunkPointCount(32 * 1024 * 1024)
                //.WithEnabledPartIndices(false)
                ;

            if (splitLimit.HasValue) config = config.WithOctreeSplitLimit(splitLimit.Value);

            Report.BeginTimed($"importing {filename}");
            var pcl = PointCloud.Import(chunks, config);
            Report.EndTimed();

            File.WriteAllText(Path.Combine(storeDir.FullName, "key.txt"), key);

            return Task.FromResult(key);
        }

        #endregion

        #region

        internal static void ParsePointCloudFile(string filename, bool verbose)
        {
            var info = PointCloud.ParseFileInfo(filename, ParseConfig.Default);
            var totalPointCount = 0L;
            var nextReport = DateTime.Now.AddSeconds(1);
            var sw = Stopwatch.StartNew();
            WriteLine($"parsing {filename}");
            var chunks = PointCloud.Parse(filename, ParseConfig.Default.WithVerbose(verbose));
            foreach (var chunk in chunks)
            {
                totalPointCount += chunk.Count;
                if (DateTime.Now > nextReport)
                {
                    Write($"\r[{100.0*totalPointCount/info.PointCount,6:N2}%] {sw.Elapsed} {totalPointCount,16:N0}/{info.PointCount:N0}");
                    nextReport = nextReport.AddSeconds(1);
                }
            }
            WriteLine($"\r[{100.0 * totalPointCount / info.PointCount,6:N2}%] {sw.Elapsed} {totalPointCount,16:N0}/{info.PointCount:N0}");
        }

        #endregion

        internal static void PerfTestJuly2019()
        {
            var filename = @"T:\Vgm\Data\2017-10-20_09-44-27_1mm_shade_norm_5pp - Cloud.pts";
            //var filename = @"T:\Vgm\Data\JBs_Haus.pts";

            var config = ImportConfig.Default
                .WithInMemoryStore()
                .WithRandomKey()
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(0)
                .WithMinDist(0.01)
                .WithNormalizePointDensityGlobal(true)
                ;

            Report.BeginTimed("total");

            var chunks = Pts.Chunks(filename, config.ParseConfig);
            var pc = PointCloud.Chunks(chunks, config);

            Report.EndTimed();

            Report.Line($"count -> {pc.PointCount}");
        }

        internal static void TestLaszip()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var basedir = @"T:\OEBB\datasets\stream_3893\lasFiles";
            var filename = @"T:\OEBB\datasets\stream_3893\lasFiles\469_0-0.las";
            //var fileSizeInBytes = new FileInfo(filename).Length;

            var key = Path.GetFileName(filename);

            var info = Laszip.LaszipInfo(filename, ParseConfig.Default);
            Report.Line($"total bounds: {info.Bounds}");
            Report.Line($"total count : {info.PointCount:N0}");

            var storePath = $@"T:\Vgm\Stores\{key}";
            using var store = new SimpleDiskStore(storePath).ToPointCloudStore();

            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey(key)
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(0)
                //.WithMinDist(0.025)
                .WithNormalizePointDensityGlobal(true)
                ;

            Report.BeginTimed("total");

            //var chunks = Laszip.Chunks(filename, config.ParseConfig);
            var chunks = Directory.EnumerateFiles(basedir, "*.las").SelectMany(f => Laszip.Chunks(f, config.ParseConfig));

            var total = 0L;
            foreach (var chunk in chunks)
            {
                total += chunk.Count;
                Report.WarnNoPrefix($"[Chunk] {chunk.Count,16:N0}; {total,16:N0}; {chunk.HasPositions} {chunk.HasColors} {chunk.HasIntensities}");
            }


            var cloud = PointCloud.Chunks(chunks, config);
            var pcl = store.GetPointSet(key);
            Report.Line($"{storePath}:{key} : {pcl.PointCount:N0} points");

            Report.EndTimed();
        }

        internal static void TestE57()
        {
            //var sw = new Stopwatch();

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var filename = @"T:\Vgm\Data\E57\2020-12-14_Trimble_Scan.e57";
            //var fileSizeInBytes = new FileInfo(filename).Length;

            var key = Path.GetFileName(filename);

            var info = E57.E57Info(filename, ParseConfig.Default);
            Report.Line($"total bounds: {info.Bounds}");
            Report.Line($"total count : {info.PointCount:N0}");

            var storePath = $@"T:\Vgm\Stores\{key}";
            using var store = new SimpleDiskStore(storePath).ToPointCloudStore();

            //var count = 0L;
            //var i = 0;
            //store.GetPointSet(key).Root.Value.ForEachNode(true, n =>
            //{
            //    if (++i % 2000 == 0) Report.Line($"[{i,8:N0}] {count,20:N0}");
            //    count += n.ToChunk().Count;
            //});
            //Report.Line($"[{i,8:N0}] {count,20:N0}");
            //return;

            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey(key)
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(0)
                .WithMinDist(0.025)
                .WithNormalizePointDensityGlobal(true)
                ;
            
            Report.BeginTimed("total");

            var chunks = E57.Chunks(filename, config.ParseConfig);

            //var total = 0L;
            //foreach (var chunk in chunks)
            //{
            //    total += chunk.Count;
            //    Report.WarnNoPrefix($"[Chunk] {chunk.Count,16:N0}; {total,16:N0}; {chunk.HasPositions} {chunk.HasColors} {chunk.HasIntensities}");
            //}


            //var cloud = PointCloud.Chunks(chunks, config);

            var pcl = store.GetPointSet(key);
            Report.Line($"{storePath}:{key} : {pcl.PointCount:N0} points");
            //var maxCount = pcl.PointCount / 30;
            //var level = pcl.GetMaxOctreeLevelWithLessThanGivenPointCount(maxCount);
            //var foo = pcl.QueryPointsInOctreeLevel(level);
            //var fooCount = 0;
            //foreach (var chunk in foo) 
            //{
            //    Report.WarnNoPrefix($"{++fooCount}");
            //}

            //var intensityRange =
            //    foo
            //    |> Seq.fold(fun intMaxima chunk-> if chunk.HasIntensities then
            //          let struct(currentMin, currentMax) = intMaxima |> Option.defaultValue((Int32.MaxValue, Int32.MinValue))
            //           let minInt = min currentMin(chunk.Intensities |> Seq.min)
            //            let maxInt = max currentMax(chunk.Intensities |> Seq.max)
            //            Some struct(minInt, maxInt)
            //        else
            //            None ) None

            Report.EndTimed();

            //var memstore = new SimpleMemoryStore().ToPointCloudStore();

            // classic
            //var foo = chunks.AsParallel().Select(x => InMemoryPointSet.Build(x, 8192).ToPointSetCell(memstore));
            //var r = foo.MapReduceParallel((first, second, ct2) =>
            //{
            //    var merged = first.Merge(second, 8192, null, default);
            //    memstore.Add(Guid.NewGuid().ToString(), merged, default);
            //    Report.Line($"{first.PointCountTree,12:N0} + {second.PointCountTree,12:N0} -> {merged.PointCountTree,12:N0}");
            //    return merged;
            //}, 0);

            //// test 1
            //Report.BeginTimed("merging all chunks");
            //var chunk = Chunk.Empty;
            //foreach (var x in chunks) chunk = Chunk.ImmutableMerge(chunk, x);
            //Report.Line($"points     : {chunk.Count:N0}");
            //Report.EndTimed();

            //Report.BeginTimed("filter mindist");
            //chunk = chunk.ImmutableFilterMinDistByCell(0.01, new Cell(chunk.BoundingBox));
            //Report.Line($"points     : {chunk.Count:N0}");
            //Report.EndTimed();

            //Report.BeginTimed("slots");
            //var slots = chunk.Positions.GroupBy(p => (V3i)p).ToArray();
            //Report.Line($"[slots] {slots.Length}");
            //var slotsCountAvg = slots.Sum(x => x.Count() / (double)slots.Length);
            //var slotsCountMin = slots.Min(x => x.Count());
            //var slotsCountMax = slots.Max(x => x.Count());
            //var slotsCountSd = (slots.Sum(x => (x.Count() - slotsCountAvg).Square()) / slots.Length).Sqrt();
            //Report.Line($"[slots] count avg = {slotsCountAvg}");
            //Report.Line($"[slots] count min = {slotsCountMin}");
            //Report.Line($"[slots] count max = {slotsCountMax}");
            //Report.Line($"[slots] count sd  = {slotsCountSd}");
            //Report.EndTimed();


            //Report.BeginTimed("build octree");
            //var octree = InMemoryPointSet.Build(chunk, 8192);
            //Report.EndTimed();

            //Report.BeginTimed("toPointSetCell");
            //var psc = octree.ToPointSetCell(memstore);
            //var ps = new PointSet(memstore, "foo", psc.Id, 8192);
            //Report.EndTimed();

            //Report.BeginTimed("generate lod");
            //var psWithNormals = ps.GenerateLod(config);
            //Report.EndTimed();



            //Report.Line($"chunks: {foo.Count}");

            

            //return;

            //var lastProgress = 0.0;
            //config = ImportConfig.Default
            //    .WithInMemoryStore()
            //    .WithRandomKey()
            //    .WithVerbose(false)
            //    .WithMaxDegreeOfParallelism(0)
            //    .WithMinDist(0.01)
            //    .WithProgressCallback(x =>
            //    {
            //        if (x < lastProgress) Console.WriteLine("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH"); else lastProgress = x;
            //        Console.WriteLine($"[PROGRESS]; {x,8:0.00000}; {sw.Elapsed.TotalSeconds:0.00}");
            //    })
            //    ;

            //chunks = E57.Chunks(filename, config.ParseConfig);
            //var pointcloud = PointCloud.Chunks(chunks, config);
            //Console.WriteLine($"pointcloud.PointCount  : {pointcloud.PointCount}");
            //Console.WriteLine($"pointcloud.Bounds      : {pointcloud.Bounds}");
            //Console.WriteLine($"pointcloud.BoundingBox : {pointcloud.BoundingBox}");

            //var chunks = E57.Chunks(filename, config);
            //var pointcloud = PointCloud.Chunks(chunks, config);
            //Console.WriteLine($"pointcloud.PointCount  : {pointcloud.PointCount}");
            //Console.WriteLine($"pointcloud.Bounds      : {pointcloud.Bounds}");
            //Console.WriteLine($"pointcloud.BoundingBox : {pointcloud.BoundingBox}");


            //var leafLodPointCount = 0L;
            //pointcloud.Root.Value.ForEachNode(true, n => { if (n.IsLeaf) leafLodPointCount += n.LodPositionsAbsolute.Length; });
            //Console.WriteLine($"leaf lod point count :{leafLodPointCount}");

            //foreach (var chunk in chunks)
            //{
            //    for (var i = 0; i < chunk.Count; i++)
            //    {
            //        Console.WriteLine($"{chunk.Positions[i]:0.000} {chunk.Colors?[i]}");
            //    }
            //}

            //Console.WriteLine($"chunks point count: {chunks.Sum(x => x.Positions.Count)}");
            //Console.WriteLine($"chunks bounds     : {new Box3d(chunks.SelectMany(x => x.Positions))}");

            //using (var w = File.CreateText("test.txt"))
            //{
            //    foreach (var chunk in chunks)
            //    {
            //        for (var i = 0; i < chunk.Count; i++)
            //        {
            //            var p = chunk.Positions[i];
            //            var c = chunk.Colors[i];
            //            w.WriteLine($"{p.X} {p.Y} {p.Z} {c.R} {c.G} {c.B}");
            //        }
            //    }
            //}
            //return;

            /*
            var stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            ASTM_E57.VerifyChecksums(stream, fileSizeInBytes);
            var header = ASTM_E57.E57FileHeader.Parse(stream);

            //Report.BeginTimed("parsing E57 file");
            //var take = int.MaxValue;
            //var data = header.E57Root.Data3D.SelectMany(x => x.StreamCartesianCoordinates(false)).Take(take).Chunk(1000000).ToList();
            //Report.EndTimed();
            //Report.Line($"#points: {data.Sum(xs => xs.Length)}");

            foreach (var p in header.E57Root.Data3D.SelectMany(x => x.StreamPoints(false))) Console.WriteLine(p.Item1);

            //var ps = PointCloud.Parse(filename, ImportConfig.Default)
            //    .SelectMany(x => x.Positions)
            //    .ToArray()
            //    ;
            */
        }

        internal static void TestImport()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var filename = @"T:\Vgm\Data\erdgeschoss.e57";

            var chunks = E57.Chunks(filename, ParseConfig.Default);

            var queue = new Queue<Chunk>();
            var parseChunksDone = false;

            Report.BeginTimed("total");

            Task.Run(() =>
            {
                foreach (var x in chunks.Take(10))
                {
                    lock (queue) queue.Enqueue(x);
        }
                Report.Line("set parseChunksDone");
                parseChunksDone = true;
            });

            static Dictionary<Cell, (List<V3f>, List<C4b>, V3d)> Grid(Chunk x)
            {
                var result = new Dictionary<Cell, (List<V3f>, List<C4b>, V3d)>();
                var dups = new HashSet<V3f>();
                for (var i = 0; i < x.Count; i++)
                {
                    var key = new Cell((V3l)x.Positions[i], 0);
                    if (!result.TryGetValue(key, out var val))
                    {
                        val = (new List<V3f>(), new List<C4b>(), key.GetCenter());
                        result[key] = val;
                    }

                    var dupKey = (V3f)(x.Positions[i]).Round(3);
                    if (dups.Add(dupKey))
                    {
                        val.Item1.Add((V3f)(x.Positions[i] - val.Item3));
                        val.Item2.Add(x.Colors[i]);
                    }
                }
                return result;
            }

            var global = new Dictionary<Cell, (List<V3f>, List<C4b>, V3d)>();

            void MergeIntoGlobal(Dictionary<Cell, (List<V3f>, List<C4b>, V3d)> x)
            {
                lock (global)
                {
                    foreach (var kv in x)
                    {
                        if (!global.TryGetValue(kv.Key, out var gval))
                        {
                            gval = kv.Value;
                            global[kv.Key] = gval;
                        }
                        else
                        {
                            gval.Item1.AddRange(kv.Value.Item1);
                            gval.Item2.AddRange(kv.Value.Item2);
                        }
                    }
                }
            }

            var globalGridReady = new ManualResetEventSlim();

            Task.Run(async () =>
            {
                var processedChunksCount = 0;
                var processedPointsCount = 0L;
                var tasks = new List<Task>();
                while (queue.Count > 0 || !parseChunksDone)
                {
                    var c = Chunk.Empty;
                    bool wait = false;
                    lock (queue)
                    {
                        if (queue.Count > 0) c = queue.Dequeue();
                        else wait = true;
                    }
                    if (wait) { await Task.Delay(1000); continue; }

                    tasks.Add(Task.Run(() =>
                    {
                        var grid = Grid(c);
                        MergeIntoGlobal(grid);

                        var a = Interlocked.Increment(ref processedChunksCount);
                        var b = Interlocked.Add(ref processedPointsCount, c.Count);
                        Report.Line($"processed chunk #{a} with {c.Count:N0} points ({b:N0} total points)");
                    }));
                }

                await Task.WhenAll(tasks);

                Report.Line("set globalGridReady");
                globalGridReady.Set();
            });

            globalGridReady.Wait();
            Report.Line($"global grid cell  count: {global.Count:N0}");
            Report.Line($"            point count: {global.Sum(x => x.Value.Item1.Count):N0}");

            Report.EndTimed("total");

            //Report.BeginTimed("parsing");
            //var foo = chunks.ToArray();
            //Report.EndTimed();

            //Console.WriteLine($"chunk count: {foo.Length}");
            //Console.WriteLine($"point count: {foo.Sum(x => x.Count)}");





            //var store = new SimpleDiskStore(@"C:\Users\sm\Desktop\Staatsoper.store").ToPointCloudStore(new LruDictionary<string, object>(1024 * 1024 * 1024));

            //var config = ImportConfig.Default
            //    .WithStorage(store)
            //    .WithKey("staatsoper")
            //    .WithVerbose(true)
            //    ;

            //Report.BeginTimed("importing");
            //var pointcloud = PointCloud.Import(filename, config);
            //Report.EndTimed();
            //store.Flush();
        }

        internal static void TestImportPts(string filename)
        {
            var chunks = Pts.Chunks(filename, ParseConfig.Default);

            Console.WriteLine(filename);
            var sw = new Stopwatch();
            var count = 0L;
            sw.Start();
            foreach (var chunk in chunks)
            {
                Console.WriteLine($"    {chunk.Count}, {chunk.BoundingBox}");
                count += chunk.Count;
            }
            sw.Stop();
            Console.WriteLine($"    {count:N0} points");
            Console.WriteLine($"    {sw.Elapsed} ({(int)(count / sw.Elapsed.TotalSeconds):N0} points/s)");
        }

        internal static void TestKNearest()
        {
            var sw = new Stopwatch();
            var rand = new Random();

            Report.BeginTimed("generating point clouds");
            var cloud0 = CreateRandomPointsInUnitCube(1000000, 8192);
            var cloud1 = CreateRandomPointsInUnitCube(1000000, 8192);
            Report.EndTimed();

            var ps0 = cloud0.QueryAllPoints().SelectMany(chunk => chunk.Positions).ToArray();
            
            sw.Restart();
            for (var i = 0; i < ps0.Length; i++)
            {
                var p = cloud1.QueryPointsNearPoint(ps0[i], 0.1, 1);
                if (i % 100000 == 0) Console.WriteLine($"{i,20:N0}     {sw.Elapsed}");
            }
            sw.Stop();
            Console.WriteLine($"{ps0.Length,20:N0}     {sw.Elapsed}");

            static PointSet CreateRandomPointsInUnitCube(int n, int splitLimit)
            {
                var r = new Random();
                var ps = new V3d[n];
                for (var i = 0; i < n; i++) ps[i] = new V3d(r.NextDouble(), r.NextDouble(), r.NextDouble());
                var config = ImportConfig.Default
                    .WithStorage(PointCloud.CreateInMemoryStore(new LruDictionary<string, object>(1024*1024*1024)))
                    .WithKey("test")
                    .WithOctreeSplitLimit(splitLimit)
                    ;
                return PointCloud.Chunks(new Chunk(ps), config);
            }
        }

        internal static void TestLoadOldStore()
        {
            var store = new SimpleDiskStore(@"T:\Vgm\Stores\referenz_2019_21_store").ToPointCloudStore(cache: default);
            _ = store.GetPointSet("770ed498-5544-4313-9873-5449f2bd823e");
            _ = store.GetPointCloudNode("e06a1e87-5ab1-4c73-8c3f-3daf1bdac1d9");
        }

        internal static void CopyTest()
        {
            var filename = @"T:\Vgm\Data\JBs_Haus.pts";
            var rootKey = "097358dc-d89a-434c-8a4e-fe03c063d886";
            var splitLimit = 65536;
            var minDist = 0.001;

            var store1Path = @"T:\Vgm\Stores\copytest1";
            //var store2Path = @"T:\Vgm\Stores\copytest2";
            //var store3Path = @"T:\Vgm\Stores\copytest3";
            //var store4Path = @"T:\Vgm\Stores\JBs_Haus.pts";

            // create stores
            var store1 = new SimpleDiskStore(store1Path).ToPointCloudStore();
            //var store2 = new SimpleDiskStore(store2Path).ToPointCloudStore();

            // import point cloud into store1
            Report.BeginTimed("importing");
            var config = ImportConfig.Default
                .WithStorage(store1)
                .WithKey(rootKey)
                .WithOctreeSplitLimit(splitLimit)
                .WithMinDist(minDist)
                .WithNormalizePointDensityGlobal(true)
                .WithVerbose(true)
                ;
            var _ = PointCloud.Import(filename, config);

            Report.EndTimed();
            store1.Flush();

            return;
            //if (!Directory.Exists(store3Path)) { Directory.CreateDirectory(store3Path); Thread.Sleep(1000); }
            //if (!Directory.Exists(store4Path)) { Directory.CreateDirectory(store4Path); Thread.Sleep(1000); }

            //// copy point cloud from store1 to store2
            //var pc1 = store1.GetPointSet(rootKey);
            //var root1 = pc1.Root.Value;
            //var totalNodes = root1.CountNodes(outOfCore: true);
            //store2.Add(rootKey, pc1);
            //Report.BeginTimed("copy");
            //var totalBytes = 0L;
            ////pc1.Root.Value.ForEachNode(outOfCore: true, n => Report.Line($"{n.Id} {n.PointCountCell,12:N0}"));
            //Convert(root1.Id);
            //Report.Line($"{totalNodes}");
            //Report.EndTimed();
            //store2.Flush();

            //// meta
            //var rootJson = JObject.FromObject(new
            //{
            //    rootId = root1.Id.ToString(),
            //    splitLimit = splitLimit,
            //    minDist = minDist,
            //    pointCount = root1.PointCountTree,
            //    bounds = root1.BoundingBoxExactGlobal,
            //    centroid = (V3d)root1.CentroidLocal + root1.Center,
            //    centroidStdDev = root1.CentroidLocalStdDev,
            //    cell = root1.Cell,
            //    totalNodes = totalNodes,
            //    totalBytes = totalBytes,
            //    gzipped = false
            //});
            //File.WriteAllText(Path.Combine(store3Path, "root.json"), rootJson.ToString(Formatting.Indented));

            //rootJson["gzipped"] = true;
            //File.WriteAllText(Path.Combine(store4Path, "root.json"), rootJson.ToString(Formatting.Indented));

            //void Convert(Guid key)
            //{
            //    if (key == Guid.Empty) return;

            //    var (def, raw) = store1.GetDurable(key);
            //    var node = raw as IDictionary<Durable.Def, object>;
            //    node.TryGetValue(Durable.Octree.SubnodesGuids, out var subnodeGuids);

            //    // write inlined node to store
            //    var inlinedNode = store1.ConvertToInline(node);
            //    var inlinedBlob = store2.Add(key, Durable.Octree.Node, inlinedNode, false);
            //    totalBytes += inlinedBlob.Length;
            //    //Report.Line($"stored node {key}");

            //    // write blob as file
            //    File.WriteAllBytes(Path.Combine(store3Path, key.ToString()), inlinedBlob);
                
            //    // write blob as file (gzipped)
            //    using (var fs = File.OpenWrite(Path.Combine(store4Path, key.ToString())))
            //    using (var zs = new GZipStream(fs, CompressionLevel.Fastest))
            //    {
            //        zs.Write(inlinedBlob, 0, inlinedBlob.Length);
            //        zs.Close();
            //    }

            //    // children
            //    if (subnodeGuids != null)
            //        foreach (var x in (Guid[])subnodeGuids) Convert(x);
            //}
        }

        internal static void TestCreateStore(string filepath, double minDist)
        {
            var filename = Path.GetFileName(filepath);
            var storename = Path.Combine(@"T:\Vgm\Stores\", filename);
            var store = new SimpleDiskStore(storename).ToPointCloudStore(new LruDictionary<string, object>(1024 * 1024 * 1024));

            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey(filename)
                .WithVerbose(true)
                //.WithOctreeSplitLimit(65536)
                .WithMaxDegreeOfParallelism(0)
                .WithMinDist(minDist)
                ;

            Report.BeginTimed("importing");
            PointCloud.Import(filepath, config);
            Report.EndTimed();
            store.Flush();
            store.Dispose();
                }

        internal static void ExportExamples(string filepath, bool collapse, bool gzipped, int? positionsRoundedToNumberOfDigits)
        {
            //// Example 1: export point cloud to folder
            //{
            //    var ct = new CancellationTokenSource(10000);
            //    using (var storeSource = new SimpleDiskStore(@"T:\Vgm\Stores\kindergarten").ToPointCloudStore())
            //    using (var storeTarget = new SimpleFolderStore(@"T:\Vgm\Stores\kindergartenExported").ToPointCloudStore())
            //    {
            //        storeSource.ExportPointSet("kindergarten", storeTarget, info => Console.Write($" ({info.Progress * 100,6:0.00}%)"), true, ct.Token);
            //    }
            //}

            //// Example 2: export point cloud to another store
            //{
            //    using (var storeSource = new SimpleDiskStore(@"T:\Vgm\Stores\copytest1").ToPointCloudStore())
            //    using (var storeTarget = new SimpleDiskStore(@"T:\Vgm\Stores\exportStore").ToPointCloudStore())
            //    {
            //        storeSource.ExportPointSet("097358dc-d89a-434c-8a4e-fe03c063d886", storeTarget, info => Console.WriteLine($"{info.Progress:0.00}"), true, CancellationToken.None);
            //        storeTarget.Flush();
            //    }
            //}

            // Example 3: inline point cloud nodes
            {
                var key = Path.GetFileName(filepath);
                var targetFolder = $@"T:\Vgm\Stores\{key}.upload2";
                using var storeSource = new SimpleDiskStore($@"T:\Vgm\Stores\{key}").ToPointCloudStore();
                using var storeTarget = new SimpleFolderStore(targetFolder).ToPointCloudStore();
                var foo = storeSource.GetPointSet(key);
                var bar = foo.Root.Value.Id;

                var config = new InlineConfig(
                    collapse, gzipped, positionsRoundedToNumberOfDigits,
                    x => Report.Line($"[progress] {x,7:0.000}")
                    );
                storeSource.ExportInlinedPointCloud(bar, storeTarget, config);
                storeTarget.Flush();

                // meta
                var pointset = storeSource.GetPointSet(key);
                var root = pointset.Root.Value;
                var rootJson = JsonSerializer.SerializeToNode(new
                {
                    Bounds = root.BoundingBoxExactGlobal,
#pragma warning disable IDE0037 // Use inferred member name
                    Cell = root.Cell,
#pragma warning restore IDE0037 // Use inferred member name
                    Centroid = (V3d)root.CentroidLocal + root.Center,
                    CentroidStdDev = root.CentroidLocalStdDev,
                    GZipped = gzipped,
                    PointCount = root.PointCountTree,
                    PointSetId = pointset.Id,
                    RootId = root.Id.ToString(),
                    TotalNodes = root.CountNodes(true),
                });

                File.WriteAllText(
                    Path.Combine(targetFolder, "root.json"),
                    rootJson.ToString()
                    );


            }
        }

        internal static void EnumerateCellsTest()
        {
            var store = new SimpleDiskStore(@"T:\Vgm\Pinter_Dachboden_3Dworx_Store\Pinter_Dachboden_store").ToPointCloudStore(cache: default);
            var pc = store.GetPointSet("1bd9ab70-0245-4bf2-bbad-8929ae94105e");
            var foo = store.GetPointCloudNode("1bd9ab70-0245-4bf2-bbad-8929ae94105e");
            var root = pc.Root.Value;

            foreach (var x in root.EnumerateCells(0))
            {
                var chunks = x.GetPoints(0).ToArray();
                Console.WriteLine($"{x.Cell,20} {chunks.Sum(c => c.Count),8:N0}    {new Box3d(chunks.Select(c => c.BoundingBox)):0.00}");
            }
        }

        internal static void EnumerateCells2dTest()
        {
            var inputFile = @"T:\Vgm\Data\E57\aibotix_ground_points.e57";

            var storeName = Path.Combine(@"T:\Vgm\Stores", Path.GetFileName(inputFile));
            var key = Path.GetFileName(storeName);
            //CreateStore(inputFile, storeName, key, 0.005);
            
            var store = new SimpleDiskStore(storeName).ToPointCloudStore(cache: default);
            var pc = store.GetPointSet(key).Root.Value;
            Console.WriteLine($"total points: {pc.PointCountTree}");

            // enumerateCells2d
            var stride = 3;
            var columns = pc.EnumerateCellColumns(cellExponent: 8, stride: new V2i(stride));

            var total = 0L;
            foreach (var c in columns)
            {
                Console.WriteLine($"{c.Cell,-20} {c.ColZ.CountTotal,15:N0}");
                var halfstride = (stride - 1) / 2;
                var xs = c.CollectPoints(int.MaxValue, new Box2i(-halfstride, -halfstride, +halfstride, +halfstride));
                var sum = xs.Sum(x => x.Points.Count);
                total += sum;
                foreach (var x in xs) Console.WriteLine($"  {x.Footprint,-20} {x.Points.Count,15:N0}");
                //Console.ReadLine();
            }


            //var oSize = 2;
            //var iSize = 1;
            //var cellExponent = 5;

            //Report.BeginTimed("total");
            //var xs = root.EnumerateCellColumns(cellExponent);
            //var i = 0;
            //foreach (var x in xs)
            //{
            //    var cs0 = x.GetPoints(int.MaxValue).ToArray();
            //    //if (cs0.Length == 0) continue;
            //    //var cs1 = x.GetPoints(0, outer: new Box2i(new V2i(-iSize, -iSize), new V2i(+iSize, +iSize))).ToArray();
            //    //var cs2 = x.GetPoints(0, outer: new Box2i(new V2i(-oSize, -oSize), new V2i(+oSize, +oSize)), inner: new Box2i(new V2i(-iSize, -iSize), new V2i(+iSize, +iSize))).ToArray();
            //    //Report.Line($"[{x.Cell.X,3}, {x.Cell.Y,3}, {x.Cell.Exponent,3}] {cs0.Sum(c => c.Count),10:N0} {cs1.Sum(c => c.Count),10:N0} {cs2.Sum(c => c.Count),10:N0}");
            //    //if (++i % 17 == 0) 
            //    if (cs0.Sum(c => c.Count) > 0)
            //        Report.Line($"[{x.Cell.X,3}, {x.Cell.Y,3}, {x.Cell.Exponent,3}] {cs0.Sum(c => c.Count),10:N0}");
            //}
            //Report.End();

            //for (cellExponent = 11; cellExponent >= -10; cellExponent--)
            //{
            //    Report.BeginTimed($"[old] e = {cellExponent,3}");
            //    var xs = root.EnumerateCellColumns(cellExponent);
            //    var totalPointCount = 0L;
            //    var count = 0L;
            //    foreach (var x in xs)
            //    {
            //        count++;
            //        var cs0 = x.GetPoints(int.MaxValue).ToArray();
            //        var tmp = cs0.Sum(c => c.Count);
            //        totalPointCount += tmp;
            //        //Report.Line($"[{x.Cell.X,3}, {x.Cell.Y,3}, {x.Cell.Exponent,3}] {tmp,10:N0}");
            //    }
            //    //if (sum != root.PointCountTree) throw new Exception();
            //    Report.End($" | cols = {count,12:N0} | points = {totalPointCount,12:N0}");
            //}

            //for (var cellExponent = 11; cellExponent >= -10; cellExponent--)
            //{
            //    Report.BeginTimed($"[new] e = {cellExponent,3}");
            //    var ys = root.EnumerateCellColumns(cellExponent);
            //    var totalPointCount = 0L;
            //    var count = 0L;
            //    foreach (var y in ys)
            //    {
            //        count++;
            //        var cs0 = y.CollectPoints(int.MaxValue);
            //        totalPointCount += cs0.Points.Count;
            //        //totalPointCount += y.CountTotal;
            //        //Report.Line($"[{y.Footprint.X,3}, {y.Footprint.Y,3}, {y.Footprint.Exponent,3}] {y.CountTotal,10:N0}");
            //    }
            //    //if (totalPointCount != root.PointCountTree) throw new Exception();
            //    Report.End($" | cols = {count,12:N0} | points = {totalPointCount,12:N0}");
            //}

            //Console.WriteLine($"BoundingBoxExactGlobal: {root.BoundingBoxExactGlobal:0.00}");
            //Console.WriteLine($"Cell.BoundingBox:       {root.Cell.BoundingBox:0.00}");
            //Console.WriteLine($"Cell:                   {root.Cell}");
            //var columns = root.EnumerateCellColumns(6, new V2i(1,1));
            //Console.WriteLine("for each column:");
            //foreach (var column in columns)
            //{
            //    Console.WriteLine($"    {column.Cell}");
            //    foreach (var chunk in column.GetPoints(4, new Box2i(-1,-1,+1,+1)))
            //    {
            //        Console.WriteLine($"        {chunk.Footprint}  {chunk.Points.Count}");
            //    }
            //}
        }

        internal static void EnumerateCells2dTestNew()
        {
            //var inputFile = @"T:\Vgm\Data\E57\KG1__002.e57";
            var inputFile = @"T:\Vgm\Data\E57\aibotix_ground_points.e57";
            
            var storeName = Path.Combine(@"T:\Vgm\Stores", Path.GetFileName(inputFile));
            var key = Path.GetFileName(storeName);
            //CreateStore(inputFile, storeName, key, 0.005);

            Report.Line($"filename    : {key}");

            var store = new SimpleDiskStore(storeName).ToPointCloudStore(cache: default);
            var pc = store.GetPointSet(key).Root.Value;
            Report.Line($"total points: {pc.PointCountTree,10:N0}");
            //Console.WriteLine($"total points: {pc.QueryAllPoints().Sum(c => c.Count),10:N0}");
            //Console.WriteLine($"total points: {new HashSet<V3d>(pc.QueryAllPoints().SelectMany(c => c.Positions)).Count,10:N0}");

            //Console.WriteLine($"total points: {pc.CountPoints()}");
            Report.Line($"bounding box: {pc.BoundingBoxExactGlobal:N2}");


            //for (var e = 11; e >= -11; e--)
            //{
            //    var d = Math.Pow(2.0, e);
            //    var stride = new V2d(d);

            //    Report.BeginTimed($"[{e}] enumerate");
            //    var grid = pc.QueryGridXY(stride, 1 << 20, int.MinValue);
            //    var count = grid.Sum(x => x.Points.Sum(y => y.Count));
            //    Report.End();
            //    Console.WriteLine($"total count = {count:N0}");
            //}

            //for (var e = pc.Cell.Exponent; e > -11; e--)
            //{
            //    Report.BeginTimed($"[{e,2}] enumerate grid cells 2^{e,-3}");
            //    var q = new GridQueryXY(pc, e);
            //    var countPoints = 0L;
            //    var countCells = 0L;
            //    foreach (var x in q.GridCells())
            //    {
            //        if (x.Footprint.Exponent != e) throw new InvalidOperationException();
            //        countCells++;
            //        countPoints += x.Count;
            //    }
            //    //Report.Line($"points {countPoints,15:N0}");
            //    //Report.Line($"cells  {countCells,15:N0}");
            //    Report.EndTimed($"{countCells,15:N0} cells");
            //}

            Report.Line();
            Report.BeginTimed("performing GridQueryXY");
            var gridCellExponent = 4;
            var subCellExponent = -3;
            Report.Line();
            Report.Line($"gridCellExponent = {gridCellExponent,3}");
            Report.Line($"subCellExponent  = {subCellExponent,3}");
            Report.Line();
            Report.Line($"EnumerateGridCells({gridCellExponent}) :");
            Report.Line();
            foreach (var x in pc.EnumerateGridCellsXY(gridCellExponent))
            {
                Report.Line($"  {x.Footprint,-25}       {x.Count,15:N0} points");
                var countSubCells = 0L;
                var countSubPoints = 0L;
                foreach (var y in x.EnumerateGridCellsXY(subCellExponent))
                {
                    countSubCells++;
                    countSubPoints += y.CollectPoints().Sum(z => z.Count);
                    if (countSubCells <= 3)
                        Report.Line($"    {y.Footprint,-25}     {y.Count,15:N0}");
                    else if (countSubCells == 4)
                        Report.Line($"    ...");
                }
                Report.Line($"    total: {countSubCells,9:N0} subcells  {countSubPoints,18:N0} points");
                Report.Line($"    ------------------------------------------------------");
                Report.Line();
            }
            Report.EndTimed();


            //// enumerateCells2d
            //var stride = 3;
            //var columns = pc.EnumerateCellColumns(cellExponent: 8, stride: new V2i(stride));

            //var total = 0L;
            //foreach (var c in columns)
            //{
            //    Console.WriteLine($"{c.Cell,-20} {c.ColZ.CountTotal,15:N0}");
            //    var halfstride = (stride - 1) / 2;
            //    var xs = c.CollectPoints(int.MaxValue, new Box2i(-halfstride, -halfstride, +halfstride, +halfstride));
            //    var sum = xs.Sum(x => x.Points.Count);
            //    total += sum;
            //    foreach (var x in xs) Console.WriteLine($"  {x.Footprint,-20} {x.Points.Count,15:N0}");
            //    //Console.ReadLine();
            //}


            //var oSize = 2;
            //var iSize = 1;
            //var cellExponent = 5;

            //Report.BeginTimed("total");
            //var xs = root.EnumerateCellColumns(cellExponent);
            //var i = 0;
            //foreach (var x in xs)
            //{
            //    var cs0 = x.GetPoints(int.MaxValue).ToArray();
            //    //if (cs0.Length == 0) continue;
            //    //var cs1 = x.GetPoints(0, outer: new Box2i(new V2i(-iSize, -iSize), new V2i(+iSize, +iSize))).ToArray();
            //    //var cs2 = x.GetPoints(0, outer: new Box2i(new V2i(-oSize, -oSize), new V2i(+oSize, +oSize)), inner: new Box2i(new V2i(-iSize, -iSize), new V2i(+iSize, +iSize))).ToArray();
            //    //Report.Line($"[{x.Cell.X,3}, {x.Cell.Y,3}, {x.Cell.Exponent,3}] {cs0.Sum(c => c.Count),10:N0} {cs1.Sum(c => c.Count),10:N0} {cs2.Sum(c => c.Count),10:N0}");
            //    //if (++i % 17 == 0) 
            //    if (cs0.Sum(c => c.Count) > 0)
            //        Report.Line($"[{x.Cell.X,3}, {x.Cell.Y,3}, {x.Cell.Exponent,3}] {cs0.Sum(c => c.Count),10:N0}");
            //}
            //Report.End();

            //for (cellExponent = 11; cellExponent >= -10; cellExponent--)
            //{
            //    Report.BeginTimed($"[old] e = {cellExponent,3}");
            //    var xs = root.EnumerateCellColumns(cellExponent);
            //    var totalPointCount = 0L;
            //    var count = 0L;
            //    foreach (var x in xs)
            //    {
            //        count++;
            //        var cs0 = x.GetPoints(int.MaxValue).ToArray();
            //        var tmp = cs0.Sum(c => c.Count);
            //        totalPointCount += tmp;
            //        //Report.Line($"[{x.Cell.X,3}, {x.Cell.Y,3}, {x.Cell.Exponent,3}] {tmp,10:N0}");
            //    }
            //    //if (sum != root.PointCountTree) throw new Exception();
            //    Report.End($" | cols = {count,12:N0} | points = {totalPointCount,12:N0}");
            //}

            //for (var cellExponent = 11; cellExponent >= -10; cellExponent--)
            //{
            //    Report.BeginTimed($"[new] e = {cellExponent,3}");
            //    var ys = root.EnumerateCellColumns(cellExponent);
            //    var totalPointCount = 0L;
            //    var count = 0L;
            //    foreach (var y in ys)
            //    {
            //        count++;
            //        var cs0 = y.CollectPoints(int.MaxValue);
            //        totalPointCount += cs0.Points.Count;
            //        //totalPointCount += y.CountTotal;
            //        //Report.Line($"[{y.Footprint.X,3}, {y.Footprint.Y,3}, {y.Footprint.Exponent,3}] {y.CountTotal,10:N0}");
            //    }
            //    //if (totalPointCount != root.PointCountTree) throw new Exception();
            //    Report.End($" | cols = {count,12:N0} | points = {totalPointCount,12:N0}");
            //}

            //Console.WriteLine($"BoundingBoxExactGlobal: {root.BoundingBoxExactGlobal:0.00}");
            //Console.WriteLine($"Cell.BoundingBox:       {root.Cell.BoundingBox:0.00}");
            //Console.WriteLine($"Cell:                   {root.Cell}");
            //var columns = root.EnumerateCellColumns(6, new V2i(1,1));
            //Console.WriteLine("for each column:");
            //foreach (var column in columns)
            //{
            //    Console.WriteLine($"    {column.Cell}");
            //    foreach (var chunk in column.GetPoints(4, new Box2i(-1,-1,+1,+1)))
            //    {
            //        Console.WriteLine($"        {chunk.Footprint}  {chunk.Points.Count}");
            //    }
            //}
        }

        internal static void DumpPointSetKeys()
        {
            //var storeFolder = @"T:\Vgm\Pinter_Dachboden_3Dworx_Store\Pinter_Dachboden_store";

            //var sds = new SimpleDiskStore(storeFolder);
            //var keys = sds.SnapshotKeys();
            //var store = sds.ToPointCloudStore(cache: default);

            //foreach (var k in keys) if (k.StartsWith("dd0f")) Console.WriteLine(k);
            //Console.WriteLine($"mmmh: {keys.SingleOrDefault(x => x == "dd0fe31f-ea1f-4bea-a1b4-f6c8bf314598")}");

            //Console.CursorVisible = false;
            //for (var i = 0; i < keys.Length; i++)
            //{
            //    var k = keys[i];
            //    try
            //    {
            //        var pc = store.GetPointSet(k);
            //        Console.WriteLine($"\r{k} (count={pc.PointCount:N0})");
            //    }
            //    catch { }
            //    if (i % 100 == 0) Console.Write($"\r{100.0 * (i + 1) / keys.Length,6:0.00}%");
            //}
            //Console.Write($"\r{100,6:0.00}%");
            //Console.CursorVisible = true;
        }

        internal static void LisaTest()
        {
            var path2store = @"E:\OEBB\stores\store_wien_small_labels";//store_wien_small_labels - Copy
            var key = "stream_3899";

            var cache = new LruDictionary<string, object>(1024 * 1024 * 1024);
            var store = PointCloud.OpenStore(path2store, cache);
            var pointset = store.GetPointSet(key);

            var ray = new Ray3d(
                new V3d(-160017.518571374, 246513.963542402, 2372.20790824948),
                new V3d(0.838664911597797, 0.54451231482662, -0.0121451659855219)
                );

            var ps = pointset.QueryPointsNearRay(ray, 0.01).SelectMany(x => x.Positions).ToArray();
            Console.WriteLine($"{ps.Length:N0}");
        }

        internal static void HeraTest()
        {
            Report.Line("Hera Test");

            var separators = new[] { '\t', ' ' };
            var culture = CultureInfo.InvariantCulture;
            var inputFile = @"T:\Hera\impact.0014";

            var storePath = Path.Combine(@"T:\Vgm\Stores", Path.GetFileName(inputFile));
            var key = Path.GetFileName(storePath);

            Report.Line($"inputFile = {inputFile}");
            Report.Line();

            //Report.BeginTimed("parsing (with string split and double.Parse)");
            //var lineCount = 0;
            //foreach (var line in File.ReadLines(inputFile))
            //{
            //    lineCount++;
            //    var ts = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            //    var p = new V3d(double.Parse(ts[0], culture), double.Parse(ts[1], culture), double.Parse(ts[2], culture));
            //    var v = new V3d(double.Parse(ts[3], culture), double.Parse(ts[4], culture), double.Parse(ts[5], culture));
            //    //if (lineCount % 100000 == 0) Report.Line($"[{lineCount}]");
            //}
            //Report.Line($"{lineCount} lines");
            //Report.End();


            Report.Line();

            Report.BeginTimed($"processing {inputFile}");

            Report.BeginTimed("parsing (with Aardvark.Data.Points.Ascii)");

            var lineDef = new[] {
                Ascii.Token.PositionX, Ascii.Token.PositionY, Ascii.Token.PositionZ
            };
            var chunks = Ascii.Chunks(inputFile, lineDef, ParseConfig.Default).ToArray();

            var lineCount = 0;
            foreach (var chunk in chunks)
            {
                lineCount += chunk.Count;
            }
            Report.Line($"{lineCount} lines");
            Report.EndTimed();

            Report.Line();

            Report.BeginTimed("octree, normals, lod");
            PointSet pointset;
            using (var store = new SimpleDiskStore(storePath).ToPointCloudStore())
            {
                var config = ImportConfig.Default
                    //.WithStorage(store)
                    .WithInMemoryStore()
                    .WithKey(key)
                    .WithVerbose(false)
                    .WithMaxDegreeOfParallelism(0)
                    .WithMinDist(0)
                    .WithNormalizePointDensityGlobal(true)
                    ;

                pointset = PointCloud.Import(chunks, config);
            }
            Report.EndTimed();

            Report.BeginTimed("flattening");
            var flat = Chunk.ImmutableMerge(pointset.Root.Value.Collect(int.MaxValue));
            var data = ImmutableDictionary<Durable.Def, object>.Empty
                .Add(Durable.Octree.PositionsLocal3f, flat.Positions.Map(p => new V3f(p)))
                .Add(Durable.Octree.Normals3f, flat.Normals.ToArray())
                ;
            Report.EndTimed();

            Report.BeginTimed("serializing");
            var buffer = data.DurableEncode(Durable.Primitives.DurableMap, false);
            Report.EndTimed();

            for (var i = 0; i < 100; i++)
            {
                Report.BeginTimed("deserializing");
                var sw = new Stopwatch(); sw.Start();
                var (def, o) = Data.Codec.Deserialize(buffer);
                var dict = (ImmutableDictionary<Durable.Def, object>)o;
                sw.Stop();
                var ps = (V3f[])dict[Durable.Octree.PositionsLocal3f];
                var ns = (V3f[])dict[Durable.Octree.Normals3f];
                //Report.Line($"positions : {ps.Length}");
                //Report.Line($"normals   : {ns.Length}");
                Report.Line($"{(buffer.Length / sw.Elapsed.TotalSeconds) / (1024 * 1024 * 1024):N3} GB/s");
                Report.EndTimed();
            }

            Report.EndTimed();
        }

        internal static void RasterTest()
        {
            //var layers = new Raster.NodeDataLayers(
            //    new Raster.Box2c(new V2l(0, 0), new V2l(100, 100), -1),
            //    (Raster.Defs.Quadtree.Heights1f.Id, new float[100 * 100]),
            //    (Raster.Defs.Quadtree.Colors4b.Id, new C4b[100 * 100])
            //    );

            //Raster.buildQuadtree(layers);

            //var data = Raster.CreateData(
            //   id: Guid.NewGuid(),
            //   bounds: new Cell2d(0, 0, 0),
            //   resolutionPowerOfTwo: 1,
            //   globalHeightOffset: 100.0,
            //   localHeights: new float[] { 1, 2, 3, 4 },
            //   heightStdDevs: null,
            //   colors4b: null,
            //   intensities1i: null
            //   );

            //var store = new Dictionary<Guid, Dictionary<Guid, object>>();

            //var foo = new Raster.RasterNode2d(data, id => store[id]);
            //Console.WriteLine(foo);



            //int n = 8192;

            //var rnd = new Random();
            //Report.BeginTimed("init source data");
            //var randomData = new int[n * n].SetByIndex(_ => rnd.Next(1000));
            //Report.EndTimed();

            //var o = new V2l(20000, 10000);
            //var tile = TileData.OfArray(Box2l.FromMinAndSize(o, n, n), randomData);

            //Report.BeginTimed("get window");
            //var randomDataWindow = tile.WithWindow(Box2l.FromMinAndSize(o + new V2l(1000, 1500), 2048, 2048));
            //Report.EndTimed();

            //Report.BeginTimed("materialize");
            //var materialized = randomDataWindow.Materialize();
            //Report.EndTimed();

            //Console.WriteLine(materialized.Bounds);
            //foreach (var x in materialized.SplitIntoTiles(new V2l(1024)))
            //{ }
            //Console.WriteLine("----------------------------------------------------");

            //foreach (var x in materialized.SplitIntoTiles(new V2l(1024L)).Select(x => x.Item2.Materialize()))
            //{
            //    Console.WriteLine(x.ToString());
            //}



            //for (var i = 0; i < 100; i++)
            //{
            //    Report.BeginTimed("split + materialize");
            //    var splitTiles = materialized.SplitIntoQuadrants().Map(x => x.Materialize());
            //    Report.EndTimed();
            //}

            //var data = new[] {
            //    1,  2,  3,  4,
            //    5,  6,  7,  8,
            //    9, 10, 11, 12,
            //    13,14, 15, 16
            //};

            //var tile0 = TileData.OfArray(Box2l.FromSize(4, 4), data);
            //var tile1 = tile0.WithWindow(Box2l.FromMinAndSize(2, 1, 2, 3));
            //var tile2 = tile1.Materialize();

            //var r = new Raster.ArrayView<int>(data, 4, 4);
            //var s = r.GetWindow(Box2i.FromMinAndSize(2, 1, 2, 3));
            //Console.WriteLine(s);

            //Console.ReadLine();
        }

        internal static bool IntersectsNew(Plane2d plane, Ray2d ray, out V2d point)
        {
            var nd = Vec.Dot(plane.Normal, ray.Direction);
            if (nd.IsTiny())
            {
                point = V2d.NaN;
                return false;
            }
            else
            {
                var t = (plane.Distance - Vec.Dot(plane.Normal, ray.Origin)) / nd;
                point = ray.Origin + t * ray.Direction;
                return true;
            }
        }

        internal static void IntersectsNewTest()
        {
            //var plane = new Plane2d(new V2d(-3.6701585479226354E-16, 1), 1.5236516412689705);
            //var ray = new Ray2d(new V2d(1.8373474556174547, 1.5236516412689713), new V2d(1.8299999713048472, -2.2204460492503131E-16));
            //var result = IntersectsNew(plane, ray, out var hit);
            //Console.WriteLine($"{result}    {hit}");

            var plane = new Plane2d(new V2d(-3.6701585479226354E-16, 1), 1.5236516412689705);
            var ray = new Ray2d(new V2d(1.8373474556174547, 1.5236516412689713), new V2d(1.8299999713048472, -2.2204460492503131E-16));
            var result = IntersectsNew(plane, ray, out var hit);
            Console.WriteLine($"{result}    {hit}");
        }

        internal static void PointCloudImportCleanup()
        {
            var filename = @"T:\Vgm\Data\E57\JBs_Haus.e57";
            _ = PointCloud.Import(filename);
        }

        class ApprInsidePolygon
        {
            #region Polygon2d contains V2d (haaser)

            internal static V3i InsideTriangleFlags(ref V2d p0, ref V2d p1, ref V2d p2, ref V2d point)
            {
                var n0 = new V2d(p0.Y - p1.Y, p1.X - p0.X);
                var n1 = new V2d(p1.Y - p2.Y, p2.X - p1.X);
                var n2 = new V2d(p2.Y - p0.Y, p0.X - p2.X);

                var t0 = Math.Sign(n0.Dot(point - p0)); if (t0 == 0) t0 = 1;
                var t1 = Math.Sign(n1.Dot(point - p1)); if (t1 == 0) t1 = 1;
                var t2 = Math.Sign(n2.Dot(point - p2)); if (t2 == 0) t2 = 1;

                return new V3i(t0, t1, t2);
            }

            internal static V3i InsideTriangleFlags(ref V2d p0, ref V2d p1, ref V2d p2, ref V2d point, int t0)
            {
                var n1 = new V2d(p1.Y - p2.Y, p2.X - p1.X);
                var n2 = new V2d(p2.Y - p0.Y, p0.X - p2.X);

                var t1 = Math.Sign(n1.Dot(point - p1)); if (t1 == 0) t1 = 1;
                var t2 = Math.Sign(n2.Dot(point - p2)); if (t2 == 0) t2 = 1;

                return new V3i(t0, t1, t2);
            }

            /// <summary>
            /// Returns true if the Polygon2d contains the given point.
            /// Works with all (convex and non-convex) Polygons.
            /// Assumes that the Vertices of the Polygon are sorted counter clockwise
            /// </summary>
            public static bool Contains(Polygon2d poly, V2d point)
            {
                return poly.Contains(point, true);
            }

            /// <summary>
            /// Returns true if the Polygon2d contains the given point.
            /// Works with all (convex and non-convex) Polygons.
            /// CCW represents the sorting order of the Polygon-Vertices (true -> CCW, false -> CW)
            /// </summary>
            public static bool Contains(Polygon2d poly, V2d point, bool CCW)
            {
                int pc = poly.PointCount;
                if (pc < 3)
                    return false;
                int counter = 0;
                V2d p0 = poly[0], p1 = poly[1], p2 = poly[2];
                V3i temp = InsideTriangleFlags(ref p0, ref p1, ref p2, ref point);
                int t2_cache = temp.Z;
                if (temp.X == temp.Y && temp.Y == temp.Z) counter += temp.X;
                for (int pi = 3; pi < pc; pi++)
                {
                    p1 = p2; p2 = poly[pi];
                    temp = InsideTriangleFlags(ref p0, ref p1, ref p2, ref point, -t2_cache);
                    t2_cache = temp.Z;
                    if (temp.X == temp.Y && temp.Y == temp.Z) counter += temp.X;
                }
                if (CCW) return counter > 0;
                else return counter < 0;
            }

            public static bool OnBorder(Polygon2d poly, V2d point, double eps)
                => poly.EdgeLines.Any(e => e.IsDistanceToPointSmallerThan(point, eps));

            #endregion
        }

        internal static void Test_20201113_Hannes()
        {
            //var filename = @"E:\e57tests\datasets\aibotix_ground_points.e57";
            //var filename = @"E:\e57tests\datasets\Register360_Berlin Office_1.e57";
            //var filename = @"E:\e57tests\datasets\Staatsoper.e57";
            //var filename = @"E:\e57tests\datasets\Innenscan_FARO.e57";
            var filename = @"E:\e57tests\datasets\1190_31_test_Frizzo.e57";
            //var filename = @"E:\e57tests\datasets\Neuhäusl-Hörschwang.e57";
            //var filename = @"E:\e57tests\datasets\2020452-B-3-5.e57";
            //var filename = @"E:\e57tests\datasets\100pct_1mm_zebcam_shade_zebcam_world.e57";

            var key = Path.GetFileName(filename);
            
            var storePath = $@"E:\e57tests\stores\{key}";
            Directory.CreateDirectory(storePath);
            using var storeRaw = new SimpleDiskStore(Path.Combine(storePath, "data.uds"));
            var store = storeRaw.ToPointCloudStore();


            var info = E57.E57Info(filename, ParseConfig.Default);
            Report.Line($"total bounds: {info.Bounds}");
            Report.Line($"total count : {info.PointCount:N0}");


            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey(key)
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(0)
                .WithMinDist(0.05)
                .WithNormalizePointDensityGlobal(true)
                .WithProgressCallback(p => { Report.Line($"{p:0.00}"); })
                ;

            Report.BeginTimed("total");

            var import = Task.Run(() =>
            {
                //var runningCount = 0L;
                var chunks = E57
                    .Chunks(filename, config.ParseConfig)
                    //.Take(50)
                    //.TakeWhile(chunk =>
                    //{
                    //    var n = Interlocked.Add(ref runningCount, chunk.Count);
                    //    Report.WarnNoPrefix($"[Chunks] {n:N0} / {info.PointCount:N0}");
                    //    return n < info.PointCount * (0.125 + 0.125 / 2);
                    //})
                    ;
                var pcl = PointCloud.Chunks(chunks, config);
                return pcl;
            });

            var pcl = import.Result;
            File.WriteAllText(Path.Combine(storePath, "key.txt"), pcl.Id);


            //var maxCount = pcl.PointCount / 30;
            //var level = pcl.GetMaxOctreeLevelWithLessThanGivenPointCount(maxCount);
            //var queryChunks = pcl.QueryPointsInOctreeLevel(level);

            //var intensityRange = queryChunks.Aggregate<Chunk, (int, int)?>(null, (intMaxima, chunk) =>
            //{
            //    if (chunk.HasIntensities)
            //    {
            //        var (currentMin, currentMax) = intMaxima ?? (int.MaxValue, int.MinValue);
            //        var minInt = Math.Min(currentMin, chunk.Intensities.Min());
            //        var maxInt = Math.Max(currentMax, chunk.Intensities.Max());
            //        return (minInt, maxInt);
            //    }
            //    else
            //    {
            //        return null;
            //    }
            //});

            //Report.Line($"intensityRange {intensityRange}");

            Report.EndTimed();
            Report.Line($"point count: {pcl.Root.Value.PointCountTree:N0}");
            //Report.Line($"number of keys: {storeRaw.SnapshotKeys().Length:N0}");
        }

        internal static void Test_20210217_cpunz()
        {
            var inputFile = @"T:\Vgm\Data\E57\aibotix_ground_points.e57";
            var storeName = Path.Combine(@"T:\Vgm\Stores", Path.GetFileName(inputFile));
            var key = Path.GetFileName(storeName);
            //CreateStore(inputFile, storeName, key, 0.005);

            var store = new SimpleDiskStore(storeName).ToPointCloudStore(cache: default);
            var pc = store.GetPointSet(key).Root.Value;
            Report.Line($"filename    : {key}");
            Report.Line($"total points: {pc.PointCountTree,10:N0}");
            Report.Line($"bounding box: {pc.BoundingBoxExactGlobal:N2}");

            Report.Line();
            Report.BeginTimed("activating filter");

            //var bb = pc.BoundingBoxExactGlobal;
            //var queryBox = new Box3d(bb.Min, new V3d(bb.Max.X, bb.Center.Y, bb.Max.Z));//.GetOctant(0);
            //var pcFiltered = FilteredNode.Create(pc, new FilterInsideBox3d(queryBox));

            var planes = new Plane3d[] {
                new (new V3d(0.833098559959325, -0.55312456950826, 0.0), -148084.543321104),
                new (new V3d(0.553124569505628, 0.833098559961073, 0.0), 210175.952298119),
                new (new V3d(0.0, 0.0, -1.0), -705.281005859375),
                new (new V3d(-0.833098559959325, 0.55312456950826, 0.0), 148100.179131675),
                new (new V3d(-0.553124569505628, -0.833098559961073, 0.0), -210155.471521074),
                new (new V3d(0.0, 0.0, 1.0), 708.76618125843)
             };
            var tempFilter = new Hull3d(planes);
            var pcFiltered = FilteredNode.Create(pc, new FilterInsideConvexHull3d(tempFilter));

            Report.EndTimed();

            //Report.Line();
            //Report.BeginTimed("performing GridQueryXY on original point cloud");
            //var dummy1 = pc.EnumerateGridCellsXY(-1).Select(x => x.Footprint.GetCenter()).ToArray();
            //Report.Line($"got {dummy1.Length} grid cells");
            //Report.EndTimed();

            Report.Line();
            Report.BeginTimed("performing GridQueryXY on filtered point cloud");
            var dummy2 = pcFiltered.EnumerateGridCellsXY(-1).Select(x => V3d.OOO).ToArray();
            Report.Line($"got {dummy2.Length} grid cells");
            Report.EndTimed();
        }

        internal static void Test_20210419_AutoUpgradeToSimpleStore3_0_0()
        {
            var store = PointCloud.OpenStore(@"T:\Vgm\Stores\2021-04-19_jbhaus_store\jbhaus_store", new LruDictionary<string, object>(2L << 30));
            var pc = store.GetPointCloudNode(@"c5eda8ca-35d8-46e5-be5a-6cf60c744421");
            Console.WriteLine($"{pc.BoundingBoxExactGlobal}");
        }

        internal static void Test_20240420_DecodeBlob()
        {
            var buffer = File.ReadAllBytes(@"T:\Vgm\Data\comparison_cli_3dworx\comparison\cli\cli_nodes\4a32683e-5d39-4a9c-991d-ff3a6db0929d");
            //var bufferOk1 = File.ReadAllBytes(@"C:\Users\sm\Downloads\839046c1-3248-4e0b-b20c-f19f0ee4b93e");
            //var bufferOk2 = File.ReadAllBytes(@"C:\Users\sm\Downloads\10075930-77ff-46d0-8648-84a73b60420c");
            using var foo = new GZipStream(new MemoryStream(buffer), CompressionMode.Decompress);
#if NETCOREAPP
            var (def, _) = DurableCodec.Deserialize(foo);
#else
            var (def, _) = DurableCodec.Deserialize(new BinaryReader(foo));
#endif
            Console.WriteLine(def);
        }

        internal static void Test_20210422_EnumerateInlinedFromFilteredNode()
        {
            //var store = PointCloud.OpenStore(@"T:\Vgm\Stores\2021-04-19_jbhaus_store\jbhaus_store", new LruDictionary<string, object>(2L << 30));
            //var pc = store.GetPointCloudNode(@"c5eda8ca-35d8-46e5-be5a-6cf60c744421");
            //var ebb = pc.BoundingBoxExactGlobal;
            //var filter = new Box3d(ebb.Min, ebb.Max - ebb.SizeZ * 0.5);

            //var f = FilteredNode.Create(pc, new FilterInsideBox3d(filter));
            //Console.WriteLine(pc.CountNodes(true));
            //Console.WriteLine(f.CountNodes(true));

            //Report.BeginTimed("exporting inlined point cloud");
            //var targetStore = new SimpleFolderStore(@"E:\tmp\20210426_net48").ToPointCloudStore();
            //f.ExportInlinedPointCloud(targetStore, new InlineConfig(true, true));
            //Report.End();

            //var foo = pc.EnumerateOctreeInlined(new InlineConfig(collapse: false, gzipped: true)).Nodes.ToArray();
            //Console.WriteLine(foo.Length);

            // check old vs new
            var folderOld = @"E:\tmp\20210424_inlined_old_collapse_gzip";
            var folderNew = @"E:\tmp\20210426_net48_fix";
            var filesOld = Directory.GetFiles(folderOld).OrderBy(x => x).ToArray();
            var filesNew = Directory.GetFiles(folderNew).OrderBy(x => x).ToArray();
            var countSizeMismatch = 0;
            var countSizeMatch = 0;
            for (var i = 0; i < filesOld.Length; i++)
            {
                Console.WriteLine(filesOld[i]);
                if (Path.GetFileName(filesOld[i]) != Path.GetFileName(filesNew[i])) throw new Exception("Mismatch");
                var bufferOld = File.ReadAllBytes(filesOld[i]);
                var bufferNew = File.ReadAllBytes(filesNew[i]);
                if (bufferOld.Length != bufferNew.Length)
                {
                    countSizeMismatch++;
                }
                else
                {
                    countSizeMatch++;
                    for (var j = 0; j < bufferOld.Length; j++) if (bufferOld[j] != bufferNew[j]) throw new Exception("Mismatch");
                }
            }
            Console.WriteLine($"blob sizes");
            Console.WriteLine($"    match    {countSizeMatch,10:N0}");
            Console.WriteLine($"    mismatch {countSizeMismatch,10:N0}");
        }


        internal static void TestDuplicatePoints()
        {
            using var store = new SimpleMemoryStore().ToPointCloudStore();
            var config = ImportConfig.Default
                .WithStorage(store)
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(0)
                .WithMinDist(0.0)
                .WithNormalizePointDensityGlobal(false)
                ;

            Report.BeginTimed($"importing");
            var chunk = new Chunk(
                positions: new [] { V3d.III, V3d.IOO, V3d.III, V3d.IOO, V3d.III, V3d.IOO, V3d.III, V3d.IOO }
                );
            var ps = PointCloud.Import(chunk, config);
            Report.EndTimed();

            Report.Line($"#points: {ps.PointCount}");
        }

        internal static void Test_20210621_InlineTransientNodes()
        {
            var filename = @"T:\Vgm\Data\JBs_Haus.pts";
            var storeFolder1 = @"E:\tmp\20210621_1";
            //var storeFolder2 = @"E:\tmp\20210621_2";
            var store1 = new SimpleDiskStore(storeFolder1).ToPointCloudStore();
            //var store2 = new SimpleDiskStore(storeFolder2).ToPointCloudStore();

            var config = ImportConfig.Default
                .WithStorage(store1)
                .WithRandomKey()
                .WithVerbose(true)
                ;

            Report.BeginTimed("import");
            var pc = PointCloud.Import(filename, config);
            var root = pc.Root.Value;
            store1.Flush();
            Report.EndTimed();

            // target store for inlined
            var store1ReadOnly = SimpleDiskStore.OpenReadOnlySnapshot(storeFolder1).ToPointCloudStore();
            var rootReadOnly = store1ReadOnly.GetPointSet(pc.Id).Root.Value;

            var centroid = root.Center + (V3d)root.CentroidLocal;
            Console.WriteLine($"root.PointCountTree           : {root.PointCountTree}");
            Console.WriteLine($"centroid                      : {centroid}");
            var filter = new Box3d(centroid - new V3d(5, 5, 5), centroid + new V3d(5, 5, 5));
            Console.WriteLine($"filter                        : {filter}");
            var f = FilteredNode.CreateTransient(rootReadOnly, new FilterInsideBox3d(filter));
            
            Report.BeginTimed("exporting inlined point cloud");
            store1ReadOnly.EnumerateOctreeInlined(f.Id, new InlineConfig(true, true));
            Report.End();
        }
        internal static void Test_20210904()
        {
            var storePath = @"E:\test1\a7b8c6f5-285e-4095-a737-faf4fce94587\store.uds";
            var store = new SimpleDiskStore(storePath).ToPointCloudStore();
            var root = store.GetPointCloudNode(Guid.Parse("9501236f-bb37-4eca-9dc1-eb5b91853595"));

            var c = new InlineConfig(
                collapse: false,
                gzipped: true,
                positionsRoundedToNumberOfDigits: null,
                progress: _ => { }
                );
            var inlined = root.EnumerateOctreeInlined(c);

            // encode nodes for PointShare
            var nodes = inlined.Nodes.ToArray();
            Console.WriteLine($"{nodes.Length}");
        }

        internal static void Test_20211013_log2int()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            //var bb = new Box3d(new V3d(-7163.84, 256990.58, 702.67), new V3d(-7146.66, 257016.8, 717.04));
            //var c = new Cell(-224, 8030, 21, 5);
            //Console.WriteLine(new Cell(bb));
            //Console.WriteLine(c.BoundingBox);
            //Console.WriteLine(c.BoundingBox.Contains(bb));
            //return;

            //var basedir = @"W:\Datasets\Vgm\Data\2021-10-13_test_rmDATA";
            var basedir = @"W:\Datasets\Vgm\Data";
            var storeFileName = @"W:\tmp\test20211013log2int.uds";

            if (File.Exists(storeFileName)) File.Delete(storeFileName);
            var store = new SimpleDiskStore(storeFileName);
            var filepaths = Directory.GetFiles(basedir, "*.*", SearchOption.AllDirectories);
            var files = new Dictionary<string, string>();
            foreach (var filepath in filepaths)
            {
                var ext = Path.GetExtension(filepath).ToLower();
                if (ext != ".pts" && ext != ".e57" && ext != ".las" && ext != ".laz") continue;
                files[Path.GetFileName(filepath)] = filepath;
            }
            var filenames = files.Values.OrderBy(x => Path.GetFileName(x)).ToArray();

            var results = new List<(string filename, PointSet pointset)>();
            var sw = new Stopwatch();

            var lineDefSitzungssaal = new[] { Ascii.Token.PositionX, Ascii.Token.PositionY, Ascii.Token.PositionZ, Ascii.Token.ColorR, Ascii.Token.ColorG, Ascii.Token.ColorB };
            filenames = filenames/*.Where(x => x.Contains("Betriebsgebäude.e57"))*/.ToArray();

            foreach (var filename in filenames)
            {
                try
                {
                    var fileinfo = new FileInfo(filename);
                    if (fileinfo.Length > 8L * 1024 * 1024 * 1024) continue;

                    var config = ImportConfig.Default
                        .WithStorage(store.ToPointCloudStore())
                        .WithKey(Path.GetFileName(filename).ToMd5Hash())
                        .WithVerbose(false)
                        .WithMaxDegreeOfParallelism(0)
                        .WithMinDist(0.005)
                        .WithNormalizePointDensityGlobal(true)
                        .WithProgressCallback(x =>
                        {
                            var col = Console.CursorLeft;
                            Console.Write($"\r{100.0 * x,6:N2}%");
                        });
                    ;

                    //Report.BeginTimed($"importing {filename}");
                    //Report.Line($"");
                    Console.BackgroundColor = ConsoleColor.Blue;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.WriteLine($"[{DateTimeOffset.Now}] {Path.GetFileName(filename),-60} {fileinfo.Length / (1024.0*1024*1024),4:N3} GiB ");
                    Console.ResetColor();
                    sw.Restart();
                    var ps = filename.EndsWith("Sitzungssaal.pts")
                        ? PointCloud.Import(Ascii.Chunks(filename, lineDefSitzungssaal, config.ParseConfig), config)
                        : PointCloud.Import(filename, config)
                        ;
                    var root = ps.Root.Value;
                    sw.Stop();
                    results.Add((filename, ps));
                    //PrintInfo(filename, ps);
                    Console.WriteLine($"\r{sw.Elapsed.TotalSeconds,10:N2}s     {root.PointCountTree,16:N0}     {root.Id}     {ToNiceString(root.Cell)}    {root.BoundingBoxExactGlobal:0.000}");
                    //Report.EndTimed();
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
            }

            Report.Line();
            Report.Line("===================================================================");
            Report.Line();
            Report.Begin("results");
            foreach (var (filename, ps) in results)
            {
                Report.Line();
                PrintInfo(filename, ps);
            }
            Report.End();

            static string ToNiceString(Cell c)
            {
                if (c.IsCenteredAtOrigin) return $"[{"-",6}, {"-",6}, {"-",6}, {c.Exponent,6}]";
                return $"[{c.X,6}, {c.Y,6}, {c.Z,6}, {c.Exponent,6}]";
            }
            static void PrintInfo(string filename, PointSet ps)
            {
                var root = ps.Root.Value;
                Report.Begin($"{filename}");
                Report.Line($"Id                          = {ps.Id}");
                Report.Line($"PointCount                  = {ps.PointCount:N0}");
                Report.Line($"root.Id                     = {root.Id}");
                Report.Line($"root.Cell                   = {root.Cell}");
                Report.Line($"root.BoundingBoxExactGlobal = {root.BoundingBoxExactGlobal}");
                Report.Line($"root.CentroidLocal          = {root.CentroidLocal}");
                Report.Line($"root.CentroidLocalStdDev    = {root.CentroidLocalStdDev}");
                Report.End();
            }
        }

        record FilterInsidePrismXYDto(string Type, V2d[][] Shape, double[] ZRange)
        {
            public FilterInsidePrismXYDto() : this("FilterInsidePrismXY", Array.Empty<V2d[]>(), Array.Empty<double>()) { }
            public FilterInsidePrismXYDto(FilterInsidePrismXY x) : this(
                "FilterInsidePrismXY",
                x.Shape.Polygons.Select(x => x.GetPointArray()).ToArray(),
                new[] { x.ZRange.Min, x.ZRange.Max }
                )
            { }

            public FilterInsidePrismXY ToFilterInsidePrismXY() => new(
                new PolyRegion(new Polygon2d(Shape[0].Map(p => new V2d(p[0], p[1])))),
                new Range1d(ZRange[0], ZRange[1])
                );
        }
        internal static void TestFilterSerialization()
        {
            var poly2d = new Polygon2d(V2d.OO, V2d.IO, V2d.II, V2d.OI);
            var shape = new PolyRegion(poly2d);
            var zRange = new Range1d(-5.3, 3.1415);
            var f = new FilterInsidePrismXY(shape, zRange);

            Console.WriteLine(JsonSerializer.Serialize(poly2d));

            var j = f.Serialize();
            var _ = Filter.Deserialize(j);
        }


        internal static void Test_Import_Regression()
        {
            //var filenames = new[]
            //{
            //    //@"W:\Datasets\Vgm\Data\E57\JBs_Haus.e57",
            //    @"W:\Datasets\pointclouds\tests\JB_Haus_2022_KG.e57"
            //};

            var filenames = Directory.GetFiles(@"W:\Datasets\pointclouds\tests");

            //filenames = Directory
            //    .EnumerateFiles(@"W:\Datasets\plytest", "*.ply", SearchOption.AllDirectories)
            //    //.Skip(4)
            //    //.Take(1)
            //    .ToArray()
            //    ;

            foreach (var filename in filenames)
            {
                if (!File.Exists(filename))
                {
                    Report.Warn($"File does not exist: {filename}");
                    continue;
                }

                Report.BeginTimed($"{ filename}");

                var key = Path.GetFileName(filename);

                var storePath = $@"T:\tmp_regression\{key}";
                Directory.CreateDirectory(storePath);
                using var storeRaw = new SimpleDiskStore(Path.Combine(storePath, "data.uds"));
                var store = storeRaw.ToPointCloudStore();


                //var info = E57.E57Info(filename, ParseConfig.Default);
                //Report.Line($"total bounds: {info.Bounds}");
                //Report.Line($"total count : {info.PointCount:N0}");


                var config = ImportConfig.Default
                    .WithStorage(store)
                    .WithKey(key)
                    .WithVerbose(true)
                    .WithMaxDegreeOfParallelism(0)
                    .WithMinDist(0.005)
                    .WithNormalizePointDensityGlobal(true)
                    //.WithMinDist(0)
                    //.WithNormalizePointDensityGlobal(false)
                    //.WithProgressCallback(p => { Report.Line($"{p:0.00}"); })
                    //.WithEnabledPartIndices(false)
                    ;

                var pcl = PointCloud
                        .Import(filename, config)
                        //.Take(20)
                        //.TakeWhile(chunk =>
                        //{
                        //    var n = Interlocked.Add(ref runningCount, chunk.Count);
                        //    Report.WarnNoPrefix($"[Chunks] {n:N0} / {info.PointCount:N0}");
                        //    return n < info.PointCount * (0.125 + 0.125 / 2);
                        //})
                        ;

                File.WriteAllText(Path.Combine(storePath, "key.txt"), pcl.Id);

                //var maxCount = pcl.PointCount / 30;
                //var level = pcl.GetMaxOctreeLevelWithLessThanGivenPointCount(maxCount);
                //var queryChunks = pcl.QueryPointsInOctreeLevel(0);

                //var intensityRange = queryChunks.Aggregate<Chunk, (int, int)?>(null, (intMaxima, chunk) =>
                //{
                //    if (chunk.HasIntensities)
                //    {
                //        var (currentMin, currentMax) = intMaxima ?? (int.MaxValue, int.MinValue);
                //        var minInt = Math.Min(currentMin, chunk.Intensities.Min());
                //        var maxInt = Math.Max(currentMax, chunk.Intensities.Max());
                //        return (minInt, maxInt);
                //    }
                //    else
                //    {
                //        return null;
                //    }
                //});

                //Report.Line($"intensityRange {intensityRange}");

                Report.Line($"point count: {pcl.Root.Value.PointCountTree:N0}");
                //Report.Line($"number of keys: {storeRaw.SnapshotKeys().Length:N0}");
                Report.EndTimed();
            }
        }

        static void LisaTest20220404()
        {
            var cache = new LruDictionary<string, object>(1024 * 1024 * 1024);
            var store = PointCloud.OpenStore($@"E:\e57tests\stores\ply\ply.uds", cache);
            var config = ImportConfig.Default.WithStorage(store).WithKey("root").WithVerbose(true);

            var chunks = Data.Points.Import.Ply.Chunks(@"C:\Users\sm\Downloads\test.ply", config.ParseConfig);
            var pointset = PointCloud.Chunks(chunks, config);
            Console.WriteLine(pointset.Id);
        }

        static void SmTest20220610()
        {
            var filename = @"E:\e57tests\stores\inference_full.binary.ply\data.uds";
            var storeRaw = SimpleDiskStore.OpenReadOnlySnapshot(filename);
            var store = storeRaw.ToPointCloudStore();
            var key = File.ReadAllText(Path.Combine(Path.GetDirectoryName(filename), "key.txt"));

            var pcl = store.GetPointSet(key);
            //var nodeCount = pcl.Root.Value.CountNodes(outOfCore: true);
            //Console.WriteLine($"nodeCount       : {nodeCount,16:N0}");

            var inlineConfig = new InlineConfig(collapse: true, gzipped: true);
            var inlinedNodes = store.EnumerateOctreeInlined(key, inlineConfig);

            var nodeCountInlined = inlinedNodes.Nodes.Count();
            Console.WriteLine($"nodeCountInlined: {nodeCountInlined,16:N0}");

            var nodeCountInlined2 = inlinedNodes.Nodes.Count();
            Console.WriteLine($"nodeCountInlined: {nodeCountInlined2,16:N0}");
        }

        static void SmTest20220815()
        {
            //var downloadPath = @"E:\tmp\20220916\download";
            //var pointCloudDirs = Directory.GetDirectories(downloadPath);
            //foreach (var pcDir in pointCloudDirs)
            //{
            //    var rootJsonPath = Path.Combine(pcDir, "root.json");
            //    var rootJson = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(rootJsonPath));
            //    var rootId = rootJson.GetProperty("rootId").GetString();

            //    var ns = enumerateTree(rootId).ToArray();

            //    IEnumerable<InlinedNode> enumerateTree(string id)
            //    {
            //        var blobPath = Path.Combine(pcDir, id);
            //        var n = new InlinedNode(File.ReadAllBytes(blobPath), gzipped: true);
            //        if (n.Intensities1b?.Length == 0) Console.WriteLine($"[blobPath] {n.PointCountCell}");

            //        if (id == "3fa600f0-6e99-4c11-918b-810430bae0cb") Console.WriteLine($"[{pcDir}] id = {id}");
            //        yield return n;

            //        if (n.SubnodesGuids != null)
            //        {
            //            foreach (var sid in n.SubnodesGuids)
            //            {
            //                if (sid == Guid.Empty) continue;
            //                var xs = enumerateTree(sid.ToString());
            //                foreach (var x in xs) yield return x;
            //            }
            //        }
            //    }
            //}

            var n0 = new InlinedNode(File.ReadAllBytes(@"E:\tmp\20220916\0c5a9202-d9d2-4565-b137-70c199d3b0c1"), gzipped: true);
            var n1 = new InlinedNode(File.ReadAllBytes(@"E:\tmp\20220916\49c753d9-c13a-4f35-8a5f-912be18ea69f"), gzipped: true);

            var storePath = @"E:\rmdata\20220915\techzentrum_store";
            //var pointcloud0key = "0c5a9202-d9d2-4565-b137-70c199d3b0c1";
            var pointcloud1key = "49c753d9-c13a-4f35-8a5f-912be18ea69f";

            var storeRaw = new SimpleDiskStore(storePath);
            var store = storeRaw.ToPointCloudStore();

            var root = store.GetPointCloudNode(pointcloud1key);
            // {[[242.683902740479, 98.1308326721191, -1.12363249063492], [279.89542388916, 137.056385040283, 2.01998573541641]]}
            var pc = FilteredNode.Create(root, new FilterInsideBox3d(new Box3d(new V3d(268.64, 116, -1), new V3d(272.70, 118, 1.70))));

            var nodeCount = pc.CountNodes(outOfCore: true);
            Console.WriteLine($"nodeCount       : {nodeCount,16:N0}");

            var inlineConfig = new InlineConfig(collapse: false, gzipped: true);

            var inlinedNodes = pc.EnumerateOctreeInlined(inlineConfig);

            var xs = inlinedNodes.Nodes.ToArray();
            var foo = xs.Where(x => x.NodeId == Guid.Parse("3fa600f0-6e99-4c11-918b-810430bae0cb")).ToArray();
            //foreach (var x in xs) Console.WriteLine($"{x.PointCountCell}");

            var nodeCountInlined = inlinedNodes.Nodes.Count();
            Console.WriteLine($"nodeCountInlined: {nodeCountInlined,16:N0}");
        }

        static void SmTest20220917()
        {
            //var filename = @"W:\Datasets\unstruk\Sensat-EWR-Bedford_22012021_Point-cloud.laz";
            var filename = @"W:\Datasets\unstruk\Christchurch.laz";
            var storePath = @"E:\e57tests\stores\20220921";
            var tmpPath = Path.Combine(storePath, "tmp");
            if (!Directory.Exists(storePath)) Directory.CreateDirectory(storePath);
            if (!Directory.Exists(tmpPath)) Directory.CreateDirectory(tmpPath);
            _ = GenericChunk.Defs.Positions3d;


            var info = Laszip.LaszipInfo(filename, ParseConfig.Default);
            Report.Line($"{new Cell(info.Bounds)}");

            var rootCell = new Cell(info.Bounds);
            var rootDir = Path.Combine(storePath, "root");

            //Check(rootDir); return;

            static void Check(string dir)
            {
                var fileCount = Directory.EnumerateFiles(dir).Count();
                var subDirs = Directory.GetDirectories(dir);
                var hasDirs = subDirs.Length > 0;

                //Report.Line($"{fileCount,4} files | {subDirs.Length} subdirs | {dir}");

                if (hasDirs)
                {
                    if (fileCount > 0) Report.Error($"[{dir}] has files AND dirs");

                    foreach (var subDir in subDirs)
                    {
                        Check(subDir);
                    }
                }
                else
                {
                    if (fileCount == 0)
                    {
                        Report.Error($"[{dir}] empty");
                    }
                    else if (fileCount > 1)
                    {
                        Report.Error($"[{dir}] {fileCount} files");
                    }
                    else
                    {
                        //Report.Line($"[{dir}] {fileCount} files");
                        //if (fileCount > 1)
                        //{
                        //    var fns = Directory.GetFiles(dir);
                        //    var merged = GenericChunk.ImmutableMerge(fns.Select(LoadChunk));
                        //    if (merged.Count > 128 * 1024)
                        //    {
                        //        Console.ForegroundColor = ConsoleColor.Red;
                        //    }
                        //    else
                        //    {
                        //        Console.ForegroundColor = ConsoleColor.Green;
                        //    }
                        //    Console.WriteLine($"{fns.Length,4} files | {merged.Count,12:N0} points | {dir}");
                        //    Console.ResetColor();
                        //}
                    }
                }
            }

            static GenericChunk LoadChunk(string path)
            {
                var buffer = File.ReadAllBytes(path);
                var map = (IReadOnlyDictionary<Durable.Def, object>)DurableCodec.DeserializeDurableMap(StorageExtensions.UnGZip(buffer));
                return new GenericChunk(map);
            }

            static byte[] SerializeChunk(GenericChunk chunk)
                => DurableCodec.SerializeDurableMap(chunk.Data).Gzip();

            static void SaveChunk(string path, GenericChunk chunk)
            {
                while (true)
                {
                    try
                    {
                        var bufferGzip = SerializeChunk(chunk);
                        File.WriteAllBytes(path, bufferGzip);
                        _ = StorageExtensions.UnGZip(File.ReadAllBytes(path)); // check if file can be read back and decompressed
                        break;
                    }
                    catch (Exception e)
                    {
                        Report.Error($"[{path}] {e.Message}");
                        continue;
                    }
                }
            }

            {
                var ts = new List<Task>();
                var q = new Queue<(string fileName, byte[] content, string[] filesToDelete)>();

                var runSaveTask = true;
                var saveTask = Task.Run(() =>
                {
                    while (q.Count > 0 || runSaveTask)
                    {
                        //Report.Line($"[QUEUE] length = {q.Count}");
                        (string fn, byte[] buffer, string[] filesToDelete) x = default;
                        var success = false;
                        lock (q)
                        {
                            if (q.Count > 0)
                            {
                                x = q.Dequeue();
                                success = true;
                            }
                            else
                            {
                                success = false;
                            }
                        }

                        if (success)
                        {
                            File.WriteAllBytes(x.fn, x.buffer);
                            foreach (var fn in x.filesToDelete) File.Delete(fn);
                        }
                        else
                        {
                            Task.Delay(1000).Wait();
                        }
                    }
                });

                var phase1Chunks = Directory.GetFiles(tmpPath, "*.gz").Where(s => Path.GetFileName(s).StartsWith("chunk-")).Take(int.MaxValue).ToArray();
                if (phase1Chunks.Length > 0)
                {
                    var totalCount = 0L;
                    Report.BeginTimed("phase 2");

                    for (var _i = 0; _i < phase1Chunks.Length; _i++)
                    {
                        var fn = phase1Chunks[_i];
                        try
                        {
                            Report.Line($"[[{_i + 1,5}/{phase1Chunks.Length}]] {fn} | {totalCount,15:N0}");
                            var chunk = LoadChunk(fn);
                            Interlocked.Add(ref totalCount, chunk.Count);
                            SplitAndSave(rootCell, rootDir, Path.GetFileName(fn), chunk);
                        }
                        catch (Exception e)
                        {
                            Report.Error($"[{fn}] {e}");
                        }
                    }

                    while (q.Count > 0) Task.Delay(1000).Wait();
                    Report.Line("merging");
                    Merge(rootCell, rootDir);


                    Report.Line("waiting for tasks to finish");
                    Task.WhenAll(ts).Wait();
                    Task.WhenAll(ts).Wait();
                    runSaveTask = false;
                    Report.Line("waiting for queue worker to finish");
                    saveTask.Wait();
                    Report.EndTimed();

                    Check(rootDir);
                    return;
                }

                void Merge(Cell cell, string dir)
                {
                    var fileCount = Directory.EnumerateFiles(dir).Count();
                    var subDirs = Directory.GetDirectories(dir);
                    var hasDirs = subDirs.Length > 0;

                    if (hasDirs)
                    {
                        if (fileCount > 0) throw new Exception($"[{dir}] has files AND dirs");

                        foreach (var subDir in subDirs)
                        {
                            var octantIndex = int.Parse(Path.GetFileName(subDir));
                            var subCell = cell.GetOctant(octantIndex);
                            Merge(subCell, subDir);
                        }
                    }
                    else
                    {
                        if (fileCount == 0)
                        {
                            throw new Exception($"[{dir}] empty");
                        }
                        else
                        {
                            if (fileCount > 1)
                            {
                                var fns = Directory.GetFiles(dir);
                                var merged = GenericChunk.ImmutableMerge(fns.Select(LoadChunk));
                                foreach (var fn in fns) File.Delete(fn);

                                if (merged.Count > 128 * 1024)
                                {
                                    SplitAndSave(cell, dir, "merged.gz", merged);
                                }
                                else
                                {
                                    ts.Add(Task.Run(() =>
                                    {
                                        var fn = Path.Combine(dir, "merged.gz");
                                        var buffer = SerializeChunk(merged);
                                        lock (q) q.Enqueue((fn, buffer, fns));
                                    }));
                                }

                                Report.Line($"{fns.Length,4} files | {merged.Count,12:N0} points | {dir}");
                            }
                        }
                    }
                }

                void SplitAndSave(Cell cell, string path, string filename, GenericChunk chunk)
                {
                    var split = chunk.Count > 128 * 1024;

                    if (Directory.Exists(path))
                    {
                        if (!split && Directory.EnumerateDirectories(path).Any())
                        {
                            split = true;
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(path);
                        //Report.Warn($"create {cell}");
                    }

                    if (split)
                    {
                        {
                            var octants = chunk.Split(cell);
                            for (var i = 0; i < 8; i++)
                            {
                                var o = octants[i];
                                if (o == null) continue;
                                var subPath = Path.Combine(path, i.ToString());
                                SplitAndSave(cell.GetOctant(i), subPath, filename, o);
                            }
                        }

                        var filesToPushDown = Directory.GetFiles(path);
                        foreach (var fn in filesToPushDown)
                        {
                            Report.Line($"push down {fn}");
                            var chunkToPushDown = LoadChunk(fn);
                            var octants = chunkToPushDown.Split(cell);
                            for (var i = 0; i < 8; i++)
                            {
                                var o = octants[i];
                                if (o == null) continue;
                                var subPath = Path.Combine(path, i.ToString());
                                SplitAndSave(cell.GetOctant(i), subPath, Path.GetFileName(fn), o);
                            }
                            File.Delete(fn);
                        }
                    }
                    else
                    {
                        ts.Add(Task.Run(() =>
                        {
                            var fn = Path.Combine(path, filename);
                            var buffer = SerializeChunk(chunk);
                            lock (q) q.Enqueue((fn, buffer, Array.Empty<string>()));
                        }));
                        //SaveChunk(Path.Combine(path, filename), chunk);
                    }
                }
            }

            //if (false)
            //{
            //    var phase1Chunks = Directory.GetFiles(tmpPath, "*.gz").Where(s => Path.GetFileName(s).StartsWith("chunk-")).ToArray();
            //    if (phase1Chunks.Length > 0)
            //    {
            //        var totalCount = 0L;
            //        Report.BeginTimed("reading phase 1 chunks (v0.0.2)");
            //        var ts = new List<Task>();
            //        for (var _i = 0; _i < phase1Chunks.Length; _i++)
            //        {
            //            var fn = phase1Chunks[_i];
            //            var buffer = File.ReadAllBytes(fn);
            //            //Report.Line($"[[{_i + 1,5}/{phase1Chunks.Length}]] {fn} | {totalCount,15:N0}");

            //            ts.Add(Task.Run(() =>
            //            {
            //                try
            //                {
            //                    var map = (IReadOnlyDictionary<Durable.Def, object>)DurableCodec.DeserializeDurableMap(StorageExtensions.UnGZip(buffer));
            //                    var chunk = new GenericChunk(map);
            //                    Interlocked.Add(ref totalCount, chunk.Count);
            //                }
            //                catch (Exception e)
            //                {
            //                    Report.Error($"[{fn}] {e}");
            //                }
            //            }));
            //        }
            //        Task.WhenAll(ts).Wait();
            //        Report.EndTimed();
            //        Report.Line($"total point count: {totalCount:N0}");
            //        return;
            //    }
            //}

            Report.BeginTimed("phase 1");
            {
                var n = 0L;
                var i = 0;
                var ts = new List<Task>();
                foreach (var _chunk in Laszip.Chunks(filename, ParseConfig.Default.WithMaxChunkPointCount(1_000_000)).Take(10))
                {
                    n += _chunk.Count;

                    var chunk = _chunk.ImmutableDeduplicate(verbose: false);
                    var d = _chunk.Count - chunk.Count;
                    Report.Line($"[{i++,5}] {100.0 * n / info.PointCount,6:N2}% | {d,10:N0} dups | {n,10:N0}/{info.PointCount:N0} | {new Cell(chunk.BoundingBox),-20}");

                    var j = i;
                    ts.Add(Task.Run(() =>
                    {
                        var outFileName = Path.Combine(tmpPath, $"chunk-{j:00000}.gz");
                        SaveChunk(outFileName, chunk.ToGenericChunk());
                    }));
                }

                Task.WhenAll(ts).Wait();
            }
            Report.EndTimed();
            return;

            //var info = Laszip.LaszipInfo(filename, ParseConfig.Default);
            //return;


            
            //var store = PointCloud.OpenStore(Path.Combine(storePath, "store.uds"));
            //var config = ImportConfig.Default
            //    .WithStorage(store)
            //    .WithKey("root")
            //    .WithOctreeSplitLimit(65536*2)
            //    .WithVerbose(true)
            //    ;
            //var pointset = store.GetPointSet("root") ?? PointCloud.Import(filename, config);
            //var root = pointset.Root.Value;
            //Console.WriteLine(root.BoundingBoxExactGlobal);
            //foreach (var p in root.PositionsAbsolute.Take(10)) Console.WriteLine(p);
        }

        static Task Test_20221218()
        {
            var filename = @"W:\Datasets\Vgm\Data\2022-12-18_punz\Scan_2.pts";
            var storePath = @"E:\tmp\2022-12-18_punz";
            var keyfile = Path.Combine(storePath, "key.txt");
            if (!Directory.Exists(storePath)) Directory.CreateDirectory(storePath);

            using var storeRaw = new SimpleDiskStore(Path.Combine(storePath, "data.uds"));
            var store = storeRaw.ToPointCloudStore();

            if (File.Exists(keyfile))
            {
                var key = File.ReadAllText(keyfile);
                var ps = store.GetPointSet(key);
                Console.WriteLine(ps.PointCount);

                var cells = ps.Root.Value.EnumerateCells(-3).ToArray();
                var foo = cells.Map(cell =>
                {
                    var justInner = cell.GetPoints(1).ToArray();
                    var useOuter = cell.GetPoints(1, new Box3i(new V3i(-1, -1, -1), new V3i(1, 1, 1)), false).ToArray();
                    return useOuter;
                });

                return Task.CompletedTask;
            }

            var config = ImportConfig.Default
                    .WithStorage(store)
                    .WithKey(Path.GetFileName(filename))
                    .WithVerbose(true)
                    .WithMinDist(0.005)
                    .WithNormalizePointDensityGlobal(true)
                    //.WithProgressCallback(p => { Report.Line($"{p:0.00}"); })
                    ;

            var pcl = PointCloud
                    .Import(filename, config)
                    ;

            File.WriteAllText(keyfile, pcl.Id);

            return Task.CompletedTask;
        }

        static Task Example_20230316() 
        {
            var filename = @"W:\Datasets\Vgm\Data\structured_pointclouds\JB_Haus_2022_KG.e57";

            var config = ParseConfig.Default.WithMaxChunkPointCount(1000000);
            var info = E57.E57Info(filename, config);

            //while (true)
            {
                var t0 = Stopwatch.StartNew();
                var chunks = E57.Chunks(filename, config);
                foreach (var chunk in chunks)
                {
                    //WriteLine($"parsed chunk with {chunk.Count} points");
                    //for (var i = 0; i < 5; i++) WriteLine($"   pos={chunk.Positions[i]:0.00} col={chunk.Colors[i]}");
                    //WriteLine("    ...");
                }
                t0.Stop();
                WriteLine(t0.Elapsed);
            }
            return Task.CompletedTask;
        }

        static Task Repro_20230308() 
        {
            const string POINTCLOUD = @"W:\Datasets\Vgm\Data\E57\JBs_Haus.e57";

            const string STOREPATH = @"./teststore.dur";

            Guid KEY1 = Guid.Parse("771ea77e-f868-4881-8a52-11502f2099e9");
            Guid KEY2 = Guid.Parse("653d7bcb-2511-40b8-9709-13efdc34736a");
            var cts = new CancellationTokenSource();

            WriteLine("delete store file (y/N)?");
            switch (ReadKey().Key) {
                case ConsoleKey.Y:
                    try { File.Delete(STOREPATH); }
                    catch (Exception e) { WriteLine($"Failed to delete store: {e.Message}"); }
                    break;
                case ConsoleKey.N:
                    break;
                default:
                    WriteLine($": INVALID KEY -> EXIT");
                    return Task.CompletedTask;
            }

            /////////////////////////////////////////////////////////////////////////////
            WriteLine($"[0] CREATE EMPTY STORE AND IMPORT POINT CLOUD");
            if (File.Exists(STOREPATH)) {
                WriteLine($"[0] store already exists (no import necessary)");
            }
            else {
                Directory.CreateDirectory(@"T:\tmp");
                using var store0 = new SimpleDiskStore(STOREPATH).ToPointCloudStore();
                WriteLine($"[0] created empty store");

                Write($"[0] importing {POINTCLOUD} ... ");
                var pointset0 = PointCloud.Import(POINTCLOUD, ImportConfig.Default
                    .WithStorage(store0)
                    .WithKey(KEY1)
                    .WithVerbose(false)
                    );
                WriteLine($"done");
            }
            WriteLine();

            /////////////////////////////////////////////////////////////////////////////
            try {
                WriteLine($"[1] OPEN STORE (READ-ONLY) and continuously read existing point cloud)");
                var storeReadOnly = SimpleDiskStore
                    .OpenReadOnlySnapshot(STOREPATH)
                    .ToPointCloudStore(cache: null) // don't use a cache to force file access
                    ;
                WriteLine($"[1] opened store (read-only)");
                _ = Task.Run(() =>
                {
                    try {
                        while (!cts.IsCancellationRequested) {
                            var pointcloud = storeReadOnly.GetPointSet(KEY1);
                            var count = 0;
                            pointcloud.Root.Value.ForEachNode(
                                outOfCore: true,
                                action: node => count++
                                );
                            ForegroundColor = ConsoleColor.Blue; Write("R"); ResetColor();
                        }
                    }
                    catch (Exception e) {
                        WriteLine($"Failed to READ point cloud. {e.Message}");
                    }
                },
                cts.Token);
            }
            catch (Exception e) {
                WriteLine($"Failed to OPEN store (READ-ONLY). {e.Message}");
            }

            WriteLine();
            WriteLine("============================================");
            WriteLine("press any key to START POINTCLOUD IMPORT ...");
            WriteLine("============================================");
            ReadKey();

            /////////////////////////////////////////////////////////////////////////////
            try {
                WriteLine();
                WriteLine($"[2] OPEN STORE (WRITE) and continuously import same point cloud");
                var storeReadWrite = new SimpleDiskStore(STOREPATH)
                    .ToPointCloudStore(cache: null) // don't use a cache to force file access
                    ;
                WriteLine($"[2] opened store (read/write)");
                _ = Task.Run(() =>
                {
                    try {
                        while (!cts.IsCancellationRequested) {
                            var pointset = PointCloud.Import(POINTCLOUD, ImportConfig.Default
                                .WithStorage(storeReadWrite)
                                .WithKey(KEY2)
                                .WithVerbose(false)
                                );

                            ForegroundColor = ConsoleColor.Green;
                            WriteLine($"\nimported point cloud, store size is {new FileInfo(STOREPATH).Length:N0} bytes");
                            ResetColor();
                        }
                    }
                    catch (Exception e) {
                        WriteLine($"Failed to IMPORT point cloud. {e.Message}");
                    }
                },
                cts.Token);
            }
            catch (Exception e) {
                WriteLine($"Failed to OPEN store (WRITE). {e.Message}");
            }

            WriteLine();
            WriteLine("=========================");
            WriteLine("press any key to stop ...");
            WriteLine("=========================");
            ReadKey();
            cts.Cancel();

            return Task.CompletedTask;
        }

        static Task Test_20230419()
        {
            //var filename = @"C:\Users\sm\Downloads\NDR-Studio3 1.e57";
            var filename = @"W:\Datasets\Vgm\Data\E57\Betriebsgebäude.e57";
            //{
            //    void VerifyChecksums(Stream stream, long streamLengthInBytes)
            //    {
            //        stream.Position = 0;

            //        if (streamLengthInBytes % 1024 != 0) throw new Exception(
            //            $"[E57] Invalid file size. Must be multiple of 1024, but is {streamLengthInBytes:N0}."
            //            );

            //        var imax = streamLengthInBytes / 1024;
            //        var buffer = new byte[1024];
            //        for (var i = 0; i < imax; i++)
            //        {
            //            var blockIndex = i + 1;
            //            if (stream.Read(buffer, 0, 1024) != 1024) throw new InvalidOperationException();

            //            // checksum is big endian unsigned 32-bit int
            //            var crcStored = (uint)((buffer[1020] << 24) + (buffer[1021] << 16) + (buffer[1022] << 8) + buffer[1023]);

            //            var crcComputed = Crc32CAlgorithm.Compute(buffer, 0, 1020);

            //            if (blockIndex % (1000000) == 0) 
            //                WriteLine($"[E57][{DateTime.Now}][{100.0 * stream.Position / stream.Length:000.00}%] checked {stream.Position:N0} bytes");

            //            if (crcStored != crcComputed)
            //            {
            //                ForegroundColor = ConsoleColor.Red;
            //                WriteLine(
            //                    $"[E57][{DateTime.Now}] block {blockIndex:N0}/{imax:N0} : checksum error (0x{crcStored:X} instead of 0x{crcComputed:X}). File offset is 0x{stream.Position - 4:X}."
            //                    );
            //                ResetColor();
            //            }
            //        }
            //        WriteLine($"[E57][{DateTime.Now}][{100.0 * stream.Position / stream.Length:000.00}%] checked {stream.Position:N0} bytes");
            //    }

            //    VerifyChecksums(File.OpenRead(filename), new FileInfo(filename).Length);
            //    return Task.CompletedTask;
            //}

            //            {
            //                var rootCell = new Cell(8);
            //                var aCell = new Cell(3, 3, 0, 6);
            //                var bCell = new Cell(1, 1, -1, 7);

            //                var foo1 = new Cell(aCell, bCell);
            //                var foo2 = new Cell(new Box3d(aCell.BoundingBox, bCell.BoundingBox));

            //                var subcells = new IPointCloudNode[8];
            //                var doneA = false;
            //                var doneB = false;
            //                for (var i = 0; i < 8; i++)
            //                {
            //                    var subcell = rootCell.GetOctant(i);

            //                    Report.Line($"subcell {subcell}, a {aCell}");
            //                    if (subcell.Intersects(aCell))
            //                    {
            //#if DEBUG
            //                        if (subcell.Intersects(bCell)) throw new InvalidOperationException();
            //#endif
            //                        //subcells[i] = JoinTreeToRootCell(subcell, a, config);
            //                        if (doneB) break;
            //                        doneA = true;
            //                    }

            //                    Report.Line($"subcell {subcell}, b {bCell}");
            //                    if (subcell.Intersects(bCell))
            //                    {
            //#if DEBUG
            //                        if (subcell.Intersects(aCell)) throw new InvalidOperationException();
            //#endif
            //                        //subcells[i] = JoinTreeToRootCell(subcell, b, config);
            //                        if (doneA == true) break;
            //                        doneB = true;
            //                    }
            //                }
            //                return Task.CompletedTask;
            //            }

            //var debugChunksDir = new DirectoryInfo(@"W:\tmp\debugChunks");
            //var debugChunks = debugChunksDir
            //    .EnumerateFiles("*.dur")
            //    .Select(filename =>
            //    {
            //        // var buffer = chunk.ToGenericChunk().Data.DurableEncode(Durable.Primitives.DurableMap, gzipped: false);
            //        var buffer = File.ReadAllBytes(filename.FullName);
            //        var (def, o) = Data.Codec.Deserialize(buffer);
            //        var chunk = new GenericChunk((ImmutableDictionary<Durable.Def, object>)o);
            //        Report.Line($"read chunk {filename.FullName}");
            //        return chunk;
            //    })
            //    //.ToArray()
            //    ;


            //var storePath = @"W:\Aardworx\pointshare2\testdata\ndrstudio3.0.005.store";
            var storePath = @"W:\Aardworx\pointshare2\testdata\2023-07-29_betriebsgebaeude.0.005.store";

            var keyfile = Path.Combine(storePath, "key.txt");
            if (!Directory.Exists(storePath)) Directory.CreateDirectory(storePath);

            using var storeRaw = new SimpleDiskStore(Path.Combine(storePath, "data.uds"));
            var store = storeRaw.ToPointCloudStore();

            if (File.Exists(keyfile))
            {
                var key = File.ReadAllText(keyfile);
                var ps = store.GetPointSet(key);
                WriteLine(ps.PointCount);
                return Task.CompletedTask;
            }

            var config = ImportConfig.Default
                    .WithStorage(store)
                    .WithKey(Path.GetFileName(filename))
                    .WithVerbose(true)
                    .WithMinDist(0.005)
                    .WithNormalizePointDensityGlobal(true)
                    .WithMaxDegreeOfParallelism(1)
                    //.WithProgressCallback(p => { Report.Line($"{p:0.00}"); })
                    ;

            var pcl = PointCloud.Import(filename, config);

            //var config = ImportConfig.Default
            //        .WithStorage(store)
            //        .WithKey(Path.GetFileName(filename))
            //        .WithVerbose(true)
            //        .WithMinDist(0)
            //        .WithNormalizePointDensityGlobal(false)
            //        .WithMaxDegreeOfParallelism(1)
            //        //.WithProgressCallback(p => { Report.Line($"{p:0.00}"); })
            //        ;
            //var pcl = PointCloud.Import(debugChunks, config);

            File.WriteAllText(keyfile, pcl.Id);

            return Task.CompletedTask;
        }

        static Task ParseTest_20230730()
        {
            var filename = @"T:\Kindergarten.pts";
            var chunks = Pts.Chunks(filename, ParseConfig.Default);
            var totalPointCount = 0L;
            var sw = Stopwatch.StartNew();
            foreach (var chunk in chunks)
            {
                totalPointCount += chunk.Count;
                WriteLine($"[{sw.Elapsed}] {totalPointCount:N0}");
            }
            sw.Stop();
            WriteLine(sw.Elapsed);
            return Task.CompletedTask;
        }

        static async Task Ranges_Test_20230802()
        {
            await Task.Delay(0); // avoid warning
            var filename = @"W:\Datasets\Vgm\Data\kinettn\A1_CP_Grossenzersdorf.e57";

            var info = E57.E57Info(filename, ParseConfig.Default);
            var chunks = E57.Chunks(filename, ParseConfig.Default);
            WriteLine($"{info.PointCount:N0} points");
            WriteLine($"{info.Bounds}");

            var pointCountTotal = 0L;

            var intensitiesRange = Range1i.Invalid;
            var jsHisto = new int[17];

            foreach (var chunk in chunks)
            {
                pointCountTotal += chunk.Count;

                var js = chunk.Intensities;
                var jsRange = new Range1i(js);


                foreach (var g in js.GroupBy(j => j >> 12)) jsHisto[g.Key] += g.Count();

                WriteLine($"[{pointCountTotal,20:N0}] {new Cell(chunk.BoundingBox),-25}; intensities {jsRange,-20}; [{string.Join('|', jsHisto.Select(x => $"{x,9}"))}]");
            }
        }

        static Task Parts_Test_20231006()
        {
            //const string STOREPATH = @"W:\Aardvark\stores\JBs_Haus.e57";
            const string STOREPATH = @"W:\Aardvark\stores\JB_Haus_2022_KG.e57";
            //const string STOREPATH = @"W:\Aardvark\stores\lowergetikum 20230321.e57";

            using var store = new SimpleDiskStore(Path.Combine(STOREPATH, "data.uds")).ToPointCloudStore();
            var key = File.ReadAllText(Path.Combine(STOREPATH, "key.txt"));
            var ps = store.GetPointSet(key);
            var root = ps.Root.Value;

            return Task.CompletedTask;
        }
        static async Task Parts_Test_20231006_Merge()
        {
            static V3d[] randomPoints(int n, Box3d bb)
            {
                var size = bb.Size;
                var ps = new V3d[n];
                for (var i = 0; i < n; i++)
                {
                    ps[i] = bb.Min + new V3d(Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble()) * size;
                }

                return ps;
            }

            var c0 = new Chunk(positions: randomPoints(10, Box3d.Unit          ), null, null, null, null, partIndices: 0, null, null);
            var c1 = new Chunk(positions: randomPoints(10, Box3d.Unit + V3d.IOO), null, null, null, null, partIndices: 1, null, null);

            var storeDir = @"W:\Datasets\Vgm\Stores\test_partindex";
            var key = await CreateStore(
                new [] { c0, c1 },
                storeDir,
                minDist: 0.01,
                splitLimit: 10
                );

            using var store = new SimpleDiskStore(Path.Combine(storeDir, "data.uds")).ToPointCloudStore();
            var ps = store.GetPointSet(key);
            var root = ps.Root.Value;
        }

        //#pragma warning disable CS1998

        static void Test_Parse_Regression()
        {
            var basedir = @"W:\Datasets\pointclouds\tests";
            foreach (var file in Directory.GetFiles(basedir, "*.e57"))
            {
                try
                {
                    ParsePointCloudFile(file, verbose: false);
                }
                catch (Exception e)
                {
                    WriteLine(e);
                }
            }
        }

        static Task CylStoreIssue_20250723()
        {
            const string STOREPATH = @"W:\Datasets\Vgm\Stores\2025-07-23_cyl_store";
            const string KEY = "d84613d1-6841-4745-8779-e498fc041064";

            using var store = new SimpleDiskStore(STOREPATH).ToPointCloudStore();
            var buffer = store.GetByteArray(KEY);
            var root = store.GetPointCloudNode(KEY);

            WriteLine($"root.PointCountTree: {root.PointCountTree:N0}");

            var ns = root.EnumerateNodes().Where(n => n.IsLeaf).ToList();
            var ps = ns.SelectMany(n => n.PositionsAbsolute).ToArray();

            {
                const int BINS = 20;
                var gs = ps.GroupBy(p => (int)(p.XY.Length * BINS)).OrderBy(g => g.Key).ToArray();
                foreach (var g in gs) WriteLine($"radius < {(g.Key + 1.0) / BINS,6:N3}: {g.Count(),16:N0}");
                var rRange = Range1d.Invalid;
                var zRange = Range1d.Invalid;
                foreach (var p in ps)
                {
                    var r = p.XY.Length;
                    rRange.ExtendBy(r);
                    zRange.ExtendBy(p.Z);
                }
                WriteLine($"rRange: {rRange}");
                WriteLine($"zRange: {zRange}");
            }

            {
                const int BINS = 1000;
                var gs = ps.Where(p => p.XY.Length < 0.98).GroupBy(p => (int)(p.Z * BINS)).OrderBy(g => g.Key).ToArray();
                foreach (var g in gs) WriteLine($"z      < {(g.Key + 1.0) / BINS,6:N3}: {g.Count(),16:N0}");
            }

            WriteLine();

            V3d[] QueryPointsNearRayGroundTruth(V3d[] ps, Ray3d ray, double maxDistanceToRay, double tMin, double tMax)
                => ps.Where(p =>
                    {
                        var d = ray.GetMinimalDistanceTo(p);
                        var tp = ray.GetTOfProjectedPoint(p);
                        return d <= maxDistanceToRay && tp >= tMin && tp <= tMax;
                    })
                    .ToArray();

            var rayOriginShift = V3d.Zero;
            //var rayOriginShift = new V3d(0.001, 0.001, 0);
            //var rayOriginShift = new V3d(0.01, 0.01, 0);

            var rayDirOffset = V3d.Zero;
            //var rayDirOffset = new V3d(0.001, 0.0, 0);

            {
                WriteLine($"Case A");

                var ray = new Ray3d(
                    new V3d(2.7977742221289563E-05, 2.093743741681459E-06, -2.4001809538969687) + rayOriginShift,
                    (new V3d(2.115032588716738E-07, 6.094078775363987E-07, 0.999999999999792) + rayDirOffset).Normalized
                    //V3d.ZAxis
                    );
                var min = 0.0;
                var max = 4.833334673038614;
                //var radius = 1.7341171501789909;
                var radius = 1.0001;

                WriteLine($"  {ray}");
                WriteLine($"  radius = {radius}");

                var chunks = root.QueryPointsNearRay(ray, radius, min, max).ToArray();
                WriteLine($"  chunks.Length           : {chunks.Length,16:N0}");
                WriteLine($"  chunks.Sum(x => x.Count): {chunks.Sum(x => x.Count),16:N0}");

                var groundTruth = QueryPointsNearRayGroundTruth(ps, ray, radius, min, max);
                WriteLine($"  groundTruth.Length      : {groundTruth.Length,16:N0}");

                WriteLine();
            }

            {
                WriteLine($"Case B");
                var ray = new Ray3d(
                    new V3d(2.7977742221289563E-05, 2.093743741681459E-06, -2.4001809538969687) + rayOriginShift,
                    (new V3d(2.115032588716738E-07, 6.094078775363987E-07, 0.999999999999792) + rayDirOffset).Normalized
                    //V3d.ZAxis
                    );
                var min = 0.0;
                var max = 4.833334673038614;
                //var radius = 1.7141171501789909;
                var radius = 1.0001;

                WriteLine($"  {ray}");
                WriteLine($"  radius = {radius}");

                var chunks = root.QueryPointsNearRay(ray, radius, min, max).ToArray();
                WriteLine($"  chunks.Length           : {chunks.Length,16:N0}");
                WriteLine($"  chunks.Sum(x => x.Count): {chunks.Sum(x => x.Count),16:N0}");

                var groundTruth = QueryPointsNearRayGroundTruth(ps, ray, radius, min, max);
                WriteLine($"  groundTruth.Length      : {groundTruth.Length,16:N0}");


                WriteLine();
            }

            return Task.CompletedTask;
        }

        public static async Task Main(string[] _)
        {
            await Task.CompletedTask; // avoid warning if no async methods are called here ...

            await CylStoreIssue_20250723();

            //EnumerateOctreeNodes(@"E:\tmp\BLK360_Atotech.e57_005_vgm", "188ac686-9567-4a7e-ae30-58dfef89bcf4");

            //await CreateStore(
            //    @"C:\Users\sm\Downloads\cloud_0.e57",
            //    @"E:\tmp\20250624_cloud0_005_aardvark",
            //    minDist: 0.005,
            //    maxDegreeOfParallelism: 0
            //    );

            //Test_Parse_Regression();

            //Test_Import_Regression();

            //ParsePointCloudFile(
            //    @"E:\Villa Vaduz gesamt.e57",
            //    //@"W:\Datasets\pointclouds\tests\6562-alle-Scans.e57",
            //    verbose: false
            //    );


            //await CreateStore(
            //    @"E:\Villa Vaduz gesamt.e57",
            //    @"T:\tmp\Villa Vaduz gesamt.e57",
            //    minDist: 0.005
            //    );

            //{
            //    var chunks = E57.Chunks(@"W:\Datasets\Vgm\Data\2024-04-30_bugreport\F_240205.e57", ParseConfig.Default);
            //    var i = 0;
            //    foreach (var chunk in chunks)
            //    {
            //        var bb = new Box3d(chunk.Positions);
            //        WriteLine($"[{i++}] {bb}");
            //    }
            //    return;
            //}

            //await Task.Delay(0); // avoid warnings if main contains no await

            //await CreateStore(
            //    @"W:\Datasets\Vgm\Data\structured_pointclouds\lowergetikum 20230321.e57",
            //    @"W:\Datasets\Vgm\Stores\lowergetikum 20230321.e57_0.001",
            //    minDist: 0.001
            //    );

            //await CreateStore(
            //    @"W:\Datasets\Vgm\Data\E57\erdgeschoss.e57",
            //    @"W:\Datasets\Vgm\Stores\erdgeschoss.e57_0.0025",
            //    minDist: 0.0025
            //    );

            //await Parts_Test_20231006_Merge();

            //await Parts_Test_20231006();

            //await Ranges_Test_20230802();

            //await ParseTest_20230730();

            //await Test_20230419();

            //{
            //    var chunks = E57.ChunksFull(@"W:\Datasets\Vgm\Data\E57\Innenscan_FARO\Innenscan_FARO.e57", ParseConfig.Default);
            //    foreach (var chunk in chunks)
            //    {
            //        WriteLine(new Box3d(chunk.Positions));
            //    }
            //}

            //{
            //    var filename = @"W:\Datasets\Vgm\Data\structured_pointclouds\JB_Haus_2022_KG.e57";
            //    var filesize = new FileInfo(filename).Length;
            //    E57FileHeader header;
            //    using (var stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            //    {
            //        header = E57FileHeader.Parse(stream, filesize, verbose: true);
            //    };

            //    var i = 0;
            //    foreach (var image2d in header.E57Root.Images2D)
            //    {
            //        var outfile = $"T:/tmp/testimages/{i++}.jpg";
            //        var fileinfo = new FileInfo(outfile); if (!fileinfo.Directory.Exists) fileinfo.Directory.Create();
            //        File.WriteAllBytes(outfile, image2d.PinholeRepresentation.JpegImage);
            //        WriteLine($"saved {outfile}");
            //    }
            //}

            //await Repro_20230308();

            //await Example_20230316();

            //Test_Import_Regression();

            //await Test_20221218();

            //{
            //    var stream = await new HttpClient().GetStreamAsync("http://localhost:8080/Vgm/Data/E57/JBs_Haus.e57");
            //    var chunks = E57.Chunks(stream, 0, ParseConfig.Default.WithMaxChunkPointCount(65 * 1024));
            //    foreach (var chunk in chunks) Console.WriteLine(chunk.Count);
            //    return;
            //}

            //{
            //    foreach (var x in Laszip.Chunks(@"W:\Datasets\unstruk\Christchurch.laz", ParseConfig.Default))
            //    {
            //        Console.WriteLine($"intensities [{x.Intensities.Min()} / {x.Intensities.Average()} / {x.Intensities.Max()}] classifications [{x.Classifications.Min()}..{x.Classifications.Max()}]");
            //    }
            //}

            //SmTest20220917();

            //SmTest20220815();

            //new InlinedNodeTests().CanInlineNode(); return;

            //SmTest20220610();

            //Test_Import_Regression();

            //LisaTest20220404();

            //TestFilterSerialization();

            //Test_20211013_log2int();

            //Test_20210904();

            //TestDuplicatePoints();

            //TestDuplicatePoints();

            //new Aardvark.Physics.Sky.SkyTests().SolarTransmitTest();
            //new Aardvark.Physics.Sky.SkyTests().SunRiseSunSetTest();
            //new Aardvark.Physics.Sky.SkyTests().DuskDawnTest();
            //new Aardvark.Physics.Sky.SkyTests().DuskDawnTest2();
            //new Aardvark.Physics.Sky.SkyTests().HorizonTest();
            //new Aardvark.Physics.Sky.SkyTests().TwilightTimesTest();

            //new Aardvark.Data.Photometry.PhotometryTest().LumFluxTest();

            //Test_20210422_EnumerateInlinedFromFilteredNode();

            //Test_20240420_DecodeBlob();

            //Test_20210419_AutoUpgradeToSimpleStore3_0_0();

            //Durable.Octree.BoundingBoxExactGlobal

            //Test_20210217_cpunz();

            //TestLaszip();

            //Test_20201113_Hannes();

            //var poly = new Polygon2d(V2d.OO, V2d.IO, V2d.II, V2d.OI);
            //Console.WriteLine(ApprInsidePolygon.Contains(poly, new V2d(0.5, 0.0)));
            //Console.WriteLine(ApprInsidePolygon.OnBorder(poly, new V2d(0.5, 0.0), 0.0));

            //EnumerateCells2dTestNew();

            //var inputFile = @"C:\Users\sm\Downloads\C_25GN1.LAZ";
            //var storeName = Path.Combine(@"T:\Vgm\Stores", Path.GetFileName(inputFile));
            //var key = Path.GetFileName(storeName);
            //CreateStore(inputFile, storeName, key, 0.0);

            //PointCloudImportCleanup();

            //RasterTest();

            //EnumerateCells2dTest();

            //HeraTest();

            //TestE57();

            //LisaTest();

            //DumpPointSetKeys();

            //var filepath = @"T:\Vgm\Data\JBs_Haus.pts";
            ////var filepath = @"T:\Vgm\Data\Technologiezentrum_Teil1.pts";
            ////var filepath = @"T:\Vgm\Data\E57\Staatsoper.e57";
            //var filepath = @"T:\Vgm\Data\Kindergarten.pts";
            //TestCreateStore(filepath, 0.001);
            //ExportExamples(filepath, collapse: true, gzipped: true, positionsRoundedToNumberOfDigits: 3);


            //TestImport();


            //DumpPointSetKeys();
            // polygon topology test
            //new PointClusteringTest().TestPointClustering();

            //var data = File.ReadAllBytes("C:\\temp\\test.mesh");
            //PolyMesh mesh = data.Decode<PolyMesh>();


            //// fix broken edges...
            ////mesh.VertexClusteredCopy(Aardvark.Geometry.Clustering.PointClustering(mesh.PositionArray));
            //mesh.WithoutDegeneratedEdges();
            ////mesh.WithoutDegeneratedFaces();
            //mesh.BuildTopology();

            //Report.Line("yeah");

            //DumpPointSetKeys();

            //EnumerateCellsTest();

            //ExportExamples();

            //CopyTest();

            //var path = JObject.Parse(File.ReadAllText(@"T:\OEBB\20190619_Trajektorie_Verbindungsbahn\path.json"));
            //File.WriteAllText(@"T:\OEBB\20190619_Trajektorie_Verbindungsbahn\path2.json", path.ToString(Formatting.Indented));
            //Console.WriteLine(path);
            //PerfTestJuly2019();

            //TestLoadOldStore();

            //new ViewsFilterTests().CanDeletePoints();

            //new DeleteTests().DeleteDelete();
            //Console.WriteLine("done");

            //new ImportTests().CanImportChunkWithoutColor();

            //var store = PointCloud.OpenStore(@"G:\cells\3280_5503_0_10\pointcloud");
            //var pc = store.GetPointSet("3280_5503_0_10", default);
            //Console.WriteLine(pc.Id);
            //Console.WriteLine(pc.PointCount);

            //TestKNearest();
            //foreach (var filename in Directory.EnumerateFiles(@"C:\", "*.pts", SearchOption.AllDirectories))
            //{
            //    TestImportPts(filename);
            //}
        }
    }
}
