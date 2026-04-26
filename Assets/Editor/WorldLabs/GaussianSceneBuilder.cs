// Purpose: Creates scene GameObjects with GaussianSplatRenderer and MeshCollider.
// Functionality: Replicates the MountainVillage pattern from MainScene, automatically
//                setting up a Gaussian Splat environment ready for VR interaction.
// Dependencies: org.nesnausk.gaussian-splatting package (via reflection), WorldImportResult.

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace WorldLabs.Unity.Editor
{
    public static class GaussianSceneBuilder
    {
        #region Public Methods

        /// <summary>
        /// Creates a fully configured scene GameObject from an import result.
        ///
        /// Hierarchy:
        ///   WorldName  (position + scale only; no rotation)
        ///   ├── GaussianSplat  (localRotation 0,0,-180 — WL SPZ → Unity axis correction)
        ///   └── Collider       (localRotation identity — GLB already in Unity space)
        ///
        /// The -180° Z correction is required because World Labs SPZ data uses a coordinate
        /// convention where Y is flipped and X is mirrored relative to what the
        /// org.nesnausk.gaussian-splatting package expects in Unity.
        /// The GLB collider mesh does NOT need this correction: Unity's built-in glTF importer
        /// already converts the mesh vertices from right-handed to left-handed at import time.
        /// Placing them as separate children with independent rotations keeps them aligned.
        /// </summary>
        public static GameObject CreateSceneObject(WorldImportResult importResult, Vector3 position, Vector3 scale)
        {
            if (importResult == null || !importResult.Success)
            {
                Debug.LogError("[WorldLabs] Cannot create scene object from failed import.");
                return null;
            }

            string objectName = importResult.WorldName ?? "WorldLabsWorld";

            // Root — owns position and scale; no rotation so children can correct independently.
            var root = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(root, $"Create {objectName}");
            root.transform.position = position;
            root.transform.localScale = scale;
            root.transform.localRotation = Quaternion.identity;

            // ── GaussianSplat child ──────────────────────────────────────────────────────
            // Applies the WL→Unity coordinate correction: -180° around Z flips both Y and X,
            // matching the Aras package's expected orientation.
            var splatChild = new GameObject("GaussianSplat");
            splatChild.transform.SetParent(root.transform, false);
            splatChild.transform.localRotation = Quaternion.Euler(0f, 0f, -180f);
            splatChild.transform.localPosition = Vector3.zero;
            splatChild.transform.localScale    = Vector3.one;

            bool rendererAdded = TryAddGaussianSplatRenderer(splatChild, importResult.GaussianAssetPath);
            if (!rendererAdded)
            {
                Debug.LogWarning($"[WorldLabs] Could not add GaussianSplatRenderer to '{objectName}'. " +
                                 "Ensure the gaussian-splatting package is installed and the asset exists at: " +
                                 importResult.GaussianAssetPath);
            }

            // ── Collider child ───────────────────────────────────────────────────────────
            // Identity rotation: Unity's GLB importer already outputs the mesh in Unity's
            // left-handed coordinate system. No additional correction needed here.
            if (!string.IsNullOrEmpty(importResult.ColliderMeshPath))
            {
                var colliderChild = new GameObject("Collider");
                colliderChild.transform.SetParent(root.transform, false);
                colliderChild.transform.localRotation = Quaternion.identity;
                colliderChild.transform.localPosition = Vector3.zero;
                colliderChild.transform.localScale    = Vector3.one;

                TryAddMeshCollider(colliderChild, importResult.ColliderMeshPath);
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log($"[WorldLabs] Created scene object '{objectName}' at position {position}");
            return root;
        }

        /// <summary>
        /// Creates a scene object with default settings.
        /// </summary>
        public static GameObject CreateSceneObject(WorldImportResult importResult)
        {
            return CreateSceneObject(importResult, Vector3.zero, Vector3.one);
        }

        /// <summary>
        /// Creates a prefab from an import result.
        /// </summary>
        public static string CreatePrefab(WorldImportResult importResult, string prefabFolder, Vector3 scale)
        {
            var gameObject = CreateSceneObject(importResult, Vector3.zero, scale);
            if (gameObject == null) return null;

            try
            {
                string safeName = importResult.WorldName?.Replace(" ", "-") ?? "WorldLabsWorld";
                string prefabPath = $"{prefabFolder}/{safeName}.prefab";

                if (!System.IO.Directory.Exists(prefabFolder))
                {
                    System.IO.Directory.CreateDirectory(prefabFolder);
                }

                prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
                var prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, prefabPath);
                Debug.Log($"[WorldLabs] Created prefab at: {prefabPath}");

                UnityEngine.Object.DestroyImmediate(gameObject);
                return prefabPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldLabs] Failed to create prefab: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Methods

        private static bool TryAddGaussianSplatRenderer(GameObject gameObject, string assetPath)
        {
            // Find the GaussianSplatRenderer type via reflection across all assemblies.
            // Log diagnostics to help identify the correct type names if lookup fails.
            Type rendererType = FindGaussianType(
                "GaussianSplatting.Runtime.GaussianSplatRenderer",
                "GaussianSplatting.GaussianSplatRenderer",
                "GaussianSplatRenderer");

            if (rendererType == null)
            {
                LogGaussianTypeDiagnostics();
                Debug.LogWarning("[WorldLabs] GaussianSplatRenderer type not found. " +
                                 "Check the [DEBUG] log above for available Gaussian types.");
                return false;
            }

            // Add the component regardless of whether the asset is available.
            // A GaussianSplatRenderer with m_Asset = null renders nothing but causes no errors.
            var renderer = gameObject.AddComponent(rendererType);
            if (renderer == null) return false;

            // Attempt to resolve and assign the GaussianSplatAsset.
            UnityEngine.Object splatAsset = TryLoadGaussianSplatAsset(assetPath);
            if (splatAsset != null)
            {
                TrySetField(renderer, "m_Asset", splatAsset);
                TrySetProperty(renderer, "asset", splatAsset);
                Debug.Log($"[WorldLabs] GaussianSplatRenderer configured with asset: {assetPath}");
            }
            else
            {
                Debug.LogWarning(
                    $"[WorldLabs] GaussianSplatRenderer added to '{gameObject.name}' but no asset was assigned.\n" +
                    $"To complete setup: open Tools > Gaussian Splats > Create GaussianSplatAsset,\n" +
                    $"point 'Input File' to '{assetPath}', then drag the created .asset " +
                    $"onto the 'm_Asset' field of the GaussianSplatRenderer component.");
                EditorUtility.DisplayDialog(
                    "Manual Step Required",
                    $"GaussianSplatRenderer was added to '{gameObject.name}', but the asset could not be assigned automatically.\n\n" +
                    $"To complete the import:\n" +
                    $"1. Open Tools > Gaussian Splats > Create GaussianSplatAsset\n" +
                    $"2. Set Input File to the .spz in {System.IO.Path.GetDirectoryName(assetPath)}\n" +
                    $"3. Drag the created .asset onto the m_Asset field of the GaussianSplatRenderer",
                    "OK");
            }

            EditorUtility.SetDirty(gameObject);
            return true;
        }

        /// <summary>
        /// Tries to load a GaussianSplatAsset from the given path.
        /// If the path is an .spz file (creator not run yet), also tries the derived .asset path.
        /// </summary>
        private static UnityEngine.Object TryLoadGaussianSplatAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // AssetDatabase requires forward slashes on all platforms.
            path = path.Replace('\\', '/');

            Type assetType = FindGaussianType(
                "GaussianSplatting.Runtime.GaussianSplatAsset",
                "GaussianSplatting.GaussianSplatAsset",
                "GaussianSplatAsset");

            var loadType = assetType ?? typeof(ScriptableObject);

            // Try the path as-is (already a .asset)
            var asset = AssetDatabase.LoadAssetAtPath(path, loadType);
            if (asset != null) return asset;

            // If it's an .spz path, try the expected .asset sibling
            if (path.EndsWith(".spz", System.StringComparison.OrdinalIgnoreCase))
            {
                string assetPath = System.IO.Path.ChangeExtension(path, ".asset").Replace('\\', '/');
                asset = AssetDatabase.LoadAssetAtPath(assetPath, loadType);
                if (asset != null) return asset;

                // Also try <name>/<name>.asset (some package versions nest assets)
                string dir      = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
                string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                string nestedPath = $"{dir}/{baseName}/{baseName}.asset";
                asset = AssetDatabase.LoadAssetAtPath(nestedPath, loadType);
                if (asset != null) return asset;
            }

            return null;
        }

        private static Type FindGaussianType(params string[] candidateNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var name in candidateNames)
            {
                foreach (var asm in assemblies)
                {
                    var t = asm.GetType(name);
                    if (t != null) return t;
                }
            }
            return null;
        }

        /// <summary>
        /// Logs all Gaussian-related types found in loaded assemblies for diagnostics.
        /// </summary>
        private static void LogGaussianTypeDiagnostics()
        {
            var gaussianTypes = new System.Collections.Generic.List<string>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.FullName != null && t.FullName.Contains("Gaussian"))
                            gaussianTypes.Add($"{t.FullName} [{asm.GetName().Name}]");
                    }
                }
                catch { /* some assemblies may throw on GetTypes() */ }
            }

            if (gaussianTypes.Count > 0)
                Debug.Log($"[WorldLabs] [DEBUG] Available Gaussian types:\n{string.Join("\n", gaussianTypes)}");
            else
                Debug.LogWarning("[WorldLabs] [DEBUG] No Gaussian types found in any loaded assembly. " +
                                 "Ensure the gaussian-splatting package is installed.");
        }

        private static bool TryAddMeshCollider(GameObject gameObject, string meshPath)
        {
            if (string.IsNullOrEmpty(meshPath)) return false;

            // AssetDatabase requires forward slashes on all platforms.
            meshPath = meshPath.Replace('\\', '/');

            // Force a synchronous import of the specific file.
            // AssetDatabase.Refresh() (called earlier) may not have finished processing
            // freshly written binary files — especially GLBs — before this point.
            AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceSynchronousImport);

            var mesh = FindMeshInAsset(meshPath);

            if (mesh == null)
            {
                LogMeshLoadDiagnostics(meshPath);
                return false;
            }

            var collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            // Use the world name from the parent so the physics material is named
            // after the world, not the generic "Collider" child object.
            string worldName = gameObject.transform.parent != null
                ? gameObject.transform.parent.name
                : gameObject.name;

            var physicMaterial = new PhysicsMaterial($"{worldName}_Physics")
            {
                dynamicFriction = 0.6f,
                staticFriction = 0.6f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Average
            };

            string matFolder = System.IO.Path.GetDirectoryName(meshPath)?.Replace('\\', '/');
            string matPath   = $"{matFolder}/{worldName}_Physics.physicMaterial";
            AssetDatabase.CreateAsset(physicMaterial, AssetDatabase.GenerateUniqueAssetPath(matPath));
            collider.sharedMaterial = physicMaterial;

            EditorUtility.SetDirty(gameObject);
            Debug.Log($"[WorldLabs] MeshCollider configured with mesh '{mesh.name}' from {meshPath}");
            return true;
        }

        /// <summary>
        /// Searches for a Mesh in the given asset path.
        /// GLBs import as multi-asset containers: the root asset is a GameObject,
        /// and meshes are sub-assets. Tries both the root and all sub-assets.
        /// </summary>
        private static Mesh FindMeshInAsset(string path)
        {
            // 1. Root asset (works if the file is a raw .mesh asset)
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            // 2. All sub-assets (GLB/FBX containers embed meshes as sub-assets)
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (allAssets != null)
            {
                foreach (var asset in allAssets)
                    if (asset is Mesh m) return m;
            }

            return null;
        }

        /// <summary>
        /// Logs what Unity actually sees at the given path so mesh load failures
        /// can be diagnosed without guesswork.
        /// </summary>
        private static void LogMeshLoadDiagnostics(string path)
        {
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);

            if (allAssets == null || allAssets.Length == 0)
            {
                Debug.LogWarning(
                    $"[WorldLabs] Could not load mesh from: {path}\n" +
                    "No assets found — Unity is treating this GLB as a binary blob.\n" +
                    "FIX: Install 'com.unity.cloud.gltfast' via Window > Package Manager > + > Add package by name.\n" +
                    "After install, Unity will reimport existing .glb files automatically.");
                return;
            }

            var found = string.Join(", ", System.Array.ConvertAll(
                allAssets, a => a != null ? $"{a.GetType().Name}('{a.name}')" : "null"));

            Debug.LogWarning(
                $"[WorldLabs] Could not load mesh from: {path}\n" +
                $"Assets found at path: {found}\n" +
                "No sub-asset of type Mesh was found. The GLB may use an unsupported format variant.\n" +
                "Try right-clicking the file in the Project window and selecting Reimport.");
        }

        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        private static void TrySetField(object target, string fieldName, object value)
        {
            try
            {
                var type = target.GetType();
                var field = type.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldLabs] Failed to set field '{fieldName}': {ex.Message}");
            }
        }

        private static void TrySetProperty(object target, string propertyName, object value)
        {
            try
            {
                var type = target.GetType();
                var prop = type.GetProperty(propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(target, value);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldLabs] Failed to set property '{propertyName}': {ex.Message}");
            }
        }

        #endregion
    }
}
