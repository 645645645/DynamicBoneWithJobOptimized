using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Jobs;


public static class MathematicsUtil
{
    public const float PI = 3.141593f;
    public static readonly float3 right = new float3(1, 0, 0);
    public static float3 LocalToWorldPosition(float3 parentPosition, quaternion parentRotation, float3 targetLocalPosition)
    {
        return parentPosition + math.mul(parentRotation, targetLocalPosition);
    }

    public static quaternion LocalToWorldRotation(quaternion parentRotation, quaternion targetLocalRotation)
    {
        return math.mul(parentRotation, targetLocalRotation);
    }

    public static float3 WorldToLocalPosition(float3 parentPosition, quaternion parentRotation, float3 targetWorldPosition)
    {
        return float3.zero;
    }

    public static quaternion WorldToLocalRotation(quaternion parentRotation, quaternion targetWorldRotation)
    {
        return quaternion.identity;
    }

    // lossyScaleMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse * transform.local2World
    // or = (transform.worldToLocalMatrix * Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one)).inverse
    public static float GetLossyScaleX(TransformAccess transformAccess)
    {
        float4x4 world2Local = transformAccess.worldToLocalMatrix;
        float4x4 TR = float4x4.TRS(transformAccess.position, transformAccess.rotation, new float3(1));
        float4x4 scaleMatrix = math.mul(world2Local, TR);
        return 1 / scaleMatrix.c0.x;
    }

    public static float Length(float3 vec)
    {
        float lengthSQ = math.lengthsq(vec);
        if (lengthSQ < float.Epsilon)
            return 0f;

        // return math.length(vec);
        return math.sqrt(lengthSQ);
    }

    public static float4 Normalize(float4 vec)
    {
        double magsqr = math.dot(vec, vec);
        return magsqr > 0.00001d ? (vec / new float4(math.sqrt(magsqr))) : float4.zero;
    }

    public static quaternion FromToRotation(float3 fromDirection, float3 toDirection)
    {
        float3 unitFrom = math.normalizesafe(fromDirection);
        float3 unitTo = math.normalizesafe(toDirection);
        float d = math.dot(unitFrom, unitTo);
        if (d >= 1f)
        {
            return quaternion.identity;
        }
        else if(d <= -1f)
        {
            float3 axis = math.cross(unitFrom, right);
            return quaternion.AxisAngle(math.normalize(axis), PI);
        }
        else
        {
            float s = 1 + d;
            float3 v = math.cross(unitFrom, unitTo);
            quaternion result = new quaternion(v.x, v.y, v.z, s);
            return math.normalizesafe(result);
        }
    }

}