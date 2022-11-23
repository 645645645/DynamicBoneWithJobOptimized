using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

namespace Plugins.FastDynamicBone
{
    public class DynamicBoneCacheLevel
    {
        [Header("每个Tree的 Particle容量")] public int MaxParticleLimit; //别填0

        [Header("最大Tree（root）容量")] public int MaxHeadLimit;

        // [Header("每组Collider最大容量")]
        public int MaxColliderLimit; //目前Collider没有分组啥的， 头发 裙子都共用同一组..
        protected int MaxParticleLength;

        protected int MaxColliderLength;

        //lateUpdate里更新的，不是add remove
        protected int colliderLength;
        protected int particleLength;
        protected int headLength;

        // [NonSerialized] public int treeCount = 0;
        public List<FastDynamicBone.ParticleTree> treeList; //取head collider particles以此index
        public List<FastDynamicBoneCollider> colliderList;
        public NativeList<HeadInfo> headInfoList;
        public NativeList<ParticleInfo> particleInfoList;

        public NativeList<ColliderInfo> colliderInfoList;

        //TransformAccessArray, remove是NativeList.. read是array..
        public TransformAccessArray rootTransformAccessArray;
        public TransformAccessArray headTransformAccessArray;
        public TransformAccessArray particleTransformAccessArray;
        public TransformAccessArray colliderTransformAccessArray;

        /// <summary>
        /// 前两个得根据项目情况填，第三个差不多行了
        /// </summary>
        /// <param name="maxParticleLimit">单个动态骨骼树的节点最大数量</param>
        /// <param name="maxColliderLimit">每组Collider的最大数量</param>
        /// <param name="maxHeadLimit"></param>
        public DynamicBoneCacheLevel(int maxParticleLimit, int maxColliderLimit = 20, int maxHeadLimit = 100)
        {
            this.MaxParticleLimit = maxParticleLimit;
            this.MaxColliderLimit = maxColliderLimit;
            this.MaxHeadLimit = maxHeadLimit;
            this.MaxParticleLength = maxHeadLimit * maxParticleLimit;
            this.MaxColliderLength = maxHeadLimit * maxColliderLimit;
        }

        public int GetTreeCount() => treeList.Count;

        public void InitNativeTable()
        {
            treeList = new List<FastDynamicBone.ParticleTree>();
            rootTransformAccessArray = new TransformAccessArray(MaxHeadLimit, 64);

            colliderList = new List<FastDynamicBoneCollider>();

            headInfoList = new NativeList<HeadInfo>(MaxHeadLimit, Allocator.Persistent);
            headTransformAccessArray = new TransformAccessArray(MaxHeadLimit, 64);
            //
            particleInfoList = new NativeList<ParticleInfo>(MaxParticleLength, Allocator.Persistent);
            particleTransformAccessArray = new TransformAccessArray(MaxParticleLength, 64);

            colliderInfoList = new NativeList<ColliderInfo>(MaxColliderLength, Allocator.Persistent);
            colliderTransformAccessArray = new TransformAccessArray(MaxColliderLength, 64);
        }

        public bool RemoveTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
        {
            int index = treeList.IndexOf(tree);
            if (index == -1) return false;
            
            treeList.RemoveAt(index);

            headInfoList.RemoveAtSwapBack(index);
            headTransformAccessArray.RemoveAtSwapBack(index);

            rootTransformAccessArray.RemoveAtSwapBack(index);

            int offset = index * this.MaxParticleLimit;
            for (int i = offset + MaxParticleLimit - 1; i >= offset; i--)
            {
                particleInfoList.RemoveAtSwapBack(i);
                particleTransformAccessArray.RemoveAtSwapBack(i);
            }

            offset = index * MaxColliderLimit;
            for (int i = offset + MaxColliderLimit - 1; i >= offset; i--)
            {
                colliderList.RemoveAt(i);
                colliderInfoList.RemoveAtSwapBack(i);
                colliderTransformAccessArray.RemoveAtSwapBack(i);
            }
            
            headLength--;
            colliderLength -= MaxColliderLimit;
            particleLength -= MaxParticleLimit;
            return true;
        }

