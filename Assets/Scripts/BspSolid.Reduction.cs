using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;

namespace CsgTest
{
    partial class BspSolid
    {
        [ThreadStatic] private static Dictionary<ushort, ushort> _sNodeRemapDict;

        /// <summary>
        /// Make sure only used nodes and planes are in the respective lists.
        /// </summary>
        public void Reduce()
        {
            if (_nodeCount == 0) return;

            // First pass, remove disconnected nodes

            var nodeRemapDict = _sNodeRemapDict ?? (_sNodeRemapDict = new Dictionary<ushort, ushort>());

            nodeRemapDict.Clear();

            var changed = DiscoverNodes(0, nodeRemapDict);

            if (!changed)
            {
                _nodeCount = nodeRemapDict.Count;
                return;
            }

            var oldNodes = new NativeArray<BspNode>(_nodeCount, Allocator.Temp);

            try
            {
                NativeArray<BspNode>.Copy(_nodes, 0, oldNodes, 0, _nodeCount);

                for (ushort oldIndex = 0; oldIndex < _nodeCount; ++oldIndex)
                {
                    if (!nodeRemapDict.TryGetValue(oldIndex, out var newIndex)) continue;
                    _nodes[newIndex] = oldNodes[oldIndex].Remapped(nodeRemapDict);
                }

                _nodeCount = nodeRemapDict.Count;
            }
            finally
            {
                oldNodes.Dispose();
            }
        }

        private bool DiscoverNodes(ushort nodeIndex, Dictionary<ushort, ushort> nodeRemapDict)
        {
            var changed = nodeIndex != nodeRemapDict.Count;

            nodeRemapDict.Add(nodeIndex, (ushort) nodeRemapDict.Count);

            var node = _nodes[nodeIndex];

            if (!BspNode.IsLeafIndex(node.NegativeIndex))
            {
                changed |= DiscoverNodes(node.NegativeIndex, nodeRemapDict);
            }

            if (!BspNode.IsLeafIndex(node.PositiveIndex))
            {
                changed |= DiscoverNodes(node.PositiveIndex, nodeRemapDict);
            }

            return changed;
        }
    }
}
