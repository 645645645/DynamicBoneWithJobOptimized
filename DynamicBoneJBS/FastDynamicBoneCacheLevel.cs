using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

public struct HeadInfo
{
    public quaternion m_RootWorldRotation;
    public float3 m_Gravity;
    public float3 m_LocalGravity;
    public float3 m_ObjectMove;

    public float3 m_ObjectPrevPosition;

    public float3 m_Force;
    public float3 m_FinalForce;

    // prepare data
    // public float3 m_RestGravity;

    public float m_ObjectScale;

    public float m_Weight;

    // public float m_BoneTotalLength;
    public int m_ParticleCount;

    public int m_ColliderCount;

    public int m_MaxDepth;
    public int m_MaxColliderLimit; // <--
    public int m_MaxParticleLimit; // <--

    public FastDynamicBone.FreezeAxis m_FreezeAxis;

    public bool m_NeedUpdate;
}

public struct ParticleInfo
{
    public float4x4 m_TransformLocalToWorldMatrix;

    public quaternion m_TransformRotation;

    public quaternion m_InitLocalRotation;
    public float3 m_Position;
    public float3 m_PrevPosition;
    public float3 m_EndOffset;

    public float3 m_InitLocalPosition;

    // prepare data
    public float3 m_TransformLocalPosition;
    public float3 m_TransformPosition;

    //可合成float3/int3 DIF ESR PCM
    public float m_Damping;
    public float m_Elasticity;
    public float m_Stiffness;
    public float m_Inert;
    public float m_Friction;
    public float m_Radius;
    public int m_ParentIndex;

    public int m_ChildCount;

    // public int m_Depth;
    public int m_MaxParticleLimit; // <--

    // public float m_BoneLength;
    public bool m_isCollide;
    public bool m_TransformNotNull;
}

public class FastDynamicBoneCacheLevel
{
    [Header("每个Tree的 Particle容量")] public int MaxParticleLimit; //别填0

    [Header("最大Tree（root）容量")] public int MaxHeadLimit;

    // [Header("每组Collider最大容量")]
    public int MaxColliderLimit; //目前Collider没有分组啥的， 头发 裙子都共用同一组..
    protected int MaxParticleLength;

    protected int MaxColliderLength;

    private int MaxParticleDepth;
    private int usedMaxDepth;

    protected int headLength;


    private NativeList<int> treeList; //取head collider particles以此index

    private NativeList<HeadInfo> headInfoList;
    private NativeList<ParticleInfo> particleInfoList;

    private NativeList<FastDynamicBoneCollider.ColliderInfo> colliderInfoList;

    //TransformAccessArray, remove是NativeList.. read是array..
    //增删查改只能一个一个来
    private TransformAccessArray rootTransformAccessArray;
    private TransformAccessArray headTransformAccessArray;
    private TransformAccessArray particleTransformAccessArray; //read

    private TransformAccessArray colliderTransformAccessArray;

    //修不了随机写的bug，按层级分组。。
    private TransformAccessArray[] particleTransformAccessArraysWrite;

    /// <summary>
    /// 前3个得根据项目情况填，最后一个差不多行了
    /// </summary>
    /// <param name="maxParticleDepth">单个动态骨骼树的节点最大层数</param>
    /// <param name="maxParticleLimit">单个动态骨骼树的节点最大数量</param>
    /// <param name="maxColliderLimit">每组Collider的最大数量</param>
    /// <param name="maxHeadLimit">同时容纳的tree最大数量</param>
    public FastDynamicBoneCacheLevel(int maxParticleLimit, int maxParticleDepth = 10, int maxColliderLimit = 20,
        int maxHeadLimit = 100)
    {
        this.MaxParticleLimit = maxParticleLimit;
        this.MaxColliderLimit = maxColliderLimit;
        this.MaxHeadLimit = maxHeadLimit;
        this.MaxParticleLength = maxHeadLimit * maxParticleLimit;
        this.MaxColliderLength = maxHeadLimit * maxColliderLimit;
        this.MaxParticleDepth = maxParticleDepth;
    }

    public int GetTreeCount() => headLength/* + addQueue.Count - removeQueue.Count*/;

