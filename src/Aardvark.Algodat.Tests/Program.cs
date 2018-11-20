﻿using Aardvark.Base;
using Aardvark.Data.Points;
using Aardvark.Data.Points.Import;
using Aardvark.Geometry.Points;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Uncodium.SimpleStore;

namespace Aardvark.Geometry.Tests
{
    public unsafe class Program
    {
        internal static void LinkedStores()
        {
            var tmpStorePath = @"G:\allStore";
            var resolver = new PatternResolver(@"G:\cells\%KEY%\pointcloud");
            
            var links = Directory
                .EnumerateDirectories(@"G:\cells", "pointcloud", SearchOption.AllDirectories)
                .Select(x => (storePath: x, key: Path.GetFileName(Path.GetDirectoryName(x))))
                //.Skip(6)
                //.Take(2)
                .ToArray();
            
            var sw = new Stopwatch(); sw.Restart();
            var totalCount = 0L;

            if (false)
            using (var storage = PointCloud.OpenStore(tmpStorePath))
            {
                var ls = links
                    .Select(x =>
                    {
                        try
                        {
                            var node = new LinkedNode(storage, x.key, x.key, resolver);
                            Console.WriteLine($"{node.PointCountTree,20:N0}");
                            totalCount += node.PointCountTree;
                            return node;
                        }
                        catch
                        {
                            Console.WriteLine($"[ERROR] could not read {x.key}@{x.storePath}");
                            return null;
                        }
                    })
                    .Where(x => x != null)
                    .ToArray();

                    sw.Stop();
                    Console.WriteLine($"{totalCount,20:N0} total");
                    Console.WriteLine(sw.Elapsed);


                foreach (var x in ls)
                {
                    storage.Add(x.Id, x);
                    //Console.WriteLine($"x.CountNodes() -> {x.CountNodes()}");
                    Console.WriteLine($"processed {x.Id}  {x.Cell}  PointCountTree -> {x.PointCountTree,20:N0}");
                }

                var config = ImportConfig.Default
                    .WithCreateOctreeLod(false)
                    //.WithEstimateNormals(ps => Normals.EstimateNormals((V3d[])ps, 8))
                    ;

                var merged = Merge.NonOverlapping(storage, resolver, ls, config);
                //Console.WriteLine($"merged.CountNodes()   -> {merged.CountNodes()}");
                Console.WriteLine($"merged.PointCountTree -> {merged.PointCountTree,20:N0}");
                storage.Add(merged.Id, merged);
                storage.Add("merged", merged);

                //var cloud = new PointSet(storage, resolver, "merged", merged, 8192);
                //storage.Add("merged", cloud, default);

                storage.Flush();
            }

            using (var storage = PointCloud.OpenStore(tmpStorePath))
            {
                var reloaded = storage.GetPointCloudNode("merged", resolver);
                //Console.WriteLine($"reloaded.CountNodes() -> {reloaded.CountNodes()}");
                Console.WriteLine($"reloaded.PointCountTree -> {reloaded.PointCountTree,20:N0}");
                printLinks(reloaded);

                void printLinks(IPointCloudNode n)
                {
                    if (n == null) return;
                    if (n is LinkedNode x)
                    {
                        Console.WriteLine($"LinkedNode: {x.Cell}  {x.LinkedStoreName}  {x.LinkedPointCloudKey}");
                        return;
                    }
                    if (n.SubNodes == null) return;
                    foreach (var y in n.SubNodes)
                    {
                        if (y == null) continue;
                        printLinks(y.Value);
                    }
                }
            }

            /*
            var key = @"3274_5507_0_10";
            using (var tmp = PointCloud.OpenStore(tmpStorePath))
            {
                var a = new LinkedNode(key, key, resolver);
                Console.WriteLine(a.CountNodes());
                tmp.Add("link", a);
                tmp.Flush();
            }
            using (var tmp = PointCloud.OpenStore(tmpStorePath))
            {
                var a = tmp.GetPointCloudNode("link", resolver);
                Console.WriteLine(a.CountNodes());
            }
            */

            //Console.WriteLine($"{a.PointCountTree:N0}");
            Environment.Exit(0);
        }

        internal static void TestE57()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var filename = @"test.pts";
            var fileSizeInBytes = new FileInfo(filename).Length;

            var config = ImportConfig.Default
                .WithInMemoryStore()
                .WithRandomKey()
                .WithVerbose(true)
                .WithMaxDegreeOfParallelism(0)
                .WithMinDist(0.005)
                ;

            var chunks = Pts.Chunks(filename, config).ToList();
            var pointcloud = PointCloud.Chunks(chunks, config);
            Console.WriteLine($"pointcloud.PointCount  : {pointcloud.PointCount}");
            Console.WriteLine($"pointcloud.Bounds      :{pointcloud.Bounds}");
            Console.WriteLine($"pointcloud.BoundingBox :{pointcloud.BoundingBox}");

            var leafLodPointCount = 0L;
            pointcloud.Octree.Value.ForEachNode(true, n => { if (n.IsLeaf()) leafLodPointCount += n.GetLodPositionsAbsolute().Length; });
            Console.WriteLine($"leaf lod point count :{leafLodPointCount}");

            //foreach (var chunk in chunks)
            //{
            //    for (var i = 0; i < chunk.Count; i++)
            //    {
            //        Console.WriteLine($"{chunk.Positions[i]:0.000} {chunk.Colors?[i]}");
            //    }
            //}

            Console.WriteLine($"chunks point count: {chunks.Sum(x => x.Positions.Count)}");
            Console.WriteLine($"chunks bounds     : {new Box3d(chunks.SelectMany(x => x.Positions))}");

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
            var filename = @"test.e57";

            var store = new SimpleDiskStore(@"./store").ToPointCloudStore();

            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey("mykey")
                .WithVerbose(true)
                ;

            Report.BeginTimed("importing");
            var pointcloud = PointCloud.Import(filename, config);
            Report.EndTimed();
            store.Flush();
        }

        internal static void TestImportPts(string filename)
        {
            var chunks = Pts.Chunks(filename, ImportConfig.Default);

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

            PointSet CreateRandomPointsInUnitCube(int n, int splitLimit)
            {
                var r = new Random();
                var ps = new V3d[n];
                for (var i = 0; i < n; i++) ps[i] = new V3d(r.NextDouble(), r.NextDouble(), r.NextDouble());
                var config = ImportConfig.Default
                    .WithStorage(PointCloud.CreateInMemoryStore())
                    .WithKey("test")
                    .WithOctreeSplitLimit(splitLimit)
                    ;
                return PointCloud.Chunks(new Chunk(ps, null), config);
            }
        }

        public static void Main(string[] args)
        {
            //LinkedStores();

            MasterLisa.Perform();
            //TestE57();

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
