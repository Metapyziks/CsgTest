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
        public float3 GridSize;

        private void SubdivideGridAxis( float3 axis, List<CsgConvexSolid> polys )
        {
            var gridSize = math.dot( axis, GridSize );

            if ( gridSize <= 0f ) return;

            for ( var i = polys.Count - 1; i >= 0; i-- )
            {
                var poly = polys[i];

                var min = math.dot( poly.VertexMin, axis );
                var max = math.dot( poly.VertexMax, axis );

                if ( max - min <= gridSize ) continue;

                var minGrid = Mathf.FloorToInt( min / gridSize ) + 1;
                var maxGrid = Mathf.CeilToInt( max / gridSize ) - 1;

                for ( var grid = minGrid; grid <= maxGrid; grid++ )
                {
                    var plane = new CsgPlane( axis, grid * gridSize );
                    var child = poly.Split( plane );

                    if ( child != null )
                    {
                        polys.Add( child );
                    }
                }
            }
        }

    }
}
