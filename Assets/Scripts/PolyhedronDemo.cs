using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    [RequireComponent(typeof(MeshFilter)), ExecuteInEditMode]
    public class PolyhedronDemo : MonoBehaviour
    {
        private readonly List<ConvexPolyhedron> _polyhedra = new List<ConvexPolyhedron>();

        public Bounds bounds;
        
        void Update()
        {
            _polyhedra.Clear();

            foreach (var brush in transform.GetComponentsInChildren<CsgBrush>())
            {
                var shape = ConvexPolyhedron.CreateCube(new Bounds(Vector3.zero, Vector3.one));
                var matrix = brush.transform.localToWorldMatrix;
                var normalMatrix = math.transpose(math.inverse(matrix));

                shape.Transform(matrix, normalMatrix);

                switch (brush.Operator)
                {
                    case BrushOperator.Add:
                        Add(shape);
                        break;

                    case BrushOperator.Subtract:
                        Subtract(shape);
                        break;
                }
            }
        }

        public void Add(ConvexPolyhedron polyhedron)
        {
            if (polyhedron.IsEmpty) return;

            Subtract(polyhedron);
            _polyhedra.Add(polyhedron);
        }

        private readonly Queue<(ConvexPolyhedron, int)> _subtractQueue = new Queue<(ConvexPolyhedron, int)>();
        private readonly List<ConvexFace> _excludedFaces = new List<ConvexFace>();

        public void Subtract(ConvexPolyhedron polyhedron)
        {
            if (polyhedron.IsEmpty) return;

            _subtractQueue.Clear();

            foreach (var poly in _polyhedra)
            {
                _subtractQueue.Enqueue((poly, 0));
            }

            while (_subtractQueue.Count > 0)
            {
                var (next, firstFace) = _subtractQueue.Dequeue();

                for (var i = firstFace; i < polyhedron.FaceCount; ++i)
                {
                    var face = polyhedron.GetFace(i);

                    _excludedFaces.Clear();

                    if (!next.Clip(face.Plane, _excludedFaces)) continue;

                    if (next.IsEmpty)
                    {
                        _polyhedra.Remove(next);
                        break;
                    }

                    var child = new ConvexPolyhedron();

                    foreach (var excludedFace in _excludedFaces)
                    {
                        child.Clip(excludedFace.Plane);
                    }

                    child.Clip(-face.Plane);

                    _polyhedra.Add(child);
                    _subtractQueue.Enqueue((child, i + 1));
                }
            }
        }

        void OnDrawGizmos()
        {
            foreach (var poly in _polyhedra)
            {
                poly.DrawGizmos();
            }
        }
    }
}
