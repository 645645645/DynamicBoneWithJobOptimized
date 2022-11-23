using System;
using UnityEngine;
using Unity.Mathematics;

namespace Plugins.FastDynamicBone
{
    public enum Direction
    {
        X, Y, Z
    }
    public enum Bound
    {
        Outside,
        Inside
    }
    public struct ColliderInfo
    {
        public int Index;

        // public bool IsGlobal;
        //local
        public Bound Bound;
        public Direction Direction;
        public float Height;
        public float Radius;
        public float Radius2;


        public float Scale;
        public float ScaledRadius;
        public float ScaledRadius2;
        public float C01Distance;
        public int CollideType;
        public float3 Center;
        public float3 C0;

        public float3 C1;

        //---
        //world
        // public int PrepareFrame;
        public float3 Position;
        public quaternion Rotation;

    }

    [AddComponentMenu("Fast Dynamic Bone/Fast Dynamic Bone Collider")]
    public class FastDynamicBoneCollider : MonoBehaviour
    {
        [Tooltip("高度的轴向：The axis of the capsule's height.")] [SerializeField]
        public Direction m_Direction = Direction.Y;

        [Tooltip("碰撞器中心位置， 相对于挂载物体的局部空间：The center of the sphere or capsule, in the object's local space.")]
        [SerializeField]
        public Vector3 m_Center = Vector3.zero;

        [Tooltip("把骨骼束缚在外面或里面：Constrain bones to outside bound or inside bound.")] [SerializeField]
        public Bound m_Bound = Bound.Outside;

        public int PrepareFrame { set; get; }

        [Tooltip("The radius of the sphere or capsule.")] [Min(0)] [SerializeField]
        public float m_Radius = 0.5f;

        [Tooltip("高度，大于0即为胶囊体：The height of the capsule.")] [Min(0)] [SerializeField]
        public float m_Height = 0;

        [Tooltip("The other radius of the capsule.")] [Min(0)] [SerializeField]
        public float m_Radius2 = 0;


        [HideInInspector] public ColliderInfo ColliderInfo;

        private bool hasInitialized;

        void OnValidate()
        {
            if (!hasInitialized)
                init();
            if (Application.isEditor && !Application.isPlaying)
            {
                ColliderInfo.Radius = math.max(m_Radius, 0);
                ColliderInfo.Height = math.max(m_Height, 0);
                ColliderInfo.Radius2 = Mathf.Max(m_Radius2, 0);
                ColliderInfo.Bound = m_Bound;
                ColliderInfo.Center = m_Center;
                ColliderInfo.Direction = m_Direction;
            }
        }

        void Awake()
        {
            init();
        }

        void init()
        {
            if (hasInitialized) return;
            ColliderInfo = new ColliderInfo
            {
                // IsGlobal = isGlobal,
                Center = m_Center,
                Radius = m_Radius,
                Height = m_Height,
                Radius2 = m_Radius2,
                Direction = m_Direction,
                Bound = m_Bound,
                Scale = transform.lossyScale.x,
            };
            hasInitialized = true;
        }

