using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

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

        private readonly List<ConvexFace> _faces = new List<ConvexFace>();

        public bool IsEmpty { get; private set; }
        public int FaceCount => _faces.Count;

        public ConvexFace GetFace(int index)
        {
            return _faces[index];
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

                _faces[i] = face;
            }
        }

        public void Clear()
        {
            // TODO: tell neighbors?

            _faces.Clear();
            IsEmpty = false;
        }

        private void SetEmpty()
        {
            // TODO: tell neighbors?

            _faces.Clear();
            IsEmpty = true;
        }

        /// <summary>
        /// Cuts the polyhedron by the given plane, discarding the negative side.
        /// </summary>
        /// <param name="plane">Plane to clip by.</param>
        /// <returns>True if anything was clipped.</returns>
        public bool Clip(BspPlane plane, List<ConvexFace> excluded = null)
        {
            if (IsEmpty)
            {
                return false;
            }

            var face = new ConvexFace
            {
                Plane = plane,
                FaceCuts = new List<FaceCut>(),
                Neighbor = null
            };

            if (_faces.Count == 0)
            {
                _faces.Add(face);
                return true;
            }

            var planeBasis = plane.GetBasis();

            var anyIntersections = false;
            var excludedAny = false;

            for (var i = _faces.Count - 1; i >= 0; --i)
            {
                var other = _faces[i];
                var otherBasis = other.Plane.GetBasis();

                var planeCut = Helpers.GetFaceCut(plane, other.Plane, planeBasis);
                var otherCut = Helpers.GetFaceCut(other.Plane, plane, otherBasis);

                var planeExclusions = face.FaceCuts.GetNewFaceCutExclusions(planeCut);
                var otherExclusions = other.FaceCuts.GetNewFaceCutExclusions(otherCut);

                if (planeExclusions.ExcludesAll && otherExclusions.ExcludesAll)
                {
                    SetEmpty();
                    return true;
                }

                if (planeExclusions.ExcludesAll)
                {
                    return false;
                }

                if (planeExclusions.ExcludesNone && otherExclusions.ExcludesNone)
                {
                    continue;
                }

                anyIntersections = true;

                if (!otherExclusions.ExcludesNone)
                {
                    excludedAny = true;
                    excluded?.Add(other);
                }

                if (otherExclusions.ExcludesAll)
                {
                    _faces.RemoveAt(i);
                    continue;
                }

                if (!planeExclusions.ExcludesNone)
                {
                    face.FaceCuts.AddFaceCut(planeCut);
                }

                if (!otherExclusions.ExcludesNone)
                {
                    other.FaceCuts.AddFaceCut(otherCut);
                    other.FaceCuts.Sort(FaceCut.Comparer);
                }
            }

            if (anyIntersections && !excludedAny)
            {
                return false;
            }

            if (anyIntersections && _faces.Count == 0)
            {
                SetEmpty();
                return true;
            }

            face.FaceCuts.Sort(FaceCut.Comparer);

            _faces.Add(face);
            return true;
        }

        public void DrawGizmos()
        {
            foreach (var face in _faces)
            {
                var basis = face.Plane.GetBasis();

                foreach (var cut in face.FaceCuts)
                {
                    Gizmos.DrawLine(cut.GetPoint(basis, cut.Min), cut.GetPoint(basis, cut.Max));
                }
            }
        }
    }

    public struct ConvexFace
    {
        public BspPlane Plane;
        public List<FaceCut> FaceCuts;
        public ConvexPolyhedron Neighbor;

        public override string ToString()
        {
            return $"{{ Plane: {Plane}, FaceCuts: {FaceCuts?.Count}, Neighbor: {Neighbor?.ToString() ?? "null"} }}";
        }
    }
}
