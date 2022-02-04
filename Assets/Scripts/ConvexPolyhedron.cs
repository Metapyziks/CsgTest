using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using Object = UnityEngine.Object;

namespace CsgTest
{
    public class ConvexPolyhedron
    {
        public static ConvexPolyhedron CreateCube(Bounds bounds)
        {
            var mesh = new ConvexPolyhedron();

            mesh.Clip(new BspPlane(new float3(1f, 0f, 0f), bounds.min));
            mesh.Clip(new BspPlane(new float3(-1f, 0f, 0f), bounds.max));
            mesh.Clip(new BspPlane(new float3(0f, 1f, 0f), bounds.min));
            mesh.Clip(new BspPlane(new float3(0f, -1f, 0f), bounds.max));
            mesh.Clip(new BspPlane(new float3(0f, 0f, 1f), bounds.min));
            mesh.Clip(new BspPlane(new float3(0f, 0f, -1f), bounds.max));

            return mesh;
        }

        public static ConvexPolyhedron CreateDodecahedron(float3 center, float radius)
        {
            var mesh = new ConvexPolyhedron();

            mesh.Clip(new BspPlane(new float3(0f, 1f, 0f), -radius));
            mesh.Clip(new BspPlane(new float3(0f, -1f, 0f), -radius));

            var rot = Quaternion.AngleAxis(60f, Vector3.right);

            for (var i = 0; i < 5; ++i)
            {
                mesh.Clip(new BspPlane(rot * Vector3.down, -radius));
                mesh.Clip(new BspPlane(rot * Vector3.up, -radius));

                rot = Quaternion.AngleAxis(72f, Vector3.up) * rot;
            }

            return mesh;
        }

        private readonly List<ConvexFace> _faces = new List<ConvexFace>();

        public static int NextIndex { get; set; }

        public int Index { get; }

        public int MaterialIndex { get; set; }

        public bool IsEmpty { get; private set; }

        public MeshCollider Collider { get; set; }
        public bool ColliderInvalid { get; private set; }

        public int FaceCount => _faces.Count;

        private bool _vertexPropertiesInvalid = true;
        private float3 _vertexAverage;
        private float3 _vertexMin;
        private float3 _vertexMax;
        private float _volume;

        public float3 VertexAverage 
        {
            get
            {
                UpdateVertexProperties();
                return _vertexAverage;
            }
        }

        public float3 VertexMin
        {
            get
            {
                UpdateVertexProperties();
                return _vertexMin;
            }
        }

        public float3 VertexMax
        {
            get
            {
                UpdateVertexProperties();
                return _vertexMax;
            }
        }

        public float Volume
        {
            get
            {
                UpdateVertexProperties();
                return _volume;
            }
        }

        public ConvexPolyhedron()
        {
            Index = NextIndex++;
        }

        public void InvalidateMesh()
        {
            _vertexPropertiesInvalid = true;
            ColliderInvalid = true;
        }

        private void UpdateVertexProperties()
        {
            if (!_vertexPropertiesInvalid) return;

            _vertexPropertiesInvalid = false;

            if (IsEmpty)
            {
                _vertexAverage = float3.zero;
                _vertexMin = float3.zero;
                _vertexMax = float3.zero;
                _volume = 0f;
                return;
            }

            var min = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var avgPos = float3.zero;
            var posCount = 0;

            foreach (var face in _faces)
            {
                var basis = face.Plane.GetBasis();

                foreach (var cut in face.FaceCuts)
                {
                    var a = cut.GetPoint(basis, cut.Min);
                    var b = cut.GetPoint(basis, cut.Max);

                    min = math.min(min, a);
                    max = math.max(max, a);

                    min = math.min(min, b);
                    max = math.max(max, b);

                    avgPos += a + b;
                    posCount += 2;
                }
            }

            _vertexAverage = posCount == 0 ? float3.zero : avgPos / posCount;
            _vertexMin = min;
            _vertexMax = max;

            var volume = 0f;

            foreach (var face in _faces)
            {
                if (face.FaceCuts.Count < 3) continue;

                var basis = face.Plane.GetBasis();

                var a = face.FaceCuts[0].GetPoint(basis, face.FaceCuts[0].Max) - _vertexAverage;
                var b = face.FaceCuts[1].GetPoint(basis, face.FaceCuts[1].Max) - _vertexAverage;

                for (var i = 2; i < face.FaceCuts.Count; ++i)
                {
                    var c = face.FaceCuts[i].GetPoint(basis, face.FaceCuts[i].Max) - _vertexAverage;

                    volume += math.length(math.dot(a, math.cross(b, c)));

                    b = c;
                }
            }

            _volume = volume / 6f;
        }

