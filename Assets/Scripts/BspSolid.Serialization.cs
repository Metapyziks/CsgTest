using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Converters;
using UnityEngine;

namespace CsgTest
{
    partial class BspSolid
    {
        public enum JsonNodeType
        {
            Branch,
            Out,
            In
        }

        public class JsonSolid
        {
            [JsonProperty("planes")]
            public JsonPlane[] Planes { get; set; }

            [JsonProperty("root")]
            public JsonNode Root { get; set; }
        }

        public struct JsonPlane
        {
            [JsonProperty("normalX")]
            public float NormalX { get; set; }

            [JsonProperty("normalY")]
            public float NormalY { get; set; }

            [JsonProperty("normalZ")]
            public float NormalZ { get; set; }

            [JsonProperty("offset")]
            public float Offset { get; set; }
        }

        public class JsonNode
        {
            [JsonProperty("nodeId")] public int? NodeIndex { get; set; }

            [JsonProperty("planeId")] public int? PlaneIndex { get; set; }
            [JsonProperty("type")] public JsonNodeType Type { get; set; }
            [JsonProperty("childCount")] public int ChildCount { get; set; }

            [JsonProperty("negative")] public JsonNode Negative { get; set; }
            [JsonProperty("positive")] public JsonNode Positive { get; set; }
        }

        private static readonly JsonSerializerSettings _sSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Converters =
            {
                new StringEnumConverter()
            }
        };

        public string ToJson(Formatting formatting = Formatting.None)
        {
            var root = new JsonSolid
            {
                Planes = new JsonPlane[_planeCount],
                Root = ToJsonNode(_nodeCount == 0 ? NodeIndex.Out : (NodeIndex) 0)
            };

            for (var i = 0; i < _planeCount; ++i)
            {
                var plane = _planes[i];

                root.Planes[i] = new JsonPlane
                {
                    NormalX = plane.Normal.x,
                    NormalY = plane.Normal.y,
                    NormalZ = plane.Normal.z,
                    Offset = plane.Offset
                };
            }

            return JsonConvert.SerializeObject(root, formatting, _sSerializerSettings);
        }

        public void FromJson(string value)
        {
            Clear();

            var jsonSolid = JsonConvert.DeserializeObject<JsonSolid>(value, _sSerializerSettings);

            if (jsonSolid == null) return;

            Helpers.EnsureCapacity(ref _planes, jsonSolid.Planes.Length);

            for (var i = 0; i < jsonSolid.Planes.Length; ++i)
            {
                var plane = jsonSolid.Planes[i];
                var normal = math.normalizesafe(new float3(plane.NormalX, plane.NormalY, plane.NormalZ));

                _planes[i] = new BspPlane(normal, plane.Offset);
            }

            _planeCount = jsonSolid.Planes.Length;

            AddJsonNode(jsonSolid.Root);
            Reduce();
        }

        private JsonNode ToJsonNode(NodeIndex index)
        {
            if (index.IsLeaf)
            {
                return new JsonNode
                {
                    Type = index.IsIn ? JsonNodeType.In : JsonNodeType.Out
                };
            }

            var node = _nodes[index];

            return new JsonNode
            {
                NodeIndex = index,
                PlaneIndex = node.PlaneIndex,
                Type = JsonNodeType.Branch,
                ChildCount = node.ChildCount,

                Negative = ToJsonNode(node.NegativeIndex),
                Positive = ToJsonNode(node.PositiveIndex)
            };
        }

        private NodeIndex AddJsonNode(JsonNode jsonNode)
        {
            switch (jsonNode.Type)
            {
                case JsonNodeType.Out:
                    return NodeIndex.Out;

                case JsonNodeType.In:
                    return NodeIndex.In;
            }

            var index = AddNode((ushort) jsonNode.PlaneIndex, NodeIndex.Out, NodeIndex.Out);

            var node = _nodes[index];

            node = node.WithNegativeIndex(AddJsonNode(jsonNode.Negative));
            node = node.WithPositiveIndex(AddJsonNode(jsonNode.Positive));

            if (node.NegativeIndex == node.PositiveIndex)
            {
                return node.NegativeIndex;
            }

            _nodes[index] = node;

            return index;
        }
    }
}
