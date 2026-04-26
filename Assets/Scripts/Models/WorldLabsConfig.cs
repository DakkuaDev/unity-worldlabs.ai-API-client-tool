// Purpose: Persistent configuration for World Labs API integration.
// Functionality: Stores API key securely via EditorPrefs and exposes default settings.
// Dependencies: None (runtime-compatible ScriptableObject).

using UnityEngine;

namespace WorldLabs.Unity.Models
{
    [CreateAssetMenu(fileName = "WorldLabsConfig", menuName = "World Labs/Configuration")]
    public class WorldLabsConfig : ScriptableObject
    {
        #region Constants

        public const string BASE_URL = "https://api.worldlabs.ai";
        public const string API_KEY_PREF = "WorldLabs_ApiKey";
        public const string CONFIG_ASSET_PATH = "Assets/Editor/WorldLabs/WorldLabsConfig.asset";

        private static readonly string[] MODEL_OPTIONS =
        {
            "marble-1.0-draft",
            "marble-1.0",
            "marble-1.1",
            "marble-1.1-plus"
        };

        private static readonly string[] RESOLUTION_OPTIONS =
        {
            "Full Resolution",
            "500k Splats",
            "100k Splats"
        };

        #endregion

        #region Serialized Fields

        [Header("Generation Defaults")]
        [SerializeField] private GenerationModel defaultModel = GenerationModel.Marble11;
        [SerializeField] private SplatResolution defaultSplatResolution = SplatResolution.FullRes;

        [Header("Import Defaults")]
        [SerializeField] private string defaultImportPath = "Assets/GaussianAssets";
        [SerializeField] private bool autoCreateSceneObject = true;
        [SerializeField] private bool importColliderMesh = true;

        [Header("API Settings")]
        [SerializeField] private int requestTimeoutSeconds = 60;
        [SerializeField] private int downloadTimeoutSeconds = 300;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private int pollIntervalSeconds = 5;

        #endregion

        #region Properties

        public GenerationModel DefaultModel
        {
            get => defaultModel;
            set => defaultModel = value;
        }

        public SplatResolution DefaultSplatResolution
        {
            get => defaultSplatResolution;
            set => defaultSplatResolution = value;
        }

        public string DefaultImportPath
        {
            get => defaultImportPath;
            set => defaultImportPath = value;
        }

        public bool AutoCreateSceneObject
        {
            get => autoCreateSceneObject;
            set => autoCreateSceneObject = value;
        }

        public bool ImportColliderMesh
        {
            get => importColliderMesh;
            set => importColliderMesh = value;
        }

        public int RequestTimeoutSeconds
        {
            get => requestTimeoutSeconds;
            set => requestTimeoutSeconds = value;
        }

        public int DownloadTimeoutSeconds
        {
            get => downloadTimeoutSeconds;
            set => downloadTimeoutSeconds = value;
        }

        public int MaxRetries
        {
            get => maxRetries;
            set => maxRetries = value;
        }

        public int PollIntervalSeconds
        {
            get => pollIntervalSeconds;
            set => pollIntervalSeconds = value;
        }

        public static string[] ModelDisplayOptions => MODEL_OPTIONS;
        public static string[] ResolutionDisplayOptions => RESOLUTION_OPTIONS;

        #endregion

        #region API Key Management

        public static string GetApiKey()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetString(API_KEY_PREF, string.Empty);
#else
            return string.Empty;
#endif
        }

        public static void SetApiKey(string apiKey)
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString(API_KEY_PREF, apiKey ?? string.Empty);
#endif
        }

        public static bool HasApiKey()
        {
            return !string.IsNullOrEmpty(GetApiKey());
        }

        public static void ClearApiKey()
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.DeleteKey(API_KEY_PREF);
#endif
        }

        #endregion

        #region Config Instance Management

        private static WorldLabsConfig _instance;

        public static WorldLabsConfig GetOrCreateInstance()
        {
#if UNITY_EDITOR
            if (_instance != null) return _instance;

            _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldLabsConfig>(CONFIG_ASSET_PATH);
            if (_instance != null) return _instance;

            _instance = CreateInstance<WorldLabsConfig>();
            string directory = System.IO.Path.GetDirectoryName(CONFIG_ASSET_PATH);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            UnityEditor.AssetDatabase.CreateAsset(_instance, CONFIG_ASSET_PATH);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("[WorldLabs] Created configuration asset at: " + CONFIG_ASSET_PATH);

            return _instance;
#else
            return null;
#endif
        }

        #endregion
    }
}
