using System;
using System.Collections.Generic;

namespace CsgTest
{
    public readonly struct NodeIndex : IEquatable<NodeIndex>
    {
        public static implicit operator NodeIndex(int value)
        {
            return new NodeIndex(value);
        }

        public static implicit operator int(NodeIndex index)
        {
            if (index.IsLeaf)
            {
                throw new Exception("Attempted to get the index of a leaf node.");
            }

            return index.Value;
        }

        public static bool operator ==(NodeIndex lhs, NodeIndex rhs)
        {
            return lhs.Value == rhs.Value;
        }

        public static bool operator !=(NodeIndex lhs, NodeIndex rhs)
        {
            return lhs.Value != rhs.Value;
        }

        private const ushort InValue = ushort.MaxValue - 1;
        private const ushort OutValue = ushort.MaxValue;

        public static NodeIndex In => new NodeIndex(InValue);
        public static NodeIndex Out => new NodeIndex(OutValue);

        public readonly ushort Value;

        public bool IsLeaf => Value >= InValue;
        public bool IsIn => Value == InValue;
        public bool IsOut => Value == OutValue;

        private NodeIndex(ushort value)
        {
            Value = value;
        }

        public NodeIndex(int value)
        {
            if (value >= InValue)
            {
                throw new Exception("Node index value out of range.");
            }

            Value = (ushort)value;
        }

        public bool Equals(NodeIndex other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is NodeIndex other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return IsOut ? "OUT" : IsIn ? "IN" : $"N{Value}";
        }
    }

    public readonly struct BspNode
    {
        public static BspNode operator -(BspNode node)
        {
            return node.WithNegativeIndex(node.PositiveIndex).WithPositiveIndex(node.NegativeIndex);
        }

        public readonly ushort PlaneIndex;
        public readonly NodeIndex NegativeIndex;
        public readonly NodeIndex PositiveIndex;

        public BspNode(ushort planeIndex, NodeIndex negativeIndex, NodeIndex positiveIndex) =>
            (PlaneIndex, NegativeIndex, PositiveIndex) = (planeIndex, negativeIndex, positiveIndex);

        public BspNode WithNegativeIndex(NodeIndex value)
        {
            return new BspNode(PlaneIndex, value, PositiveIndex);
        }

        public BspNode WithPositiveIndex(NodeIndex value)
        {
            return new BspNode(PlaneIndex, NegativeIndex, value);
        }

        public BspNode WithPlaneIndex(ushort value)
        {
            return new BspNode(value, NegativeIndex, PositiveIndex);
        }

        public BspNode Remapped(Dictionary<ushort, ushort> nodeRemapDict)
        {
            return new BspNode(PlaneIndex,
                NegativeIndex.IsLeaf ? NegativeIndex : nodeRemapDict[NegativeIndex.Value],
                PositiveIndex.IsLeaf ? PositiveIndex : nodeRemapDict[PositiveIndex.Value]);
        }

        public override string ToString()
        {
            return $"{{ Plane: P{PlaneIndex}, Negative: {NegativeIndex}, Positive: {PositiveIndex} }}";
        }
    }
}
