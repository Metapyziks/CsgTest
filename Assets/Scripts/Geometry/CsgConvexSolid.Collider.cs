using System;
using System.Linq;
using UnityEngine;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        public MeshCollider Collider { get; set; }
        public bool ColliderInvalid { get; private set; }

        [ThreadStatic]
        private static Vector3[] _sPhysicsMeshVertices;

        [ThreadStatic]
        private static ushort[] _sPhysicsMeshIndices;

        partial void InvalidateCollider()
        {
            ColliderInvalid = true;
        }

        public void UpdateCollider( CsgSolid parent )
        {
            ColliderInvalid = false;

            if (Collider == null)
            {
                Collider = new GameObject("Collider", typeof(MeshCollider)).GetComponent<MeshCollider>();
                Collider.convex = true;
                Collider.sharedMesh = new Mesh
                {
                    hideFlags = HideFlags.DontSave
                };
            }

            if (Collider.transform.parent != parent.transform)
            {
                Collider.transform.SetParent(parent.transform, false);
            }

            var vertexCount = _faces.Where(x => x.FaceCuts.Count >= 3).Sum(x => x.FaceCuts.Count);
            var indexCount = _faces.Where(x => x.FaceCuts.Count >= 3).Sum(x => x.FaceCuts.Count - 2) * 3;

            CsgHelpers.EnsureCapacity(ref _sPhysicsMeshVertices, vertexCount);
            CsgHelpers.EnsureCapacity(ref _sPhysicsMeshIndices, indexCount);

            var vertices = _sPhysicsMeshVertices;
            var indices = _sPhysicsMeshIndices;

            var vertexOffset = 0;
            var indexOffset = 0;

            foreach (var face in _faces)
            {
                if (face.FaceCuts.Count < 3) continue;

                var firstIndex = (ushort)vertexOffset;
                var basis = face.Plane.GetHelper();

                foreach (var cut in face.FaceCuts)
                {
                    var vertex = basis.GetPoint( cut, cut.Max );

                    vertices[vertexOffset] = vertex;

                    ++vertexOffset;
                }

                for (var i = 2; i < face.FaceCuts.Count; ++i)
                {
                    indices[indexOffset++] = firstIndex;
                    indices[indexOffset++] = (ushort)(firstIndex + i - 1);
                    indices[indexOffset++] = (ushort)(firstIndex + i);
                }
            }

            var mesh = Collider.sharedMesh;

            mesh.Clear();
            mesh.SetVertices(vertices, 0, vertexOffset);
            mesh.SetIndices(indices, 0, indexOffset, MeshTopology.Triangles, 0);

            mesh.MarkModified();

            Collider.sharedMesh = mesh;
        }
    }
}
