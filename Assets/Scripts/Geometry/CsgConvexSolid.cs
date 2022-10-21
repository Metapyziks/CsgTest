using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest.Geometry
{
    public partial class CsgConvexSolid
    {
        private readonly List<Face> _faces = new List<Face>();

        public static int NextIndex { get; set; }

        public int Index { get; }

        public int MaterialIndex { get; set; }

        public bool IsEmpty { get; private set; }

        public IReadOnlyList<Face> Faces => _faces;

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

        public CsgConvexSolid()
        {
            Index = NextIndex++;
        }

        public void InvalidateMesh()
        {
            _vertexPropertiesInvalid = true;

            InvalidateCollider();
        }

        partial void InvalidateCollider();

        public CsgConvexSolid Clone()
        {
            var copy = new CsgConvexSolid
            {
                MaterialIndex = MaterialIndex,
                IsEmpty = IsEmpty
            };

            foreach ( var face in _faces )
            {
                _faces.Add( face.Clone() );
            }

            return copy;
        }

        public int GetSign( float3 pos )
        {
            if ( IsEmpty ) return -1;

            var sign = 1;

            foreach ( var face in _faces )
            {
                sign = Math.Min( sign, face.Plane.GetSign( pos ) );

                if ( sign == -1 ) break;
            }

            return sign;
        }

        public bool TryGetFace( CsgPlane plane, out Face face )
        {
            foreach ( var candidate in _faces )
            {
                if ( candidate.Plane.ApproxEquals( plane ) )
                {
                    face = candidate;
                    return true;
                }
            }

            face = default;
            return false;
        }

        private void UpdateVertexProperties()
        {
            if ( !_vertexPropertiesInvalid ) return;

            _vertexPropertiesInvalid = false;

            if ( IsEmpty )
            {
                _vertexAverage = float3.zero;
                _vertexMin = float3.zero;
                _vertexMax = float3.zero;
                _volume = 0f;
                return;
            }

            var min = new float3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
            var max = new float3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );
            var avgPos = float3.zero;
            var posCount = 0;

            foreach ( var face in _faces )
            {
                var basis = face.Plane.GetHelper();

                foreach ( var cut in face.FaceCuts )
                {
                    var a = basis.GetPoint( cut, cut.Min );
                    var b = basis.GetPoint( cut, cut.Max );

                    min = math.min( min, a );
                    max = math.max( max, a );

                    min = math.min( min, b );
                    max = math.max( max, b );

                    avgPos += a + b;
                    posCount += 2;
                }
            }

            _vertexAverage = posCount == 0 ? float3.zero : avgPos / posCount;
            _vertexMin = min;
            _vertexMax = max;

            var volume = 0f;

            foreach ( var face in _faces )
            {
                if ( face.FaceCuts.Count < 3 ) continue;

                var basis = face.Plane.GetHelper();

                var a = basis.GetPoint( face.FaceCuts[0], face.FaceCuts[0].Max ) - _vertexAverage;
                var b = basis.GetPoint( face.FaceCuts[1], face.FaceCuts[1].Max ) - _vertexAverage;

                for ( var i = 2; i < face.FaceCuts.Count; ++i )
                {
                    var c = basis.GetPoint( face.FaceCuts[i], face.FaceCuts[i].Max ) - _vertexAverage;

                    volume += math.length( math.dot( a, math.cross( b, c ) ) );

                    b = c;
                }
            }

            _volume = volume / 6f;
        }
    }
}
