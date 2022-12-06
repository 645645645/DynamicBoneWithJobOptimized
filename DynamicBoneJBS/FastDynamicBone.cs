using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Fast Dynamic Bone/Fast Dynamic Bone")]
public class FastDynamicBone : MonoBehaviour
{
    [Tooltip("The roots of the transform hierarchy to apply physics.")]
    public Transform m_Root = null;

    public List<Transform> m_Roots = null;

    [Tooltip("How much the bones slowed down.")] [Range(0, 1)]
    public float m_Damping = 0.1f;

    public AnimationCurve m_DampingDistrib = null;

    [Tooltip("How much the force applied to return each bone to original orientation.")] [Range(0, 1)]
    public float m_Elasticity = 0.1f;

    public AnimationCurve m_ElasticityDistrib = null;

    [Tooltip("How much bone's original orientation are preserved.")] [Range(0, 1)]
    public float m_Stiffness = 0.1f;

    public AnimationCurve m_StiffnessDistrib = null;

    [Tooltip("How much character's position change is ignored in physics simulation.")] [Range(0, 1)]
    public float m_Inert = 0;

    public AnimationCurve m_InertDistrib = null;

    [Tooltip("How much the bones slowed down when collide.")]
    public float m_Friction = 0;

    public AnimationCurve m_FrictionDistrib = null;

    [Tooltip("Each bone can be a sphere to collide with colliders. Radius describe sphere's size.")]
    public float m_Radius = 0;

    public AnimationCurve m_RadiusDistrib = null;

    [Tooltip("If End Length is not zero, an extra bone is generated at the end of transform hierarchy.")]
    public float m_EndLength = 0;

    [Tooltip("If End Offset is not zero, an extra bone is generated at the end of transform hierarchy.")]
    public Vector3 m_EndOffset = Vector3.zero;

    [Tooltip("The force apply to bones. Partial force apply to character's initial pose is cancelled out.")]
    public Vector3 m_Gravity = Vector3.zero;

    [Tooltip("The force apply to bones.")] public Vector3 m_Force = Vector3.zero;

    [Tooltip("Control how physics blends with existing animation.")] [Range(0, 1)]
    public float m_BlendWeight = 1.0f;

    [Tooltip("Collider objects interact with the bones.")]
    public List<FastDynamicBoneCollider> m_Colliders = null;

    [Tooltip("Bones exclude from physics simulation.")]
    public List<Transform> m_Exclusions = null;

    public enum FreezeAxis
    {
        None,
        X,
        Y,
        Z
    }

    [Tooltip("Constrain bones to move on specified plane.")]
    public FreezeAxis m_FreezeAxis = FreezeAxis.None;

    [Tooltip("Disable physics simulation automatically if character is far from camera or player.")]
    public bool m_DistantDisable = false;

    public Transform m_ReferenceObject = null;
    public float m_DistanceToObject = 20;

    [HideInInspector] public Vector3 m_ObjectMove;
    [HideInInspector] public Vector3 m_ObjectPrevPosition;
    [HideInInspector] public float m_ObjectScale;

    [HideInInspector] public float m_Weight = 1.0f;

    bool m_DistantDisabled = false;

    public struct Particle
    {
        public Transform m_Transform;
        public int m_ParentIndex;
        public int m_ChildCount;
        public int m_Depth;
        public float m_Damping;
        public float m_Elasticity;
        public float m_Stiffness;
        public float m_Inert;
        public float m_Friction;
        public float m_Radius;
        public float m_BoneLength;
        public bool m_isCollide;
        public bool m_TransformNotNull;

        public Vector3 m_Position;
        public Vector3 m_PrevPosition;
        public Vector3 m_EndOffset;
        public Vector3 m_InitLocalPosition;
        public Quaternion m_InitLocalRotation;
        public Quaternion m_InitRotation;

        // prepare data
        // public float3 m_TransformPosition;
        // public float3 m_TransformLocalPosition;
        public Matrix4x4 m_TransformLocalToWorldMatrix;
    }

