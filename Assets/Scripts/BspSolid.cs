using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Codice.CM.Client.Differences;
using Unity.Collections;
using UnityEngine;

using Unity.Mathematics;

namespace CsgTest
{
    public partial class BspSolid : IDisposable
    {
        private NativeArray<BspPlane> _planes;
        private NativeArray<BspNode> _nodes;

        private readonly Dictionary<BspPlane, ushort> _planeDict = new Dictionary<BspPlane, ushort>();

        private int _planeCount;
        private int _nodeCount;

        private bool _verticesValid;
        private int _vertexCount;
        private NativeArray<float3> _vertices;

        private void UpdateVertices()
        {
            if (_verticesValid) return;

            var meshGen = GetMeshGenerator();

            meshGen.Write(this);

            _vertexCount = meshGen.VertexCount;

            Helpers.EnsureCapacity(ref _vertices, _vertexCount);
            meshGen.CopyVertices(_vertices);

            _verticesValid = true;
        }

        public static BspSolid CreateBox(float3 center, float3 size)
        {
            var mesh = new BspSolid
            {
                _planes = new NativeArray<BspPlane>(6, Allocator.Persistent),
                _nodes = new NativeArray<BspNode>(6, Allocator.Persistent)
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
                _planes = new NativeArray<BspPlane>(12, Allocator.Persistent),
                _nodes = new NativeArray<BspNode>(12, Allocator.Persistent)
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

            _planeDict.Clear();

            _verticesValid = false;
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

        private ushort AddNode(ushort planeIndex, ushort parentIndex, ushort negativeIndex, ushort positiveIndex)
        {
            if (_nodeCount >= ushort.MaxValue - 1)
            {
                throw new Exception("Too many nodes!");
            }

            Helpers.EnsureCapacity(ref _nodes, _nodeCount + 1);

            _nodes[_nodeCount] = new BspNode(planeIndex, parentIndex, negativeIndex, positiveIndex);
            _verticesValid = false;

            return (ushort)_nodeCount++;
        }

        public void Transform(float4x4 matrix)
        {
            var transInvMatrix = math.transpose(math.inverse(matrix));

            for (var i = 0; i < _planeCount; ++i)
            {
                _planes[i] = _planes[i].Transform(matrix, transInvMatrix);
            }

            _verticesValid = false;
        }

        public void Dispose()
        {
            if (_planes.IsCreated)
            {
                _planes.Dispose();
            }

            _planeCount = 0;

            if (_nodes.IsCreated)
            {
                _nodes.Dispose();
            }

            _nodeCount = 0;

            if (_vertices.IsCreated)
            {
                _vertices.Dispose();
            }

            _vertexCount = 0;
            _verticesValid = false;
        }

        public override string ToString()
        {
            if (_nodeCount == 0)
            {
                return "Empty";
            }

            var writer = new StringWriter();

            WriteNode(writer, 0, 0);

            return writer.ToString();
        }

        private void WriteIndentation(StringWriter writer, int depth)
        {
            for (var i = 0; i < depth; ++i)
            {
                writer.Write("  ");
            }
        }

        private void WriteNode(StringWriter writer, ushort index, int depth)
        {
            var node = _nodes[index];

            var plane = _planes[node.PlaneIndex];
            writer.WriteLine($"NODE {index}: PLANE {node.PlaneIndex} {plane}");

            WriteIndentation(writer, depth);
            writer.Write("- ");

            switch (node.NegativeIndex)
            {
                case BspNode.OutIndex:
                    writer.WriteLine("OUT");
                    break;
                case BspNode.InIndex:
                    writer.WriteLine("IN");
                    break;
                default:
                    WriteNode(writer, node.NegativeIndex, depth + 1);
                    break;
            }

            WriteIndentation(writer, depth);
            writer.Write("+ ");

            switch (node.PositiveIndex)
            {
                case BspNode.OutIndex:
                    writer.WriteLine("OUT");
                    break;
                case BspNode.InIndex:
                    writer.WriteLine("IN");
                    break;
                default:
                    WriteNode(writer, node.PositiveIndex, depth + 1);
                    break;
            }
        }

        public void LogInfo()
        {
            Debug.Log($"Nodes: {_nodeCount}, Planes: {_planeCount}");
        }
    }
}
