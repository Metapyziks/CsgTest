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

        private int _planeCount;
        private int _nodeCount;

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
        }

        private ushort AddPlane(BspPlane plane)
        {
            // TODO: merge planes

            Helpers.EnsureCapacity(ref _planes, _planeCount + 1);

            _planes[_planeCount] = plane;

            return (ushort)_planeCount++;
        }

        private ushort AddNode(ushort planeIndex, ushort parentIndex, ushort negativeIndex, ushort positiveIndex)
        {
            Helpers.EnsureCapacity(ref _nodes, _nodeCount + 1);

            _nodes[_nodeCount] = new BspNode(planeIndex, parentIndex, negativeIndex, positiveIndex);

            return (ushort)_nodeCount++;
        }

        public void Transform(float4x4 matrix)
        {
            var transInvMatrix = math.transpose(math.inverse(matrix));

            for (var i = 0; i < _planeCount; ++i)
            {
                _planes[i] = _planes[i].Transform(matrix, transInvMatrix);
            }
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
        }
    }
}
