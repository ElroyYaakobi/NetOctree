﻿// <copyright file="BoundsOctreeNode.cs">
//     Distributed under the BSD Licence (see LICENCE file).
//     
//     Copyright (c) 2014, Nition, http://www.momentstudio.co.nz/
//     Copyright (c) 2017, Máté Cserép, http://codenet.hu
//     All rights reserved.
// </copyright>

using UnityEngine;

namespace mcserep.Octree
{
    using System.Collections.Generic;

    public partial class BoundsOctree<T>
    {
        /// <summary>
        /// A node in a BoundsOctree
        /// </summary>
        private class Node
        {
            /// <summary>
            /// Centre of this node
            /// </summary>
            public Vector3 Center { get; private set; }

            /// <summary>
            /// Length of this node if it has a looseness of 1.0
            /// </summary>
            public float BaseLength { get; private set; }

            /// <summary>
            /// Looseness value for this node
            /// </summary>
            private float _looseness;

            /// <summary>
            /// Minimum size for a node in this octree
            /// </summary>
            private float _minSize;

            /// <summary>
            /// Actual length of sides, taking the looseness value into account
            /// </summary>
            private float _adjLength;

            /// <summary>
            /// Bounding box that represents this node
            /// </summary>
            private Bounds _bounds;

            /// <summary>
            /// Objects in this node
            /// </summary>
            private readonly List<OctreeObject> _objects = new List<OctreeObject>();

            /// <summary>
            /// Child nodes, if any
            /// </summary>
            private Node[] _children = null;

            /// <summary>
            /// Bounds of potential children to this node. These are actual size (with looseness taken into account), not base size
            /// </summary>
            private Bounds[] _childBounds;

            /// <summary>
            /// If there are already NumObjectsAllowed in a node, we split it into children
            /// </summary>
            /// <remarks>
            /// A generally good number seems to be something around 8-15
            /// </remarks>
            private const int NumObjectsAllowed = 8;

            /// <summary>
            /// Gets a value indicating whether this node has children
            /// </summary>
            private bool HasChildren => _children != null;

            /// <summary>
            /// An object in the octree
            /// </summary>
            private class OctreeObject
            {
                /// <summary>
                /// Object content
                /// </summary>
                public T Obj;

                /// <summary>
                /// Object bounds
                /// </summary>
                public Bounds Bounds;
            }

            /// <summary>
            /// Gets the bounding box that represents this node
            /// </summary>
            public Bounds Bounds => _bounds;

            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="baseLengthVal">Length of this node, not taking looseness into account.</param>
            /// <param name="minSizeVal">Minimum size of nodes in this octree.</param>
            /// <param name="loosenessVal">Multiplier for baseLengthVal to get the actual size.</param>
            /// <param name="centerVal">Centre position of this node.</param>
            public Node(float baseLengthVal, float minSizeVal, float loosenessVal, Vector3 centerVal)
            {
                SetValues(baseLengthVal, minSizeVal, loosenessVal, centerVal);
            }

            // #### PUBLIC METHODS ####

            /// <summary>
            /// Add an object.
            /// </summary>
            /// <param name="obj">Object to add.</param>
            /// <param name="objBounds">3D bounding box around the object.</param>
            /// <returns>True if the object fits entirely within this node.</returns>
            public bool Add(T obj, Bounds objBounds)
            {
                if (!Encapsulates(_bounds, objBounds))
                {
                    return false;
                }
                SubAdd(obj, objBounds);
                return true;
            }

            /// <summary>
            /// Remove an object. Makes the assumption that the object only exists once in the tree.
            /// </summary>
            /// <param name="obj">Object to remove.</param>
            /// <returns>True if the object was removed successfully.</returns>
            public bool Remove(T obj)
            {
                bool removed = false;

                for (int i = 0; i < _objects.Count; i++)
                {
                    if (_objects[i].Obj.Equals(obj))
                    {
                        removed = _objects.Remove(_objects[i]);
                        break;
                    }
                }

                if (!removed && _children != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        removed = _children[i].Remove(obj);
                        if (removed) break;
                    }
                }