        //数据刷新 跟画gizmos耦合了。。
        public static void Prepare(ref ColliderInfo c)
        {
            float scale = c.Scale;
            float halfHeight = c.Height * 0.5f;
            if (c.Radius2 <= 0 || math.abs(c.Radius - c.Radius2) < 0.01f)
            {
                c.ScaledRadius = c.Radius * scale;

                float h = halfHeight - c.Radius;
                if (h <= 0)
                {
                    c.C0 = MathematicsUtil.LocalToWorldPosition(c.Position,
                        c.Rotation, c.Center);

                    if (c.Bound == Bound.Outside)
                    {
                        c.CollideType = 0;
                    }
                    else
                    {
                        c.CollideType = 1;
                    }
                }
                else
                {
                    float3 c0 = c.Center;
                    float3 c1 = c.Center;

                    switch (c.Direction)
                    {
                        case Direction.X:
                            c0.x += h;
                            c1.x -= h;
                            break;
                        case Direction.Y:
                            c0.y += h;
                            c1.y -= h;
                            break;
                        case Direction.Z:
                            c0.z += h;
                            c1.z -= h;
                            break;
                    }

                    c.C0 =
                        MathematicsUtil.LocalToWorldPosition(c.Position, c.Rotation, c0);
                    c.C1 =
                        MathematicsUtil.LocalToWorldPosition(c.Position, c.Rotation, c1);
                    c.C01Distance = math.distance(c.C1, c.C0);

                    if (c.Bound == Bound.Outside)
                    {
                        c.CollideType = 2;
                    }
                    else
                    {
                        c.CollideType = 3;
                    }
                }
            }
            else
            {
                float r = math.max(c.Radius, c.Radius2);
                if (halfHeight - r <= 0)
                {
                    c.ScaledRadius = r * scale;
                    c.C0 = MathematicsUtil.LocalToWorldPosition(c.Position,
                        c.Rotation, c.Center);

                    if (c.Bound == Bound.Outside)
                    {
                        c.CollideType = 0;
                    }
                    else
                    {
                        c.CollideType = 1;
                    }
                }
                else
                {
                    c.ScaledRadius = c.Radius * scale;
                    c.ScaledRadius2 = c.Radius2 * scale;

                    float h0 = halfHeight - c.Radius;
                    float h1 = halfHeight - c.Radius2;
                    float3 c0 = c.Center;
                    float3 c1 = c.Center;

                    switch (c.Direction)
                    {
                        case Direction.X:
                            c0.x += h0;
                            c1.x -= h1;
                            break;
                        case Direction.Y:
                            c0.y += h0;
                            c1.y -= h1;
                            break;
                        case Direction.Z:
                            c0.z += h0;
                            c1.z -= h1;
                            break;
                    }

                    c.C0 =
                        MathematicsUtil.LocalToWorldPosition(c.Position, c.Rotation, c0);
                    c.C1 =
                        MathematicsUtil.LocalToWorldPosition(c.Position, c.Rotation, c1);
                    c.C01Distance = math.distance(c.C1, c.C0);

                    if (c.Bound == Bound.Outside)
                    {
                        c.CollideType = 4;
                    }
                    else
                    {
                        c.CollideType = 5;
                    }
                }
            }
        }

        public static bool HandleCollision(in ColliderInfo collider, ref float3 particlePosition,
            in float particleRadius)
        {
            switch (collider.CollideType)
            {
                case 0:
                    return OutsideSphere(ref particlePosition, particleRadius, collider.C0, collider.ScaledRadius);
                case 1:
                    return InsideSphere(ref particlePosition, particleRadius, collider.C0, collider.ScaledRadius);
                case 2:
                    return OutsideCapsule(ref particlePosition, particleRadius, collider.C0, collider.C1,
                        collider.ScaledRadius, collider.C01Distance);
                case 3:
                    return InsideCapsule(ref particlePosition, particleRadius, collider.C0, collider.C1,
                        collider.ScaledRadius, collider.C01Distance);
                case 4:
                    return OutsideCapsule2(ref particlePosition, particleRadius, collider.C0, collider.C1,
                        collider.ScaledRadius, collider.ScaledRadius2, collider.C01Distance);
                case 5:
                    return InsideCapsule2(ref particlePosition, particleRadius, collider.C0, collider.C1,
                        collider.ScaledRadius, collider.ScaledRadius2, collider.C01Distance);
            }

            return false;
        }

        static bool OutsideSphere(ref float3 particlePosition, float particleRadius, float3 sphereCenter,
            float sphereRadius)
        {
            float r = sphereRadius + particleRadius;
            float r2 = r * r;
            float3 d = particlePosition - sphereCenter;
            float dlen2 = math.lengthsq(d);

            // if is inside sphere, project onto sphere surface
            if (dlen2 > 0 && dlen2 < r2)
            {
                float dlen = math.sqrt(dlen2);
                particlePosition = sphereCenter + d * (r / dlen);
                return true;
            }

            return false;
        }

        static bool InsideSphere(ref float3 particlePosition, float particleRadius, float3 sphereCenter,
            float sphereRadius)
        {
            float r = sphereRadius - particleRadius;
            float r2 = r * r;
            float3 d = particlePosition - sphereCenter;
            float dlen2 = math.lengthsq(d);

            // if is outside sphere, project onto sphere surface
            if (dlen2 > r2)
            {
                float dlen = math.sqrt(dlen2);
                particlePosition = sphereCenter + d * (r / dlen);
                return true;
            }

            return false;
        }

