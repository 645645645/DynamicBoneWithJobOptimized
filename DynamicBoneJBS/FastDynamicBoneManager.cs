using UnityEngine;
using Unity.Collections;

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
                    GameObject obj = new GameObject("FastDynamicBoneManager");
                    m_instance = obj.AddComponent<FastDynamicBoneManager>();
                    DontDestroyOnLoad(obj);
                    // m_instance.Init();
                }
            }

            return m_instance;
        }
    }

    private void Awake()
    {
        // if (!m_instance)
        {
            m_instance = this;
            m_instance.Init();
        }
    }

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
    
    /// <summary>
    /// 该值为Tree的最大层数
    /// </summary>
    public const int MaxParticleDepth = 10;
    
    private FastDynamicBoneCacheLevel L0 = new FastDynamicBoneCacheLevel(20, MaxParticleDepth, MaxColliderLimit, 200);

    private FastDynamicBoneCacheLevel L1 = new FastDynamicBoneCacheLevel(50, MaxParticleDepth, MaxColliderLimit, 100);

    private FastDynamicBoneCacheLevel L2 = new FastDynamicBoneCacheLevel(100, MaxParticleDepth, MaxColliderLimit, 50);

    private FastDynamicBoneCacheLevel L3 = new FastDynamicBoneCacheLevel(200, MaxParticleDepth, MaxColliderLimit, 100);

    // public bool colliderNullCheck = false;

    private int treeCount;

    private NativeList<int> dynamicBoneList;

    private void Init()
    {
        dynamicBoneList = new NativeList<int>(Allocator.Persistent);

        L0.InitNativeTable();
        L1.InitNativeTable();
        L2.InitNativeTable();
        L3.InitNativeTable();

        cacheLevelMaxParmas =
            new Vector4(L0.MaxParticleLimit, L1.MaxParticleLimit, L2.MaxParticleLimit, L3.MaxParticleLimit);
    }

    FastDynamicBoneCacheLevel GetCacheLevel(FastDynamicBone.ParticleTree tree)
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
                    "FastDynamicBone : some one root's Particles Count = {0} greater than MaxParticleLimit = {1}",
                    pCount, cacheLevelMaxParmas.w);
            }

            return L3;
        }
    }

    public void AddBone(FastDynamicBone target)
    {
        if (dynamicBoneList.Contains(target.GetHashCode())) return;

        //m_ParticleTrees = 0 不管
        for (int i = 0; i < target.m_ParticleTrees.Count; i++)
        {
            if (AddTree(target.m_ParticleTrees[i], target))
            {
                dynamicBoneList.Add(target.GetHashCode()); //component复制跟head对齐 冗余到家了
                treeCount++;
            }
        }
    }

    public void RemoveBone(FastDynamicBone target)
    {
        if (treeCount <= 0) return;
        int index = dynamicBoneList.IndexOf(target.GetHashCode());
        if (index == -1) return;

        for (int i = target.m_ParticleTrees.Count - 1; i >= 0; i--)
        {
            if (RemoveTree(target.m_ParticleTrees[i], target))
            {
                dynamicBoneList.RemoveAtSwapBack(index);//ID相同移除谁都一样
                treeCount--;
            }
        }
    }

    public void ReadBone(FastDynamicBone target)
    {
        if (treeCount <= 0) return;
        if (!dynamicBoneList.Contains(target.GetHashCode())) return;

        for (int i = 0; i < target.m_ParticleTrees.Count; i++)
        {
            ReadTree(target.m_ParticleTrees[i]);
        }
    }

    public void UpdateBone(FastDynamicBone target)
    {
        if (treeCount <= 0) return;
        if (!dynamicBoneList.Contains(target.GetHashCode())) return;

        for (int i = 0; i < target.m_ParticleTrees.Count; i++)
        {
            UpdateTree(target.m_ParticleTrees[i], target);
        }
    }

    private bool RemoveTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
    {
        FastDynamicBoneCacheLevel level = GetCacheLevel(tree);

        return level.RemoveTree(tree, target);
    }

    private bool AddTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
    {
        FastDynamicBoneCacheLevel level = GetCacheLevel(tree);

        return level.AddTree(tree, target);
    }

    private void ReadTree(FastDynamicBone.ParticleTree tree)
    {
        FastDynamicBoneCacheLevel level = GetCacheLevel(tree);

        level.ReadTree(tree);
    }

    private void UpdateTree(FastDynamicBone.ParticleTree tree, FastDynamicBone target)
    {
        FastDynamicBoneCacheLevel level = GetCacheLevel(tree);

        level.UpdateTree(tree, target);
    }

    private float m_Time;
    private float m_DeltaTime;
    private int m_PreUpdateCount;

    private void FixedUpdate()
    {
        // if (treeCount <= 0) return;
        if (m_UpdateMode == UpdateMode.AnimatePhysics)
        {
            PreUpdate();
        }
    }

    private void Update()
    {
        // if (treeCount <= 0) return;
        if (m_UpdateMode != UpdateMode.AnimatePhysics)
        {
            PreUpdate();
        }
    }

    public void BeforeLateUpdate(){}
    
    private void LateUpdate()
    {
        if (treeCount <= 0) return;
        if (m_PreUpdateCount == 0)
            return;
        
        UpdateAll();
    }

    public void AfterLateUpdate() 
    {
        // if (treeCount <= 0) return;
        L3.ApplyParticlesTransformJob();
        L2.ApplyParticlesTransformJob();
        L1.ApplyParticlesTransformJob();
        L0.ApplyParticlesTransformJob();
        m_PreUpdateCount = 0;
    }

    public void PostLateUpdate()
    {
    }

    public void AfterRendering()
    {
    }

    void PreUpdate()
    {
        L3.Complete();
        L2.Complete();
        L1.Complete();
        L0.Complete();
        L3.PreUpdateJob();
        L2.PreUpdateJob();
        L1.PreUpdateJob();
        L0.PreUpdateJob();

        ++m_PreUpdateCount;
    }

    private int loop = 1;
    private float timeVar = 1f;

    void UpdateAll()
    {
        L3.PrepareJob();
        L2.PrepareJob();
        L1.PrepareJob();
        L0.PrepareJob();

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

        loop = 1;
        timeVar = 1;
        float dt = m_DeltaTime;
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
        L3.UpdateParticlesJob(timeVar, loop);
        L2.UpdateParticlesJob(timeVar, loop);
        L1.UpdateParticlesJob(timeVar, loop);
        L0.UpdateParticlesJob(timeVar, loop);
        Unity.Jobs.JobHandle.ScheduleBatchedJobs();
    }

    private void OnDestroy()
    {
        _applicationIsQuitting = true;
        treeCount = 0;
        if(dynamicBoneList.IsCreated)
            dynamicBoneList.Dispose();
        L0.Dispose();
        L1.Dispose();
        L2.Dispose();
        L3.Dispose();
    }

    private void OnValidate()
    {
        if (Application.isEditor && Application.isPlaying)
        {
            this.treeCount = 0;
        }
    }
}