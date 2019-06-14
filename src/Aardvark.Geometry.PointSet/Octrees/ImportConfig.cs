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
using System;
using System.Collections.Generic;
using System.Threading;

namespace Aardvark.Data.Points
{
    /// <summary>
    /// General info for a point cloud data file.
    /// </summary>
    public class ImportConfig
    {
        /// <summary>
        /// Default configuration.
        /// </summary>
        public static readonly ImportConfig Default = new ImportConfig();

        #region Properties

        /// <summary></summary>
        public ParseConfig ParseConfig { get; private set; } = ParseConfig.Default;
        
        /// <summary>
        /// Store imported pointcloud with this key.
        /// </summary>
        public string Key { get; private set; } = null;

        /// <summary></summary>
        public CancellationToken CancellationToken => ParseConfig.CancellationToken;

        /// <summary></summary>
        public int MaxDegreeOfParallelism => ParseConfig.MaxDegreeOfParallelism;

        /// <summary>
        /// Remove points on import with less than this distance to previous point.
        /// </summary>
        public double MinDist => ParseConfig.MinDist;
        /// <summary>
        /// Large files should be read in chunks with this maximum size.
        /// </summary>
        public int ReadBufferSizeInBytes => ParseConfig.ReadBufferSizeInBytes;

        /// <summary>Removes duplicate points in chunk after MinDist filtering and before Reproject and EstimateNormals.</summary>
        public bool DeduplicateChunks { get; private set; } = true;

        /// <summary>Normalizes point density globally using MinDist distance.</summary>
        public bool NormalizePointDensityGlobal { get; private set; } = false;

        /// <summary>
        /// Max number of points in octree cell.
        /// </summary>
        public int OctreeSplitLimit { get; private set; } = 8192;

        /// <summary></summary>
        public Action<double> ProgressCallback { get; private set; } = _ => { };

        /// <summary></summary>
        public Func<IList<V3d>, IList<V3d>> Reproject { get; private set; } = null;

        /// <summary></summary>
        public Storage Storage { get; private set; } = null;

        /// <summary></summary>
        public bool Verbose => ParseConfig.Verbose;

        /// <summary></summary>
        public int MaxChunkPointCount => ParseConfig.MaxChunkPointCount;

        #endregion

        #region Immutable updates

        private ImportConfig() { }

        /// <summary></summary>
        public ImportConfig(ImportConfig x)
        {
            Key = x.Key;
            DeduplicateChunks = x.DeduplicateChunks;
            NormalizePointDensityGlobal = x.NormalizePointDensityGlobal;
            OctreeSplitLimit = x.OctreeSplitLimit;
            ProgressCallback = x.ProgressCallback;
            ParseConfig = x.ParseConfig;
            Reproject = x.Reproject;
            Storage = x.Storage;
        }

        /// <summary></summary>
        public ImportConfig WithCancellationToken(CancellationToken x) => new ImportConfig(this) { ParseConfig = ParseConfig.WithCancellationToken(x) };

        /// <summary></summary>
        public ImportConfig WithKey(string x) => new ImportConfig(this) { Key = x };

        /// <summary></summary>
        public ImportConfig WithRandomKey() => WithKey(Guid.NewGuid().ToString());

        /// <summary></summary>
        public ImportConfig WithMaxDegreeOfParallelism(int x) => new ImportConfig(this) { ParseConfig = ParseConfig.WithMaxDegreeOfParallelism(x) };

        /// <summary></summary>
        public ImportConfig WithMinDist(double x) => new ImportConfig(this) { ParseConfig = ParseConfig.WithMinDist(x) };

        /// <summary></summary>
        public ImportConfig WithDeduplicateChunks(bool x) => new ImportConfig(this) { DeduplicateChunks = x };

        /// <summary></summary>
        public ImportConfig WithNormalizePointDensityGlobal(bool x) => new ImportConfig(this) { NormalizePointDensityGlobal = x };

        /// <summary></summary>
        public ImportConfig WithOctreeSplitLimit(int x) => new ImportConfig(this) { OctreeSplitLimit = x };

        /// <summary></summary>
        public ImportConfig WithProgressCallback(Action<double> x) => new ImportConfig(this) { ProgressCallback = x ?? throw new ArgumentNullException() };

        /// <summary></summary>
        public ImportConfig WithReadBufferSizeInBytes(int x) => new ImportConfig(this) { ParseConfig = ParseConfig.WithReadBufferSizeInBytes(x) };

        /// <summary></summary>
        public ImportConfig WithMaxChunkPointCount(int x) => new ImportConfig(this) { ParseConfig = ParseConfig.WithMaxChunkPointCount(Math.Max(x, 1)) };

        /// <summary></summary>
        public ImportConfig WithReproject(Func<IList<V3d>, IList<V3d>> x) => new ImportConfig(this) { Reproject = x };

        /// <summary></summary>
        public ImportConfig WithVerbose(bool x) => new ImportConfig(this) { ParseConfig = ParseConfig.WithVerbose(x) };

        /// <summary></summary>
        public ImportConfig WithStorage(Storage x) => new ImportConfig(this) { Storage = x };

        #endregion
    }
}