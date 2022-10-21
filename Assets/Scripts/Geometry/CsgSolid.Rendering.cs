using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest.Geometry
{
    partial class CsgSolid
    {
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private Vector4[] _texCoords;
        private ushort[] _indices;
        private Mesh _mesh;

        private bool _meshInvalid;
        private bool _hasRigidBody;

        partial void RenderingStart()
        {
            _hasRigidBody = GetComponent<Rigidbody>() != null;
        }

        partial void RenderingUpdate()
        {
            if ( !_meshInvalid ) return;

            _meshInvalid = false;

#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
#endif
            {
                if (_polyhedra.Count == 0)
                {
                    Destroy(gameObject);
                    return;
                }

                RemoveDisconnectedPolyhedra();
            }

            if (_mesh == null)
            {
                var meshFilter = GetComponent<MeshFilter>();

                meshFilter.sharedMesh = _mesh = new Mesh
                {
                    hideFlags = HideFlags.DontSave
                };

                _mesh.MarkDynamic();
            }

            UpdateMesh(_mesh, _polyhedra);

            var meshCollider = GetComponent<MeshCollider>();

            if (meshCollider != null)
            {
                meshCollider.sharedMesh = _mesh;
            }
        }

        private void WritePolyhedron( ref int vertexOffset, ref int indexOffset, CsgConvexSolid poly )
        {
            var (faceCount, vertexCount) = poly.GetMeshInfo();

            CsgHelpers.EnsureCapacity(ref _vertices, vertexOffset + vertexCount);
            CsgHelpers.EnsureCapacity(ref _normals, vertexOffset + vertexCount);
            CsgHelpers.EnsureCapacity(ref _texCoords, vertexOffset + vertexCount);
            CsgHelpers.EnsureCapacity(ref _indices, indexOffset + faceCount * 3);

            poly.WriteMesh(ref vertexOffset, ref indexOffset,
                _vertices, _normals, _texCoords, _indices);

            if (_hasRigidBody && poly.ColliderInvalid)
            {
                poly.UpdateCollider(this);
            }
        }

        private void UpdateMesh<T>( Mesh mesh, T polyhedra )
            where T : IEnumerable<CsgConvexSolid>
        {
            var indexOffset = 0;
            var vertexOffset = 0;

            var mass = 0f;

            // TODO: can have different densities for different materials
            const float density = 1f;

            foreach (var poly in polyhedra)
            {
                WritePolyhedron(ref vertexOffset, ref indexOffset, poly);

                mass += poly.Volume * density;
            }

            mesh.Clear();
            mesh.SetVertices(_vertices, 0, vertexOffset);
            mesh.SetNormals(_normals, 0, vertexOffset);
            mesh.SetUVs(0, _texCoords, 0, vertexOffset);
            mesh.SetIndices(_indices, 0, indexOffset, MeshTopology.Triangles, 0);

            mesh.MarkModified();

            if (_hasRigidBody)
            {
                GetComponent<Rigidbody>().mass = mass;
            }
        }
    }

    partial class CsgConvexSolid
    {
        public (int FaceCount, int VertexCount) GetMeshInfo()
        {
            var faceCount = 0;
            var vertexCount = 0;

            foreach (var face in _faces)
            {
                foreach (var subFace in face.SubFaces)
                {
                    if (subFace.Neighbor != null) continue;
                    if (subFace.FaceCuts.Count < 3) continue;

                    faceCount += subFace.FaceCuts.Count - 2;
                    vertexCount += subFace.FaceCuts.Count;
                }
            }

            return (faceCount, vertexCount);
        }

        public void WriteMesh( ref int vertexOffset, ref int indexOffset,
            Vector3[] vertices, Vector3[] normals, Vector4[] texCoords, ushort[] indices )
        {
            foreach (var face in _faces)
            {
                var basis = face.Plane.GetHelper();
                var normal = -face.Plane.Normal;

                foreach (var subFace in face.SubFaces)
                {
                    if (subFace.Neighbor != null) continue;
                    if (subFace.FaceCuts.Count < 3) continue;

                    var firstIndex = (ushort)vertexOffset;
                    var materialIndex = subFace.MaterialIndex ?? MaterialIndex;

                    subFace.FaceCuts.Sort(FaceCut.Comparer);

                    foreach (var cut in subFace.FaceCuts)
                    {
                        var vertex = basis.GetPoint( cut, cut.Max );

                        vertices[vertexOffset] = vertex;
                        normals[vertexOffset] = normal;
                        texCoords[vertexOffset] = new Vector4(
                            math.dot(basis.Tu, vertex),
                            math.dot(basis.Tv, vertex),
                            materialIndex,
                            0f);

                        ++vertexOffset;
                    }

                    for (var i = 2; i < subFace.FaceCuts.Count; ++i)
                    {
                        indices[indexOffset++] = firstIndex;
                        indices[indexOffset++] = (ushort)(firstIndex + i - 1);
                        indices[indexOffset++] = (ushort)(firstIndex + i);
                    }
                }
            }
        }
    }
}
