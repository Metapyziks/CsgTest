using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        public void Paint( CsgConvexSolid brush, int? materialIndex )
        {
            var negCuts = CsgHelpers.RentFaceCutList();

            try
            {
                foreach ( var face in _faces )
                {
                    for ( var i = face.SubFaces.Count - 1; i >= 0; i-- )
                    {
                        var subFace = face.SubFaces[i];

                        if ( subFace.Neighbor != null )
                        {
                            continue;
                        }

                        if ( (materialIndex ?? MaterialIndex) == (subFace.MaterialIndex ?? MaterialIndex) )
                        {
                            continue;
                        }

                        var helper = face.Plane.GetHelper();

                        foreach ( var brushFace in brush.Faces )
                        {
                            var cut = helper.GetCut( brushFace.Plane );

                            if ( !subFace.FaceCuts.Split( cut, negCuts ) )
                            {
                                continue;
                            }

                            face.SubFaces.Add( new SubFace
                            {
                                FaceCuts = new List<FaceCut>( negCuts ),
                                MaterialIndex = subFace.MaterialIndex
                            } );
                        }

                        if ( brush.GetSign( helper.GetAveragePos( subFace.FaceCuts ) ) < 0 )
                        {
                            continue;
                        }

                        subFace.MaterialIndex = materialIndex;
                        face.SubFaces[i] = subFace;
                    }
                }
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( negCuts );
            }
        }
    }
}
