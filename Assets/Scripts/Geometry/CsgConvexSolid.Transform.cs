﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        public void Transform( in float4x4 matrix )
        {
            InvalidateMesh();

            for ( var i = 0; i < _faces.Count; ++i )
            {
                var face = _faces[i];

                var oldBasis = face.Plane.GetHelper();

                face.Plane = face.Plane.Transform( matrix );

                var newBasis = face.Plane.GetHelper();

                for ( var j = 0; j < face.FaceCuts.Count; ++j )
                {
                    face.FaceCuts[j] = oldBasis.Transform( face.FaceCuts[j], newBasis, matrix );
                }

                foreach ( var subFace in face.SubFaces )
                {
                    for ( var j = 0; j < subFace.FaceCuts.Count; ++j )
                    {
                        subFace.FaceCuts[j] = oldBasis.Transform( subFace.FaceCuts[j], newBasis, matrix );
                    }
                }

                _faces[i] = face;
            }
        }
    }
}
