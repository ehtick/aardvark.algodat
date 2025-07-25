﻿/*
    Copyright (C) 2006-2025. Aardvark Platform Team. http://github.com/aardvark-platform.
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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aardvark.Geometry.Points;

public static class BoxHullThings
{
    public static Box3d IntersectionBounds(this Hull3d hull, Box3d box)
    {
        if (box.IsInvalid) return box;

        var bh = new Hull3d(box);
        var pp = new Plane3d[6 + hull.PlaneCount];
        bh.PlaneArray.CopyTo(pp, 0);
        hull.PlaneArray.CopyTo(pp, 6);
        var h = new Hull3d(pp);
        return new Box3d(h.ComputeCorners());
    }

}

/// <summary></summary>
/// <remarks></remarks>
public class FilterInsideSphere3d(Sphere3d sphere) : ISpatialFilter
{
    /// <summary></summary>
    public const string Type = "FilterInsideSphere3d";

    public Sphere3d Sphere { get; } = sphere;

    private readonly double m_radiusSquared = sphere.RadiusSquared;

    public bool Contains(V3d pt)
    {
        return Vec.DistanceSquared(Sphere.Center, pt) <= m_radiusSquared;
    }

    /// <summary></summary>
    public HashSet<int> FilterPoints(IPointCloudNode node, HashSet<int>? selected = null)
    {
        if (selected != null)
        {
            var c = node.Center;
            var ps = node.Positions.Value;
            return [.. selected.Where(i => Contains(c + (V3d)ps[i]))];
        }
        else
        {
            var c = node.Center;
            var ps = node.Positions.Value;
            var result = new HashSet<int>();
            for (var i = 0; i < ps.Length; i++)
            {
                if (Contains(c + (V3d)ps[i])) result.Add(i);
            }
            return result;
        }
    }

    /// <summary></summary>
    public bool IsFullyInside(Box3d box)
    {
        return box.ComputeCorners().TrueForAll(Contains);
    }
    /// <summary></summary>
    public bool IsFullyInside(IPointCloudNode node)
    {
        return IsFullyInside(node.BoundingBoxExactGlobal);
    }

    /// <summary></summary>
    public bool IsFullyOutside(Box3d box)
    {
        return !box.Intersects(Sphere);
    }

    /// <summary></summary>
    public bool IsFullyOutside(IPointCloudNode node)
    {
        return IsFullyOutside(node.BoundingBoxExactGlobal);
    }

    /// <summary></summary>
    public JsonNode Serialize() => JsonSerializer.SerializeToNode(new { Type, Sphere = Sphere.ToString() })!;

    /// <summary></summary>
    public static FilterInsideSphere3d Deserialize(JsonNode json) => new(Sphere3d.Parse((string)json["Sphere"]!));

    public Box3d Clip(Box3d box) => Sphere.BoundingBox3d.Intersection(box);

    public bool Equals(IFilter other)
        => other is FilterInsideSphere3d x && Sphere == x.Sphere;
}
