// Purpose: Downloads and imports World Labs assets into Unity as GaussianSplatAssets.
// Functionality: Handles SPZ download, GaussianSplatAsset creation via the gaussian-splatting
//                package, collider mesh import, and panorama image import.
// Dependencies: WorldLabsAPIClient, WorldLabsModels, org.nesnausk.gaussian-splatting package.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using WorldLabs.Unity.Models;
using WorldLabs.Unity.Services;

namespace WorldLabs.Unity.Editor
{
    public class WorldLabsAssetImporter
    {
        #region Private Fields

        private readonly WorldLabsAPIClient _apiClient;
        private readonly WorldLabsConfig _config;

        #endregion

        #region Events

        public event Action<string, float> OnProgressChanged;
        public event Action<string> OnStatusChanged;
        public event Action<WorldImportResult> OnImportCompleted;
        public event Action<string> OnImportFailed;

        #endregion

        #region Constructor

        public WorldLabsAssetImporter(WorldLabsAPIClient apiClient, WorldLabsConfig config)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _config = config;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Imports a world's assets into Unity based on the given settings.
        /// </summary>
        public async Task<WorldImportResult> ImportWorld(WorldData world, WorldImportSettings settings, CancellationToken ct = default)
        {
            var result = new WorldImportResult
            {
                WorldId = world.world_id,
                WorldName = settings.worldName ?? world.display_name ?? world.world_id
            };

            try
            {
                EnsureDirectoryExists(settings.importFolder);
                string safeName = SanitizeFileName(result.WorldName);

                // All assets for this world live in their own subfolder: <importFolder>/<worldName>/
                string worldFolder = Path.Combine(settings.importFolder, safeName);
                EnsureDirectoryExists(worldFolder);

                // Step 1: Download and import SPZ gaussian splat
                ReportStatus("Downloading Gaussian Splat data...");
                string spzUrl = GetSpzUrl(world, settings.splatResolution);
                if (string.IsNullOrEmpty(spzUrl))
                {
                    throw new Exception("No SPZ URL available for the selected resolution.");
                }

                string spzPath = await DownloadFile(spzUrl, worldFolder, $"{safeName}.spz", ct);
                result.SpzFilePath = spzPath;

                // Step 2: Create GaussianSplatAsset from SPZ
                ReportStatus("Creating GaussianSplatAsset...");
                string gaussianAssetPath = await CreateGaussianSplatAsset(spzPath, worldFolder, safeName, ct);
                result.GaussianAssetPath = gaussianAssetPath;

                // Step 3: Download collider mesh (optional)
                if (settings.importColliderMesh && world.assets?.mesh?.collider_mesh_url != null)
                {
                    ReportStatus("Downloading collider mesh...");
                    string meshPath = await DownloadFile(
                        world.assets.mesh.collider_mesh_url,
                        worldFolder,
                        $"{safeName}_collider.glb",
                        ct);
                    result.ColliderMeshPath = meshPath;
                }

                // Step 4: Download panorama (optional)
                if (settings.importPanorama && world.assets?.imagery?.pano_url != null)
                {
                    ReportStatus("Downloading panorama...");
                    string panoPath = await DownloadFile(
                        world.assets.imagery.pano_url,
                        worldFolder,
                        $"{safeName}_pano.jpg",
                        ct);
                    result.PanoramaPath = panoPath;
                }

                // Step 5: Download thumbnail
                if (world.assets?.thumbnail_url != null)
                {
                    try
                    {
                        string thumbPath = await DownloadFile(
                            world.assets.thumbnail_url,
                            worldFolder,
                            $"{safeName}_thumb.jpg",
                            ct);
                        result.ThumbnailPath = thumbPath;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[WorldLabs] Thumbnail download failed (non-critical): {ex.Message}");
                    }
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                result.Success = true;
                ReportStatus("Import completed successfully.");
                OnImportCompleted?.Invoke(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                ReportStatus("Import cancelled.");
                OnImportFailed?.Invoke("Import was cancelled by user.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldLabs] Import failed: {ex.Message}");
                ReportStatus($"Import failed: {ex.Message}");
                OnImportFailed?.Invoke(ex.Message);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Re-fetches fresh signed URLs for a world and returns updated WorldData.
        /// Use when asset URLs have expired.
        /// </summary>
        public async Task<WorldData> RefreshWorldUrls(string worldId, CancellationToken ct = default)
        {
            return await _apiClient.GetWorld(worldId, ct);
        }

        #endregion

        #region Private Methods

        private async Task<string> DownloadFile(string url, string folder, string fileName, CancellationToken ct)
        {
            // Use Path.Combine for OS-level file I/O, then normalize to forward slashes
            // so every path stored in WorldImportResult is valid for AssetDatabase APIs.
            string filePath = ToUnityPath(Path.Combine(folder, fileName));

            byte[] data = await _apiClient.DownloadData(
                url,
                _config != null ? _config.DownloadTimeoutSeconds : 300,
                progress => ReportProgress($"Downloading {fileName}", progress),
                ct);

            if (data == null || data.Length == 0)
            {
                throw new Exception($"Downloaded empty data for {fileName}");
            }

            EnsureDirectoryExists(folder);
            File.WriteAllBytes(filePath, data);
            Debug.Log($"[WorldLabs] Downloaded {fileName} ({data.Length / 1024f:F1} KB) to {filePath}");
            return filePath;
        }

        /// <summary>
        /// Converts any OS path separator to forward slash, which is required by all
        /// UnityEditor.AssetDatabase methods regardless of the host operating system.
        /// </summary>
        private static string ToUnityPath(string path) => path?.Replace('\\', '/');

        private async Task<string> CreateGaussianSplatAsset(string spzFilePath, string outputFolder, string assetName, CancellationToken ct)
        {
            var creatorType = FindGaussianSplatAssetCreatorType();

            if (creatorType != null)
            {
                string result = await CreateAssetViaPackageAPI(creatorType, spzFilePath, outputFolder, assetName, ct);
                if (!string.IsNullOrEmpty(result) && result.EndsWith(".asset"))
                    return result;
            }

            // Fallback: return the EXPECTED .asset path (not the .spz path).
            // GaussianSceneBuilder will attempt to load from this path after AssetDatabase.Refresh().
            // If the asset doesn't exist yet, the user is prompted to create it manually.
            string expectedAssetPath = ToUnityPath(Path.Combine(outputFolder, $"{assetName}.asset"));
            Debug.LogWarning(
                $"[WorldLabs] Automatic GaussianSplatAsset creation failed.\n" +
                $"SPZ downloaded to: {spzFilePath}\n" +
                $"To create the asset: open Tools > Gaussian Splats > Create GaussianSplatAsset,\n" +
                $"set Input File to the .spz above, set Output Folder to '{outputFolder}', then click Create Asset.");
            return expectedAssetPath;
        }

        private Type FindGaussianSplatAssetCreatorType()
        {
            string[] typeNames = new[]
            {
                "GaussianSplatting.Editor.GaussianSplatAssetCreator",
                "GaussianSplatting.GaussianSplatAssetCreator",
                "GaussianSplatAssetCreator",
                "GaussianSplatting.Editor.GaussianSplatAssetCreatorWindow",
                "GaussianSplatting.Editor.GaussianSplatCreator"
            };

            // First pass: direct lookup
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var name in typeNames)
                {
                    var t = asm.GetType(name);
                    if (t != null)
                    {
                        Debug.Log($"[WorldLabs] Found creator type: {t.FullName} in {asm.GetName().Name}");
                        return t;
                    }
                }
            }

            // Second pass: fuzzy scan (slower but catches unexpected package versions)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name.Contains("GaussianSplat") && t.Name.Contains("Creator"))
                        {
                            Debug.Log($"[WorldLabs] Found fuzzy-match creator type: {t.FullName} in {asm.GetName().Name}");
                            return t;
                        }
                    }
                }
                catch { /* skip assemblies that throw on GetTypes() */ }
            }