    public class ParticleTree
    {
        public Transform m_Root;
        public Vector3 m_LocalGravity;
        public Matrix4x4 m_RootWorldToLocalMatrix;
        public Quaternion m_RootWorldRotation;
        public float m_BoneTotalLength;
        public int m_MaxDepth;
        public List<Particle> m_Particles = new List<Particle>();
        
        public void InitTransforms(ParticleTree pt)
        {
            for (int i = 0; i < pt.m_Particles.Count; ++i)
            {
                Particle p = pt.m_Particles[i];
                if (p.m_TransformNotNull && p.m_Transform)
                {
                    p.m_Transform.localPosition = p.m_InitLocalPosition;
                    p.m_Transform.localRotation = p.m_InitLocalRotation;
                }
            }
        }
    }

    [HideInInspector] public List<ParticleTree> m_ParticleTrees = new List<ParticleTree>();

    void LateUpdate()
    {
        CheckDistance();
    }

    // 角色隐藏是关MeshRender，
    // transform、animator 和 DynamicBone 都是活的 what's fk
    // void OnBeforeTransformParentChanged()
    // {
    //     DynamicBoneManager.Instance?.RemoveBone(this);
    //     InitTransforms();
    // }
    // void OnTransformParentChanged()
    // {
    //     SetupParticles();
    // }

    void CheckDistance()
    {
        if (!m_DistantDisable)
            return;

        Transform rt = m_ReferenceObject;
        if (rt == null && Camera.main != null)
        {
            rt = Camera.main.transform;
        }

        if (rt != null)
        {
            float d2 = (rt.position - transform.position).sqrMagnitude;
            bool disable = d2 > m_DistanceToObject * m_DistanceToObject;
            if (disable != m_DistantDisabled)
            {
                if (!disable)
                {
                    ResetParticlesPosition();
                    FastDynamicBoneManager.Instance?.AddBone(this);
                }
                else
                {
                    FastDynamicBoneManager.Instance?.RemoveBone(this);
                }

                m_DistantDisabled = disable;
            }
        }
    }

    // public bool IsNeedUpdate()
    // {
    //     return m_Weight > 0 && !(m_DistantDisable && m_DistantDisabled);
    // }

    void Start()
    {
        //SetupParticles();
    }
    
    void OnDestroy()
    {
        FastDynamicBoneManager.Instance?.RemoveBone(this);
        // InitTransforms();//复位通用bone
    }

    //OnValidate跟单例相性不合..
    void OnValidate()
    {
        m_Damping = Mathf.Clamp01(m_Damping);
        m_Elasticity = Mathf.Clamp01(m_Elasticity);
        m_Stiffness = Mathf.Clamp01(m_Stiffness);
        m_Inert = Mathf.Clamp01(m_Inert);
        m_Friction = Mathf.Clamp01(m_Friction);
        m_Radius = Mathf.Max(m_Radius, 0);
    
        // Awake附近调一次.slider持续
        // 拖拽填空的不会触发这里...so 原版collider是每次clear再add
        if (Application.isEditor && Application.isPlaying && isActiveAndEnabled)
        {
            if (IsRootChanged())
            {
                InitTransforms();
                SetupParticles();
            }
            else
            {
                UpdateParameters();
                // manager update value 
                FastDynamicBoneManager.Instance.UpdateBone(this);
            }
        }
    }

    bool IsRootChanged()
    {
        var roots = new List<Transform>();
        if (m_Root != null)
        {
            roots.Add(m_Root);
        }

        if (m_Roots != null)
        {
            foreach (var root in m_Roots)
            {
                if (root != null && !roots.Contains(root))
                {
                    roots.Add(root);
                }
            }
        }

        if (roots.Count != m_ParticleTrees.Count)
            return true;

        for (int i = 0; i < roots.Count; ++i)
        {
            if (roots[i] != m_ParticleTrees[i].m_Root)
                return true;
        }

        return false;
    }

