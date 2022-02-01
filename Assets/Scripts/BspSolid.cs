using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Unity.Collections;
using UnityEngine;

using Unity.Mathematics;

namespace CsgTest
{
    public partial class BspSolid
    {
        private BspPlane[] _planes;
        private BspNode[] _nodes;
        private NodeIndex _rootIndex = NodeIndex.Out;

        private readonly Dictionary<BspPlane, ushort> _planeDict = new Dictionary<BspPlane, ushort>();

        private int _planeCount;
        private int _nodeCount;

        public static BspSolid CreateBox(float3 center, float3 size)
        {
            var mesh = new BspSolid
            {
                _planes = new BspPlane[6],
                _nodes = new BspNode[6]
            };

            var min = center - size * 0.5f;
            var max = center + size * 0.5f;

            mesh.Cut(new BspPlane(new float3(1f, 0f, 0f), min));
            mesh.Cut(new BspPlane(new float3(-1f, 0f, 0f), max));
            mesh.Cut(new BspPlane(new float3(0f, 1f, 0f), min));
            mesh.Cut(new BspPlane(new float3(0f, -1f, 0f), max));
            mesh.Cut(new BspPlane(new float3(0f, 0f, 1f), min));
            mesh.Cut(new BspPlane(new float3(0f, 0f, -1f), max));

            return mesh;
        }

        public static BspSolid CreateDodecahedron(float3 center, float radius)
        {
            var mesh = new BspSolid
            {
                _planes = new BspPlane[12],
                _nodes = new BspNode[12]
            };

            mesh.Cut(new BspPlane(new float3(0f, 1f, 0f), -radius));
            mesh.Cut(new BspPlane(new float3(0f, -1f, 0f), -radius));

            var rot = Quaternion.AngleAxis(60f, Vector3.right);

            for (var i = 0; i < 5; ++i)
            {
                mesh.Cut(new BspPlane(rot * Vector3.down, -radius));
                mesh.Cut(new BspPlane(rot * Vector3.up, -radius));

                rot = Quaternion.AngleAxis(72f, Vector3.up) * rot;
            }

            return mesh;
        }

        public void Clear()
        {
            _nodeCount = 0;
            _planeCount = 0;
            _rootIndex = NodeIndex.Out;

            _planeDict.Clear();
        }

        private (ushort Index, bool flipped) AddPlane(BspPlane plane)
        {
            if (_planeDict.TryGetValue(plane, out var index))
            {
                return (index, false);
            }

            if (_planeDict.TryGetValue(-plane, out index))
            {
                return (index, true);
            }

            if (_planeCount >= ushort.MaxValue - 1)
            {
                throw new Exception("Too many planes!");
            }

            Helpers.EnsureCapacity(ref _planes, _planeCount + 1);

            index = (ushort)_planeCount++;
            _planes[index] = plane;

            _planeDict.Add(plane, index);

            return (index, false);
        }

        private NodeIndex AddNode(ushort planeIndex, NodeIndex negativeIndex, NodeIndex positiveIndex)
        {
            Helpers.EnsureCapacity(ref _nodes, _nodeCount + 1);

            _nodes[_nodeCount] = new BspNode(planeIndex, negativeIndex, positiveIndex, 0);

            return _nodeCount++;
        }

        private NodeIndex AddNode((ushort Index, bool Flipped) plane, NodeIndex negativeIndex, NodeIndex positiveIndex)
        {
            Helpers.EnsureCapacity(ref _nodes, _nodeCount + 1);

            _nodes[_nodeCount] = new BspNode(plane.Index,
                plane.Flipped ? positiveIndex : negativeIndex,
                plane.Flipped ? negativeIndex : positiveIndex, 0);

            return _nodeCount++;
        }

        public void Transform(float4x4 matrix)
        {
            for (var i = 0; i < _planeCount; ++i)
            {
                _planes[i] = _planes[i].Transform(matrix);
            }
        }

        public override string ToString()
        {
            return ToJson(Formatting.Indented);
        }

        public void LogInfo()
        {
            Debug.Log($"Nodes: {_nodeCount}, Planes: {_planeCount}");
        }
    }
}