        public bool AddTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
        {
            if (GetTreeCount() >= MaxHeadLimit)
            {
                Debug.LogWarningFormat("DynamicBoneManager : root out of range ~");
                return false;
            }

            int index = treeList.IndexOf(tree);

            if (index != -1)
            {
                //重复添加
                return false;
            }


            // index = treeList.Count;
            treeList.Add(tree);

            rootTransformAccessArray.Add(target.transform);

            int m_ParticleCount = tree.m_Particles.Count;
            var targetCollider = target.m_Colliders;
            int cCount = targetCollider.Count;
            int colliderCount = 0; //去空和重复统计

            for (int i = 0; i < MaxColliderLimit; i++)
            {
                if (i < cCount)
                {
                    FastDynamicBoneCollider c = targetCollider[i];
                    if (c != null)
                    {
                        colliderList.Add(c);
                        colliderInfoList.Add(c.ColliderInfo);
                        colliderTransformAccessArray.Add(c.transform);
                        colliderCount++;
                    }
                    else
                    {
                        Debug.LogWarningFormat("DynamicBone : Collider has someone null.");
                    }
                }
                else
                {
                    colliderList.Add(null);
                    colliderInfoList.Add(new ColliderInfo());
                    colliderTransformAccessArray.Add(null);
                }
            }

            HeadInfo head = new HeadInfo()
            {
                // m_Index = index,//删除index会变..这个不要了，每次重取indexof
                // m_RootWorldToLocalMatrix = tree.m_RootWorldToLocalMatrix,
                m_RootLocalToWorldMatrix = tree.m_RootLocalToWorldMatrix,
                m_Gravity = target.m_Gravity,
                m_LocalGravity = tree.m_LocalGravity,
                m_ObjectMove = target.m_ObjectMove,
                m_ObjectPrevPosition = target.m_ObjectPrevPosition,
                m_Force = target.m_Force,
                m_ObjectScale = target.m_ObjectScale,
                m_Weight = target.m_Weight,
                // m_BoneTotalLength = tree.m_BoneTotalLength,
                m_ParticleCount = m_ParticleCount,
                m_ColliderCount = colliderCount,
                m_MaxColliderLimit = MaxColliderLimit,
                m_FreezeAxis = target.m_FreezeAxis,
                m_NeedUpdate = (m_ParticleCount > 0) && (target.m_Weight > 0)
            };
            headInfoList.Add(head);
            headTransformAccessArray.Add(tree.m_Root);

            int pCount = m_ParticleCount;
            int pLimit = MaxParticleLimit;
            for (int i = 0; i < pLimit; i++)
            {
                if (i < pCount)
                {
                    var p = tree.m_Particles[i];
                    ParticleInfo pInfo = new ParticleInfo()
                    {
                        m_ParentIndex = p.m_ParentIndex,
                        m_ChildCount = p.m_ChildCount,
                        m_MaxParticleLimit = pLimit,
                        m_Damping = p.m_Damping,
                        m_Elasticity = p.m_Elasticity,
                        m_Stiffness = p.m_Stiffness,
                        m_Inert = p.m_Inert,
                        m_Friction = p.m_Friction,
                        m_Radius = p.m_Radius,
                        // m_BoneLength = p.m_BoneLength,
                        m_isCollide = p.m_isCollide,
                        m_TransformNotNull = p.m_TransformNotNull,

                        m_Position = p.m_Position,
                        m_PrevPosition = p.m_PrevPosition,
                        m_EndOffset = p.m_EndOffset,
                        m_InitLocalPosition = p.m_InitLocalPosition,
                        m_InitLocalRotation = p.m_InitLocalRotation
                    };
                    particleInfoList.Add(pInfo);
                    particleTransformAccessArray.Add(tree.m_Particles[i].m_Transform);
                }
                else
                {
                    //超过  占位/空 即冗余
                    particleInfoList.Add(new ParticleInfo() { });
                    particleTransformAccessArray.Add(null);
                }
            }

            headLength++;
            colliderLength += MaxColliderLimit;
            particleLength += MaxParticleLimit;
            return true;
        }

        public void Dispose()
        {
            if (rootTransformAccessArray.isCreated) rootTransformAccessArray.Dispose();
            if (headInfoList.IsCreated) headInfoList.Dispose();
            if (headTransformAccessArray.isCreated) headTransformAccessArray.Dispose();
            if (particleInfoList.IsCreated) particleInfoList.Dispose();
            if (particleTransformAccessArray.isCreated) particleTransformAccessArray.Dispose();
            if (colliderInfoList.IsCreated) colliderInfoList.Dispose();
            if (colliderTransformAccessArray.isCreated) colliderTransformAccessArray.Dispose();
        }

