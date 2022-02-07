using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    partial class ConvexPolyhedron
    {
        private class MeshDecomposer
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<int> _indices = new List<int>();
            private readonly List<ConvexFace> _faces = new List<ConvexFace>();

            public ConvexPolyhedron[] Decompose(Mesh mesh)
            {
                _vertices.Clear();
                _indices.Clear();
                _faces.Clear();

                mesh.GetVertices(_vertices);
                mesh.GetIndices(_indices, 0);

                for (var i = 0; i < _indices.Count; i += 3)
                {
                    var a = (float3) _vertices[_indices[i + 0]];
                    var b = (float3) _vertices[_indices[i + 1]];
                    var c = (float3) _vertices[_indices[i + 2]];

                    var plane = new BspPlane(math.cross(a - b, c - b), a);
                    var abPlane = new BspPlane(math.cross(plane.Normal, a - b), a);
                    var bcPlane = new BspPlane(math.cross(plane.Normal, b - c), b);
                    var caPlane = new BspPlane(math.cross(plane.Normal, c - a), c);

                    var basis = plane.GetBasis();

                    var face = new ConvexFace
                    {
                        Plane = plane,
                        FaceCuts = new List<FaceCut>(),
                        SubFaces = new List<SubFace>(1)
                    };

                    face.FaceCuts.AddFaceCut(Helpers.GetFaceCut(plane, abPlane, basis));
                    face.FaceCuts.AddFaceCut(Helpers.GetFaceCut(plane, bcPlane, basis));
                    face.FaceCuts.AddFaceCut(Helpers.GetFaceCut(plane, caPlane, basis));

                    face.SubFaces.Add(new SubFace
                    {
                        FaceCuts = new List<FaceCut>(face.FaceCuts)
                    });

                    _faces.Add(face);
                }

                var poly = new ConvexPolyhedron();

                poly._faces.AddRange(_faces);
                poly.InvalidateMesh();

                return new ConvexPolyhedron[] { poly };
            }
        }

        [ThreadStatic]
        private static MeshDecomposer _sDecomposer;

        public static ConvexPolyhedron[] CreateFromMesh(Mesh mesh)
        {
            return (_sDecomposer ?? (_sDecomposer = new MeshDecomposer())).Decompose(mesh);
        }
    }
}
