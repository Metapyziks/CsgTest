using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

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

        public bool IsEmpty { get; private set; }
        public int FaceCount => _faces.Count;

        public ConvexFace GetFace(int index)
        {
            return _faces[index];
        }

        public void Transform(in float4x4 matrix)
        {
            var normalMatrix = math.transpose(math.inverse(matrix));
            Transform(matrix, normalMatrix);
        }

        public void Transform(in float4x4 matrix, in float4x4 normalMatrix)
        {
            for (var i = 0; i < _faces.Count; ++i)
            {
                var face = _faces[i];

                var oldBasis = face.Plane.GetBasis();

                face.Plane = face.Plane.Transform(matrix, normalMatrix);

                var newBasis = face.Plane.GetBasis();

                for (var j = 0; j < face.FaceCuts.Count; ++j)
                {
                    face.FaceCuts[j] = face.FaceCuts[j].Transform(matrix, normalMatrix, oldBasis, newBasis);
                }

                foreach (var subFace in face.SubFaces)
                {
                    for (var j = 0; j < subFace.FaceCuts.Count; ++j)
                    {
                        subFace.FaceCuts[j] = subFace.FaceCuts[j].Transform(matrix, normalMatrix, oldBasis, newBasis);
                    }
                }

                _faces[i] = face;
            }
        }

        public void Clear()
        {
            Removed();

            _faces.Clear();
            IsEmpty = false;
        }

        private void SetEmpty()
        {
            Removed();

            _faces.Clear();
            IsEmpty = true;
        }

        internal void Removed()
        {
            foreach (var face in _faces)
            {
                foreach (var subFace in face.SubFaces)
                {
                    subFace.Neighbor?.RemoveNeighbor(-face.Plane, this, false);
                }
            }
        }

        private void AddNeighbor(BspPlane plane, ConvexPolyhedron neighbor, List<FaceCut> faceCuts)
        {
            foreach (var face in _faces)
            {
                if (!face.Plane.Equals(plane)) continue;

                var subFace = new SubFace
                {
                    Neighbor = neighbor,
                    FaceCuts = new List<FaceCut>(faceCuts.Count)
                };

                var basis = face.Plane.GetBasis();
                var invBasis = (-plane).GetBasis();

                foreach (var invCut in faceCuts)
                {
                    var cut = invCut.GetCompliment(invBasis, basis);
                    var (excludeNone, excludeAll) = subFace.FaceCuts.GetNewFaceCutExclusions(cut);

                    if (excludeNone) continue;
                    if (excludeAll) return;

                    subFace.FaceCuts.AddFaceCut(cut);
                }

                subFace.FaceCuts.Sort(FaceCut.Comparer);

                face.SubFaces.Add(subFace);

                break;
            }
        }

        internal void RemoveNeighbor(BspPlane plane, ConvexPolyhedron neighbor, bool removeSubFace)
        {
            foreach (var face in _faces)
            {
                if (!face.Plane.Equals(plane)) continue;

                for (var i = 0; i < face.SubFaces.Count; ++i)
                {
                    var subFace = face.SubFaces[i];
                    if (subFace.Neighbor != neighbor) continue;

                    if (removeSubFace)
                    {
                        face.SubFaces.RemoveAt(i);
                    }
                    else
                    {
                        subFace.Neighbor = null;
                        face.SubFaces[i] = subFace;
                    }

                    return;
                }
            }
        }

        private void AddSubFaceCut(BspPlane plane, ConvexPolyhedron neighbor, BspPlane cutPlane)
        {
            foreach (var face in _faces)
            {
                if (!face.Plane.Equals(plane)) continue;

                for (var i = 0; i < face.SubFaces.Count; ++i)
                {
                    var subFace = face.SubFaces[i];
                    if (subFace.Neighbor != neighbor) continue;

                    var basis = face.Plane.GetBasis();
                    var faceCut = Helpers.GetFaceCut(face.Plane, cutPlane, basis);

                    var (excludeNone, excludeAll) = subFace.FaceCuts.GetNewFaceCutExclusions(faceCut);

                    if (excludeNone) return;
                    if (excludeAll)
                    {
                        subFace.Neighbor = null;
                        face.SubFaces.RemoveAt(i);
                        return;
                    }

                    subFace.FaceCuts.AddFaceCut(faceCut);
                    subFace.FaceCuts.Sort(FaceCut.Comparer);
                    return;
                }
            }
        }

        internal void CopyFaces(HashSet<ConvexFace> faces)
        {
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
                        FaceCuts = new List<FaceCut>(subFace.FaceCuts)
                    });

                    subFace.Neighbor?.AddNeighbor(-face.Plane, this, subFace.FaceCuts);
                }

                _faces.Add(copy);
            }
        }

        public bool Clip(BspPlane plane)
        {
            var (excludedNone, excludedAll) = Clip(plane, null, null);

            return !excludedNone;
        }

        [ThreadStatic]
        private static List<FaceCut> _tempCuts;

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
                }

                return (false, false);
            }

            var planeBasis = plane.GetBasis();

            var anyIntersections = false;
            var excludedAny = false;
            var remainingFacesCount = _faces.Count;

            if (faceCuts != null && faceCuts.Count > 0)
            {
                var tempCuts = _tempCuts ?? (_tempCuts = new List<FaceCut>());

                tempCuts.Clear();
                tempCuts.AddRange(faceCuts);

                for (var i = _faces.Count - 1; i >= 0; --i)
                {
                    var other = _faces[i];

                    var planeCut = Helpers.GetFaceCut(plane, other.Plane, planeBasis, BspSolid.Epsilon * 10f);
                    var auxExclusions = tempCuts.GetNewFaceCutExclusions(planeCut);

                    if (auxExclusions.ExcludesAll)
                    {
                        //var middle = tempCuts.DebugDraw(planeBasis, Color.red);
                        //Debug.DrawLine(middle, middle + other.Plane.Normal);
                        //Debug.DrawLine(middle, other.Plane.Normal * other.Plane.Offset);

                        tempCuts.Clear();
                        break;
                    }

                    if (auxExclusions.ExcludesNone)
                    {
                        continue;
                    }

                    tempCuts.AddFaceCut(planeCut);
                }

                if (tempCuts.Count == 0)
                {
                    return (true, true);
                }
            }

            for (var i = _faces.Count - 1; i >= 0; --i)
            {
                var other = _faces[i];
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
                            subFace.Neighbor?.RemoveNeighbor(-other.Plane, this, true);
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
                            subFace.Neighbor?.RemoveNeighbor(-other.Plane, this, true);
                            continue;
                        }

                        if (subFaceExclusions.ExcludesNone)
                        {
                            continue;
                        }

                        subFace.FaceCuts.AddFaceCut(otherCut);
                        subFace.FaceCuts.Sort(FaceCut.Comparer);

                        subFace.Neighbor?.AddSubFaceCut(-other.Plane, this, plane);
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
            Vector3[] vertices, Vector3[] normals, ushort[] indices)
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

                    foreach (var cut in subFace.FaceCuts)
                    {
                        vertices[vertexOffset] = cut.GetPoint(basis, cut.Max);
                        normals[vertexOffset] = normal;

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

        public void DrawGizmos()
        {
            var avgPos = float3.zero;
            var posCount = 0;

            foreach (var face in _faces)
            {
                var basis = face.Plane.GetBasis();

                foreach (var cut in face.FaceCuts)
                {
                    var min = cut.GetPoint(basis, cut.Min);
                    var max = cut.GetPoint(basis, cut.Max);

                    avgPos += min + max;
                    posCount += 2;

                    Gizmos.DrawLine(min, max);
                }
            }
        }
    }

    public struct SubFace
    {
        public ConvexPolyhedron Neighbor;
        public List<FaceCut> FaceCuts;
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
    }
}
