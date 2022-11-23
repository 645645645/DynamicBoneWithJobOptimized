using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

namespace Plugins.FastDynamicBone
{
    public struct HeadInfo
    {
        // public float4x4 m_RootWorldToLocalMatrix;
        public float4x4 m_RootLocalToWorldMatrix;
        public float3 m_Gravity;
        public float3 m_LocalGravity;
        public float3 m_ObjectMove;

        public float3 m_ObjectPrevPosition;

        public float3 m_Force;
        // public float3 m_FinalForce;

        // prepare data
        // public float3 m_RestGravity;

        public float m_ObjectScale;

        public float m_Weight;

        // public float m_BoneTotalLength;
        public int m_ParticleCount;
        public int m_ColliderCount;
        public int m_MaxColliderLimit; // <--

        public FastDynamicBone.FreezeAxis m_FreezeAxis;

        public bool m_NeedUpdate;
    }

    public struct ParticleInfo
    {
        public int m_ParentIndex;
        public int m_ChildCount;
        public int m_MaxParticleLimit; // <--
        public float m_Damping;
        public float m_Elasticity;
        public float m_Stiffness;
        public float m_Inert;
        public float m_Friction;

        public float m_Radius;

        // public float m_BoneLength;
        public bool m_isCollide;
        public bool m_TransformNotNull;

        public float3 m_Position;
        public float3 m_PrevPosition;
        public float3 m_EndOffset;
        public float3 m_InitLocalPosition;
        public quaternion m_InitLocalRotation;

        // prepare data
        public float3 m_TransformPosition;
        public quaternion m_TransformRotation;
        public float3 m_TransformLocalPosition;
        public float4x4 m_TransformLocalToWorldMatrix;
    }

    public class FastDynamicBoneManager : MonoBehaviour
    {
        private static FastDynamicBoneManager m_instance;

        private static bool _applicationIsQuitting = false;

        public static FastDynamicBoneManager Instance
        {
            get
            {
                if (_applicationIsQuitting)
                {
                    return null;
                }

                if (null == m_instance)
                {
                    m_instance = GameObject.FindObjectOfType<FastDynamicBoneManager>();
                    if (!m_instance)
                    {
                        GameObject obj = new GameObject("DynamicBoneManager");
                        m_instance = obj.AddComponent<FastDynamicBoneManager>();
                        DontDestroyOnLoad(obj);
                        m_instance.Init();
                    }
                }

                return m_instance;
            }
        }

        // private void Awake()
        // {
        //     // if (!m_instance)
        //     {
        //         m_instance = this;
        //         m_instance.Init();
        //     }
        // }

        public enum UpdateMode
        {
            Normal,
            AnimatePhysics,
            UnscaledTime,
            Default
        }

        [HideInInspector] public float m_UpdateRate = 60.0f;

        [HideInInspector] public UpdateMode m_UpdateMode = UpdateMode.Default;


        private Vector4 cacheLevelMaxParmas;

        /// <summary>
        /// 该值为每组Collider的最大数量
        /// </summary>
        public const int MaxColliderLimit = 20; //别填0

        private DynamicBoneCacheLevel L0 = new DynamicBoneCacheLevel(20, MaxColliderLimit, 300);

        private DynamicBoneCacheLevel L1 = new DynamicBoneCacheLevel(50, MaxColliderLimit, 100);

        private DynamicBoneCacheLevel L2 = new DynamicBoneCacheLevel(100, MaxColliderLimit, 100);

        private DynamicBoneCacheLevel L3 = new DynamicBoneCacheLevel(200, MaxColliderLimit, 150);

        // public bool colliderNullCheck = false;

        private int treeCount;

        //官方推荐冗余结构
        private List<FastDynamicBone> dynamicBoneList;
        private List<FastDynamicBone.ParticleTree> treeList; //取head collider particles以此index
        private List<FastDynamicBoneCollider> colliderList;
        private NativeList<HeadInfo> headInfoList;
        private NativeList<ParticleInfo> particleInfoList;

        private NativeList<ColliderInfo> colliderInfoList;

        private TransformAccessArray rootTransformAccessArray;
        private TransformAccessArray headTransformAccessArray;
        private TransformAccessArray particleTransformAccessArray;
        private TransformAccessArray colliderTransformAccessArray;

        private void Init()
        {
            dynamicBoneList = new List<FastDynamicBone>();

            L0.InitNativeTable();
            L1.InitNativeTable();
            L2.InitNativeTable();
            L3.InitNativeTable();

            cacheLevelMaxParmas =
                new Vector4(L0.MaxParticleLimit, L1.MaxParticleLimit, L2.MaxParticleLimit, L3.MaxParticleLimit);

        }

        DynamicBoneCacheLevel GetCacheLevel(FastDynamicBone.ParticleTree tree)
        {
            int pCount = tree.m_Particles.Count;
            if (pCount <= cacheLevelMaxParmas.x)
                return L0;
            else if (pCount <= cacheLevelMaxParmas.y)
                return L1;
            else if (pCount <= cacheLevelMaxParmas.z)
                return L2;
            else
            {
                if (Application.isEditor && pCount > cacheLevelMaxParmas.w)
                {
                    Debug.LogWarningFormat(
                        "DynamicBone : some one root's Particles Count greater than L3.MaxParticleLimit = {0}",
                        cacheLevelMaxParmas.w);
                }

                return L3;
            }
        }

        public void AddBone(FastDynamicBone target)
        {
            int index = dynamicBoneList.IndexOf(target);
            if (index != -1) return;

            //m_ParticleTrees = 0 不管
            for (int i = 0; i < target.m_ParticleTrees.Count; i++)
            {
                AddTree(target.m_ParticleTrees[i], target);
            }
        }