        static bool OutsideCapsule(ref float3 particlePosition, float particleRadius, float3 capsuleP0,
            float3 capsuleP1,
            float capsuleRadius, float dirlen)
        {
            float r = capsuleRadius + particleRadius;
            float r2 = r * r;
            float3 dir = capsuleP1 - capsuleP0;
            float3 d = particlePosition - capsuleP0;
            float t = math.dot(d, dir);

            if (t <= 0)
            {
                // check sphere1
                float dlen2 = math.lengthsq(d);
                if (dlen2 > 0 && dlen2 < r2)
                {
                    float dlen = math.sqrt(dlen2);
                    particlePosition = capsuleP0 + d * (r / dlen);
                    return true;
                }
            }
            else
            {
                float dirlen2 = dirlen * dirlen;
                if (t >= dirlen2)
                {
                    // check sphere2
                    d = particlePosition - capsuleP1;
                    float dlen2 = math.lengthsq(d);
                    if (dlen2 > 0 && dlen2 < r2)
                    {
                        float dlen = math.sqrt(dlen2);
                        particlePosition = capsuleP1 + d * (r / dlen);
                        return true;
                    }
                }
                else
                {
                    // check cylinder
                    float3 q = d - dir * (t / dirlen2);
                    float qlen2 = math.lengthsq(q);
                    if (qlen2 > 0 && qlen2 < r2)
                    {
                        float qlen = math.sqrt(qlen2);
                        particlePosition += q * ((r - qlen) / qlen);
                        return true;
                    }
                }
            }

            return false;
        }

        static bool InsideCapsule(ref float3 particlePosition, float particleRadius, float3 capsuleP0, float3 capsuleP1,
            float capsuleRadius, float dirlen)
        {
            float r = capsuleRadius - particleRadius;
            float r2 = r * r;
            float3 dir = capsuleP1 - capsuleP0;
            float3 d = particlePosition - capsuleP0;
            float t = math.dot(d, dir);

            if (t <= 0)
            {
                // check sphere1
                float dlen2 = math.lengthsq(d);
                if (dlen2 > r2)
                {
                    float dlen = math.sqrt(dlen2);
                    particlePosition = capsuleP0 + d * (r / dlen);
                    return true;
                }
            }
            else
            {
                float dirlen2 = dirlen * dirlen;
                if (t >= dirlen2)
                {
                    // check sphere2
                    d = particlePosition - capsuleP1;
                    float dlen2 = math.lengthsq(d);
                    if (dlen2 > r2)
                    {
                        float dlen = math.sqrt(dlen2);
                        particlePosition = capsuleP1 + d * (r / dlen);
                        return true;
                    }
                }
                else
                {
                    // check cylinder
                    float3 q = d - dir * (t / dirlen2);
                    float qlen2 = math.lengthsq(q);
                    if (qlen2 > r2)
                    {
                        float qlen = math.sqrt(qlen2);
                        particlePosition += q * ((r - qlen) / qlen);
                        return true;
                    }
                }
            }

            return false;
        }

