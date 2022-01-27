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

                var normalTransform = math.transpose(math.inverse(transform.Value));

                for (var i = 0; i < solid._planeCount; ++i)
                {
                    planes[i] = solid._planes[i].Transform(transform.Value, normalTransform);
                }
            }

            var lhs = new Solid(true, _nodes, _planes);
            var rhs = new Solid(false, solid._nodes, planes);

            var lhsRoot = _rootIndex;
            var rhsRoot = solid._rootIndex;

            _rootIndex = Merge(leafPlanes, lhs, lhsRoot, rhs, rhsRoot, op);
            Reduce();
        }

        [ThreadStatic] private static Stack<BspPlane> _sMergePlanes;

        private readonly struct Solid
        {
            public readonly bool IsDestination;
            public readonly BspNode[] Nodes;
            public readonly BspPlane[] Planes;

            public Solid(bool isDestination, BspNode[] nodes, BspPlane[] planes)
            {
                IsDestination = isDestination;
                Nodes = nodes;
                Planes = planes;
            }
        }

        private NodeIndex Merge(Stack<BspPlane> leafPlanes, Solid lhs, NodeIndex lhsIndex, Solid rhs, NodeIndex rhsIndex, CsgOperator op)
        {
            if (lhsIndex.IsLeaf && rhsIndex.IsLeaf)
            {
                return ApplyOperator(op, lhsIndex, rhsIndex);
            }

            if (lhsIndex.IsLeaf)
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
                return Merge(leafPlanes, lhs, lhsNode.PositiveIndex, rhs, rhsIndex, op);
            }

            if (excludesPositive)
            {
                return Merge(leafPlanes, lhs, lhsNode.NegativeIndex, rhs, rhsIndex, op);
            }

            var nodeIndex = lhsIndex;
            
            if (!lhs.IsDestination)
            {
                var (planeIndex, flipped) = AddPlane(plane);

                nodeIndex = AddNode(planeIndex, NodeIndex.Out, NodeIndex.Out);

                lhsNode = lhsNode.WithPlaneIndex(planeIndex);

                if (flipped)
                {
                    lhsNode = -lhsNode;
                }
            }

            leafPlanes.Push(-plane);
            lhsNode = lhsNode.WithNegativeIndex(Merge(leafPlanes, lhs, lhsNode.NegativeIndex, rhs, rhsIndex, op));
            leafPlanes.Pop();

            leafPlanes.Push(plane);
            lhsNode = lhsNode.WithPositiveIndex(Merge(leafPlanes, lhs, lhsNode.PositiveIndex, rhs, rhsIndex, op));
            leafPlanes.Pop();

            if (lhsNode.NegativeIndex == lhsNode.PositiveIndex)
            {
                return lhsNode.NegativeIndex;
            }

            _nodes[nodeIndex] = lhsNode;
            return nodeIndex;
        }
        
        [ThreadStatic]
        private static List<FaceCut> _sFaceCuts;

        private float3 GetAnyPoint(List<FaceCut> cuts, float3 origin, float3 tu, float3 tv)
        {
            if (cuts.Count == 0)
            {
                return origin;
            }

            if (cuts.Count == 1)
            {
                return cuts[0].GetPoint(origin, tu, tv);
            }

            var point = float3.zero;

            foreach (var cut in cuts)
            {
                point += cut.GetPoint(origin, tu, tv);
            }

            return point / cuts.Count;
        }

        private (bool ExcludesNegative, bool ExcludesPositive) GetPlaneExclusions(Stack<BspPlane> planes, BspPlane plane)
        {
            var faceCuts = _sFaceCuts ?? (_sFaceCuts = new List<FaceCut>());

            faceCuts.Clear();

            var (origin, tu, tv) = plane.GetBasis();

            BspPlane excludingPlane = default;
            var excluded = false;

            foreach (var otherPlane in planes)
            {
                var cut = Helpers.GetFaceCut(plane, otherPlane, origin, tu, tv);

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

            (origin, tu, tv) = excludingPlane.GetBasis();

            foreach (var otherPlane in planes)
            {
                if (otherPlane.Equals(excludingPlane))
                {
                    continue;
                }

                var cut = Helpers.GetFaceCut(excludingPlane, otherPlane, origin, tu, tv);
                var (excludesNegative, excludesPositive) = faceCuts.GetNewFaceCutExclusions(cut);

                if (excludesPositive || excludesNegative) continue;

                faceCuts.AddFaceCut(cut);
            }

            var insidePoint = GetAnyPoint(faceCuts, origin, tu, tv);

            return math.dot(insidePoint, plane.Normal) > plane.Offset ? (true, false) : (false, true);
        }
    }
}