    void OnDidApplyAnimationProperties()
    {
        UpdateParameters();
        FastDynamicBoneManager.Instance?.UpdateBone(this);
    }

    void OnDrawGizmosSelected()
    {
        if (!enabled)
            return;

        if (Application.isEditor && !Application.isPlaying && transform.hasChanged)
        {
            transform.hasChanged = false;
            //InitTransforms();
            SetupParticles(false);
        }
        else
            FastDynamicBoneManager.Instance?.ReadBone(this);

        Gizmos.color = Color.white;
        for (int i = 0; i < m_ParticleTrees.Count; ++i)
        {
            DrawGizmos(m_ParticleTrees[i]);
        }
        // Gizmos.color = Color.green;
        // for (int i = 0; i < m_ParticleTrees.Count; ++i)
        // {
        //     DrawGizomsInitPosForDebug(m_ParticleTrees[i]);
        // }
    }

    void DrawGizmos(ParticleTree pt)
    {
        for (int i = 0; i < pt.m_Particles.Count; ++i)
        {
            Particle p = pt.m_Particles[i];
            if (p.m_ParentIndex >= 0)
            {
                Particle p0 = pt.m_Particles[p.m_ParentIndex];
                Gizmos.DrawLine(p.m_Position, p0.m_Position);
            }

            if (p.m_Radius > 0)
            {
                Gizmos.DrawWireSphere(p.m_Position, p.m_Radius * m_ObjectScale);
            }
        }
    }
    void DrawGizomsInitPosForDebug(ParticleTree pt)
    {
        for (int i = 1; i < pt.m_Particles.Count; ++i)
        {
            Particle p = pt.m_Particles[i];
            if (p.m_ParentIndex >= 0)
            {
                Particle p0 = pt.m_Particles[p.m_ParentIndex];
                if (p.m_ParentIndex == 0)
                {
                    p.m_PrevPosition = MathematicsUtil.LocalToWorldPosition(p0.m_Transform.position,
                        p0.m_Transform.rotation, p.m_InitLocalPosition);
                    p.m_InitRotation = p0.m_Transform.rotation * p.m_InitLocalRotation;
                }
                else
                {
                    p.m_PrevPosition = MathematicsUtil.LocalToWorldPosition(p0.m_PrevPosition, p0.m_InitRotation,
                        p.m_InitLocalPosition);
                    p.m_InitRotation = p0.m_InitRotation * p.m_InitLocalRotation;
                }

                pt.m_Particles[i] = p;
            }
        }
        for (int i = 1; i < pt.m_Particles.Count; ++i)
        {
            Particle p = pt.m_Particles[i];
            if (p.m_ParentIndex >= 0)
            {
                Particle p0 = pt.m_Particles[p.m_ParentIndex];
                // float3 v1 = p.m_PrevPosition.xyz;
                // float3 v2 = p0.m_PrevPosition.xyz;
                Vector3 v1 = p.m_PrevPosition;
                Vector3 v2 = p0.m_ParentIndex >= 0 ? p0.m_PrevPosition : p0.m_Transform.position;
                Gizmos.DrawLine(v1, v2);
            }
    
        }
    }
    public void SetWeight(float w)
    {
        if (m_Weight != w)
        {
            if (w == 0)
            {
                InitTransforms();
            }
            else if (m_Weight == 0)
            {
                ResetParticlesPosition();
            }

            m_Weight = m_BlendWeight = w;
        }
    }

