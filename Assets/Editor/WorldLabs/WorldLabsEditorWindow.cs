// Purpose: Main Editor Window for the World Labs Unity Middleware.
// Functionality: Provides a tabbed interface for Settings, World Browser, World Generation,
//                and Import Configuration. Accessible via Window > World Labs.
// Dependencies: WorldLabsAPIClient, WorldLabsAssetImporter, GaussianSceneBuilder, WorldLabsModels.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using WorldLabs.Unity.Models;
using WorldLabs.Unity.Services;

namespace WorldLabs.Unity.Editor
{
    public class WorldLabsEditorWindow : EditorWindow
    {
        #region Constants

        private const string WINDOW_TITLE = "World Labs";
        private const float MIN_WIDTH = 480f;
        private const float MIN_HEIGHT = 600f;

        private static readonly string[] TAB_NAMES = { "Settings", "My Worlds", "Generate", "Import" };
        private static readonly string[] STATUS_FILTER_OPTIONS = { "All", "SUCCEEDED", "RUNNING", "PENDING", "FAILED" };
        private static readonly string[] MODEL_FILTER_OPTIONS = { "All", "marble-1.0-draft", "marble-1.0", "marble-1.1", "marble-1.1-plus" };
        private static readonly string[] PROMPT_TYPE_OPTIONS = { "Text", "Image", "Video" };
        private static readonly string[] SPLAT_RESOLUTION_OPTIONS = { "Full Resolution", "500k Splats", "100k Splats" };

        #endregion

        #region Private Fields

        // Core services
        private WorldLabsAPIClient _apiClient;
        private WorldLabsConfig _config;
        private WorldLabsAssetImporter _assetImporter;
        private CancellationTokenSource _cts;

        // Tab state
        private int _selectedTab;

        // Settings tab
        private string _apiKeyInput = "";
        private bool _showApiKey;
        private bool _testingConnection;
        private string _connectionTestResult;
        private bool _connectionTestSuccess;

        // Browser tab
        private List<WorldData> _worlds = new();
        private bool _loadingWorlds;
        private string _nextPageToken;
        private int _selectedStatusFilter;
        private int _selectedModelFilter;
        private Vector2 _worldListScroll;
        private WorldData _selectedWorld;
        private bool _showWorldDetail;
        private string _browserError;

        // Generate tab
        private int _selectedPromptType;
        private string _textPrompt = "";
        private string _imageUrl = "";
        private string _videoUrl = "";
        private string _displayName = "";
        private int _selectedModelIndex = 2; // marble-1.1
        private int _seedValue = -1;
        private string _tagsInput = "";
        private bool _isGenerating;
        private string _currentOperationId;
        private string _generationStatus;
        private float _generationProgress;

        // Import tab
        private WorldData _worldToImport;
        private int _selectedResolution;
        private string _importFolder = "Assets/GaussianAssets";
        private string _assetName = "";
        private bool _importColliderMesh = true;
        private bool _importPanorama;
        private bool _createSceneObject = true;
        private Vector3 _scenePosition = Vector3.zero;
        private Vector3 _sceneScale = Vector3.one;
        private bool _isImporting;
        private string _importStatus;
        private float _importProgress;

        // Styles (lazy initialized)
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _statusSuccessStyle;
        private GUIStyle _statusRunningStyle;
        private GUIStyle _statusFailedStyle;
        private GUIStyle _statusPendingStyle;
        private GUIStyle _worldItemStyle;
        private GUIStyle _worldItemSelectedStyle;
        private bool _stylesInitialized;

        #endregion

        #region Window Lifecycle

        [MenuItem("Window/World Labs")]
        public static void ShowWindow()
        {
            var window = GetWindow<WorldLabsEditorWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/world-labs-icon.png");

            if(icon != null)
                window.titleContent = new GUIContent(WINDOW_TITLE, icon);
            else
                window.titleContent = new GUIContent(WINDOW_TITLE, EditorGUIUtility.IconContent("d_SceneAsset Icon").image);

            window.minSize = new Vector2(MIN_WIDTH, MIN_HEIGHT);
            window.Show();
        }