                if (removed && _children != null)
                {
                    // Check if we should merge nodes now that we've removed an item
                    if (ShouldMerge())
                    {
                        Merge();
                    }
                }

                return removed;
            }

            /// <summary>
            /// Removes the specified object at the given position. Makes the assumption that the object only exists once in the tree.
            /// </summary>
            /// <param name="obj">Object to remove.</param>
            /// <param name="objBounds">3D bounding box around the object.</param>
            /// <returns>True if the object was removed successfully.</returns>
            public bool Remove(T obj, Bounds objBounds)
            {
                if (!Encapsulates(_bounds, objBounds))
                {
                    return false;
                }
                return SubRemove(obj, objBounds);
            }

            /// <summary>
            /// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
            /// </summary>
            /// <param name="checkBounds">Bounds to check.</param>
            /// <returns>True if there was a collision.</returns>
            public bool IsColliding(ref Bounds checkBounds)
            {
                // Are the input bounds at least partially in this node?
                if (!_bounds.Intersects(checkBounds))
                {
                    return false;
                }

                // Check against any objects in this node
                for (int i = 0; i < _objects.Count; i++)
                {
                    if (_objects[i].Bounds.Intersects(checkBounds))
                    {
                        return true;
                    }
                }

                // Check children
                if (_children != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if (_children[i].IsColliding(ref checkBounds))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>
            /// Check if the specified ray intersects with anything in the tree. See also: GetColliding.
            /// </summary>
            /// <param name="checkRay">Ray to check.</param>
            /// <param name="maxDistance">Distance to check.</param>
            /// <returns>True if there was a collision.</returns>
            public bool IsColliding(ref Ray checkRay, float maxDistance = float.PositiveInfinity)
            {
                // Is the input ray at least partially in this node?
                float distance;
                if (!_bounds.IntersectRay(checkRay, out distance) || distance > maxDistance)
                {
                    return false;
                }

                // Check against any objects in this node
                for (int i = 0; i < _objects.Count; i++)
                {
                    if (_objects[i].Bounds.IntersectRay(checkRay, out distance) && distance <= maxDistance)
                    {
                        return true;
                    }
                }

                // Check children
                if (_children != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if (_children[i].IsColliding(ref checkRay, maxDistance))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>
            /// Returns an array of objects that intersect with the specified bounds, if any. Otherwise returns an empty array. See also: IsColliding.
            /// </summary>
            /// <param name="checkBounds">Bounds to check. Passing by ref as it improves performance with structs.</param>
            /// <param name="result">List result.</param>
            /// <returns>Objects that intersect with the specified bounds.</returns>
            public void GetColliding(ref Bounds checkBounds, List<T> result)
            {
                // Are the input bounds at least partially in this node?
                if (!_bounds.Intersects(checkBounds))
                {
                    return;
                }

                // Check against any objects in this node
                for (int i = 0; i < _objects.Count; i++)
                {
                    if (_objects[i].Bounds.Intersects(checkBounds))
                    {
                        result.Add(_objects[i].Obj);
                    }
                }

                // Check children
                if (_children != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        _children[i].GetColliding(ref checkBounds, result);
                    }
                }
            }

            /// <summary>
            /// Returns an array of objects that intersect with the specified ray, if any. Otherwise returns an empty array. See also: IsColliding.
            /// </summary>
            /// <param name="checkRay">Ray to check. Passing by ref as it improves performance with structs.</param>
            /// <param name="maxDistance">Distance to check.</param>
            /// <param name="result">List result.</param>
            /// <returns>Objects that intersect with the specified ray.</returns>
            public void GetColliding(ref Ray checkRay, List<T> result, float maxDistance = float.PositiveInfinity)
            {
                float distance;
                // Is the input ray at least partially in this node?
                if (!_bounds.IntersectRay(checkRay, out distance) || distance > maxDistance)
                {
                    return;
                }

                // Check against any objects in this node
                for (int i = 0; i < _objects.Count; i++)
                {
                    if (_objects[i].Bounds.IntersectRay(checkRay, out distance) && distance <= maxDistance)
                    {
                        result.Add(_objects[i].Obj);
                    }
                }

                // Check children
                if (_children != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        _children[i].GetColliding(ref checkRay, result, maxDistance);
                    }
                }
            }

            /// <summary>
            /// Set the 8 children of this octree.
            /// </summary>
            /// <param name="childOctrees">The 8 new child nodes.</param>
            public void SetChildren(Node[] childOctrees)
            {
                if (childOctrees.Length != 8)
                {
                    Debug.LogError("Child octree array must be length 8. Was length: " + childOctrees.Length);
                    return;
                }

                _children = childOctrees;
            }

            /// <summary>
            /// We can shrink the octree if:
            /// - This node is >= double minLength in length
            /// - All objects in the root node are within one octant
            /// - This node doesn't have children, or does but 7/8 children are empty
            /// We can also shrink it if there are no objects left at all!
            /// </summary>
            /// <param name="minLength">Minimum dimensions of a node in this octree.</param>
            /// <returns>The new root, or the existing one if we didn't shrink.</returns>
            public Node ShrinkIfPossible(float minLength)
            {
                if (BaseLength < (2 * minLength))
                {
                    return this;
                }
                if (_objects.Count == 0 && (_children == null || _children.Length == 0))
                {
                    return this;
                }

                // Check objects in root
                var bestFit = -1;
                for (int i = 0; i < _objects.Count; i++)
                {
                    var curObj = _objects[i];
                    var newBestFit = BestFitChild(curObj.Bounds.center);
                    if (i == 0 || newBestFit == bestFit)
                    {
                        // In same octant as the other(s). Does it fit completely inside that octant?
                        if (Encapsulates(_childBounds[newBestFit], curObj.Bounds))
                        {
                            if (bestFit < 0)
                            {
                                bestFit = newBestFit;
                            }
                        }
                        else
                        {
                            // Nope, so we can't reduce. Otherwise we continue
                            return this;
                        }
                    }
                    else
                    {
                        return this; // Can't reduce - objects fit in different octants
                    }
                }

                // Check objects in children if there are any
                if (_children != null)
                {
                    bool childHadContent = false;
                    for (int i = 0; i < _children.Length; i++)
                    {
                        if (_children[i].HasAnyObjects())
                        {
                            if (childHadContent)
                            {
                                return this; // Can't shrink - another child had content already
                            }
                            if (bestFit >= 0 && bestFit != i)
                            {
                                return this; // Can't reduce - objects in root are in a different octant to objects in child
                            }
                            childHadContent = true;
                            bestFit = i;
                        }
                    }
                }

                // Can reduce
                if (_children == null)
                {
                    // We don't have any children, so just shrink this node to the new size
                    // We already know that everything will still fit in it
                    SetValues(BaseLength / 2, _minSize, _looseness, _childBounds[bestFit].center);
                    return this;
                }

                // No objects in entire octree
                if (bestFit == -1)
                {
                    return this;
                }

                // We have children. Use the appropriate child as the new root node
                return _children[bestFit];
            }

            /// <summary>
            /// Find which child node this object would be most likely to fit in.
            /// </summary>
            /// <param name="objBoundsCenter">The object's bounds center.</param>
            /// <returns>One of the eight child octants.</returns>
            public int BestFitChild(Vector3 objBoundsCenter)
            {
                return (objBoundsCenter.x <= Center.x ? 0 : 1)
                       + (objBoundsCenter.y >= Center.y ? 0 : 4)
                       + (objBoundsCenter.z <= Center.z ? 0 : 2);
            }

            /// <summary>
            /// Checks if this node or anything below it has something in it.
            /// </summary>
            /// <returns>True if this node or any of its children, grandchildren etc have something in them</returns>
            public bool HasAnyObjects()
            {
                if (_objects.Count > 0) return true;

                if (_children != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if (_children[i].HasAnyObjects()) return true;
                    }
                }

                return false;
            }

            public void DrawGizmos()
            {
                Gizmos.DrawWireCube(Center, _bounds.size);
                if (HasChildren)
                {
                    foreach (var child in _children)
                    {
                        child.DrawGizmos();
                    }
                }
            }
            
            // #### PRIVATE METHODS ####

            /// <summary>
            /// Set values for this node. 
            /// </summary>
            /// <param name="baseLengthVal">Length of this node, not taking looseness into account.</param>
            /// <param name="minSizeVal">Minimum size of nodes in this octree.</param>
            /// <param name="loosenessVal">Multiplier for baseLengthVal to get the actual size.</param>
            /// <param name="centerVal">Center position of this node.</param>
            private void SetValues(float baseLengthVal, float minSizeVal, float loosenessVal, Vector3 centerVal)
            {
                BaseLength = baseLengthVal;
                _minSize = minSizeVal;
                _looseness = loosenessVal;
                Center = centerVal;
                _adjLength = _looseness * baseLengthVal;

                // Create the bounding box.
                var size = new Vector3(_adjLength, _adjLength, _adjLength);
                _bounds = new Bounds(Center, size);

                var quarter = BaseLength / 4f;
                var childActualLength = (BaseLength / 2) * _looseness;
                var childActualSize = new Vector3(childActualLength, childActualLength, childActualLength);
                _childBounds = new Bounds[8];
                _childBounds[0] = new Bounds(Center + new Vector3(-quarter, quarter, -quarter), childActualSize);
                _childBounds[1] = new Bounds(Center + new Vector3(quarter, quarter, -quarter), childActualSize);
                _childBounds[2] = new Bounds(Center + new Vector3(-quarter, quarter, quarter), childActualSize);
                _childBounds[3] = new Bounds(Center + new Vector3(quarter, quarter, quarter), childActualSize);
                _childBounds[4] = new Bounds(Center + new Vector3(-quarter, -quarter, -quarter), childActualSize);
                _childBounds[5] = new Bounds(Center + new Vector3(quarter, -quarter, -quarter), childActualSize);
                _childBounds[6] = new Bounds(Center + new Vector3(-quarter, -quarter, quarter), childActualSize);
                _childBounds[7] = new Bounds(Center + new Vector3(quarter, -quarter, quarter), childActualSize);
            }

            /// <summary>
            /// Private counterpart to the public Add method.
            /// </summary>
            /// <param name="obj">Object to add.</param>
            /// <param name="objBounds">3D bounding box around the object.</param>
            private void SubAdd(T obj, Bounds objBounds)
            {
                // We know it fits at this level if we've got this far

                // We always put things in the deepest possible child
                // So we can skip some checks if there are children already
                if (!HasChildren)
                {
                    // Just add if few objects are here, or children would be below min size
                    if (_objects.Count < NumObjectsAllowed || (BaseLength / 2) < _minSize)
                    {
                        OctreeObject newObj = new OctreeObject { Obj = obj, Bounds = objBounds };
                        _objects.Add(newObj);
                        return; // We're done. No children yet
                    }

                    // Fits at this level, but we can go deeper. Would it fit there?
                    // Create the 8 children
                    if (_children == null)
                    {
                        Split();
                        if (_children == null)
                        {
                            Debug.LogError("Child creation failed for an unknown reason. Early exit.");
                            return;
                        }

                        // Now that we have the new children, see if this node's existing objects would fit there
                        for (int i = _objects.Count - 1; i >= 0; i--)
                        {
                            OctreeObject existingObj = _objects[i];
                            // Find which child the object is closest to based on where the
                            // object's center is located in relation to the octree's center
                            int bestFitChild = BestFitChild(existingObj.Bounds.center);
                            // Does it fit?
                            if (Encapsulates(_children[bestFitChild]._bounds, existingObj.Bounds))
                            {
                                _children[bestFitChild].SubAdd(existingObj.Obj, existingObj.Bounds); // Go a level deeper					
                                _objects.Remove(existingObj); // Remove from here
                            }
                        }
                    }
                }

                // Handle the new object we're adding now
                int bestFit = BestFitChild(objBounds.center);
                if (Encapsulates(_children[bestFit]._bounds, objBounds))
                {
                    _children[bestFit].SubAdd(obj, objBounds);
                }
                else
                {
                    // Didn't fit in a child. We'll have to it to this node instead
                    OctreeObject newObj = new OctreeObject { Obj = obj, Bounds = objBounds };
                    _objects.Add(newObj);
                }
            }

            /// <summary>
            /// Private counterpart to the public <see cref="Remove(T, BoundingBox)"/> method.
            /// </summary>
            /// <param name="obj">Object to remove.</param>
            /// <param name="objBounds">3D bounding box around the object.</param>
            /// <returns>True if the object was removed successfully.</returns>
            private bool SubRemove(T obj, Bounds objBounds)
            {
                bool removed = false;

                for (int i = 0; i < _objects.Count; i++)
                {
                    if (_objects[i].Obj.Equals(obj))
                    {
                        removed = _objects.Remove(_objects[i]);
                        break;
                    }
                }

                if (!removed && _children != null)
                {
                    int bestFitChild = BestFitChild(objBounds.center);
                    removed = _children[bestFitChild].SubRemove(obj, objBounds);
                }

                if (removed && _children != null)
                {
                    // Check if we should merge nodes now that we've removed an item
                    if (ShouldMerge())
                    {
                        Merge();
                    }
                }

                return removed;
            }

            /// <summary>
            /// Splits the octree into eight children.
            /// </summary>
            private void Split()
            {
                float quarter = BaseLength / 4f;
                float newLength = BaseLength / 2;
                _children = new Node[8];
                _children[0] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(-quarter, quarter, -quarter));
                _children[1] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(quarter, quarter, -quarter));
                _children[2] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(-quarter, quarter, quarter));
                _children[3] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(quarter, quarter, quarter));
                _children[4] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(-quarter, -quarter, -quarter));
                _children[5] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(quarter, -quarter, -quarter));
                _children[6] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(-quarter, -quarter, quarter));
                _children[7] = new Node(
                    newLength,
                    _minSize,
                    _looseness,
                    Center + new Vector3(quarter, -quarter, quarter));
            }

            /// <summary>
            /// Merge all children into this node - the opposite of Split.
            /// Note: We only have to check one level down since a merge will never happen if the children already have children,
            /// since THAT won't happen unless there are already too many objects to merge.
            /// </summary>
            private void Merge()
            {
                // Note: We know children != null or we wouldn't be merging
                for (int i = 0; i < 8; i++)
                {
                    Node curChild = _children[i];
                    int numObjects = curChild._objects.Count;
                    for (int j = numObjects - 1; j >= 0; j--)
                    {
                        OctreeObject curObj = curChild._objects[j];
                        _objects.Add(curObj);
                    }
                }
                // Remove the child nodes (and the objects in them - they've been added elsewhere now)
                _children = null;
            }

            /// <summary>
            /// Checks if outerBounds encapsulates innerBounds.
            /// </summary>
            /// <param name="outerBounds">Outer bounds.</param>
            /// <param name="innerBounds">Inner bounds.</param>
            /// <returns>True if innerBounds is fully encapsulated by outerBounds.</returns>
            private static bool Encapsulates(Bounds outerBounds, Bounds innerBounds)
            {
                return outerBounds.Contains(innerBounds.min) && outerBounds.Contains(innerBounds.max);
            }

            /// <summary>
            /// Checks if there are few enough objects in this node and its children that the children should all be merged into this.
            /// </summary>
            /// <returns>True there are less or the same amount of objects in this and its children than <see cref="NumObjectsAllowed"/>.</returns>
            private bool ShouldMerge()
            {
                int totalObjects = _objects.Count;
                if (_children != null)
                {
                    foreach (Node child in _children)
                    {
                        if (child._children != null)
                        {
                            // If any of the *children* have children, there are definitely too many to merge,
                            // or the child would have been merged already
                            return false;
                        }
                        totalObjects += child._objects.Count;
                    }
                }
                return totalObjects <= NumObjectsAllowed;
            }
        }
    }
}