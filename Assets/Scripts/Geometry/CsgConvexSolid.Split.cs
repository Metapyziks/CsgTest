using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        /// <summary>
        /// Split this solid by the given plane, returning the negative side.
        /// </summary>
        /// <param name="plane">Plane to split by.</param>
        /// <param name="faceCuts">Optional constraints on the split plane.</param>
        /// <returns>If a split occurred, returns a new solid with the removed faces. Otherwise null.</returns>
        public CsgConvexSolid Split( CsgPlane plane, List<FaceCut> faceCuts = null )
        {
            var result = Split( plane, faceCuts, false );

            if ( result.Changed )
            {
                InvalidateMesh();
            }

            return result.NegativeSolid;
        }

        /// <summary>
        /// Split this solid by the given plane, discarding the negative side.
        /// </summary>
        /// <param name="plane">Plane to clip by.</param>
        /// <param name="faceCuts">Optional constraints on the clip plane.</param>
        /// <returns>Returns true if a clip occurred.</returns>
        public bool Clip( CsgPlane plane, List<FaceCut> faceCuts = null )
        {
            var result = Split( plane, faceCuts, true );

            if ( result.Changed )
            {
                InvalidateMesh();
                result.NegativeSolid?.InvalidateMesh();
            }

            return result.Changed;
        }
        
        [ThreadStatic]
        private static List<FaceCut> _sPositiveCuts;

        [ThreadStatic]
        private static List<FaceCut> _sNegativeCuts;

        private (bool Changed, CsgConvexSolid NegativeSolid) Split( CsgPlane cutPlane, List<FaceCut> faceCuts, bool discard )
        {
            if ( IsEmpty ) return (false, null);
            
            var posCuts = _sPositiveCuts ??= new List<FaceCut>();
            var negCuts = _sNegativeCuts ??= new List<FaceCut>();

            var splitPlaneHelper = cutPlane.GetHelper();

            var splitFace = new Face
            {
                Plane = cutPlane,
                FaceCuts = new List<FaceCut>()
            };

            if ( faceCuts != null )
            {
                splitFace.FaceCuts.AddRange( faceCuts );
            }

            // Cut down split face to see if there is any intersection

            foreach ( var face in _faces )
            {
                var splitPlaneCut = splitPlaneHelper.GetCut( face.Plane );
                splitFace.FaceCuts.Split( splitPlaneCut, posCuts, negCuts );

                if ( negCuts.Count == 0 )
                {
                    continue;
                }

                if ( posCuts.Count == 0 )
                {
                    // Bounded cut plane is fully outside of this solid

                    return (false, null);
                }

                splitFace.FaceCuts.Clear();
                splitFace.FaceCuts.AddRange( posCuts );
            }

            // If we survived that, the cut plane must intersect this solid

            var negSolid = discard
                ? null
                : new CsgConvexSolid
                {
                    MaterialIndex = MaterialIndex
                };

            for ( var i = _faces.Count - 1; i >= 0; i-- )
            {
                var face = _faces[i];
                var facePlaneHelper = face.Plane.GetHelper();
                var facePlaneCut = facePlaneHelper.GetCut( cutPlane );
                
                face.FaceCuts.Split( facePlaneCut, posCuts, negCuts );

                // Check if face is all on positive or negative side

                if ( negCuts.Count == 0 )
                {
                    continue;
                }

                if ( posCuts.Count == 0 )
                {
                    _faces.RemoveAt( i );
                    negSolid?._faces.Add( face );
                    continue;
                }

                // Otherwise split face in two

                var posFace = new Face
                {
                    Plane = face.Plane,
                    FaceCuts = new List<FaceCut>( posCuts ),
                    SubFaces = new List<SubFace>()
                };

                var negFace = discard ? (Face?) null : new Face
                {
                    Plane = face.Plane,
                    FaceCuts = new List<FaceCut>( negCuts ),
                    SubFaces = new List<SubFace>()
                };

                _faces[i] = posFace;
                negSolid?._faces.Add( negFace.Value );

                foreach ( var subFace in face.SubFaces )
                {
                    subFace.FaceCuts.Split( facePlaneCut, posCuts, negCuts );

                    // Check if sub-face is all on positive or negative side

                    if ( negCuts.Count == 0 )
                    {
                        posFace.SubFaces.Add( subFace );
                        continue;
                    }

                    if ( posCuts.Count == 0 )
                    {
                        negFace?.SubFaces.Add( subFace );
                        continue;
                    }

                    // Otherwise split sub-face in two

                    posFace.SubFaces.Add( new SubFace
                    {
                        FaceCuts = new List<FaceCut>( posCuts ),
                        MaterialIndex = subFace.MaterialIndex,
                        Neighbor = subFace.Neighbor
                    } );

                    negFace?.SubFaces.Add( new SubFace
                    {
                        FaceCuts = new List<FaceCut>( negCuts ),
                        MaterialIndex = subFace.MaterialIndex,
                        Neighbor = subFace.Neighbor
                    } );
                }
            }
            
            splitFace.SubFaces = new List<SubFace>
            {
                new SubFace
                {
                    FaceCuts = new List<FaceCut>( splitFace.FaceCuts ),
                    Neighbor = negSolid
                }
            };

            _faces.Add( splitFace );
            negSolid?._faces.Add( splitFace.CloneFlipped( this ) );

            return (true, negSolid);
        }
        
        /// <summary>
        /// Merge sub-faces from another solid that is entirely contained within this one.
        /// </summary>
        public void MergeSubFacesFrom( CsgConvexSolid other )
        {
            if ( other.IsEmpty || IsEmpty ) return;

            var posCuts = _sPositiveCuts ??= new List<FaceCut>();
            var negCuts = _sNegativeCuts ??= new List<FaceCut>();

            foreach ( var thisFace in _faces )
            {
                if ( !other.TryGetFace( thisFace.Plane, out var otherFace ) )
                {
                    continue;
                }

                // First remove all sub-faces that overlap

                for ( var i = thisFace.SubFaces.Count - 1; i >= 0; i-- )
                {
                    var thisSubFace = thisFace.SubFaces[i];
                    var allInside = true;

                    foreach ( var otherFaceCut in otherFace.FaceCuts )
                    {
                        thisSubFace.FaceCuts.Split( otherFaceCut, posCuts, negCuts );

                        if ( posCuts.Count == 0 )
                        {
                            allInside = false;
                            break;
                        }

                        thisSubFace.FaceCuts.Clear();
                        thisSubFace.FaceCuts.AddRange( posCuts );

                        thisFace.SubFaces.Add( new SubFace
                        {
                            FaceCuts = new List<FaceCut>( negCuts ),
                            MaterialIndex = thisSubFace.MaterialIndex,
                            Neighbor = thisSubFace.Neighbor
                        } );
                    }

                    if ( allInside )
                    {
                        thisFace.SubFaces.RemoveAt( i );
                    }
                }

                // Now just add the sub-faces from other

                foreach ( var subFace in otherFace.SubFaces )
                {
                    thisFace.SubFaces.Add( subFace.Clone() );
                }
            }
        }
    }
}