        private void OnEnable()
        {
            _config = WorldLabsConfig.GetOrCreateInstance();
            _apiClient = new WorldLabsAPIClient(_config);
            _assetImporter = new WorldLabsAssetImporter(_apiClient, _config);
            _cts = new CancellationTokenSource();

            _apiKeyInput = WorldLabsConfig.GetApiKey();
            if (_config != null)
            {
                _importFolder = _config.DefaultImportPath;
                _selectedResolution = (int)_config.DefaultSplatResolution;
                _importColliderMesh = _config.ImportColliderMesh;
                _createSceneObject = _config.AutoCreateSceneObject;
            }
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private void OnGUI()
        {
            InitializeStyles();

            EditorGUILayout.Space(4);
            _selectedTab = GUILayout.Toolbar(_selectedTab, TAB_NAMES, GUILayout.Height(28));
            EditorGUILayout.Space(8);

            switch (_selectedTab)
            {
                case 0: DrawSettingsTab(); break;
                case 1: DrawBrowserTab(); break;
                case 2: DrawGenerateTab(); break;
                case 3: DrawImportTab(); break;
            }
        }

        #endregion

        #region Settings Tab

        private void DrawSettingsTab()
        {
            EditorGUILayout.LabelField("API Configuration", _headerStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("API Key", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                {
                    if (_showApiKey)
                        _apiKeyInput = EditorGUILayout.TextField(_apiKeyInput);
                    else
                        _apiKeyInput = EditorGUILayout.PasswordField(_apiKeyInput);

                    if (GUILayout.Button(_showApiKey ? "Hide" : "Show", GUILayout.Width(50)))
                    {
                        _showApiKey = !_showApiKey;
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("Save API Key"))
                    {
                        WorldLabsConfig.SetApiKey(_apiKeyInput);
                        _apiClient = new WorldLabsAPIClient(_config);
                        Debug.Log("[WorldLabs] API key saved.");
                    }

                    if (GUILayout.Button("Clear"))
                    {
                        _apiKeyInput = "";
                        WorldLabsConfig.ClearApiKey();
                    }

                    EditorGUI.BeginDisabledGroup(_testingConnection || string.IsNullOrEmpty(_apiKeyInput));
                    if (GUILayout.Button(_testingConnection ? "Testing..." : "Test Connection"))
                    {
                        TestConnectionAsync();
                    }
                    EditorGUI.EndDisabledGroup();
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_connectionTestResult))
                {
                    EditorGUILayout.Space(4);
                    var style = _connectionTestSuccess ? _statusSuccessStyle : _statusFailedStyle;
                    EditorGUILayout.LabelField(_connectionTestResult, style);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Default Settings", _headerStyle);
            EditorGUILayout.Space(4);

            if (_config != null)
            {
                EditorGUILayout.BeginVertical("box");
                {
                    _config.DefaultModel = (GenerationModel)EditorGUILayout.EnumPopup("Default Model", _config.DefaultModel);
                    _config.DefaultSplatResolution = (SplatResolution)EditorGUILayout.EnumPopup("Splat Resolution", _config.DefaultSplatResolution);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    {
                        _config.DefaultImportPath = EditorGUILayout.TextField("Import Path", _config.DefaultImportPath);
                        if (GUILayout.Button("...", GUILayout.Width(30)))
                        {
                            string folder = EditorUtility.OpenFolderPanel("Select Import Folder", "Assets", "");
                            if (!string.IsNullOrEmpty(folder))
                            {
                                if (folder.Contains("Assets"))
                                {
                                    _config.DefaultImportPath = "Assets" + folder.Substring(folder.IndexOf("Assets") + 6);
                                }
                            }
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    _config.AutoCreateSceneObject = EditorGUILayout.Toggle("Auto Create Scene Object", _config.AutoCreateSceneObject);
                    _config.ImportColliderMesh = EditorGUILayout.Toggle("Import Collider Mesh", _config.ImportColliderMesh);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("API Settings", EditorStyles.boldLabel);
                    _config.RequestTimeoutSeconds = EditorGUILayout.IntSlider("Request Timeout (s)", _config.RequestTimeoutSeconds, 10, 120);
                    _config.DownloadTimeoutSeconds = EditorGUILayout.IntSlider("Download Timeout (s)", _config.DownloadTimeoutSeconds, 60, 600);
                    _config.MaxRetries = EditorGUILayout.IntSlider("Max Retries", _config.MaxRetries, 0, 5);
                    _config.PollIntervalSeconds = EditorGUILayout.IntSlider("Poll Interval (s)", _config.PollIntervalSeconds, 2, 30);

                    EditorGUILayout.Space(4);
                    if (GUILayout.Button("Save Settings"))
                    {
                        EditorUtility.SetDirty(_config);
                        AssetDatabase.SaveAssets();
                        Debug.Log("[WorldLabs] Settings saved.");
                    }
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Links", _headerStyle);
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            {
                if (GUILayout.Button("Open World Labs API Docs"))
                {
                    Application.OpenURL("https://docs.worldlabs.ai/api");
                }
                if (GUILayout.Button("Open Marble Viewer"))
                {
                    Application.OpenURL("https://marble.worldlabs.ai");
                }
            }
            EditorGUILayout.EndVertical();
        }

        private async void TestConnectionAsync()
        {
            _testingConnection = true;
            _connectionTestResult = "";
            Repaint();

            try
            {
                WorldLabsConfig.SetApiKey(_apiKeyInput);
                _apiClient = new WorldLabsAPIClient(_config);

                var (success, message) = await _apiClient.TestConnection(_cts.Token);
                _connectionTestSuccess = success;
                _connectionTestResult = message;
            }
            catch (Exception ex)
            {
                _connectionTestSuccess = false;
                _connectionTestResult = $"Error: {ex.Message}";
            }
            finally
            {
                _testingConnection = false;
                Repaint();
            }
        }

        #endregion

        #region Browser Tab

        private void DrawBrowserTab()
        {
            if (!WorldLabsConfig.HasApiKey())
            {
                EditorGUILayout.HelpBox("Please configure your API key in the Settings tab first.", MessageType.Warning);
                return;
            }

            // Filters bar
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Status:", GUILayout.Width(45));
                _selectedStatusFilter = EditorGUILayout.Popup(_selectedStatusFilter, STATUS_FILTER_OPTIONS, GUILayout.Width(100));

                EditorGUILayout.LabelField("Model:", GUILayout.Width(45));
                _selectedModelFilter = EditorGUILayout.Popup(_selectedModelFilter, MODEL_FILTER_OPTIONS, GUILayout.Width(120));

                GUILayout.FlexibleSpace();

                EditorGUI.BeginDisabledGroup(_loadingWorlds);
                if (GUILayout.Button(_loadingWorlds ? "Loading..." : "Refresh", GUILayout.Width(80)))
                {
                    _nextPageToken = null;
                    _worlds.Clear();
                    LoadWorldsAsync();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_browserError))
            {
                EditorGUILayout.HelpBox(_browserError, MessageType.Error);
            }

            EditorGUILayout.Space(4);

            if (_worlds.Count == 0 && !_loadingWorlds)
            {
                EditorGUILayout.HelpBox("No worlds loaded. Click 'Refresh' to fetch your worlds.", MessageType.Info);
                return;
            }

            // World list and detail split view
            if (_showWorldDetail && _selectedWorld != null)
            {
                DrawWorldDetail();
            }
            else
            {
                DrawWorldList();
            }
        }

        private void DrawWorldList()
        {
            _worldListScroll = EditorGUILayout.BeginScrollView(_worldListScroll);
            {
                foreach (var world in _worlds)
                {
                    DrawWorldListItem(world);
                }

                if (!string.IsNullOrEmpty(_nextPageToken) && !_loadingWorlds)
                {
                    EditorGUILayout.Space(4);
                    if (GUILayout.Button("Load More"))
                    {
                        LoadWorldsAsync();
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Showing {_worlds.Count} world(s)", EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawWorldListItem(WorldData world)
        {
            bool isSelected = _selectedWorld?.world_id == world.world_id;
            var style = isSelected ? _worldItemSelectedStyle : _worldItemStyle;

            EditorGUILayout.BeginVertical(style);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.BeginVertical();
                    {
                        EditorGUILayout.LabelField(
                            world.display_name ?? world.world_id,
                            EditorStyles.boldLabel);

                        EditorGUILayout.LabelField(
                            world.model ?? "",
                            EditorStyles.miniLabel);

                        if (!string.IsNullOrEmpty(world.created_at))
                        {
                            string dateStr = FormatDate(world.created_at);
                            EditorGUILayout.LabelField($"Created: {dateStr}", EditorStyles.miniLabel);
                        }
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical(GUILayout.Width(80));
                    {
                        if (GUILayout.Button("Details", GUILayout.Height(24)))
                        {
                            _selectedWorld = world;
                            _showWorldDetail = true;
                            Repaint();
                        }

                        if (CanImportWorld(world))
                        {
                            if (GUILayout.Button("Import", GUILayout.Height(24)))
                            {
                                PrepareImport(world);
                            }
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void DrawWorldDetail()
        {
            var world = _selectedWorld;

            // Back button
            if (GUILayout.Button("< Back to List", GUILayout.Height(24)))
            {
                _showWorldDetail = false;
                Repaint();
                return;
            }

            EditorGUILayout.Space(4);

            _worldListScroll = EditorGUILayout.BeginScrollView(_worldListScroll);
            {
                // Header
                EditorGUILayout.LabelField(world.display_name ?? world.world_id, _headerStyle);
                EditorGUILayout.Space(8);

                // Metadata
                EditorGUILayout.BeginVertical("box");
                {
                    EditorGUILayout.LabelField("World Details", _subHeaderStyle);
                    EditorGUILayout.Space(2);

                    DrawReadOnlyField("World ID", world.world_id);
                    DrawReadOnlyField("Status", world.status);
                    DrawReadOnlyField("Model", world.model);
                    DrawReadOnlyField("Created", FormatDate(world.created_at));

                    if (world.world_prompt != null && !string.IsNullOrEmpty(world.world_prompt.text_prompt))
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Prompt:", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(world.world_prompt.text_prompt, EditorStyles.wordWrappedLabel);
                    }

                    if (world.assets?.caption != null)
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Caption:", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(world.assets.caption, EditorStyles.wordWrappedLabel);
                    }

                    if (world.tags != null && world.tags.Count > 0)
                    {
                        EditorGUILayout.Space(4);
                        DrawReadOnlyField("Tags", string.Join(", ", world.tags));
                    }
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(8);

                // Actions
                EditorGUILayout.BeginVertical("box");
                {
                    EditorGUILayout.LabelField("Actions", _subHeaderStyle);
                    EditorGUILayout.Space(4);

                    if (world.GetStatus() == WorldStatus.SUCCEEDED)
                    {
                        if (GUILayout.Button("Import to Project", GUILayout.Height(30)))
                        {
                            PrepareImport(world);
                        }
                    }
                    else if (CanImportWorld(world))
                    {
                        EditorGUILayout.HelpBox("Status is unrecognized but assets appear available. Import may still work.", MessageType.Warning);
                        if (GUILayout.Button("Import to Project (Status Unknown)", GUILayout.Height(30)))
                        {
                            PrepareImport(world);
                        }
                    }

                    if (!string.IsNullOrEmpty(world.world_marble_url))
                    {
                        if (GUILayout.Button("Open in Marble Viewer", GUILayout.Height(24)))
                        {
                            Application.OpenURL(world.world_marble_url);
                        }
                    }

                    EditorGUILayout.Space(4);

                    GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                    if (GUILayout.Button("Delete World"))
                    {
                        if (EditorUtility.DisplayDialog("Delete World",
                            $"Are you sure you want to permanently delete '{world.display_name ?? world.world_id}'?\n\nThis action cannot be undone.",
                            "Delete", "Cancel"))
                        {
                            DeleteWorldAsync(world.world_id);
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private async void LoadWorldsAsync()
        {
            _loadingWorlds = true;
            _browserError = null;
            Repaint();

            try
            {
                var request = new ListWorldsRequest
                {
                    page_size = 20,
                    page_token = _nextPageToken,
                    sort_by = "created_at"
                };

                if (_selectedStatusFilter > 0)
                {
                    request.status = STATUS_FILTER_OPTIONS[_selectedStatusFilter];
                }

                if (_selectedModelFilter > 0)
                {
                    request.model = MODEL_FILTER_OPTIONS[_selectedModelFilter];
                }

                var response = await _apiClient.ListWorlds(request, _cts.Token);

                if (string.IsNullOrEmpty(_nextPageToken))
                {
                    _worlds.Clear();
                }

                if (response?.worlds != null)
                {
                    _worlds.AddRange(response.worlds);
                }
                _nextPageToken = response?.next_page_token;
            }
            catch (Exception ex)
            {
                _browserError = $"Failed to load worlds: {ex.Message}";
                Debug.LogError($"[WorldLabs] {_browserError}");
            }
            finally
            {
                _loadingWorlds = false;
                Repaint();
            }
        }

        private async void DeleteWorldAsync(string worldId)
        {
            try
            {
                await _apiClient.DeleteWorld(worldId, _cts.Token);
                _worlds.RemoveAll(w => w.world_id == worldId);
                _showWorldDetail = false;
                _selectedWorld = null;
                Debug.Log($"[WorldLabs] World {worldId} deleted successfully.");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Delete Failed", $"Failed to delete world: {ex.Message}", "OK");
            }
            finally
            {
                Repaint();
            }
        }

        #endregion

        #region Generate Tab

        private void DrawGenerateTab()
        {
            if (!WorldLabsConfig.HasApiKey())
            {
                EditorGUILayout.HelpBox("Please configure your API key in the Settings tab first.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Generate New World", _headerStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical("box");
            {
                // Prompt type
                _selectedPromptType = EditorGUILayout.Popup("Prompt Type", _selectedPromptType, PROMPT_TYPE_OPTIONS);

                EditorGUILayout.Space(4);

                switch (_selectedPromptType)
                {
                    case 0: // Text
                        EditorGUILayout.LabelField("Text Prompt:", EditorStyles.boldLabel);
                        _textPrompt = EditorGUILayout.TextArea(_textPrompt, GUILayout.Height(80));
                        break;

                    case 1: // Image
                        EditorGUILayout.LabelField("Image URL:", EditorStyles.boldLabel);
                        _imageUrl = EditorGUILayout.TextField(_imageUrl);
                        EditorGUILayout.Space(2);
                        EditorGUILayout.LabelField("Additional Text Prompt (optional):");
                        _textPrompt = EditorGUILayout.TextArea(_textPrompt, GUILayout.Height(40));
                        break;

                    case 2: // Video
                        EditorGUILayout.LabelField("Video URL:", EditorStyles.boldLabel);
                        _videoUrl = EditorGUILayout.TextField(_videoUrl);
                        EditorGUILayout.Space(2);
                        EditorGUILayout.LabelField("Additional Text Prompt (optional):");
                        _textPrompt = EditorGUILayout.TextArea(_textPrompt, GUILayout.Height(40));
                        break;
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("Generation Settings", _subHeaderStyle);
                EditorGUILayout.Space(4);

                _displayName = EditorGUILayout.TextField("Display Name", _displayName);
                _selectedModelIndex = EditorGUILayout.Popup("Model", _selectedModelIndex, WorldLabsConfig.ModelDisplayOptions);

                EditorGUILayout.BeginHorizontal();
                {
                    _seedValue = EditorGUILayout.IntField("Seed (-1 = random)", _seedValue);
                    if (GUILayout.Button("Random", GUILayout.Width(60)))
                    {
                        _seedValue = -1;
                    }
                }
                EditorGUILayout.EndHorizontal();

                _tagsInput = EditorGUILayout.TextField("Tags (comma-separated)", _tagsInput);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Generate button
            EditorGUI.BeginDisabledGroup(_isGenerating || !HasValidPrompt());
            if (GUILayout.Button(_isGenerating ? "Generating..." : "Generate World", GUILayout.Height(36)))
            {
                GenerateWorldAsync();
            }
            EditorGUI.EndDisabledGroup();

            // Generation progress
            if (_isGenerating)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginVertical("box");
                {
                    EditorGUILayout.LabelField("Generation Progress", _subHeaderStyle);
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(GUILayout.Height(20)), _generationProgress, _generationStatus ?? "Working...");

                    if (!string.IsNullOrEmpty(_currentOperationId))
                    {
                        EditorGUILayout.LabelField($"Operation: {_currentOperationId}", EditorStyles.miniLabel);
                    }

                    EditorGUILayout.Space(4);
                    GUI.backgroundColor = new Color(1f, 0.5f, 0.3f);
                    if (GUILayout.Button("Cancel Generation"))
                    {
                        _cts?.Cancel();
                        _cts = new CancellationTokenSource();
                        _isGenerating = false;
                        _generationStatus = "Cancelled";
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndVertical();
            }
        }

        private bool HasValidPrompt()
        {
            return _selectedPromptType switch
            {
                0 => !string.IsNullOrWhiteSpace(_textPrompt),
                1 => !string.IsNullOrWhiteSpace(_imageUrl),
                2 => !string.IsNullOrWhiteSpace(_videoUrl),
                _ => false
            };
        }

        private async void GenerateWorldAsync()
        {
            _isGenerating = true;
            _generationProgress = 0f;
            _generationStatus = "Submitting generation request...";
            Repaint();

            try
            {
                // Build the request
                WorldPrompt prompt = _selectedPromptType switch
                {
                    0 => WorldPrompt.CreateTextPrompt(_textPrompt),
                    1 => WorldPrompt.CreateImagePrompt(_imageUrl, string.IsNullOrWhiteSpace(_textPrompt) ? null : _textPrompt),
                    2 => WorldPrompt.CreateVideoPrompt(_videoUrl, string.IsNullOrWhiteSpace(_textPrompt) ? null : _textPrompt),
                    _ => WorldPrompt.CreateTextPrompt(_textPrompt)
                };

                var request = new WorldGenerateRequest
                {
                    world_prompt = prompt,
                    display_name = string.IsNullOrWhiteSpace(_displayName) ? null : _displayName,
                    model = WorldLabsConfig.ModelDisplayOptions[_selectedModelIndex],
                    seed = _seedValue
                };

                if (!string.IsNullOrWhiteSpace(_tagsInput))
                {
                    request.tags = new List<string>();
                    foreach (var tag in _tagsInput.Split(','))
                    {
                        string trimmed = tag.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            request.tags.Add(trimmed);
                    }
                }

                // Submit generation
                var response = await _apiClient.GenerateWorld(request, _cts.Token);
                _currentOperationId = response.operation_id;
                _generationStatus = "Generation submitted. Polling for completion...";
                _generationProgress = 0.1f;
                Repaint();

                // Poll for completion
                int pollInterval = _config != null ? _config.PollIntervalSeconds : 5;
                var operation = await _apiClient.WaitForOperation(
                    _currentOperationId,
                    pollInterval,
                    op =>
                    {
                        if (op.metadata != null)
                        {
                            _generationStatus = op.metadata.message ?? "Generating...";
                        }
                        _generationProgress = Mathf.Min(0.9f, _generationProgress + 0.05f);
                        Repaint();
                    },
                    _cts.Token);

                _generationProgress = 1f;
                _generationStatus = "Generation completed!";

                if (operation.response?.world != null)
                {
                    var world = operation.response.world;
                    Debug.Log($"[WorldLabs] World generated: {world.display_name ?? world.world_id}");

                    if (EditorUtility.DisplayDialog("Generation Complete",
                        $"World '{world.display_name ?? world.world_id}' has been generated.\n\nWould you like to import it now?",
                        "Import", "Later"))
                    {
                        PrepareImport(world);
                    }
                    else
                    {
                        // Refresh world list
                        _nextPageToken = null;
                        LoadWorldsAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _generationStatus = "Generation cancelled.";
            }
            catch (Exception ex)
            {
                _generationStatus = $"Generation failed: {ex.Message}";
                Debug.LogError($"[WorldLabs] {_generationStatus}");
                EditorUtility.DisplayDialog("Generation Failed", ex.Message, "OK");
            }
            finally
            {
                _isGenerating = false;
                Repaint();
            }
        }

        #endregion

        #region Import Tab

        private void DrawImportTab()
        {
            if (_worldToImport == null)
            {
                EditorGUILayout.HelpBox(
                    "No world selected for import.\n\nGo to the 'My Worlds' tab, find a completed world, and click 'Import'.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Import Configuration", _headerStyle);
            EditorGUILayout.Space(4);

            // World info
            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("Selected World", _subHeaderStyle);
                DrawReadOnlyField("Name", _worldToImport.display_name ?? _worldToImport.world_id);
                DrawReadOnlyField("ID", _worldToImport.world_id);
                DrawReadOnlyField("Model", _worldToImport.model);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Import settings
            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("Import Settings", _subHeaderStyle);
                EditorGUILayout.Space(4);

                _assetName = EditorGUILayout.TextField("Asset Name", _assetName);
                _selectedResolution = EditorGUILayout.Popup("Splat Resolution", _selectedResolution, SPLAT_RESOLUTION_OPTIONS);

                EditorGUILayout.BeginHorizontal();
                {
                    _importFolder = EditorGUILayout.TextField("Destination Folder", _importFolder);
                    if (GUILayout.Button("...", GUILayout.Width(30)))
                    {
                        string folder = EditorUtility.OpenFolderPanel("Select Import Folder", "Assets", "");
                        if (!string.IsNullOrEmpty(folder) && folder.Contains("Assets"))
                        {
                            _importFolder = "Assets" + folder.Substring(folder.IndexOf("Assets") + 6);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
                _importColliderMesh = EditorGUILayout.Toggle("Import Collider Mesh", _importColliderMesh);
                _importPanorama = EditorGUILayout.Toggle("Import Panorama", _importPanorama);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Scene Integration", EditorStyles.boldLabel);
                _createSceneObject = EditorGUILayout.Toggle("Create Scene GameObject", _createSceneObject);

                if (_createSceneObject)
                {
                    EditorGUI.indentLevel++;
                    _scenePosition = EditorGUILayout.Vector3Field("Position", _scenePosition);
                    _sceneScale = EditorGUILayout.Vector3Field("Scale", _sceneScale);
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Import button
            EditorGUI.BeginDisabledGroup(_isImporting);
            if (GUILayout.Button(_isImporting ? "Importing..." : "Import World", GUILayout.Height(36)))
            {
                ImportWorldAsync();
            }
            EditorGUI.EndDisabledGroup();

            // Import progress
            if (_isImporting)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginVertical("box");
                {
                    EditorGUI.ProgressBar(
                        EditorGUILayout.GetControlRect(GUILayout.Height(20)),
                        _importProgress,
                        _importStatus ?? "Importing...");

                    EditorGUILayout.Space(4);
                    GUI.backgroundColor = new Color(1f, 0.5f, 0.3f);
                    if (GUILayout.Button("Cancel Import"))
                    {
                        _cts?.Cancel();
                        _cts = new CancellationTokenSource();
                        _isImporting = false;
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Clear Selection"))
            {
                _worldToImport = null;
                Repaint();
            }
        }

        private void PrepareImport(WorldData world)
        {
            _worldToImport = world;
            _assetName = SanitizeDisplayName(world.display_name ?? world.world_id);
            _selectedTab = 3; // Switch to Import tab
            Repaint();
        }

        /// <summary>
        /// Returns true if the world can be imported: either status is SUCCEEDED,
        /// or status is unresolved (UNKNOWN) but the world has splat assets available.
        /// </summary>
        private static bool CanImportWorld(WorldData world)
        {
            var status = world.GetStatus();
            if (status == WorldStatus.SUCCEEDED) return true;
            if (status == WorldStatus.UNKNOWN && world.assets?.splats != null) return true;
            return false;
        }

        private async void ImportWorldAsync()
        {
            _isImporting = true;
            _importProgress = 0f;
            _importStatus = "Starting import...";
            Repaint();

            try
            {
                // Refresh the world data to get fresh signed URLs
                _importStatus = "Refreshing asset URLs...";
                Repaint();

                WorldData freshWorld;
                try
                {
                    freshWorld = await _apiClient.GetWorld(_worldToImport.world_id, _cts.Token);
                }
                catch
                {
                    Debug.LogWarning("[WorldLabs] Could not refresh URLs, using existing ones.");
                    freshWorld = _worldToImport;
                }

                var settings = new WorldImportSettings
                {
                    worldId = freshWorld.world_id,
                    worldName = _assetName,
                    splatResolution = (SplatResolution)_selectedResolution,
                    importFolder = _importFolder,
                    importColliderMesh = _importColliderMesh,
                    importPanorama = _importPanorama,
                    createSceneGameObject = _createSceneObject,
                    scenePosition = _scenePosition,
                    sceneScale = _sceneScale
                };

                _assetImporter.OnProgressChanged += OnImportProgress;
                _assetImporter.OnStatusChanged += OnImportStatus;

                var result = await _assetImporter.ImportWorld(freshWorld, settings, _cts.Token);

                if (result.Success && _createSceneObject)
                {
                    GaussianSceneBuilder.CreateSceneObject(result, _scenePosition, _sceneScale);
                }

                if (result.Success)
                {
                    _importProgress = 1f;
                    _importStatus = "Import completed successfully!";
                    EditorUtility.DisplayDialog("Import Complete",
                        $"World '{_assetName}' imported successfully.\n\n" +
                        $"Assets saved to: {_importFolder}",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Import Failed", result.ErrorMessage, "OK");
                }
            }
            catch (OperationCanceledException)
            {
                _importStatus = "Import cancelled.";
            }
            catch (Exception ex)
            {
                _importStatus = $"Import failed: {ex.Message}";
                EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK");
            }
            finally
            {
                _assetImporter.OnProgressChanged -= OnImportProgress;
                _assetImporter.OnStatusChanged -= OnImportStatus;
                _isImporting = false;
                Repaint();
            }
        }

        private void OnImportProgress(string description, float progress)
        {
            _importProgress = progress;
            _importStatus = description;
            Repaint();
        }

        private void OnImportStatus(string status)
        {
            _importStatus = status;
            Repaint();
        }

        #endregion

        #region Utility Methods

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 4, 4)
            };

            _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 2, 2)
            };

            _statusSuccessStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.2f, 0.8f, 0.2f) },
                fontStyle = FontStyle.Bold
            };

            _statusRunningStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.2f, 0.6f, 1f) },
                fontStyle = FontStyle.Bold
            };

            _statusFailedStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.3f, 0.3f) },
                fontStyle = FontStyle.Bold
            };

            _statusPendingStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.8f, 0.2f) },
                fontStyle = FontStyle.Bold
            };

            _worldItemStyle = new GUIStyle("box")
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 1, 1)
            };

            _worldItemSelectedStyle = new GUIStyle("box")
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 1, 1)
            };

            _stylesInitialized = true;
        }

        private GUIStyle GetStatusStyle(WorldStatus status)
        {
            return status switch
            {
                WorldStatus.SUCCEEDED => _statusSuccessStyle,
                WorldStatus.RUNNING => _statusRunningStyle,
                WorldStatus.FAILED => _statusFailedStyle,
                WorldStatus.PENDING => _statusPendingStyle,
                _ => EditorStyles.miniLabel
            };
        }

        private static void DrawReadOnlyField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            EditorGUILayout.SelectableLabel(value ?? "N/A",
                EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static string FormatDate(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate)) return "N/A";
            try
            {
                var dt = DateTime.Parse(isoDate);
                return dt.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return isoDate;
            }
        }

        private static string SanitizeDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "world";
            return name.Replace(" ", "-").Replace("/", "-").Replace("\\", "-");
        }

        #endregion
    }
}
