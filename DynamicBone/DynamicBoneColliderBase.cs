﻿using UnityEngine;


public class DynamicBoneColliderBase : MonoBehaviour
{

    [Tooltip("The axis of the capsule's height.")]
    public Direction m_Direction = Direction.Y;

    [Tooltip("The center of the sphere or capsule, in the object's local space.")]
    public Vector3 m_Center = Vector3.zero;


    [Tooltip("Constrain bones to outside bound or inside bound.")]
    public Bound m_Bound = Bound.Outside;

    public enum Direction
    {
        X, Y, Z
    }
    public enum Bound
    {
        Outside,
        Inside
    }
    public int PrepareFrame { set; get; }

    public virtual void Start()
    {        
    }

    public virtual void Prepare()
    {
    }

    public virtual bool Collide(ref Vector3 particlePosition, float particleRadius)
    {
        return false;
    }
}
