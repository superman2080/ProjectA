using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hovl
{
    [CreateAssetMenu(
        fileName = "HS_SwordTrailPreset",
        menuName = "Hovl/Sword Trail Preset")]
    public class HS_SwordTrailPreset : ScriptableObject
    {
        [Serializable]
        public class MaterialLayer
        {
            public Material material;
            public int sortingOrder;

            [Tooltip("Shader float property controlled after StopTrail. For example: _Dissolve.")]
            public string dissolvePropertyName = "_Dissolve";

            [Tooltip("Target dissolve value reached over Trail Lifetime after StopTrail. The value never goes below the material's starting value. Set to 0 to leave the material property completely unchanged.")]
            [Range(0f, 1f)] public float maximumDissolve;
        }

        [Header("Trail")]
        [Min(0.01f)] public float trailLifetime = 0.35f;
        [Min(0f)] public float minimumSectionDistance = 0.015f;
        [Min(0f)] public float sampleInterval;
        [Range(0, 10)] public int linesAlongTrail = 2;

        [Tooltip("When enabled, vertex alpha fades from the newest to the oldest trail sections using Alpha Over Lifetime.")]
        public bool fadeAlphaOverLifetime = true;

        public AnimationCurve alphaOverLifetime =
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        public bool startActive;
        public bool clearPreviousTrailOnStart = true;

        [Header("Low FPS Curve Smoothing")]
        [Tooltip("Adds intermediate mesh sections along a smoothed Catmull-Rom curve instead of connecting low-FPS samples with straight segments.")]
        public bool smoothLowFps = true;

        [Tooltip("Maximum approximate distance between neighboring generated sections on the smoothed curve. Smaller values create a smoother trail.")]
        [Min(0.001f)] public float maximumSmoothedSectionDistance = 0.08f;

        [Tooltip("Maximum number of additional curved sections generated between two recorded trail samples.")]
        [Range(0, 32)] public int maxIntermediateSectionsPerFrame = 8;

        [Header("Automatic Trail Points")]
        public HS_SwordMeshTrail.AutomaticAxis automaticAxis =
            HS_SwordMeshTrail.AutomaticAxis.Longest;

        public bool recalculatePointsOnAwake = true;
        [Min(0f)] public float pointAInset;
        [Min(0f)] public float pointBInset;
        public float endpointPadding;

        [Header("Material Layers")]
        [Tooltip("Each material is rendered by a separate MeshRenderer layer.")]
        public List<MaterialLayer> materialLayers = new List<MaterialLayer>
        {
            new MaterialLayer()
        };

        [Header("Rendering")]
        public bool receiveShadows;
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;

        [Header("Trail Point A Effects")]
        [Tooltip("These prefabs are instantiated as children of Trail Point A when the preset is applied.")]
        public List<GameObject> pointAEffectPrefabs = new List<GameObject>();

        private void OnValidate()
        {
            trailLifetime = Mathf.Max(0.01f, trailLifetime);
            minimumSectionDistance = Mathf.Max(0f, minimumSectionDistance);
            sampleInterval = Mathf.Max(0f, sampleInterval);
            linesAlongTrail = Mathf.Clamp(linesAlongTrail, 0, 10);
            maximumSmoothedSectionDistance =
                Mathf.Max(0.001f, maximumSmoothedSectionDistance);
            maxIntermediateSectionsPerFrame =
                Mathf.Clamp(maxIntermediateSectionsPerFrame, 0, 32);
            pointAInset = Mathf.Max(0f, pointAInset);
            pointBInset = Mathf.Max(0f, pointBInset);

            if (materialLayers == null)
            {
                materialLayers = new List<MaterialLayer>();
            }

            for (int i = 0; i < materialLayers.Count; i++)
            {
                MaterialLayer layer = materialLayers[i];
                if (layer != null)
                {
                    layer.maximumDissolve =
                        Mathf.Clamp01(layer.maximumDissolve);
                }
            }

            if (pointAEffectPrefabs == null)
            {
                pointAEffectPrefabs = new List<GameObject>();
            }
        }
    }
}