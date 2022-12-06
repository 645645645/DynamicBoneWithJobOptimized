using Unity.Mathematics;
using UnityEngine.Jobs;


public static class MathematicsUtil
{
    public const float PI = 3.14159265f;
    public static readonly float3 right = new float3(1, 0, 0);
    public static readonly float3 up = new float3(0, 1, 0);
    public static readonly float3 forward = new float3(0, 0, 1);
    public static readonly float3 one = new float3(1, 1, 1);
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
    
    public static float3 GetMatrixPosition(float4x4 matrix)
    {
        return matrix.c3.xyz;
    }
    
    public static quaternion GetMatrixRotation(float4x4 matrix)
    {
        return quaternion.LookRotation(matrix.c2.xyz, matrix.c1.xyz);
    }

    public static float3 GetMatrixScale(float4x4 matrix)
    {
        float x = Length(matrix.c0);
        float y = Length(matrix.c1);
        float z = Length(matrix.c2);
        return new float3(x, y, z);
    }

    public static float3 MatrixMultiplyPoint3x4(float4x4 matrix, float4 pos)
    {
        return matrix.c0.xyz * pos.x + matrix.c1.xyz * pos.y + matrix.c2.xyz * pos.z + matrix.c3.xyz * pos.w;
    }

    // lossyScaleMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse * transform.local2World
    // or = (transform.worldToLocalMatrix * Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one)).inverse
    public static float GetLossyScaleX(TransformAccess transformAccess)
    {
        float4x4 world2Local = transformAccess.worldToLocalMatrix;
        float4x4 TR = float4x4.TRS(transformAccess.position, transformAccess.rotation, 1);
        float4x4 scaleMatrix = math.mul(world2Local, TR);
        return 1 / scaleMatrix.c0.x;
    }
    
    public static float Length(float2 vec)
    {
        double lengthSQ = math.lengthsq(vec);
        return lengthSQ > float.Epsilon ? (float)math.sqrt(lengthSQ) : 0f;
    }
    
    public static float Length(float3 vec)
    {
        double lengthSQ = math.lengthsq(vec);
        return lengthSQ > float.Epsilon ? (float)math.sqrt(lengthSQ) : 0f;
    }
    
    public static float Length(float4 vec)
    {
        double lengthSQ = math.lengthsq(vec);
        return lengthSQ > float.Epsilon ? (float)math.sqrt(lengthSQ) : 0f;
    }
    
    public static float2 Normalize(float2 vec)
    {
        double magsqr = math.dot(vec, vec);
        return magsqr > float.Epsilon ? (vec * new float2(math.rsqrt(magsqr))) : float2.zero;
    }
    
    public static float3 Normalize(float3 vec)
    {
        double magsqr = math.dot(vec, vec);
        return magsqr > float.Epsilon ? (vec * new float3(math.rsqrt(magsqr))) : float3.zero;
    }
    
    public static float4 Normalize(float4 vec)
    {
        double magsqr = math.dot(vec, vec);
        return magsqr > float.Epsilon ? (vec * new float4(math.rsqrt(magsqr))) : float4.zero;
    }
    
    //lossyscale有负缩放要补反方向... sign(parentScale)
    public static float3 TransformDirection(float3 parentWorldPos, quaternion parentWorldRot, float3 localDir)
    {
        float4x4 Local2WorldTR = float4x4.TRS(float3.zero, parentWorldRot, 1);
        //求个逆就有误差了..
        float4x4 Local2WorldTR_IT = math.transpose(math.inverse(Local2WorldTR));
        // return math.mul(Local2WorldTR_IT, new float4(localDir, 0)).xyz;
        return math.mul((float3x3)Local2WorldTR_IT, localDir).xyz;
    }
    
    public static float3 InverseTransformDirection(float3 parentWorldPos, quaternion parentWorldRot, float3 worldDir)
    {
        float4x4 World2LocalTR_I = float4x4.TRS(float3.zero, parentWorldRot, 1);
        float4x4 World2LocalTR_IT = math.transpose((World2LocalTR_I));
        return math.mul((float3x3)World2LocalTR_IT, worldDir).xyz;
    }
    
    // public static quaternion FromToRotation(float3 fromDirection, float3 toDirection)
    // {
    //     float fromSq = math.dot(fromDirection, fromDirection);
    //     float toSq = math.dot(toDirection, toDirection);
    //     if(fromSq <= float.Epsilon || toSq <= float.Epsilon)
    //         return quaternion.identity;
    //     
    //     float3 unitFrom = fromDirection * math.rsqrt(fromSq);
    //     float3 unitTo = toDirection * math.rsqrt(toSq);
    //     float d = math.dot(unitFrom, unitTo);
    //     if (d >= 1f)
    //     {
    //         return quaternion.identity;
    //     }
    //     else if(d <= -1f)
    //     {
    //         float3 axis = math.cross(unitFrom, right);
    //         return quaternion.AxisAngle(math.normalize(axis), PI);
    //     }
    //     else
    //     {
    //         float s = 1 + d;
    //         float3 v = math.cross(unitFrom, unitTo);
    //         quaternion result = new quaternion(v.x, v.y, v.z, s);
    //         return math.normalize(result);
    //     }
    // }
    
    public static quaternion FromToRotation(float3 from, float3 to)
    {
        float3
            v1 = Normalize(from),
            v2 = Normalize(to),
            cr = math.cross(v1, v2);
        float4 q = new float4(cr, 1 + math.dot(v1, v2));
        return math.normalize(q);
    }
}