        public JobHandle PreUpdateJob(JobHandle dependencies)
        {
            return new InitTransformsJob()
            {
                ParticleInfos = particleInfoList
            }.Schedule(particleTransformAccessArray, dependencies);
        }

        public JobHandle PrepareJob(JobHandle dependencies)
        {
            headLength = headInfoList.Length;
            particleLength = headLength * MaxParticleLimit;
            colliderLength = colliderInfoList.Length;

            JobHandle headSetupHandle = new HeadSetupJob()
            {
                HeadInfos = headInfoList,
            }.Schedule(rootTransformAccessArray);

            JobHandle headPrepareHandle = new HeadPrepareJob()
            {
                HeadInfos = headInfoList,
            }.Schedule(headLength, 32, headSetupHandle);

            JobHandle colliderSetupHandle = new ColliderSetupJob()
            {
                ColliderInfos = colliderInfoList
            }.Schedule(colliderTransformAccessArray);

            JobHandle colliderPrepareHandle = new ColliderPrepareJob()
            {
                ColliderInfos = colliderInfoList
            }.Schedule(colliderLength, 32, colliderSetupHandle);

            //先放短的 后放长的..
            JobHandle particleSetupHandle = new ParticleSetupJob()
            {
                ParticleInfos = particleInfoList
            }.Schedule(particleTransformAccessArray, dependencies);

            return JobHandle.CombineDependencies(headPrepareHandle, particleSetupHandle, colliderPrepareHandle);
        }

        public JobHandle UpdateParticlesJob(JobHandle dependencies, float timeVar, int loop)
        {
            //
            JobHandle updateAllParticlesHandle = new UpdateAllParticlesJob()
            {
                timeVar = timeVar,
                loop = loop,
                HeadInfos = headInfoList,
                ParticleInfos = particleInfoList,
                ColliderInfos = colliderInfoList
            }.Schedule(particleLength, 64, dependencies);

            JobHandle applyParticlesHandle = new ApplyParticlesJob()
            {
                HeadInfos = headInfoList,
                ParticleInfos = particleInfoList
            }.Schedule(particleLength, 64, updateAllParticlesHandle);

            JobHandle applyParticlesToTransformsHandle = new ApplyParticlesToTransformsJob()
            {
                ParticleInfos = particleInfoList
            }.Schedule(particleTransformAccessArray, applyParticlesHandle);

            return applyParticlesToTransformsHandle;
        }


        #region prePare

        [BurstCompile]
        private struct ColliderSetupJob : IJobParallelForTransform
        {
            // [ReadOnly] public NativeArray<HeadInfo> HeadInfos;

            [NativeDisableParallelForRestriction] public NativeArray<ColliderInfo> ColliderInfos;

            public void Execute(int index, TransformAccess transform)
            {
                ColliderInfo colliderInfo = ColliderInfos[index];
                colliderInfo.Position = transform.position;
                colliderInfo.Rotation = transform.rotation;
                colliderInfo.Scale = MathematicsUtil.GetLossyScaleX(transform);
                ColliderInfos[index] = colliderInfo;
            }
        }

        [BurstCompile]
        private struct HeadSetupJob : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction] public NativeArray<HeadInfo> HeadInfos;