    public void SetupParticles(bool setManager = true)
    {
        //manager clear
        if (setManager)
            FastDynamicBoneManager.Instance.RemoveBone(this);

        m_ParticleTrees.Clear();

        if (m_Root != null)
        {
            AppendParticleTree(m_Root);
        }

        if (m_Roots != null)
        {
            for (int i = 0; i < m_Roots.Count; ++i)
            {
                Transform root = m_Roots[i];
                if (root == null)
                    continue;

                if (m_ParticleTrees.Exists(x => x.m_Root == root))
                    continue;

                AppendParticleTree(root);
            }
        }

        m_ObjectScale = Mathf.Abs(transform.lossyScale.x);
        m_ObjectPrevPosition = transform.position;
        m_ObjectMove = Vector3.zero;

        for (int i = 0; i < m_ParticleTrees.Count; ++i)
        {
            ParticleTree pt = m_ParticleTrees[i];
            AppendParticles(pt, pt.m_Root, -1, 0, 0);
        }


        UpdateParameters();

        //manager add 
        if (setManager)
            FastDynamicBoneManager.Instance.AddBone(this);
    }

    void AppendParticleTree(Transform root)
    {
        if (root == null)
            return;

        var pt = new ParticleTree();
        pt.m_Root = root;
        pt.m_RootWorldToLocalMatrix = root.worldToLocalMatrix;
        pt.m_RootWorldRotation = root.rotation;
        m_ParticleTrees.Add(pt);
    }

    void AppendParticles(ParticleTree pt, Transform b, int parentIndex, float boneLength, int depth)
    {
        var p = new Particle();
        p.m_Transform = b;
        p.m_TransformNotNull = b != null;
        p.m_ParentIndex = parentIndex;
        p.m_Depth = depth;

        if (b != null)
        {
            p.m_Position = p.m_PrevPosition = b.position;
            p.m_InitLocalPosition = b.localPosition;
            p.m_InitLocalRotation = b.localRotation;
        }
        else // end bone
        {
            Transform pb = pt.m_Particles[parentIndex].m_Transform;
            if (m_EndLength > 0)
            {
                Transform ppb = pb.parent;
                if (ppb != null)
                {
                    p.m_EndOffset = pb.InverseTransformPoint((pb.position * 2 - ppb.position)) * m_EndLength;
                }
                else
                {
                    p.m_EndOffset = new Vector3(m_EndLength, 0, 0);
                }
            }
            else
            {
                p.m_EndOffset = pb.InverseTransformPoint(transform.TransformDirection(m_EndOffset) + pb.position);
            }

            p.m_Position = p.m_PrevPosition = pb.TransformPoint(p.m_EndOffset);
            // p.m_Position = p.m_PrevPosition =
            //     MathematicsUtil.LocalToWorldPosition(pb.position, pb.rotation, p.m_EndOffset);
            p.m_InitLocalPosition = Vector3.zero;
            p.m_InitLocalRotation = Quaternion.identity;
        }

        if (parentIndex >= 0)
        {
            boneLength += Vector3.Distance(pt.m_Particles[parentIndex].m_Transform.position, p.m_Position);
            p.m_BoneLength = boneLength;
            pt.m_BoneTotalLength = Mathf.Max(pt.m_BoneTotalLength, boneLength);
            pt.m_MaxDepth = Mathf.Max(pt.m_MaxDepth, depth);
            var mp = pt.m_Particles[parentIndex];
            mp.m_ChildCount++;
            pt.m_Particles[parentIndex] = mp;
        }

        int index = pt.m_Particles.Count;
        pt.m_Particles.Add(p);
        depth++;

        if (b != null)
        {
            for (int i = 0; i < b.childCount; ++i)
            {
                Transform child = b.GetChild(i);
                bool exclude = false;
                if (m_Exclusions != null)
                {
                    exclude = m_Exclusions.Contains(child);
                }

                if (!exclude)
                {
                    AppendParticles(pt, child, index, boneLength, depth);
                }
                else if (m_EndLength > 0 || m_EndOffset != Vector3.zero)
                {
                    AppendParticles(pt, null, index, boneLength, depth);
                }
            }

            if (b.childCount == 0 && (m_EndLength > 0 || m_EndOffset != Vector3.zero))
            {
                AppendParticles(pt, null, index, boneLength, depth);
            }
        }
    }

    public void UpdateParameters()
    {
        // SetWeight(m_BlendWeight);
        m_Weight = m_BlendWeight;
        
        for (int i = 0; i < m_ParticleTrees.Count; ++i)
        {
            UpdateParameters(m_ParticleTrees[i]);
        }
    }