    private JobHandle lastJobHandle;

    private Dictionary<FastDynamicBone.ParticleTree, FastDynamicBone> addQueue =
        new Dictionary<FastDynamicBone.ParticleTree, FastDynamicBone>();
    private new Dictionary<FastDynamicBone.ParticleTree, FastDynamicBone> updateQueue = 
        new Dictionary<FastDynamicBone.ParticleTree, FastDynamicBone>();

    private List<FastDynamicBone.ParticleTree> removeQueue = new List<FastDynamicBone.ParticleTree>();
    private List<FastDynamicBone.ParticleTree> readQueue = new List<FastDynamicBone.ParticleTree>();

    public void InitNativeTable()
    {
        treeList = new NativeList<int>(Allocator.Persistent);
        rootTransformAccessArray = new TransformAccessArray(MaxHeadLimit, 2);

        headInfoList = new NativeList<HeadInfo>(Allocator.Persistent);
        headTransformAccessArray = new TransformAccessArray(MaxHeadLimit, 2);
        //
        particleInfoList = new NativeList<ParticleInfo>(Allocator.Persistent);
        particleTransformAccessArray = new TransformAccessArray(MaxParticleLength, 8);

        colliderInfoList = new NativeList<FastDynamicBoneCollider.ColliderInfo>(Allocator.Persistent);
        colliderTransformAccessArray = new TransformAccessArray(MaxColliderLength, 8);

        //tree depth 从0开始
        particleTransformAccessArraysWrite = new TransformAccessArray[MaxParticleDepth];
        for (int i = 0; i < MaxParticleDepth; i++)
        {
            TransformAccessArray tmpArray = new TransformAccessArray(MaxParticleLength, 8);
            particleTransformAccessArraysWrite[i] = tmpArray;
        }
    }

    public void Dispose()
    {
        lastJobHandle.Complete();
        if (treeList.IsCreated) treeList.Dispose();
        if (rootTransformAccessArray.isCreated) rootTransformAccessArray.Dispose();
        if (headInfoList.IsCreated) headInfoList.Dispose();
        if (headTransformAccessArray.isCreated) headTransformAccessArray.Dispose();
        if (particleInfoList.IsCreated) particleInfoList.Dispose();
        if (particleTransformAccessArray.isCreated) particleTransformAccessArray.Dispose();
        if (colliderInfoList.IsCreated) colliderInfoList.Dispose();
        if (colliderTransformAccessArray.isCreated) colliderTransformAccessArray.Dispose();
        for (int i = 0; i < MaxParticleDepth; i++)
        {
            TransformAccessArray tmpArray = particleTransformAccessArraysWrite[i];
            if (tmpArray.isCreated)
                tmpArray.Dispose();
        }

        particleTransformAccessArraysWrite = null;
    }

    public bool RemoveTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
    {
        if (addQueue.ContainsKey(tree))
        {
            addQueue.Remove(tree);
            return true;
        }

        if (removeQueue.Contains(tree))
        {
            Debug.LogWarningFormat("DynamicBoneManager : 你先别急，下次LateUpdate才执行移除， name = {0}, m_Root = {1}",
                target.transform.name, tree.m_Root.name);
            return false;
        }

        if (!treeList.Contains(tree.GetHashCode()))
            return false;

        removeQueue.Add(tree);

        if (readQueue.Contains(tree))
            readQueue.Remove(tree);

        if (updateQueue.ContainsKey(tree))
            updateQueue.Remove(tree);

        return true;
    }

    public bool AddTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
    {
        if (GetTreeCount() >= MaxHeadLimit)
        {
            Debug.LogWarningFormat("DynamicBoneManager : root out of range ~");
            return false;
        }

        if (addQueue.ContainsKey(tree))
        {
            Debug.LogWarningFormat("DynamicBoneManager : 你先别急，下次LateUpdate才执行注册， name = {0}, m_Root = {1}",
                target.transform.name, tree.m_Root.name);
            return false;
        }

        // 移除 但还没执行
        if (removeQueue.Contains(tree))
        {
            removeQueue.Remove(tree);
            return true;
        }

        // 重复
        if (treeList.Contains(tree.GetHashCode()))
            return false;

        addQueue.Add(tree, target);

        return true;
    }

