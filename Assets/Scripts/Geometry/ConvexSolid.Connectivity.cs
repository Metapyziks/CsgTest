using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        public void AddNeighbors( HashSet<CsgConvexSolid> visited, Queue<CsgConvexSolid> queue )
        {
            foreach ( var face in _faces )
            {
                foreach ( var subFace in face.SubFaces )
                {
                    if ( subFace.Neighbor != null && visited.Add( subFace.Neighbor ) )
                    {
                        queue.Enqueue( subFace.Neighbor );
                    }
                }
            }
        }
    }

    partial class CsgSolid
    {
        [ThreadStatic] private static List<(CsgConvexSolid, float)> _sChunks;
        [ThreadStatic] private static HashSet<CsgConvexSolid> _sVisited;
        [ThreadStatic] private static Queue<CsgConvexSolid> _sVisitQueue;

        private static void GetConnectivityContainers( out List<(CsgConvexSolid Root, float Volume)> chunks,
            out HashSet<CsgConvexSolid> visited, out Queue<CsgConvexSolid> queue )
        {
            chunks = _sChunks ??= new List<(CsgConvexSolid, float)>();
            visited = _sVisited ??= new HashSet<CsgConvexSolid>();
            queue = _sVisitQueue ??= new Queue<CsgConvexSolid>();

            chunks.Clear();
            visited.Clear();
            queue.Clear();
        }

        private void FindChunks( List<(CsgConvexSolid Root, float Volume)> chunks, HashSet<CsgConvexSolid> visited, Queue<CsgConvexSolid> queue )
        {
            while ( visited.Count < _polyhedra.Count )
            {
                queue.Clear();

                CsgConvexSolid root = null;

                foreach ( var poly in _polyhedra )
                {
                    if ( visited.Contains( poly ) ) continue;

                    root = poly;
                    break;
                }

                Debug.Assert( root != null );

                visited.Add( root );
                queue.Enqueue( root );

                var volume = 0f;
                var count = 0;

                while ( queue.Count > 0 )
                {
                    var next = queue.Dequeue();

                    volume += next.Volume;
                    count += 1;

                    next.AddNeighbors( visited, queue );
                }

                chunks.Add( (root, volume) );
            }
        }

        private void RemoveDisconnectedPolyhedra()
        {
            if ( _polyhedra.Count == 0 ) return;

            GetConnectivityContainers( out var chunks, out var visited, out var queue );
            FindChunks( chunks, visited, queue );

            if ( chunks.Count == 1 ) return;

            chunks.Sort( ( a, b ) => Math.Sign( b.Volume - a.Volume ) );

            foreach ( var chunk in chunks.Skip( 1 ) )
            {
                visited.Clear();
                queue.Clear();

                queue.Enqueue( chunk.Root );
                visited.Add( chunk.Root );

                while ( queue.Count > 0 )
                {
                    var next = queue.Dequeue();

                    next.InvalidateMesh();
                    next.AddNeighbors( visited, queue );
                }

                _polyhedra.RemoveAll( x => visited.Contains( x ) );

                var child = new GameObject( "Debris",
                    typeof( Rigidbody ),
                    typeof( MeshFilter ),
                    typeof( MeshRenderer ),
                    typeof( CsgSolid ) )
                {
                    transform =
                    {
                        localPosition = transform.localPosition,
                        localRotation = transform.localRotation,
                        localScale = transform.localScale
                    }
                }.GetComponent<CsgSolid>();

                child.GetComponent<MeshRenderer>().sharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;

                child._polyhedra.AddRange( visited );
                child._meshInvalid = true;

                child.Start();
                child.Update();
            }
        }
    }
}