    void UpdateParameters(ParticleTree pt)
    {
        // m_LocalGravity = m_Root.InverseTransformDirection(m_Gravity);
        pt.m_LocalGravity = pt.m_RootWorldToLocalMatrix.MultiplyVector(m_Gravity).normalized * m_Gravity.magnitude;

        for (int i = 0; i < pt.m_Particles.Count; ++i)
        {
            Particle p = pt.m_Particles[i];
            p.m_Damping = m_Damping;
            p.m_Elasticity = m_Elasticity;
            p.m_Stiffness = m_Stiffness;
            p.m_Inert = m_Inert;
            p.m_Friction = m_Friction;
            p.m_Radius = m_Radius;

            if (pt.m_BoneTotalLength > 0)
            {
                float a = p.m_BoneLength / pt.m_BoneTotalLength;
                if (m_DampingDistrib != null && m_DampingDistrib.keys.Length > 0)
                    p.m_Damping *= m_DampingDistrib.Evaluate(a);
                if (m_ElasticityDistrib != null && m_ElasticityDistrib.keys.Length > 0)
                    p.m_Elasticity *= m_ElasticityDistrib.Evaluate(a);
                if (m_StiffnessDistrib != null && m_StiffnessDistrib.keys.Length > 0)
                    p.m_Stiffness *= m_StiffnessDistrib.Evaluate(a);
                if (m_InertDistrib != null && m_InertDistrib.keys.Length > 0)
                    p.m_Inert *= m_InertDistrib.Evaluate(a);
                if (m_FrictionDistrib != null && m_FrictionDistrib.keys.Length > 0)
                    p.m_Friction *= m_FrictionDistrib.Evaluate(a);
                if (m_RadiusDistrib != null && m_RadiusDistrib.keys.Length > 0)
                    p.m_Radius *= m_RadiusDistrib.Evaluate(a);
            }

            p.m_Damping = Mathf.Clamp01(p.m_Damping);
            p.m_Elasticity = Mathf.Clamp01(p.m_Elasticity);
            p.m_Stiffness = Mathf.Clamp01(p.m_Stiffness);
            p.m_Inert = Mathf.Clamp01(p.m_Inert);
            p.m_Friction = Mathf.Clamp01(p.m_Friction);
            p.m_Radius = Mathf.Max(p.m_Radius, 0);
            pt.m_Particles[i] = p;
        }
    }

    void InitTransforms()
    {
        for (int i = 0; i < m_ParticleTrees.Count; ++i)
        {
            InitTransforms(m_ParticleTrees[i]);
        }
    }

    void InitTransforms(ParticleTree pt)
    {
        for (int i = 0; i < pt.m_Particles.Count; ++i)
        {
            Particle p = pt.m_Particles[i];
            if (p.m_TransformNotNull)
            {
                p.m_Transform.localPosition = p.m_InitLocalPosition;
                p.m_Transform.localRotation = p.m_InitLocalRotation;
            }
        }
    }

    void ResetParticlesPosition()
    {
        for (int i = 0; i < m_ParticleTrees.Count; ++i)
        {
            ResetParticlesPosition(m_ParticleTrees[i]);
        }

        m_ObjectPrevPosition = transform.position;
    }

    void ResetParticlesPosition(ParticleTree pt)
    {
        for (int i = 0; i < pt.m_Particles.Count; ++i)
        {
            Particle p = pt.m_Particles[i];
            if (p.m_TransformNotNull)
            {
                p.m_Position = p.m_PrevPosition = p.m_Transform.position;
            }
            else // end bone
            {
                p.m_Position = p.m_PrevPosition =
                    pt.m_Particles[p.m_ParentIndex].m_TransformLocalToWorldMatrix.MultiplyPoint3x4(p.m_EndOffset);
            }

            p.m_isCollide = false;
            pt.m_Particles[i] = p;
        }
    }
}