    public void ReadTree(FastDynamicBone.ParticleTree tree)
    {
        if (readQueue.Contains(tree))
            return;

        readQueue.Add(tree);
    }

    //手动分层，保证结果
    private void AddWriteParticle(int depth, Transform transform)
    {
        if (depth >= MaxParticleDepth)
        {
            Debug.LogErrorFormat("骨骼链太长 /{name = {0}/}，目前限制单链最大长度 = {1}", transform.name, MaxParticleDepth - 1);
        }

        for (int i = 0; i < MaxParticleDepth; i++)
        {
            particleTransformAccessArraysWrite[i].Add(i == depth ? transform : null);
        }
    }

    private void RemoveWriteParticle(int index)
    {
        for (int i = 0; i < MaxParticleDepth; i++)
        {
            particleTransformAccessArraysWrite[i].RemoveAtSwapBack(index);
        }
    }

    public void UpdateTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
    {
        if (updateQueue.ContainsKey(tree))
            return;

        updateQueue.Add(tree, target);
    }

    private void RemoveTree()
    {
        int count = removeQueue.Count;
        if (count <= 0) return;
        for (int j = count - 1; j >= 0; j--)
        {
            FastDynamicBone.ParticleTree tree = removeQueue[j];

            if (tree != null)
            {
                int index = treeList.IndexOf(tree.GetHashCode());

                if (index == -1)
                    continue;

                treeList.RemoveAtSwapBack(index);

                headInfoList.RemoveAtSwapBack(index);
                headTransformAccessArray.RemoveAtSwapBack(index);

                rootTransformAccessArray.RemoveAtSwapBack(index);

                int offset = index * this.MaxParticleLimit;
                particleInfoList.RemoveRangeSwapBack(offset, MaxParticleLimit);
                for (int i = offset + MaxParticleLimit - 1; i >= offset; i--)
                {
                    // particleInfoList.RemoveAtSwapBack(i);
                    particleTransformAccessArray.RemoveAtSwapBack(i);
                    RemoveWriteParticle(i);
                }

                offset = index * MaxColliderLimit;
                colliderInfoList.RemoveRangeSwapBack(offset, MaxColliderLimit);
                for (int i = offset + MaxColliderLimit - 1; i >= offset; i--)
                {
                    // colliderInfoList.RemoveAtSwapBack(i);
                    colliderTransformAccessArray.RemoveAtSwapBack(i);
                }

                tree.InitTransforms(tree);

                headLength--;
                // colliderLength -= MaxColliderLimit;
                // particleLength -= MaxParticleLimit;
            }
        }

        int maxDepth = -1;
        for (int i = 0; i < headLength; i++)
        {
            maxDepth = math.max(maxDepth, headInfoList[i].m_MaxDepth);
        }
        usedMaxDepth = maxDepth;
        removeQueue.Clear();
    }

