// Unity native class IDs (5.x) and the managed UnityEngine type for each.
using System;
using System.Collections.Generic;

namespace FF9Engine.Assets;

public static class ClassIds {
    public const int GameObject = 1, Transform = 4, Camera = 20, Material = 21, MeshRenderer = 23, Texture2D = 28,
        MeshFilter = 33, Mesh = 43, Shader = 48, TextAsset = 49, AnimationClip = 74, AudioClip = 83, RenderTexture = 84,
        MonoBehaviour = 114, MonoScript = 115, Font = 128, AssetBundle = 142, SpriteRenderer = 212, Sprite = 213,
        RectTransform = 224, BuildSettings = 141, TagManager = 78, ResourceManager = 147, PreloadData = 150;

    public static readonly Dictionary<int, string> Names = new() {
        [1] = "GameObject", [2] = "Component", [4] = "Transform", [8] = "Behaviour", [12] = "ParticleAnimator",
        [15] = "EllipsoidParticleEmitter", [20] = "Camera", [21] = "Material", [23] = "MeshRenderer", [25] = "Renderer",
        [26] = "ParticleRenderer", [27] = "Texture", [28] = "Texture2D", [33] = "MeshFilter", [41] = "OcclusionPortal",
        [43] = "Mesh", [45] = "Skybox", [48] = "Shader", [49] = "TextAsset", [50] = "Rigidbody2D", [53] = "Collider2D",
        [54] = "Rigidbody", [56] = "Collider", [57] = "Joint", [58] = "CircleCollider2D", [59] = "HingeJoint",
        [60] = "PolygonCollider2D", [61] = "BoxCollider2D", [62] = "PhysicsMaterial2D", [64] = "MeshCollider",
        [65] = "BoxCollider", [68] = "EdgeCollider2D", [72] = "ComputeShader", [74] = "AnimationClip",
        [75] = "ConstantForce", [81] = "AudioListener", [82] = "AudioSource", [83] = "AudioClip", [84] = "RenderTexture",
        [87] = "MeshParticleEmitter", [88] = "ParticleEmitter", [89] = "Cubemap", [90] = "Avatar",
        [91] = "AnimatorController", [92] = "GUILayer", [93] = "RuntimeAnimatorController", [95] = "Animator",
        [96] = "TrailRenderer", [102] = "TextMesh", [104] = "RenderSettings", [108] = "Light", [111] = "Animation",
        [114] = "MonoBehaviour", [115] = "MonoScript", [117] = "Texture3D", [119] = "Projector", [120] = "LineRenderer",
        [121] = "Flare", [122] = "Halo", [123] = "LensFlare", [124] = "FlareLayer", [128] = "Font", [131] = "GUITexture",
        [132] = "GUIText", [134] = "PhysicMaterial", [135] = "SphereCollider", [136] = "CapsuleCollider",
        [137] = "SkinnedMeshRenderer", [138] = "FixedJoint", [142] = "AssetBundle", [143] = "CharacterController",
        [144] = "CharacterJoint", [145] = "SpringJoint", [146] = "WheelCollider", [152] = "MovieTexture",
        [153] = "ConfigurableJoint", [154] = "TerrainCollider", [156] = "TerrainData", [157] = "LightmapSettings",
        [164] = "AudioReverbFilter", [165] = "AudioHighPassFilter", [166] = "AudioChorusFilter", [167] = "AudioReverbZone",
        [168] = "AudioEchoFilter", [169] = "AudioLowPassFilter", [170] = "AudioDistortionFilter", [182] = "WindZone",
        [183] = "Cloth", [192] = "OcclusionArea", [193] = "Tree", [195] = "NavMeshAgent", [198] = "ParticleSystem",
        [199] = "ParticleSystemRenderer", [200] = "ShaderVariantCollection", [205] = "LODGroup", [207] = "Motion",
        [208] = "NavMeshObstacle", [212] = "SpriteRenderer", [213] = "Sprite", [215] = "ReflectionProbe",
        [218] = "Terrain", [220] = "LightProbeGroup", [221] = "AnimatorOverrideController", [222] = "CanvasRenderer",
        [223] = "Canvas", [224] = "RectTransform", [225] = "CanvasGroup", [226] = "BillboardAsset",
        [227] = "BillboardRenderer", [240] = "AudioMixer", [258] = "LightProbes", [273] = "AudioMixerGroup",
        [272] = "AudioMixerSnapshot", [290] = "AssetBundleManifest",
    };

    static readonly Dictionary<int, Type?> types = new();

    public static Type? ManagedType(int classId) {
        if (types.TryGetValue(classId, out var t)) return t;
        t = null;
        if (Names.TryGetValue(classId, out var n)) {
            t = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine." + n);
            if (t == null && n == "AudioMixer") t = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.Audio.AudioMixer");
            if (t == null && n == "AudioMixerGroup") t = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.Audio.AudioMixerGroup");
            if (t == null && n == "AudioMixerSnapshot") t = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.Audio.AudioMixerSnapshot");
        }
        types[classId] = t;
        return t;
    }

    public static int ForType(Type t) {
        for (var c = t; c != null; c = c.BaseType)
            foreach (var kv in Names)
                if (c.Assembly == typeof(UnityEngine.Object).Assembly && ("UnityEngine." + kv.Value == c.FullName ||
                    "UnityEngine.Audio." + kv.Value == c.FullName)) return kv.Key;
        return 0;
    }

    public static string Name(int id) => Names.TryGetValue(id, out var n) ? n : "class" + id;
}
