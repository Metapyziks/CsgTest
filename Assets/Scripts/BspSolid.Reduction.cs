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

            var nodeRemapDict = _sNodeRemapDict ?? (_sNodeRemapDict = new Dictionary<ushort, ushort>());

            nodeRemapDict.Clear();

            var changed = DiscoverNodes(0, nodeRemapDict);

            if (!changed)
            {
                _nodeCount = nodeRemapDict.Count;
                return;
            }

            var oldNodes = new NativeArray<BspNode>(_nodeCount, Allocator.Temp);

            _planeDict.Clear();

            try
            {
                NativeArray<BspNode>.Copy(_nodes, 0, oldNodes, 0, _nodeCount);

                for (ushort oldIndex = 0; oldIndex < _nodeCount; ++oldIndex)
                {
                    if (!nodeRemapDict.TryGetValue(oldIndex, out var newIndex)) continue;

                    var oldNode = oldNodes[oldIndex];
                    var plane = _planes[oldNode.PlaneIndex];

                    if (_planeDict.TryGetValue(plane, out var planeIndex))
                    {

                    }
                    else if (_planeDict.TryGetValue(-plane, out planeIndex))
                    {
                        oldNode = -oldNode;
                    }
                    else
                    {
                        planeIndex = (ushort) _planeDict.Count;
                        _planeDict.Add(plane, planeIndex);
                    }

                    oldNode = oldNode.WithPlaneIndex(planeIndex);

                    _nodes[newIndex] = oldNode.Remapped(nodeRemapDict);
                }

                _nodeCount = nodeRemapDict.Count;
            }
            finally
            {
                oldNodes.Dispose();
            }

            _planeCount = _planeDict.Count;

            foreach (var pair in _planeDict)
            {
                _planes[pair.Value] = pair.Key;
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