    private void AddTree()
    {
        int count = addQueue.Count;
        if (count <= 0) return;
        foreach (var item in addQueue)
        {
            FastDynamicBone.ParticleTree tree = item.Key;
            FastDynamicBone target = item.Value;

            if (tree != null && target != null)
            {
                usedMaxDepth = math.max(usedMaxDepth, tree.m_MaxDepth);
                treeList.Add(tree.GetHashCode());
                rootTransformAccessArray.Add(target.transform);

                int m_ParticleCount = tree.m_Particles.Count;
                var targetCollider = target.m_Colliders;
                int cCount = targetCollider.Count;
                int colliderCount = 0; //去空统计

                for (int i = 0; i < MaxColliderLimit; i++)
                {
                    if (i < cCount)
                    {
                        FastDynamicBoneCollider c = targetCollider[i];
                        if (c != null)
                        {
                            c.init();
                            colliderInfoList.Add(c.colliderInfo);
                            colliderTransformAccessArray.Add(c.transform);
                            colliderCount++;
                        }
                        else
                        {
                            Debug.LogErrorFormat("FastDynamicBone : Collider has someone null.");
                        }
                    }
                    else
                    {
                        colliderInfoList.Add(new FastDynamicBoneCollider.ColliderInfo());
                        colliderTransformAccessArray.Add(null);
                    }
                }

                HeadInfo head = new HeadInfo()
                {
                    m_RootWorldRotation = tree.m_RootWorldRotation,
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
                    m_MaxDepth = tree.m_MaxDepth,
                    m_MaxColliderLimit = MaxColliderLimit,
                    m_MaxParticleLimit = MaxParticleLimit,
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
                            // m_Depth = p.m_Depth,
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
                            m_InitLocalRotation = p.m_InitLocalRotation,
                        };
                        particleInfoList.Add(pInfo);
                        particleTransformAccessArray.Add(p.m_Transform);
                        // Debug.LogWarningFormat("Add : name = {0}, i = {1}", p.m_Transform.name, i);
                        AddWriteParticle(p.m_Depth, p.m_Transform);
                    }
                    else
                    {
                        //超过  占位/空 即冗余
                        particleInfoList.Add(new ParticleInfo() { m_MaxParticleLimit = pLimit });
                        particleTransformAccessArray.Add(null);
                        AddWriteParticle(-1, null);
                    }
                }

                headLength++;
                // colliderLength += MaxColliderLimit;
                // particleLength += MaxParticleLimit;
            }
        }

        addQueue.Clear();
    }

    private void ReadTree()
    {
        int rcount = readQueue.Count;
        if (rcount <= 0) return;
        for (int j = 0; j < readQueue.Count; j++)
        {
            FastDynamicBone.ParticleTree tree = readQueue[j];

            if (tree != null)
            {
                int index = treeList.IndexOf(tree.GetHashCode());
                if (index == -1) return;

                // lastJobHandle.Complete();

                HeadInfo head = headInfoList[index];
                int count = math.min(head.m_ParticleCount, tree.m_Particles.Count);

                int offset = index * MaxParticleLimit;
                for (int i = 0; i < count; i++)
                {
                    ParticleInfo pInfo = particleInfoList[i + offset];
                    FastDynamicBone.Particle p = tree.m_Particles[i];
                    p.m_Position = pInfo.m_TransformPosition;
                    p.m_TransformLocalToWorldMatrix = pInfo.m_TransformLocalToWorldMatrix;
                    tree.m_Particles[i] = p;
                }
            }
        }

        readQueue.Clear();
    }

    private void UpdateTree()
    {
        int count = updateQueue.Count;
        if (count <= 0) return;
        foreach (var item in updateQueue)
        {
            FastDynamicBone.ParticleTree tree = item.Key;
            FastDynamicBone target = item.Value;

            if (tree != null && target != null)
            {
                int index = treeList.IndexOf(tree.GetHashCode());
                if (index == -1) continue;

                // lastJobHandle.Complete();

                HeadInfo head = headInfoList[index];
                int m_ParticleCount = tree.m_Particles.Count;
                head.m_LocalGravity = tree.m_LocalGravity;
                head.m_Force = target.m_Force;
                head.m_Weight = target.m_Weight;
                head.m_ParticleCount = m_ParticleCount;
                head.m_FreezeAxis = target.m_FreezeAxis;
                head.m_NeedUpdate = (m_ParticleCount > 0) && (target.m_Weight > 0);

                int offset = index * MaxParticleLimit;
                int pIndex;
                for (int i = 0; i < m_ParticleCount; i++)
                {
                    pIndex = i + offset;
                    ParticleInfo pInfo = particleInfoList[pIndex];
                    FastDynamicBone.Particle p = tree.m_Particles[i];
                    // p.m_Position = pInfo.m_Position;
                    pInfo.m_Damping = p.m_Damping;
                    pInfo.m_Elasticity = p.m_Elasticity;
                    pInfo.m_Stiffness = p.m_Stiffness;
                    pInfo.m_Inert = p.m_Inert;
                    pInfo.m_Friction = p.m_Friction;
                    pInfo.m_Radius = p.m_Radius;
                    particleInfoList[pIndex] = pInfo;
                }

                var targetCollider = target.m_Colliders;
                int cCount = targetCollider.Count;
                int colliderCount = 0; //去空和重复统计

                offset = index * MaxColliderLimit;
                for (int i = 0; i < MaxColliderLimit; i++)
                {
                    if (i < cCount)
                    {
                        FastDynamicBoneCollider c = targetCollider[i];
                        if (c != null)
                        {
                            c.init();
                            int cIndex = offset + i;
                            colliderInfoList[cIndex] = c.colliderInfo;
                            colliderTransformAccessArray[cIndex] = c.transform;
                            colliderCount++;
                        }
                        else
                        {
                            Debug.LogErrorFormat("FastDynamicBone : Collider has someone null.");
                        }
                    }
                }

                head.m_ColliderCount = colliderCount;
                headInfoList[index] = head;
            }
        }
        updateQueue.Clear();
    }

    public void Complete()
    {
        lastJobHandle.Complete();
        // feature:
        // 收集增删查改操作，弄个队列 放到这里执行..
        AddTree();
        RemoveTree();
        UpdateTree();
        ReadTree();
    }


    public void PreUpdateJob()
    {
        if (!lastJobHandle.IsCompleted)
            return;
        

        if (GetTreeCount() <= 0) return;
        
        // initJob不管层级顺序好像也没啥问题
        lastJobHandle = new InitTransformsJob()
        {
            ParticleInfos = particleInfoList
        }.Schedule(particleTransformAccessArray, lastJobHandle);

        // for (int i = 0; i <= usedMaxDepth; i++)
        // {
        //     lastJobHandle = new InitTransformsJob()
        //     {
        //         ParticleInfos = particleInfoList
        //     }.Schedule(particleTransformAccessArraysWrite[i], lastJobHandle);
        // }
    }

    public void PrepareJob()
    {
        if (GetTreeCount() <= 0) return;
        JobHandle headSetupHandle = new HeadSetupJob()
        {
            HeadInfos = headInfoList,
        }.ScheduleReadOnly(rootTransformAccessArray, 1, lastJobHandle);

        JobHandle colliderSetupHandle = new ColliderSetupJob()
        {
            ColliderInfos = colliderInfoList
        }.ScheduleReadOnly(colliderTransformAccessArray, 20, lastJobHandle);

        //先放短的 后放长的..
        JobHandle particleSetupHandle = new ParticleSetupJob()
        {
            ParticleInfos = particleInfoList
        }.ScheduleReadOnly(particleTransformAccessArray, 16, lastJobHandle);

        lastJobHandle =
            JobHandle.CombineDependencies(headSetupHandle, particleSetupHandle, colliderSetupHandle);
    }

    public void UpdateParticlesJob(float timeVar, int loop)
    {
        if (GetTreeCount() <= 0) return;
        //UpdateParticles1可以拆出来...实测particle多才有点提升.
        lastJobHandle = new UpdateAllParticlesJobUseHead()
        {
            timeVar = timeVar,
            loop = loop,
            HeadInfos = headInfoList,
            ParticleInfos = particleInfoList,
            ColliderInfos = colliderInfoList
        }.Schedule(headLength, 1, lastJobHandle);
        
        lastJobHandle = new ApplyParticlesJob()
        {
            HeadInfos = headInfoList,
            ParticleInfos = particleInfoList
        }.Schedule(headLength, 1, lastJobHandle);
    }

    public void ApplyParticlesTransformJob()
    {
        for (int i = 0; i <= usedMaxDepth; i++)
        {
            lastJobHandle = new ApplyParticlesToTransformsJob()
            {
                ParticleInfos = particleInfoList
            }.Schedule(particleTransformAccessArraysWrite[i], lastJobHandle);
        }
    }

    #region prePare

    // ScheduleReadOnly是并行的 可以放别的计算
    [BurstCompile]
    private struct ColliderSetupJob : IJobParallelForTransform
    {
        // [ReadOnly] public NativeArray<HeadInfo> HeadInfos;

        [NativeDisableParallelForRestriction] public NativeArray<FastDynamicBoneCollider.ColliderInfo> ColliderInfos;

        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                FastDynamicBoneCollider.ColliderInfo colliderInfo = ColliderInfos[index];
                colliderInfo.Position = transform.position;
                colliderInfo.Rotation = transform.rotation;
                colliderInfo.Scale = MathematicsUtil.GetLossyScaleX(transform);
                FastDynamicBoneCollider.Prepare(ref colliderInfo);
                ColliderInfos[index] = colliderInfo;
            }
        }
    }

    [BurstCompile]
    private struct HeadSetupJob : IJobParallelForTransform
    {
        [NativeDisableParallelForRestriction] public NativeArray<HeadInfo> HeadInfos;

        //入参 root
        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                HeadInfo head = HeadInfos[index];
                if (head.m_NeedUpdate)
                {
                    head.m_ObjectScale = math.abs(MathematicsUtil.GetLossyScaleX(transform));
                    head.m_ObjectMove = (float3)transform.position - head.m_ObjectPrevPosition;
                    head.m_ObjectPrevPosition = transform.position;
                    head.m_RootWorldRotation = transform.rotation;
                    float3 m_RestGravity =
                        MathematicsUtil.TransformDirection(head.m_ObjectPrevPosition, head.m_RootWorldRotation,
                            head.m_LocalGravity);
                    float3 force = head.m_Gravity;
                    float3 fdir = MathematicsUtil.Normalize(head.m_Gravity);
                    float3 pf = fdir * math.max(math.dot(m_RestGravity, fdir),
                        0); // project current gravity to rest gravity
                    force -= pf; // remove projected gravity
                    force = (force + head.m_Force) * (head.m_ObjectScale);
                    head.m_FinalForce = force;
                    HeadInfos[index] = head;
                }
            }
        }
    }


    [BurstCompile]
    private struct ParticleSetupJob : IJobParallelForTransform
    {
        [NativeDisableParallelForRestriction] public NativeArray<ParticleInfo> ParticleInfos;

        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                ParticleInfo p = ParticleInfos[index];
                // if (p.m_TransformNotNull)
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
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<ParticleInfo> ParticleInfos;

        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                ParticleInfo p = ParticleInfos[index];
                // if (p.m_TransformNotNull)
                {
                    transform.localPosition = p.m_InitLocalPosition;
                    transform.localRotation = p.m_InitLocalRotation;
                }
            }
        }
    }

    #endregion

    #region LateUpdate

    //考虑拆开，UpdateParticles1可以以Particle为单位并行
    [BurstCompile]
    private struct UpdateAllParticlesJobUseHead : IJobParallelFor
    {
        [ReadOnly] public float timeVar;

        [ReadOnly] public int loop;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<HeadInfo> HeadInfos;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<FastDynamicBoneCollider.ColliderInfo> ColliderInfos;

        [NativeDisableParallelForRestriction] public NativeArray<ParticleInfo> ParticleInfos;

        public void Execute(int index)
        {
            HeadInfo head = HeadInfos[index];
            if (!head.m_NeedUpdate)
                return;
            int pMaxParticleLimit = head.m_MaxParticleLimit;
            if (pMaxParticleLimit <= 0)
                return;
            int offset = index * pMaxParticleLimit;
            if (loop > 0)
            {
                float3 force = head.m_FinalForce * timeVar;
                for (int loopIndex = 0; loopIndex < loop; loopIndex++)
                {
                    float3 objectMove =
                        loopIndex == 0 ? head.m_ObjectMove : float3.zero;
                    for (int i = 0; i < pMaxParticleLimit; i++)
                    {
                        int pIndex = i + offset;
                        ParticleInfo p = ParticleInfos[pIndex];
                        if (i < head.m_ParticleCount)
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

                                p.m_Position += v * (1 - damping) + force + rmove;
                            }
                            else
                            {
                                p.m_PrevPosition = p.m_Position;
                                p.m_Position = p.m_TransformPosition;
                            }

                            if (i > 0)
                            {
                                int ppIndex = p.m_ParentIndex + offset;
                                ParticleInfo p0 = ParticleInfos[ppIndex];

                                float restLen = p.m_TransformNotNull
                                    ? math.distance(p0.m_TransformPosition, p.m_TransformPosition)
                                    : MathematicsUtil.Length(math.mul((float3x3)p0.m_TransformLocalToWorldMatrix,
                                        p.m_EndOffset));

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
                                    int cOffset = index * head.m_MaxColliderLimit;
                                    float particleRadius = p.m_Radius * head.m_ObjectScale;
                                    for (int j = 0; j < head.m_MaxColliderLimit; j++)
                                    {
                                        int cIndex;
                                        if (j < head.m_ColliderCount)
                                        {
                                            cIndex = j + cOffset;
                                            FastDynamicBoneCollider.ColliderInfo c = ColliderInfos[cIndex];
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
                                            planeNormal = MathematicsUtil
                                                .Normalize(p0.m_TransformLocalToWorldMatrix.c0).xyz;
                                            break;
                                        case 1:
                                            planeNormal = MathematicsUtil
                                                .Normalize(p0.m_TransformLocalToWorldMatrix.c1).xyz;
                                            break;
                                        case 2:
                                        default:
                                            planeNormal = MathematicsUtil
                                                .Normalize(p0.m_TransformLocalToWorldMatrix.c2).xyz;
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
            }
            else
            {
                for (int i = 0; i < pMaxParticleLimit; i++)
                {
                    int pIndex = i + offset;
                    ParticleInfo p = ParticleInfos[pIndex];
                    if (i < head.m_ParticleCount)
                    {
                        if (p.m_ParentIndex > 0)
                        {
                            p.m_PrevPosition += head.m_ObjectMove;
                            p.m_Position += head.m_ObjectMove;


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
                    else
                    {
                        return;
                    }
                }
            }
        }
    }

    // 依赖parent 必须从根开始
    [BurstCompile]
    private struct ApplyParticlesJob : IJobParallelFor
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<HeadInfo> HeadInfos;

        [NativeDisableParallelForRestriction] public NativeArray<ParticleInfo> ParticleInfos;

        public void Execute(int index)
        {
            HeadInfo head = HeadInfos[index];
            if (!head.m_NeedUpdate)
                return;
            int pMaxParticleLimit = head.m_MaxParticleLimit;
            if (pMaxParticleLimit <= 0)
                return;

            int offset = index * pMaxParticleLimit;
            for (int i = 1; i < pMaxParticleLimit; i++)
            {
                if (i < head.m_ParticleCount)
                {
                    int pIndex = i + offset;
                    ParticleInfo p = ParticleInfos[pIndex];
                    int ppIndex = offset + p.m_ParentIndex;
                    ParticleInfo p0 = ParticleInfos[ppIndex];
                    if (p0.m_ChildCount <= 1)
                    {
                        float3 localPos = p.m_TransformNotNull ? p.m_TransformLocalPosition : p.m_EndOffset;

                        float3 v0 = MathematicsUtil.TransformDirection(p0.m_TransformPosition,
                            p0.m_TransformRotation, localPos);
                        float3 v1 = p.m_Position - p0.m_Position;

                        quaternion rot = MathematicsUtil.FromToRotation(v0, v1);
                        p0.m_TransformRotation = math.mul(rot, p0.m_TransformRotation);
                    }

                    if (p.m_TransformNotNull)
                    {
                        p.m_TransformPosition = p.m_Position;
                    }

                    ParticleInfos[pIndex] = p;
                    ParticleInfos[ppIndex] = p0;
                }
                else
                {
                    return;
                }
            }
        }
    }

    // prefab重复创建的transform 引擎判断节点层级关系失效，随机的顺序...
    // 本质是后设置父节点，带跑了已经在正确位置的子节点...改算local是没有用的
    // 没有Particle级别平铺 不能随机写Pos/Rot
    [BurstCompile]
    private struct ApplyParticlesToTransformsJob : IJobParallelForTransform
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<ParticleInfo> ParticleInfos;

        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                ParticleInfo p = ParticleInfos[index];
                int pMaxParticleLimit = p.m_MaxParticleLimit;
                if (pMaxParticleLimit <= 0)
                    return;
                // if (p.m_TransformNotNull)
                {
                    if (p.m_ChildCount <= 1)
                        transform.rotation = p.m_TransformRotation;
                    if ((index % pMaxParticleLimit) == 0)
                        return;
                    transform.position = p.m_TransformPosition;
                }
            }
        }
    }

    #endregion
}