            // Log all Gaussian types found so we can diagnose the correct name
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
                catch { }
            }

            if (gaussianTypes.Count > 0)
                Debug.Log($"[WorldLabs] [DEBUG] GaussianSplatAssetCreator not found. Available Gaussian types:\n{string.Join("\n", gaussianTypes)}");
            else
                Debug.LogWarning("[WorldLabs] [DEBUG] No Gaussian types found at all — is the package installed?");

            return null;
        }

        private async Task<string> CreateAssetViaPackageAPI(Type creatorType, string spzFilePath, string outputFolder, string assetName, CancellationToken ct)
        {
            try
            {
                ReportProgress("Creating GaussianSplatAsset...", 0.5f);

                // Try static creation methods by multiple candidate names
                string[] methodNames = { "CreateAsset", "Create", "CreateGaussianSplatAsset", "ImportAsset" };
                foreach (var methodName in methodNames)
                {
                    var method = creatorType.GetMethod(methodName,
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method == null) continue;

                    var parameters = method.GetParameters();
                    Debug.Log($"[WorldLabs] Invoking {creatorType.Name}.{methodName} ({parameters.Length} params)");

                    try
                    {
                        object result = parameters.Length switch
                        {
                            3 => method.Invoke(null, new object[] { spzFilePath, outputFolder, assetName }),
                            2 => method.Invoke(null, new object[] { spzFilePath, outputFolder }),
                            1 => method.Invoke(null, new object[] { spzFilePath }),
                            _ => null
                        };

                        if (result is string resultPath && resultPath.EndsWith(".asset"))
                            return resultPath;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[WorldLabs] {creatorType.Name}.{methodName} threw: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }

                // Last resort: open the EditorWindow and inform the user
                var showWindowMethod = creatorType.GetMethod("ShowWindow",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (showWindowMethod != null)
                {
                    showWindowMethod.Invoke(null, null);
                    Debug.Log($"[WorldLabs] Opened GaussianSplatAssetCreator window. " +
                              $"Set Input File to: {spzFilePath}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldLabs] CreateAssetViaPackageAPI failed: {ex.Message}");
                return null;
            }
        }

        private string GetSpzUrl(WorldData world, SplatResolution resolution)
        {
            if (world?.assets?.splats?.spz_urls == null)
                return null;

            var urls = world.assets.splats.spz_urls;
            return resolution switch
            {
                SplatResolution.FullRes => urls.full_res,
                SplatResolution.Resolution500k => urls.resolution_500k ?? urls.full_res,
                SplatResolution.Resolution100k => urls.resolution_100k ?? urls.full_res,
                _ => urls.full_res
            };
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "world";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) < 0 && c != ' ')
                    sb.Append(c);
                else if (c == ' ')
                    sb.Append('-');
            }

            string result = sb.ToString().Trim('-');
            return string.IsNullOrEmpty(result) ? "world" : result;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private void ReportProgress(string description, float progress)
        {
            OnProgressChanged?.Invoke(description, progress);
            EditorUtility.DisplayProgressBar("World Labs Import", description, progress);
        }

        private void ReportStatus(string status)
        {
            OnStatusChanged?.Invoke(status);
            Debug.Log($"[WorldLabs] {status}");
        }

        #endregion
    }

    #region Import Result

    public class WorldImportResult
    {
        public bool Success { get; set; }
        public string WorldId { get; set; }
        public string WorldName { get; set; }
        public string SpzFilePath { get; set; }
        public string GaussianAssetPath { get; set; }
        public string ColliderMeshPath { get; set; }
        public string PanoramaPath { get; set; }
        public string ThumbnailPath { get; set; }
        public string ErrorMessage { get; set; }
    }

    #endregion
}
