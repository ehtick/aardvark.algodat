﻿using Aardvark.Base;
using Aardvark.Data.Points;
using Aardvark.Geometry.Points;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Aardvark.Geometry.Tests
{
    static class MasterLisa
    {
        public static void Perform()
        {
            var path2store = @"C:\Users\kellner\Desktop\Diplomarbeit\Store2";

            //var dirLabels = @"C:\Users\kellner\Desktop\Diplomarbeit\Semantic3d\sem8_labels_training";
            //var dirData = @"C:\Users\kellner\Desktop\Diplomarbeit\Semantic3d\sem8_data_training";

            var key = "bildstein_5_labelled";
            //var fn = "bildstein_station5_xyz_intensity_rgb";

            //Report.Line("importing pointcloud");
            //Import(
            //    Path.Combine(dirData, fn + ".txt"),
            //    Path.Combine(dirLabels, fn + ".labels"),
            //    path2store, key);

            //Report.Line("estimating normals");
            //AddNormals(path2store, key);

            //FilterPoints(path2store, key, 0); // filter all unlabelled points

            // -----------------------------------
            var store = PointCloud.OpenStore(path2store);
            var pointset = store.GetPointSet(key, CancellationToken.None);
            
            var nodes = traverse(pointset.Octree.Value, 3, 0);

            IPointCloudNode[] traverse(IPointCloudNode n, int maxLevel, int currLevel)
            {
                if (n.IsLeaf())
                    return new IPointCloudNode[] { };

                if (currLevel < maxLevel)
                {
                    currLevel = currLevel + 1;
                    return n.SubNodes.
                        Where(sn => sn != null).
                        SelectMany(sn => 
                        traverse(sn.Value, maxLevel, currLevel)).ToArray();
                }

                if (currLevel == maxLevel)
                    return new IPointCloudNode[] { n };

                return new IPointCloudNode[] { };
            }
        }

        /// <summary>
        /// Removes all points with given label from the pointcloud.
        /// </summary>
        private static void FilterPoints(string path2store, string key, int label2filter)
        {
            var store = PointCloud.OpenStore(path2store);
            var pointset = store.GetPointSet(key, CancellationToken.None);

            var chunks = pointset.QueryAllPoints().ToArray();
            
            var positions = new List<V3d>();
            var colors = new List<C4b>();
            var normals = new List<V3f>();
            var intensities = new List<int>();
            var classifications = new List<byte>();

            // filter chunks
            var amountFilteredPoints = 0;
            chunks.ForEach((chunk, k) =>
            {
               Report.Line($"filtering chunk #{k}");

               var indices = new List<int>();
               chunk.Classifications.ForEach((c, i) =>
               {
                   if ((int)c != label2filter)
                       indices.Add(i);
               });
               amountFilteredPoints += indices.Count();

               indices.ForEach(idx =>
               {
                   if (chunk.HasPositions)
                       positions.Add(chunk.Positions[idx]);

                   if (chunk.HasColors)
                       colors.Add(chunk.Colors[idx]);

                   if (chunk.HasNormals)
                       normals.Add(chunk.Normals[idx]);

                   if (chunk.HasIntensities)
                       intensities.Add(chunk.Intensities[idx]);

                   if (chunk.HasClassifications)
                       classifications.Add(chunk.Classifications[idx]);
               });
            });
            Report.Line($"filtered {amountFilteredPoints} points");

            // create new chunks of filtered data
            var maxChunkSize = 1024 * 1024;

            var posChunks = positions.Chunk(maxChunkSize).ToArray(); 
            var colChunks = colors.IsEmptyOrNull() ? null : colors.Chunk(maxChunkSize).ToArray();
            var normChunks = normals.IsEmptyOrNull() ? null : normals.Chunk(maxChunkSize).ToArray();
            var intChunks = intensities.IsEmptyOrNull() ? null : intensities.Chunk(maxChunkSize).ToArray();
            var classChunks = classifications.IsEmptyOrNull() ? null :classifications.Chunk(maxChunkSize).ToArray();

            var newChunks = posChunks.Select((pos, i) => 
            {
                Report.Line($"creating new chunk #{i}");
                var bbChunk = new Box3d(pos);
                return new Chunk(pos, colChunks?[i], normChunks?[i], intChunks?[i], classChunks?[i], bbChunk);
            });
            
            var config = ImportConfig.Default
               .WithStorage(store)
               .WithKey(key + "_filtered")
               .WithMaxChunkPointCount(maxChunkSize)
               .WithVerbose(true);

            // add point-cloud to store
            PointCloud.Chunks(newChunks, config);
            store.Dispose();
        }

        /// <summary>
        /// Import pointcloud with labels from different files.
        /// </summary>
        private static void Import(string path2data, string path2labels, string path2store,
            string key, int maxChunkSize = 1024 * 1024)
        {
            var labels = File.ReadLines(path2labels)
                .Select(l => (byte)int.Parse(l, CultureInfo.InvariantCulture));

            var data = File.ReadLines(path2data);

            var chunkedData = data.Chunk(maxChunkSize);
            var chunkedLabels = labels.Chunk(maxChunkSize);
            
            // parsing files
            var parsedChunks = chunkedData.Select( (ch, i) =>
            {
                var chunkPos = new V3d[ch.Length];
                var chunkCol = new C4b[ch.Length];

                ch.ForEach( (l,j) => 
                {
                    var s = l.Split(' ');

                    var pos = new V3d(double.Parse(s[0], CultureInfo.InvariantCulture),
                        double.Parse(s[1], CultureInfo.InvariantCulture),
                        double.Parse(s[2], CultureInfo.InvariantCulture));

                    var col = new C4b((byte)int.Parse(s[4], CultureInfo.InvariantCulture),
                        (byte)int.Parse(s[5], CultureInfo.InvariantCulture),
                        (byte)int.Parse(s[6], CultureInfo.InvariantCulture));

                    chunkPos[j] = pos;
                    chunkCol[j] = col;
                });
                return (chunkPos,chunkCol);
            });
            
            var chunks = parsedChunks.Select( (chunk, i) =>
            {
                Report.Line($"creating chunk #{i}");
                var bbChunk = new Box3d(chunk.chunkPos);
                return new Chunk(chunk.chunkPos, chunk.chunkCol, null, null, chunkedLabels.ToArray()[i], bbChunk);
            });

            //var chunk = new Chunk(positions, colors, null, null, labels, bb);

            var store = PointCloud.OpenStore(path2store);

            var config = ImportConfig.Default
               .WithStorage(store)
               .WithKey(key)
               .WithMaxChunkPointCount(maxChunkSize)
               .WithVerbose(true);

            // add point-cloud to store
            var pointset = PointCloud.Chunks(chunks, config);
            store.Dispose();
        }

        /// <summary>
        /// Computes and adds normals to pointset.
        /// </summary>
        /// <param name="k">Estimate per-point normals using k-nearest neighbours.</param>
        private static void AddNormals(string path2store, string key, int k = 16)
        {
            // compute normals
            var store = PointCloud.OpenStore(path2store);
            var pointset = store.GetPointSet(key, CancellationToken.None);
            var info = PointCloud.StoreInfo(path2store, key);

            Func<IList<V3d>, IList<V3f>> estimateNormals = (points) =>
            {
                var p = (V3d[])points;
                var n = p.EstimateNormals(k);
                return (IList<V3f>)n;
            };

            var config = ImportConfig.Default
                .WithStorage(store)
                .WithKey(key)
                .WithMaxChunkPointCount((int)info.PointCount + 1)
                .WithEstimateNormals(estimateNormals)
                .WithVerbose(true);

            var pointsetWithNormals = pointset.GenerateNormals(config);
            store.Dispose();
        }
    }
}