        public void AddNeighbors(HashSet<ConvexPolyhedron> visited, Queue<ConvexPolyhedron> queue)
        {
            foreach (var face in _faces)
            {
                foreach (var subFace in face.SubFaces)
                {
                    if (subFace.Neighbor != null && visited.Add(subFace.Neighbor))
                    {
                        queue.Enqueue(subFace.Neighbor);
                    }
                }
            }
        }

        [ThreadStatic]
        private static Vector3[] _sPhysicsMeshVertices;

        [ThreadStatic]
        private static ushort[] _sPhysicsMeshIndices;

        public void UpdateCollider(PolyhedronDemo parent)
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

            Helpers.EnsureCapacity(ref _sPhysicsMeshVertices, vertexCount);
            Helpers.EnsureCapacity(ref _sPhysicsMeshIndices, indexCount);

            var vertices = _sPhysicsMeshVertices;
            var indices = _sPhysicsMeshIndices;

            var vertexOffset = 0;
            var indexOffset = 0;

            foreach (var face in _faces)
            {
                if (face.FaceCuts.Count < 3) continue;

                var firstIndex = (ushort)vertexOffset;
                var basis = face.Plane.GetBasis();

                foreach (var cut in face.FaceCuts)
                {
                    var vertex = cut.GetPoint(basis, cut.Max);

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

        public ConvexPolyhedron Clone()
        {
            var copy = new ConvexPolyhedron
            {
                MaterialIndex = MaterialIndex,
                IsEmpty = IsEmpty
            };

            copy.CopyFaces(_faces);

            return copy;
        }

        public ConvexFace GetFace(int index)
        {
            return _faces[index];
        }
        
        public void Transform(in float4x4 matrix)
        {
            InvalidateMesh();

            for (var i = 0; i < _faces.Count; ++i)
            {
                var face = _faces[i];

                var oldBasis = face.Plane.GetBasis();

                face.Plane = face.Plane.Transform(matrix);

                var newBasis = face.Plane.GetBasis();

                for (var j = 0; j < face.FaceCuts.Count; ++j)
                {
                    face.FaceCuts[j] = face.FaceCuts[j].Transform(matrix, oldBasis, newBasis);
                }

                face.FaceCuts.Sort(FaceCut.Comparer);

                foreach (var subFace in face.SubFaces)
                {
                    for (var j = 0; j < subFace.FaceCuts.Count; ++j)
                    {
                        subFace.FaceCuts[j] = subFace.FaceCuts[j].Transform(matrix, oldBasis, newBasis);
                    }

                    subFace.FaceCuts.Sort(FaceCut.Comparer);
                }

                _faces[i] = face;
            }
        }

        private void SetEmpty()
        {
            Removed(null);

            _faces.Clear();
            IsEmpty = true;

            InvalidateMesh();
        }

        internal void Removed(ConvexPolyhedron replacement)
        {
            foreach (var face in _faces)
            {
                foreach (var subFace in face.SubFaces)
                {
                    subFace.Neighbor?.ReplaceNeighbor(-face.Plane, this, replacement);
                }
            }

            if (Collider != null)
            {
                Object.Destroy(Collider.gameObject);
                Collider = null;
            }
        }

        internal void ReplaceNeighbor(BspPlane plane, ConvexPolyhedron neighbor, ConvexPolyhedron newNeighbor)
        {
            foreach (var face in _faces)
            {
                if (!face.Plane.Equals(plane)) continue;

                for (var i = 0; i < face.SubFaces.Count; ++i)
                {
                    var subFace = face.SubFaces[i];
                    if (subFace.Neighbor != neighbor) continue;

                    subFace.Neighbor = newNeighbor;
                    face.SubFaces[i] = subFace;

                    return;
                }
            }
        }

        private void AddSubFaceCut(BspPlane plane, ConvexPolyhedron neighbor, ConvexPolyhedron newNeighbor, BspPlane cutPlane)
        {
            foreach (var face in _faces)
            {
                if (!face.Plane.Equals(plane)) continue;

                for (var i = face.SubFaces.Count - 1; i >= 0; --i)
                {
                    var subFace = face.SubFaces[i];
                    if (subFace.Neighbor != neighbor) continue;

                    var basis = face.Plane.GetBasis();
                    var faceCut = Helpers.GetFaceCut(face.Plane, cutPlane, basis);

                    var (excludeNone, _) = AddSubFaceCut(face, ref subFace, faceCut, newNeighbor);

                    if (!excludeNone)
                    {
                        face.SubFaces[i] = subFace;
                    }
                }
            }
        }

        public void PaintMaterial(ConvexPolyhedron poly)
        {
            foreach (var face in _faces)
            {
                var basis = face.Plane.GetBasis();

                for (var i = face.SubFaces.Count - 1; i >= 0; --i)
                {
                    var subFace = face.SubFaces[i];
                    if (subFace.Neighbor != null) continue;

                    var oldMaterialIndex = subFace.MaterialIndex ?? MaterialIndex;
                    if (oldMaterialIndex == poly.MaterialIndex) continue;

                    var allInside = true;

                    foreach (var otherFace in poly._faces)
                    {
                        var cutPlane = otherFace.Plane;

                        var faceCut = Helpers.GetFaceCut(face.Plane, cutPlane, basis);

                        var (_, excludeAll) = AddSubFaceCut(face, ref subFace, faceCut, null);

                        if (excludeAll)
                        {
                            allInside = false;
                            break;
                        }
                    }

                    if (allInside)
                    {
                        subFace.MaterialIndex = poly.MaterialIndex;
                        face.SubFaces[i] = subFace;
                    }
                }
            }
        }

        private (bool ExcludeNone, bool ExcludeAll) AddSubFaceCut(ConvexFace face, ref SubFace subFace, FaceCut faceCut, ConvexPolyhedron newNeighbor)
        {
            faceCut.Min = float.NegativeInfinity;
            faceCut.Max = float.PositiveInfinity;

            var (excludeNone, excludeAll) = subFace.FaceCuts.GetNewFaceCutExclusions(faceCut);

            if (excludeNone) return (true, false);
            if (excludeAll)
            {
                subFace.Neighbor = newNeighbor;
                return (false, true);
            }

            var newSubFace = new SubFace
            {
                FaceCuts = new List<FaceCut>(subFace.FaceCuts),
                Neighbor = newNeighbor
            };

            subFace.FaceCuts.AddFaceCut(faceCut);
            subFace.FaceCuts.Sort(FaceCut.Comparer);

            newSubFace.FaceCuts.AddFaceCut(-faceCut);
            newSubFace.FaceCuts.Sort(FaceCut.Comparer);

            face.SubFaces.Add(newSubFace);
            return (false, false);
        }

        internal void CopyFaces(IEnumerable<ConvexFace> faces)
        {
            InvalidateMesh();

            foreach (var face in faces)
            {
                var copy = new ConvexFace
                {
                    Plane = face.Plane,
                    FaceCuts = new List<FaceCut>(face.FaceCuts),
                    SubFaces = new List<SubFace>(face.SubFaces.Count)
                };

                foreach (var subFace in face.SubFaces)
                {
                    copy.SubFaces.Add(new SubFace
                    {
                        Neighbor = subFace.Neighbor,
                        FaceCuts = new List<FaceCut>(subFace.FaceCuts),
                        MaterialIndex = subFace.MaterialIndex
                    });
                }

                _faces.Add(copy);
            }
        }

        private bool TryGetFace(BspPlane plane, out ConvexFace matchingFace)
        {
            foreach (var face in _faces)
            {
                if (face.Plane.Equals(plane))
                {
                    matchingFace = face;
                    return true;
                }
            }

            matchingFace = default;
            return false;
        }
        
        internal void CopySubFaces(ConvexPolyhedron other)
        {
            foreach (var otherFace in other._faces)
            {
                if (!TryGetFace(otherFace.Plane, out var thisFace))
                {
                    continue;
                }

                foreach (var otherSubFace in otherFace.SubFaces)
                {
                    if (otherSubFace.Neighbor == null) continue;

                    for (var i = thisFace.SubFaces.Count - 1; i >= 0; --i)
                    {
                        var subFace = thisFace.SubFaces[i];
                        if (subFace.Neighbor != null) continue;

                        var allInside = true;

                        foreach (var subFaceCut in otherSubFace.FaceCuts)
                        {
                            if (thisFace.FaceCuts.Contains(subFaceCut, BspSolid.Epsilon * 8f))
                            {
                                continue;
                            }

                            var (excludeNone, excludeAll) = AddSubFaceCut(thisFace, ref subFace,
                                subFaceCut, null);

                            if (excludeAll)
                            {
                                allInside = false;
                                break;
                            }
                        }

                        if (allInside)
                        {
                            subFace.Neighbor = otherSubFace.Neighbor;
                            subFace.MaterialIndex = otherSubFace.MaterialIndex;
                            thisFace.SubFaces[i] = subFace;
                        }
                    }
                }
            }
        }

        public bool Clip(BspPlane plane)
        {
            var (excludedNone, excludedAll) = Clip(plane, null, null);

            return !excludedNone;
        }

        /// <summary>
        /// Cuts the polyhedron by the given plane, discarding the negative side.
        /// </summary>
        /// <param name="plane">Plane to clip by.</param>
        /// <returns>True if anything was clipped.</returns>
        internal (bool ExcludedNone, bool ExcludedAll) Clip(BspPlane plane, List<FaceCut> faceCuts,
            ConvexPolyhedron neighbor, HashSet<ConvexFace> excluded = null, bool dryRun = false)
        {
            if (IsEmpty)
            {
                return (true, false);
            }

            var face = new ConvexFace
            {
                Plane = plane,
                FaceCuts = new List<FaceCut>()
            };

            if (_faces.Count == 0)
            {
                if (!dryRun)
                {
                    face.SubFaces = new List<SubFace>
                    {
                        new SubFace
                        {
                            FaceCuts = new List<FaceCut>(),
                            Neighbor = neighbor
                        }
                    };

                    _faces.Add(face);

                    InvalidateMesh();
                }

                return (false, false);
            }

            var planeBasis = plane.GetBasis();

            var anyIntersections = false;
            var excludedAny = false;
            var remainingFacesCount = _faces.Count;

            if (faceCuts != null && faceCuts.Count > 0)
            {
                for (var i = _faces.Count - 1; i >= 0; --i)
                {
                    var other = _faces[i];

                    if (plane.Equals(other.Plane)) continue;

                    var planeCut = Helpers.GetFaceCut(plane, other.Plane, planeBasis);

                    var auxExclusions = faceCuts.GetNewFaceCutExclusions(planeCut);

                    if (auxExclusions.ExcludesAll)
                    {
                        return (true, true);
                    }
                }
            }

            for (var i = _faces.Count - 1; i >= 0; --i)
            {
                var other = _faces[i];

                if (plane.Equals(other.Plane))
                {
                    continue;
                }

                var otherBasis = other.Plane.GetBasis();

                var planeCut = Helpers.GetFaceCut(plane, other.Plane, planeBasis);
                var otherCut = Helpers.GetFaceCut(other.Plane, plane, otherBasis);

                var planeExclusions = face.FaceCuts.GetNewFaceCutExclusions(planeCut);
                var otherExclusions = other.FaceCuts.GetNewFaceCutExclusions(otherCut);

                if (otherExclusions.ExcludesNone)
                {
                    if (planeExclusions.ExcludesAll)
                    {
                        Assert.IsFalse(excludedAny);

                        return (true, false);
                    }

                    if (planeExclusions.ExcludesNone)
                    {
                        continue;
                    }
                }

                anyIntersections = true;

                if (!otherExclusions.ExcludesNone)
                {
                    excludedAny = true;
                    excluded?.Add(other);
                }

                if (otherExclusions.ExcludesAll)
                {
                    if (!dryRun)
                    {
                        foreach (var subFace in other.SubFaces)
                        {
                            subFace.Neighbor?.ReplaceNeighbor(-other.Plane, this, neighbor);
                        }

                        _faces.RemoveAt(i);
                    }

                    --remainingFacesCount;
                    continue;
                }

                if (!planeExclusions.ExcludesNone)
                {
                    face.FaceCuts.AddFaceCut(planeCut);
                }

                if (!otherExclusions.ExcludesNone && !dryRun)
                {
                    other.FaceCuts.AddFaceCut(otherCut);
                    other.FaceCuts.Sort(FaceCut.Comparer);

                    for (var subFaceIndex = other.SubFaces.Count - 1; subFaceIndex >= 0; --subFaceIndex)
                    {
                        var subFace = other.SubFaces[subFaceIndex];
                        var subFaceExclusions = subFace.FaceCuts.GetNewFaceCutExclusions(otherCut);

                        if (subFaceExclusions.ExcludesAll)
                        {
                            other.SubFaces.RemoveAt(subFaceIndex);
                            subFace.Neighbor?.ReplaceNeighbor(-other.Plane, this, neighbor);
                            continue;
                        }

                        if (subFaceExclusions.ExcludesNone)
                        {
                            continue;
                        }

                        subFace.FaceCuts.AddFaceCut(otherCut);
                        subFace.FaceCuts.Sort(FaceCut.Comparer);

                        subFace.Neighbor?.AddSubFaceCut(-other.Plane, this, neighbor, plane);
                    }
                }
            }

            if (anyIntersections && !excludedAny)
            {
                return (true, false);
            }

            if (anyIntersections && remainingFacesCount == 0)
            {
                if (!dryRun)
                {
                    SetEmpty();
                }

                return (false, true);
            }

            if (!dryRun)
            {
                face.FaceCuts.Sort(FaceCut.Comparer);

                face.SubFaces = new List<SubFace>
                {
                    new SubFace
                    {
                        Neighbor = neighbor,
                        FaceCuts = new List<FaceCut>(face.FaceCuts)
                    }
                };

                _faces.Add(face);
                InvalidateMesh();
            }

            return (false, false);
        }

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

        public void WriteMesh(ref int vertexOffset, ref int indexOffset,
            Vector3[] vertices, Vector3[] normals, Vector4[] texCoords, ushort[] indices)
        {
            foreach (var face in _faces)
            {
                var basis = face.Plane.GetBasis();
                var normal = -face.Plane.Normal;

                foreach (var subFace in face.SubFaces)
                {
                    if (subFace.Neighbor != null) continue;
                    if (subFace.FaceCuts.Count < 3) continue;

                    var firstIndex = (ushort)vertexOffset;
                    var materialIndex = subFace.MaterialIndex ?? MaterialIndex;

                    foreach (var cut in subFace.FaceCuts)
                    {
                        var vertex = cut.GetPoint(basis, cut.Max);

                        vertices[vertexOffset] = vertex;
                        normals[vertexOffset] = normal;
                        texCoords[vertexOffset] = new Vector4(
                            math.dot(basis.tu, vertex),
                            math.dot(basis.tv, vertex),
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

        public bool Contains(float3 pos)
        {
            if (IsEmpty) return false;

            foreach (var face in _faces)
            {
                if (math.dot(pos, face.Plane.Normal) < face.Plane.Offset)
                {
                    return false;
                }
            }

            return true;
        }

        public void DrawGizmos()
        {
            foreach (var face in _faces)
            {
                face.DrawGizmos();
            }

#if UNITY_EDITOR
            UnityEditor.Handles.Label(VertexAverage, ToString());
#endif
        }

        public void DrawDebug(Color color)
        {
            foreach (var face in _faces)
            {
                face.DrawDebug(color);
            }
        }

        public override string ToString()
        {
            return $"[{Index}]";
        }
    }

    public struct SubFace
    {
        public ConvexPolyhedron Neighbor;
        public List<FaceCut> FaceCuts;
        public int? MaterialIndex;

        public void DrawDebug(BspPlane plane, Color color)
        {
            var basis = plane.GetBasis();

            foreach (var cut in FaceCuts)
            {
                cut.DrawDebug(basis, color);
            }
        }
    }

    public struct ConvexFace : IEquatable<ConvexFace>
    {
        public BspPlane Plane;
        public List<FaceCut> FaceCuts;
        public List<SubFace> SubFaces;

        public override string ToString()
        {
            return $"{{ Plane: {Plane}, FaceCuts: {FaceCuts?.Count} }}";
        }

        public bool Equals(ConvexFace other)
        {
            return Plane.Equals(other.Plane);
        }

        public override bool Equals(object obj)
        {
            return obj is ConvexFace other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Plane.GetHashCode();
        }

        public void DrawGizmos()
        {
            var basis = Plane.GetBasis();

            foreach (var cut in FaceCuts)
            {
                var min = cut.GetPoint(basis, cut.Min);
                var max = cut.GetPoint(basis, cut.Max);

                Gizmos.DrawLine(min, max);
            }
        }

        public void DrawDebug(Color color)
        {
            var basis = Plane.GetBasis();

            foreach (var cut in FaceCuts)
            {
                cut.DrawDebug(basis, color);
            }
        }
    }
}
