using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgTest
{
    public enum CsgOperator
    {
        Or,
        And,
        Xor,
        Subtract,
        InvSubtract
    }

    partial class BspSolid
    {
        public const float Epsilon = 0.000001f;

        public void Cut(BspPlane plane)
        {
            var (planeIndex, flipped) = AddPlane(plane);
            var negative = flipped ? NodeIndex.In : NodeIndex.Out;
            var positive = flipped ? NodeIndex.Out : NodeIndex.In;

            if (_nodeCount == 0)
            {
                _rootIndex = AddNode(planeIndex, negative, positive);
                return;
            }

            var oldNodeCount = _nodeCount;

            for (ushort i = 0; i < oldNodeCount; ++i)
            {
                var node = _nodes[i];

                if (node.NegativeIndex.IsIn)
                {
                    node = node.WithNegativeIndex(AddNode(planeIndex, negative, positive));
                }

                if (node.PositiveIndex.IsIn)
                {
                    node = node.WithPositiveIndex(AddNode(planeIndex, negative, positive));
                }

                _nodes[i] = node;
            }

            Reduce();
        }

        private static CsgOperator InvertOperator(CsgOperator op)
        {
            switch (op)
            {
                case CsgOperator.Subtract:
                    return CsgOperator.InvSubtract;

                case CsgOperator.InvSubtract:
                    return CsgOperator.Subtract;

                default:
                    return op;
            }
        }

        private static NodeIndex ApplyOperator(CsgOperator op, NodeIndex lhs, NodeIndex rhs)
        {
            var lhsIn = lhs.IsIn;
            var rhsIn = rhs.IsIn;

            switch (op)
            {
                case CsgOperator.Or:
                    return lhsIn | rhsIn ? NodeIndex.In : NodeIndex.Out;

                case CsgOperator.And:
                    return lhsIn & rhsIn ? NodeIndex.In : NodeIndex.Out;

                case CsgOperator.Xor:
                    return lhsIn ^ rhsIn ? NodeIndex.In : NodeIndex.Out;

                case CsgOperator.Subtract:
                    return lhsIn & !rhsIn ? NodeIndex.In : NodeIndex.Out;

                case CsgOperator.InvSubtract:
                    return !lhsIn & rhsIn ? NodeIndex.In : NodeIndex.Out;

                default:
                    return NodeIndex.Out;
            }
        }

        private static BspPlane[] _sTempPlanes;

        public void Merge(BspSolid solid, CsgOperator op, float4x4? transform = null)
        {
            if (solid == this)
            {
                throw new NotImplementedException();
            }

            var leafPlanes = _sMergePlanes ?? (_sMergePlanes = new Stack<BspPlane>());

            leafPlanes.Clear();

            BspPlane[] planes;

            if (transform == null)
            {
                planes = solid._planes;
            }
            else
            {
                Helpers.EnsureCapacity(ref _sTempPlanes, solid._planeCount);

                planes = _sTempPlanes;

                for (var i = 0; i < solid._planeCount; ++i)
                {
                    planes[i] = solid._planes[i].Transform(transform.Value);
                }
            }

            var lhs = new Solid(_nodes, _planes);
            var rhs = new Solid(solid._nodes, planes);

            var lhsRoot = _rootIndex;
            var rhsRoot = solid._rootIndex;

            _rootIndex = Merge(leafPlanes, lhs, lhsRoot, rhs, rhsRoot, op, false);
            Reduce();
        }

        [ThreadStatic] private static Stack<BspPlane> _sMergePlanes;

        private readonly struct Solid
        {
            public readonly BspNode[] Nodes;
            public readonly BspPlane[] Planes;

            public Solid(BspNode[] nodes, BspPlane[] planes)
            {
                Nodes = nodes;
                Planes = planes;
            }
        }

        private NodeIndex Merge(Stack<BspPlane> leafPlanes, Solid lhs, NodeIndex lhsIndex, Solid rhs, NodeIndex rhsIndex, CsgOperator op, bool hack)
        {
            if (lhsIndex.IsLeaf && rhsIndex.IsLeaf)
            {
                return ApplyOperator(op, lhsIndex, rhsIndex);
            }

            if (lhsIndex.IsLeaf || !rhsIndex.IsLeaf && lhs.Nodes[lhsIndex].ChildCount < rhs.Nodes[rhsIndex].ChildCount)
            {
                (lhs, rhs) = (rhs, lhs);
                (lhsIndex, rhsIndex) = (rhsIndex, lhsIndex);
                op = InvertOperator(op);
            }

            if (rhsIndex.IsLeaf && ApplyOperator(op, NodeIndex.In, rhsIndex) == ApplyOperator(op, NodeIndex.Out, rhsIndex))
            {
                return ApplyOperator(op, NodeIndex.In, rhsIndex);
            }

            var lhsNode = lhs.Nodes[lhsIndex];
            var plane = lhs.Planes[lhsNode.PlaneIndex];

            var (excludesNegative, excludesPositive) = GetPlaneExclusions(leafPlanes, plane);

            if (excludesNegative && excludesPositive)
            {
                throw new Exception();
            }

            if (excludesNegative)
            {
                return Merge(leafPlanes, lhs, lhsNode.PositiveIndex, rhs, rhsIndex, op, hack);
            }

            if (excludesPositive)
            {
                return Merge(leafPlanes, lhs, lhsNode.NegativeIndex, rhs, rhsIndex, op, hack);
            }
            
            leafPlanes.Push(-plane);
            lhsIndex = Merge(leafPlanes, lhs, lhsNode.NegativeIndex, rhs, rhsIndex, op, hack);
            leafPlanes.Pop();

            leafPlanes.Push(plane);
            rhsIndex = Merge(leafPlanes, lhs, lhsNode.PositiveIndex, rhs, rhsIndex, op, hack);
            leafPlanes.Pop();

            if (lhsIndex == rhsIndex)
            {
                return lhsIndex;
            }

            var planeInfo = AddPlane(plane);

            if (hack) return AddNode(planeInfo, lhsIndex, rhsIndex);

            var mergedNode = new BspNode(0, lhsIndex, rhsIndex, 0)
                .WithPlaneIndex(planeInfo);

            var meshGen = GetMeshGenerator();

            meshGen.Init(this);
            meshGen.StatsOnly = true;

            var stats = meshGen.TriangulateFace(leafPlanes, mergedNode);

            if (stats.FaceCount == 0)
            {
                var lhsCut = AddNode(planeInfo, NodeIndex.In, NodeIndex.Out);
                var rhsCut = AddNode(planeInfo, NodeIndex.Out, NodeIndex.In);

                if (planeInfo.flipped)
                {
                    (lhsCut, rhsCut) = (rhsCut, lhsCut);
                }

                var solid = new Solid(_nodes, _planes);
                lhsIndex = Merge(leafPlanes, solid, lhsIndex, solid, lhsCut, CsgOperator.And, false);
                solid = new Solid(_nodes, _planes);
                rhsIndex = Merge(leafPlanes, solid, rhsIndex, solid, rhsCut, CsgOperator.And, false);

                solid = new Solid(_nodes, _planes);
                return Merge(leafPlanes, solid, lhsIndex, solid, rhsIndex, CsgOperator.Or, true);
            }

            return AddNode(planeInfo, lhsIndex, rhsIndex);
        }
        
        [ThreadStatic]
        private static List<FaceCut> _sFaceCuts;

        private float3 GetAnyPoint(List<FaceCut> cuts, in (float3 origin, float3 tu, float3 tv) basis)
        {
            if (cuts.Count == 0)
            {
                return basis.origin;
            }

            if (cuts.Count == 1)
            {
                return cuts[0].GetPoint(basis);
            }

            var point = float3.zero;

            foreach (var cut in cuts)
            {
                point += cut.GetPoint(basis);
            }

            return point / cuts.Count;
        }

        private (bool ExcludesNegative, bool ExcludesPositive) GetPlaneExclusions(Stack<BspPlane> planes, BspPlane plane)
        {
            var faceCuts = _sFaceCuts ?? (_sFaceCuts = new List<FaceCut>());

            faceCuts.Clear();

            var basis = plane.GetBasis();

            BspPlane excludingPlane = default;
            var excluded = false;

            foreach (var otherPlane in planes)
            {
                var cut = Helpers.GetFaceCut(plane, otherPlane, basis);

                if (cut.ExcludesAll)
                {
                    var aligned = math.dot(otherPlane.Normal, plane.Normal) > 0f;
                    return (aligned, !aligned);
                }

                var (excludesNegative, excludesPositive) = faceCuts.GetNewFaceCutExclusions(cut);

                if (excludesPositive)
                {
                    excluded = true;
                    excludingPlane = otherPlane;
                    break;
                }

                if (excludesNegative) continue;

                faceCuts.AddFaceCut(cut);
            }

            if (!excluded)
            {
                return (false, false);
            }

            faceCuts.Clear();

            basis = excludingPlane.GetBasis();

            foreach (var otherPlane in planes)
            {
                if (otherPlane.ApproxEquals(excludingPlane))
                {
                    continue;
                }

                var cut = Helpers.GetFaceCut(excludingPlane, otherPlane, basis);
                var (excludesNegative, excludesPositive) = faceCuts.GetNewFaceCutExclusions(cut);

                if (excludesPositive || excludesNegative) continue;

                faceCuts.AddFaceCut(cut);
            }

            var insidePoint = GetAnyPoint(faceCuts, basis);

            return math.dot(insidePoint, plane.Normal) > plane.Offset ? (true, false) : (false, true);
        }
    }
}