        static bool OutsideCapsule2(ref float3 particlePosition, float particleRadius, float3 capsuleP0,
            float3 capsuleP1,
            float capsuleRadius0, float capsuleRadius1, float dirlen)
        {
            float3 dir = capsuleP1 - capsuleP0;
            float3 d = particlePosition - capsuleP0;
            float t = math.dot(d, dir);

            if (t <= 0)
            {
                // check sphere1
                float r = capsuleRadius0 + particleRadius;
                float r2 = r * r;
                float dlen2 = math.lengthsq(d);
                if (dlen2 > 0 && dlen2 < r2)
                {
                    float dlen = math.sqrt(dlen2);
                    particlePosition = capsuleP0 + d * (r / dlen);
                    return true;
                }
            }
            else
            {
                float dirlen2 = dirlen * dirlen;
                if (t >= dirlen2)
                {
                    // check sphere2
                    float r = capsuleRadius1 + particleRadius;
                    float r2 = r * r;
                    d = particlePosition - capsuleP1;
                    float dlen2 = math.lengthsq(d);
                    if (dlen2 > 0 && dlen2 < r2)
                    {
                        float dlen = math.sqrt(dlen2);
                        particlePosition = capsuleP1 + d * (r / dlen);
                        return true;
                    }
                }
                else
                {
                    // check cylinder
                    float3 q = d - dir * (t / dirlen2);
                    float qlen2 = math.lengthsq(q);

                    float klen = math.dot(d, dir / dirlen);
                    float r = math.lerp(capsuleRadius0, capsuleRadius1, klen / dirlen) + particleRadius;
                    float r2 = r * r;

                    if (qlen2 > 0 && qlen2 < r2)
                    {
                        float qlen = math.sqrt(qlen2);
                        particlePosition += q * ((r - qlen) / qlen);
                        return true;
                    }
                }
            }

            return false;
        }

        static bool InsideCapsule2(ref float3 particlePosition, float particleRadius, float3 capsuleP0,
            float3 capsuleP1,
            float capsuleRadius0, float capsuleRadius1, float dirlen)
        {
            float3 dir = capsuleP1 - capsuleP0;
            float3 d = particlePosition - capsuleP0;
            float t = math.dot(d, dir);

            if (t <= 0)
            {
                // check sphere1
                float r = capsuleRadius0 - particleRadius;
                float r2 = r * r;
                float dlen2 = math.lengthsq(d);
                if (dlen2 > r2)
                {
                    float dlen = math.sqrt(dlen2);
                    particlePosition = capsuleP0 + d * (r / dlen);
                    return true;
                }
            }
            else
            {
                float dirlen2 = dirlen * dirlen;
                if (t >= dirlen2)
                {
                    // check sphere2
                    float r = capsuleRadius1 - particleRadius;
                    float r2 = r * r;
                    d = particlePosition - capsuleP1;
                    float dlen2 = math.lengthsq(d);
                    if (dlen2 > r2)
                    {
                        float dlen = math.sqrt(dlen2);
                        particlePosition = capsuleP1 + d * (r / dlen);
                        return true;
                    }
                }
                else
                {
                    // check cylinder
                    float3 q = d - dir * (t / dirlen2);
                    float qlen2 = math.lengthsq(q);

                    float klen = math.dot(d, dir / dirlen);
                    float r = math.lerp(capsuleRadius0, capsuleRadius1, klen / dirlen) - particleRadius;
                    float r2 = r * r;

                    if (qlen2 > r2)
                    {
                        float qlen = math.sqrt(qlen2);
                        particlePosition += q * ((r - qlen) / qlen);
                        return true;
                    }
                }
            }

            return false;
        }

        void OnDrawGizmosSelected()
        {
            if (!enabled)
                return;

            if (!hasInitialized)
                init();
            ColliderInfo.Position = transform.position;
            ColliderInfo.Rotation = transform.rotation;
            ColliderInfo.Scale = transform.lossyScale.x;
            Prepare(ref ColliderInfo);

            if (m_Bound == Bound.Outside)
            {
                Gizmos.color = Color.yellow;
            }
            else
            {
                Gizmos.color = Color.magenta;
            }

            switch (ColliderInfo.CollideType)
            {
                case 0:
                case 1:
                    Gizmos.DrawWireSphere(ColliderInfo.C0, ColliderInfo.ScaledRadius);
                    break;
                case 2:
                case 3:
                    DrawCapsule(ColliderInfo.C0, ColliderInfo.C1, ColliderInfo.ScaledRadius, ColliderInfo.ScaledRadius);
                    break;
                case 4:
                case 5:
                    DrawCapsule(ColliderInfo.C0, ColliderInfo.C1, ColliderInfo.ScaledRadius,
                        ColliderInfo.ScaledRadius2);
                    break;
            }
        }

        static void DrawCapsule(float3 c0, float3 c1, float radius0, float radius1)
        {
            Gizmos.DrawLine(c0, c1);
            Gizmos.DrawWireSphere(c0, radius0);
            Gizmos.DrawWireSphere(c1, radius1);
        }
    }
}