            //入参 root
            public void Execute(int index, TransformAccess transform)
            {
                HeadInfo head = HeadInfos[index];
                if (head.m_NeedUpdate)
                {
                    head.m_ObjectScale = math.abs(MathematicsUtil.GetLossyScaleX(transform));
                    head.m_ObjectMove = (float3)transform.position - head.m_ObjectPrevPosition;
                    head.m_ObjectPrevPosition = transform.position;
                    HeadInfos[index] = head;
                }
            }
        }


        [BurstCompile]
        private struct ParticleSetupJob : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction] public NativeArray<ParticleInfo> ParticleInfos;

            public void Execute(int index, TransformAccess transform)
            {
                {
                    ParticleInfo p = ParticleInfos[index];
                    if (p.m_TransformNotNull)
                    {
                        p.m_TransformPosition = transform.position;
                        p.m_TransformRotation = transform.rotation;
                        p.m_TransformLocalPosition = transform.localPosition;
                        p.m_TransformLocalToWorldMatrix = transform.localToWorldMatrix;
                        ParticleInfos[index] = p;
                    }
                }
            }
        }

        #endregion


        #region preUpdate

        [BurstCompile]
        private struct InitTransformsJob : IJobParallelForTransform
        {
            [ReadOnly] [NativeDisableParallelForRestriction]
            public NativeArray<ParticleInfo> ParticleInfos;

            public void Execute(int index, TransformAccess transform)
            {
                ParticleInfo p = ParticleInfos[index];
                if (p.m_TransformNotNull)
                {
                    transform.localPosition = p.m_InitLocalPosition;
                    transform.localRotation = p.m_InitLocalRotation;
                }
            }
        }

        #endregion

        #region LateUpdate

        //collider比head长
        //但不依赖head会更快
        [BurstCompile]
        private struct ColliderPrepareJob : IJobParallelFor
        {
            // [ReadOnly] public NativeArray<HeadInfo> HeadInfos;
            [NativeDisableParallelForRestriction] public NativeArray<ColliderInfo> ColliderInfos;

            public void Execute(int index)
            {
                // int hIndex = index / MaxColliderLimit;
                // HeadInfo head = HeadInfos[hIndex];
                // int cIndex = index % MaxColliderLimit;
                // if(head.m_NeedUpdate && head.m_ColliderCount > 0 && cIndex < head.m_ColliderCount)
                {
                    //collider prepare
                    ColliderInfo c = ColliderInfos[index];
                    FastDynamicBoneCollider.Prepare(ref c);
                    ColliderInfos[index] = c;
                }
            }
        }

        [BurstCompile]
        private struct HeadPrepareJob : IJobParallelFor
        {
            [ReadOnly] public float timeVar;
            [NativeDisableParallelForRestriction] public NativeArray<HeadInfo> HeadInfos;

            public void Execute(int index)
            {
                HeadInfo head = HeadInfos[index];
                if (head.m_NeedUpdate)
                {
                    float3 m_RestGravity =
                        math.normalizesafe(math.mul(head.m_RootLocalToWorldMatrix, new float4(head.m_LocalGravity, 0))
                            .xyz) *
                        MathematicsUtil.Length(head.m_LocalGravity);
                    float3 force = head.m_Gravity;
                    float3 fdir = math.normalizesafe(head.m_Gravity);
                    float3 pf = fdir * math.max(math.dot(m_RestGravity, fdir),
                        0); // project current gravity to rest gravity
                    force -= pf; // remove projected gravity
                    head.m_Force = (force + head.m_Force) * (head.m_ObjectScale * timeVar);
                    HeadInfos[index] = head;
                }
            }
        }


        [BurstCompile]
        private struct UpdateAllParticlesJob : IJobParallelFor
        {
            [ReadOnly] public float timeVar;

            [ReadOnly] public int loop;

            [ReadOnly] [NativeDisableParallelForRestriction]
            public NativeArray<HeadInfo> HeadInfos;

            [ReadOnly] [NativeDisableParallelForRestriction]
            public NativeArray<ColliderInfo> ColliderInfos;

            [NativeDisableParallelForRestriction] public NativeArray<ParticleInfo> ParticleInfos;

            public void Execute(int index)
            {
                int pIndex = index;
                ParticleInfo p = ParticleInfos[pIndex];
                int pMaxParticleLimit = p.m_MaxParticleLimit;
                if (pMaxParticleLimit <= 0)
                    return;
                int hIndex = index / pMaxParticleLimit;
                HeadInfo head = HeadInfos[hIndex];
                if (!head.m_NeedUpdate)
                    return;
                int pIndexLocal = index % pMaxParticleLimit;
                int offset = hIndex * pMaxParticleLimit;
                if (loop > 0)
                {
                    for (int loopIndex = 0; loopIndex < loop; loopIndex++)
                    {
                        float3 objectMove =
                            loopIndex == 0 ? head.m_ObjectMove : float3.zero; // only first loop consider object move

                        if (pIndexLocal < head.m_ParticleCount)
                        {
                            if (p.m_ParentIndex >= 0)
                            {
                                float3 v = p.m_Position - p.m_PrevPosition;
                                float3 rmove = objectMove * p.m_Inert;
                                p.m_PrevPosition = p.m_Position + rmove;
                                float damping = p.m_Damping;
                                if (p.m_isCollide)
                                {
                                    damping += p.m_Friction;
                                    if (damping > 1)
                                    {
                                        damping = 1;
                                    }

                                    p.m_isCollide = false;
                                }

                                float3 force = head.m_Force;
                                p.m_Position += v * (1 - damping) + force + rmove;
                            }
                            else
                            {
                                p.m_PrevPosition = p.m_Position;
                                p.m_Position = p.m_TransformPosition;
                            }


                            //UpdateParticles2
                            if (pIndexLocal > 0)
                            {
                                int ppIndex = p.m_ParentIndex + offset;
                                ParticleInfo p0 = ParticleInfos[ppIndex];

                                float restLen = p.m_TransformNotNull
                                    ? math.distance(p0.m_TransformPosition, p.m_TransformPosition)
                                    : MathematicsUtil.Length(math.mul(p0.m_TransformLocalToWorldMatrix,
                                        new float4(p.m_EndOffset, 0)).xyz);

                                // keep shape
                                float stiffness = math.lerp(1.0f, p.m_Stiffness, head.m_Weight);
                                if (stiffness > 0 || p.m_Elasticity > 0)
                                {
                                    float4x4 m0 = p0.m_TransformLocalToWorldMatrix;
                                    m0.c3 = new float4(p0.m_Position, 1);
                                    float3 restPos = p.m_TransformNotNull
                                        ? math.mul(m0, new float4(p.m_TransformLocalPosition, 1)).xyz
                                        : math.mul(m0, new float4(p.m_EndOffset, 1)).xyz;


                                    float3 d = restPos - p.m_Position;
                                    p.m_Position += d * (p.m_Elasticity * timeVar);

                                    if (stiffness > 0)
                                    {
                                        d = restPos - p.m_Position;
                                        float len = MathematicsUtil.Length(d);
                                        float maxlen = restLen * (1 - stiffness) * 2;
                                        if (len > maxlen)
                                        {
                                            p.m_Position += d * ((len - maxlen) / len);
                                        }
                                    }
                                }

                                // collide
                                if (head.m_ColliderCount > 0)
                                {
                                    int cOffset = hIndex * head.m_MaxColliderLimit;
                                    float particleRadius = p.m_Radius * head.m_ObjectScale;
                                    for (int j = 0; j < head.m_MaxColliderLimit; j++)
                                    {
                                        int cIndex;
                                        if (j < head.m_ColliderCount)
                                        {
                                            cIndex = j + cOffset;
                                            ColliderInfo c = ColliderInfos[cIndex];
                                            p.m_isCollide |= FastDynamicBoneCollider.HandleCollision(in c,
                                                ref p.m_Position, in particleRadius);
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                                }

                                FastDynamicBone.FreezeAxis freezeAxis = head.m_FreezeAxis;
                                if (freezeAxis != FastDynamicBone.FreezeAxis.None)
                                {
                                    float3 planeNormal;
                                    switch ((int)freezeAxis - 1)
                                    {
                                        case 0:
                                            planeNormal = MathematicsUtil.Normalize(p0.m_TransformLocalToWorldMatrix.c0).xyz;
                                            break;
                                        case 1:
                                            planeNormal = MathematicsUtil.Normalize(p0.m_TransformLocalToWorldMatrix.c1).xyz;
                                            break;
                                        case 2:
                                        default:
                                            planeNormal = MathematicsUtil.Normalize(p0.m_TransformLocalToWorldMatrix.c2).xyz;
                                            break;
                                    }

                                    float d = math.dot(planeNormal, p0.m_Position);
                                    p.m_Position -= planeNormal * (math.dot(planeNormal, p.m_Position) - d);
                                }

                                float3 dd = p0.m_Position - p.m_Position;
                                float leng = MathematicsUtil.Length(dd);
                                if (leng > 0)
                                {
                                    p.m_Position += dd * ((leng - restLen) / leng);
                                }
                            }

                            ParticleInfos[pIndex] = p;
                        }
                    }
                }
                else
                {
                    // SkipUpdateParticles();
                    if (pIndexLocal < head.m_ParticleCount)
                    {
                        if (p.m_ParentIndex >= 0)
                        {
                            p.m_PrevPosition += head.m_ObjectMove;
                            p.m_Position += head.m_ObjectMove;


                            int ppIndex = p.m_ParentIndex + offset;
                            ParticleInfo p0 = ParticleInfos[ppIndex];


                            float restLen = p.m_TransformNotNull
                                ? math.distance(p0.m_TransformPosition, p.m_TransformPosition)
                                : MathematicsUtil.Length(math.mul(p0.m_TransformLocalToWorldMatrix, new float4(p.m_EndOffset, 0)).xyz);

                            // keep shape
                            float stiffness = math.lerp(1.0f, p.m_Stiffness, head.m_Weight);
                            if (stiffness > 0 || p.m_Elasticity > 0)
                            {
                                float4x4 m0 = p0.m_TransformLocalToWorldMatrix;
                                m0.c3 = new float4(p0.m_Position, 1);
                                float3 restPos = p.m_TransformNotNull
                                    ? math.mul(m0, new float4(p.m_TransformLocalPosition, 1)).xyz
                                    : math.mul(m0, new float4(p.m_EndOffset, 1)).xyz;


                                float3 d = restPos - p.m_Position;
                                p.m_Position += d * (p.m_Elasticity * timeVar);

                                if (stiffness > 0)
                                {
                                    d = restPos - p.m_Position;
                                    float len = MathematicsUtil.Length(d);
                                    float maxlen = restLen * (1 - stiffness) * 2;
                                    if (len > maxlen)
                                    {
                                        p.m_Position += d * ((len - maxlen) / len);
                                    }
                                }
                            }

                            // keep length
                            float3 dd = p0.m_Position - p.m_Position;
                            float leng = MathematicsUtil.Length(dd);
                            if (leng > 0)
                            {
                                p.m_Position += dd * ((leng - restLen) / leng);
                            }
                        }
                        else
                        {
                            p.m_PrevPosition = p.m_Position;
                            p.m_Position = p.m_TransformPosition;
                        }

                        ParticleInfos[pIndex] = p;
                    }
                }
            }
        }

        //考虑与UpdateAll合并
        [BurstCompile]
        private struct ApplyParticlesJob : IJobParallelFor
        {
            [ReadOnly] [NativeDisableParallelForRestriction]
            public NativeArray<HeadInfo> HeadInfos;

            [NativeDisableParallelForRestriction] public NativeArray<ParticleInfo> ParticleInfos;

            public void Execute(int index)
            {
                ParticleInfo p = ParticleInfos[index];
                int pMaxParticleLimit = p.m_MaxParticleLimit;
                if (pMaxParticleLimit <= 0)
                    return;
                int pIndexLocal = index % pMaxParticleLimit;
                if (pIndexLocal == 0)
                    return;
                int hIndex = index / pMaxParticleLimit;
                HeadInfo head = HeadInfos[hIndex];
                if (!head.m_NeedUpdate)
                    return;
                int offset = hIndex * pMaxParticleLimit;

                if (pIndexLocal < head.m_ParticleCount)
                {
                    int ppIndex = offset + p.m_ParentIndex;
                    ParticleInfo p0 = ParticleInfos[ppIndex];
                    if (p0.m_ChildCount <= 1)
                    {
                        float3 localPos = p.m_TransformNotNull ? p.m_TransformLocalPosition : p.m_EndOffset;

                        float3 v0 = math.normalizesafe(math.mul(p0.m_TransformLocalToWorldMatrix,new float4(localPos, 0)).xyz) *
                                    MathematicsUtil.Length(localPos);
                        float3 v1 = p.m_Position - p0.m_Position;

                        quaternion rot = MathematicsUtil.FromToRotation(v0, v1);
                        p0.m_TransformRotation = math.mul(rot, p0.m_TransformRotation);
                    }

                    if (p.m_TransformNotNull)
                    {
                        p.m_TransformPosition = p.m_Position;
                    }

                    ParticleInfos[index] = p;
                    ParticleInfos[ppIndex] = p0;
                }
            }
        }

        [BurstCompile]
        private struct ApplyParticlesToTransformsJob : IJobParallelForTransform
        {
            [ReadOnly] [NativeDisableParallelForRestriction]
            public NativeArray<ParticleInfo> ParticleInfos;

            public void Execute(int index, TransformAccess transform)
            {
                ParticleInfo p = ParticleInfos[index];
                int pMaxParticleLimit = p.m_MaxParticleLimit;
                if (pMaxParticleLimit <= 0)
                    return;
                if (p.m_ChildCount <= 1)
                    transform.rotation = p.m_TransformRotation;
                if ((index % pMaxParticleLimit) == 0)
                    return;
                if (p.m_TransformNotNull)
                    transform.position = p.m_TransformPosition;
            }
        }

        #endregion
    }
}