        public void RemoveBone(FastDynamicBone target)
        {
            if (treeCount <= 0) return;
            int index = dynamicBoneList.IndexOf(target);
            if (index == -1) return;

            for (int i = target.m_ParticleTrees.Count - 1; i >= 0; i--)
            {
                RemoveTree(target.m_ParticleTrees[i], target);
            }
        }

        public void ReadBone(FastDynamicBone target)
        {
            if (treeCount <= 0) return;
            int index = dynamicBoneList.IndexOf(target);
            if (index == -1) return;

            for (int i = 0; i < target.m_ParticleTrees.Count; i++)
            {
                ReadTree(target.m_ParticleTrees[i]);
            }
        }

        private void RemoveTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
        {
            DynamicBoneCacheLevel level = GetCacheLevel(tree);

            if (level.RemoveTree(tree, target))
            {
                dynamicBoneList.Remove(target);
                treeCount--;
            }
        }

        private void AddTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
        {
            DynamicBoneCacheLevel level = GetCacheLevel(tree);

            if (level.AddTree(tree, target))
            {
                dynamicBoneList.Add(target); //component复制跟head对齐 冗余到家了
                treeCount++;
            }
        }

        private void ReadTree(FastDynamicBone.ParticleTree tree)
        {
            DynamicBoneCacheLevel level = GetCacheLevel(tree);

            int index = level.treeList.IndexOf(tree);
            if (index == -1) return;

            HeadInfo head = level.headInfoList[index];
            int count = head.m_ParticleCount;

            int offset = index * level.MaxParticleLimit;
            for (int i = 0; i < count; i++)
            {
                ParticleInfo pInfo = level.particleInfoList[i + offset];
                FastDynamicBone.Particle p = tree.m_Particles[i];
                p.m_Position = pInfo.m_Position;
            }
        }

        private float m_Time;
        private float m_DeltaTime;
        private int m_PreUpdateCount;

        private JobHandle lastJobHandle;

        private void FixedUpdate()
        {
            if (treeCount <= 0) return;
            if (m_UpdateMode == UpdateMode.AnimatePhysics)
            {
                PreUpdate();
            }
        }

        private void Update()
        {
            if (treeCount <= 0) return;
            if (m_UpdateMode != UpdateMode.AnimatePhysics)
            {
                PreUpdate();
            }
        }


        private void LateUpdate()
        {
            if (treeCount <= 0) return;
            if (m_PreUpdateCount == 0)
                return;

            UpdateAll();
            lastJobHandle.Complete();

            m_PreUpdateCount = 0;
        }

        void PreUpdate()
        {
            if (!lastJobHandle.IsCompleted)
                return;

            lastJobHandle = JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(L0.PreUpdateJob(lastJobHandle), L3.PreUpdateJob(lastJobHandle)),
                L1.PreUpdateJob(lastJobHandle), L2.PreUpdateJob(lastJobHandle));

            ++m_PreUpdateCount;
        }

        void UpdateAll()
        {
            switch (m_UpdateMode)
            {
                case UpdateMode.UnscaledTime:
                    m_DeltaTime = Time.unscaledDeltaTime;
                    break;
                case UpdateMode.AnimatePhysics:
                    m_DeltaTime = Time.fixedDeltaTime * m_PreUpdateCount;
                    break;
                case UpdateMode.Default:
                case UpdateMode.Normal:
                default:
                    m_DeltaTime = Time.deltaTime;
                    break;
            }

            int loop = 1;
            float timeVar = 1;
            float dt = m_DeltaTime;

            JobHandle timerHandle = new FastDynamicBoneManagerTimerJob()
            {
                dt = dt,
                m_UpdateMode = this.m_UpdateMode,
                m_UpdateRate = this.m_UpdateRate,
                loop = loop,
                timeVar = timeVar,
                m_Time = this.m_Time,
            }.Schedule();

            lastJobHandle = JobHandle.CombineDependencies(
                timerHandle,
                JobHandle.CombineDependencies(L0.PrepareJob(lastJobHandle), L3.PrepareJob(lastJobHandle)),
                JobHandle.CombineDependencies(L1.PrepareJob(lastJobHandle), L2.PrepareJob(lastJobHandle)));

            lastJobHandle = JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(L0.UpdateParticlesJob(lastJobHandle, timeVar, loop),
                    L3.UpdateParticlesJob(lastJobHandle, timeVar, loop)),
                L1.UpdateParticlesJob(lastJobHandle, timeVar, loop),
                L2.UpdateParticlesJob(lastJobHandle, timeVar, loop));
        }

        [BurstCompile]
        public struct FastDynamicBoneManagerTimerJob : IJob
        {
            [ReadOnly] public float dt;
            [ReadOnly] public float m_UpdateRate;
            [ReadOnly] public UpdateMode m_UpdateMode;
            public int loop;
            public float timeVar;
            public float m_Time;

            public void Execute()
            {
                if (m_UpdateMode == UpdateMode.Default)
                {
                    if (m_UpdateRate > 0)
                    {
                        timeVar = dt * m_UpdateRate;
                    }
                }
                else
                {
                    if (m_UpdateRate > 0)
                    {
                        float frameTime = 1.0f / m_UpdateRate;
                        m_Time += dt;
                        loop = 0;

                        while (m_Time >= frameTime)
                        {
                            m_Time -= frameTime;
                            if (++loop >= 3)
                            {
                                m_Time = 0;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            _applicationIsQuitting = true;
            treeCount = 0;
            L0.Dispose();
            L1.Dispose();
            L2.Dispose();
            L3.Dispose();
        }
    }
}