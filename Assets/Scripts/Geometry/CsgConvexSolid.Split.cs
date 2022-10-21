using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
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
        
        private (bool Changed, CsgConvexSolid NegativeSolid) Split( CsgPlane cutPlane, List<FaceCut> faceCuts, bool discard )
        {
            if ( IsEmpty ) return (false, null);

            var splitPlaneHelper = cutPlane.GetHelper();

            var intersectionCuts = CsgHelpers.RentFaceCutList();

            var posCuts = CsgHelpers.RentFaceCutList();
            var negCuts = CsgHelpers.RentFaceCutList();

            try
            {
                intersectionCuts.Clear();

                if ( faceCuts != null )
                {
                    intersectionCuts.AddRange( faceCuts );
                }

                // Cut down split face to see if there is any intersection

                foreach ( var face in _faces )
                {
                    var splitPlaneCut = splitPlaneHelper.GetCut( face.Plane );
                    intersectionCuts.Split( splitPlaneCut, posCuts, negCuts );

                    if ( negCuts.Count == 0 )
                    {
                        continue;
                    }

                    if ( posCuts.Count == 0 )
                    {
                        // Bounded cut plane is fully outside of this solid

                        return (false, null);
                    }

                    (intersectionCuts, posCuts) = (posCuts, intersectionCuts);
                }

                // If we survived that, the cut plane must intersect this solid

                var negSolid = discard
                    ? null
                    : new CsgConvexSolid
                    {
                        MaterialIndex = MaterialIndex
                    };

                var posSubFace = new SubFace
                {
                    FaceCuts = new List<FaceCut>( intersectionCuts ),
                    Neighbor = negSolid
                };

                if ( faceCuts != null )
                {
                    intersectionCuts.Clear();
                }

                for ( var i = _faces.Count - 1; i >= 0; i-- )
                {
                    var face = _faces[i];

                    if ( faceCuts != null )
                    {
                        // Cut unbounded split plane to find shared split face.
                        // If faceCuts == null, we've already found it (posSubFace will be the whole face)

                        var splitPlaneCut = splitPlaneHelper.GetCut( face.Plane );
                        intersectionCuts.Split( splitPlaneCut, posCuts, negCuts );

                        if ( negCuts.Count != 0 )
                        {
                            Assert.IsFalse( posCuts.Count == 0 );

                            (intersectionCuts, posCuts) = (posCuts, intersectionCuts);
                        }
                    }

                    // Cut original face

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

                        foreach ( var subFace in face.SubFaces )
                        {
                            subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, negSolid );
                        }

                        continue;
                    }

                    // Otherwise split face in two

                    var posFace = new Face
                    {
                        Plane = face.Plane,
                        FaceCuts = new List<FaceCut>( posCuts ),
                        SubFaces = new List<SubFace>()
                    };

                    var negFace = discard
                        ? (Face?)null
                        : new Face
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

                            subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, negSolid, -cutPlane );
                            continue;
                        }

                        // Otherwise split sub-face in two

                        posFace.SubFaces.Add( new SubFace
                        {
                            FaceCuts = new List<FaceCut>( posCuts ),
                            MaterialIndex = subFace.MaterialIndex,
                            Neighbor = subFace.Neighbor
                        } );

                        subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, negSolid, -cutPlane );

                        negFace?.SubFaces.Add( new SubFace
                        {
                            FaceCuts = new List<FaceCut>( negCuts ),
                            MaterialIndex = subFace.MaterialIndex,
                            Neighbor = subFace.Neighbor
                        } );
                    }
                }

                var posSplitFace = new Face
                {
                    Plane = cutPlane,
                    FaceCuts = new List<FaceCut>( intersectionCuts )
                };

                posSplitFace.SubFaces = new List<SubFace>
                {
                    new SubFace
                    {
                        FaceCuts = new List<FaceCut>( posSplitFace.FaceCuts ),
                        Neighbor = negSolid
                    }
                };

                if ( faceCuts != null )
                {
                    // If cut was already constrained, add the constrained sub-face

                    posSplitFace.RemoveSubFacesInside( posSubFace.FaceCuts );
                    posSplitFace.SubFaces.Add( posSubFace );
                }

                _faces.Add( posSplitFace );
                negSolid?._faces.Add( posSplitFace.CloneFlipped( this ) );

                return (true, negSolid);
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( intersectionCuts );
                CsgHelpers.ReturnFaceCutList( posCuts );
                CsgHelpers.ReturnFaceCutList( negCuts );
            }
        }

        public void RemoveFromNeighbors( CsgConvexSolid replacement )
        {
            foreach ( var face in _faces )
            {
                foreach ( var subFace in face.SubFaces )
                {
                    subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, replacement );
                }
            }
        }

        private void ReplaceNeighbor( CsgPlane plane, CsgConvexSolid oldNeighbor, CsgConvexSolid newNeighbor )
        {
            if ( !TryGetFace( plane, out var face ) ) return;

            for ( var i = 0; i < face.SubFaces.Count; ++i )
            {
                var subFace = face.SubFaces[i];

                if ( subFace.Neighbor != oldNeighbor ) continue;
                
                subFace.Neighbor = newNeighbor;
                face.SubFaces[i] = subFace;
            }
        }

        private void ReplaceNeighbor( CsgPlane plane, CsgConvexSolid oldNeighbor, CsgConvexSolid newNeighbor, CsgPlane cutPlane )
        {
            if ( !TryGetFace( plane, out var face ) ) return;
            
            var helper = plane.GetHelper();

            var faceCut = helper.GetCut( cutPlane );

            var posCuts = CsgHelpers.RentFaceCutList();
            var negCuts = CsgHelpers.RentFaceCutList();

            try
            {
                for ( var i = face.SubFaces.Count - 1; i >= 0; --i )
                {
                    var subFace = face.SubFaces[i];

                    if ( subFace.Neighbor != oldNeighbor ) continue;

                    subFace.FaceCuts.Split( faceCut, posCuts, negCuts );

                    if ( posCuts.Count == 0 )
                    {
                        continue;
                    }

                    if ( negCuts.Count > 0 )
                    {
                        subFace.FaceCuts.Clear();
                        subFace.FaceCuts.AddRange( posCuts );

                        face.SubFaces.Add( new SubFace
                        {
                            FaceCuts = new List<FaceCut>( negCuts ),
                            MaterialIndex = subFace.MaterialIndex,
                            Neighbor = subFace.Neighbor
                        } );
                    }
                    
                    subFace.Neighbor = newNeighbor;
                    face.SubFaces[i] = subFace;
                }
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( posCuts );
                CsgHelpers.ReturnFaceCutList( negCuts );
            }
        }
        
        /// <summary>
        /// Merge sub-faces from another solid.
        /// </summary>
        public void MergeSubFacesFrom( CsgConvexSolid other )
        {
            if ( other.IsEmpty || IsEmpty ) return;
            
            foreach ( var thisFace in _faces )
            {
                if ( !other.TryGetFace( thisFace.Plane, out var otherFace ) )
                {
                    continue;
                }
                
                thisFace.RemoveSubFacesInside( otherFace.FaceCuts );

                // Now just add the sub-faces from other

                foreach ( var subFace in otherFace.SubFaces )
                {
                    thisFace.SubFaces.Add( subFace.Clone() );

                    subFace.Neighbor?.ReplaceNeighbor( -thisFace.Plane, other, this );
                }
            }
        }
    }
}
