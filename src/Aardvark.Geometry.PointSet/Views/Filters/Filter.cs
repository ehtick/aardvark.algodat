﻿using System;
using System.Text.Json.Nodes;

namespace Aardvark.Geometry.Points
{
    /// <summary>
    /// </summary>
    public static class Filter
    {
        /// <summary></summary>
        public static IFilter Deserialize(string s) => Deserialize(JsonNode.Parse(s));
        
        /// <summary></summary>
        public static IFilter Deserialize(JsonNode json)
        {
            var type = (string)json["Type"];

            return type switch
            {
                FilterInsideBox3d.Type => FilterInsideBox3d.Deserialize(json),
                FilterOutsideBox3d.Type => FilterOutsideBox3d.Deserialize(json),
                FilterOr.Type => FilterOr.Deserialize(json),
                FilterAnd.Type => FilterAnd.Deserialize(json),
                FilterIntensity.Type => FilterIntensity.Deserialize(json),
                FilterNormalDirection.Type => FilterNormalDirection.Deserialize(json),
                FilterInsideConvexHull3d.Type => FilterInsideConvexHull3d.Deserialize(json),
                FilterClassification.Type => FilterClassification.Deserialize(json),
                FilterInsideSphere3d.Type => FilterInsideSphere3d.Deserialize(json),
                _ => throw new NotImplementedException($"Unknown filter type: '{type}'"),
            };
        }
    }
}
