// Purpose: HTTP client for the World Labs REST API.
// Functionality: Provides async methods for all World Labs API endpoints with
//                authentication, error handling, and retry logic.
// Dependencies: WorldLabsModels, WorldLabsJsonHelper, WorldLabsConfig.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using WorldLabs.Unity.Models;

namespace WorldLabs.Unity.Services
{
    public class WorldLabsAPIClient
    {
        #region Private Fields

        private readonly string _baseUrl;
        private readonly int _timeoutSeconds;
        private readonly int _maxRetries;

        #endregion

        #region Constructor

        public WorldLabsAPIClient(string baseUrl = null, int timeoutSeconds = 60, int maxRetries = 3)
        {
            _baseUrl = baseUrl ?? WorldLabsConfig.BASE_URL;
            _timeoutSeconds = timeoutSeconds;
            _maxRetries = maxRetries;
        }

        public WorldLabsAPIClient(WorldLabsConfig config) : this(
            WorldLabsConfig.BASE_URL,
            config != null ? config.RequestTimeoutSeconds : 60,
            config != null ? config.MaxRetries : 3)
        {
        }

        #endregion

        #region Public API Methods

        /// <summary>
        /// Tests the API connection by attempting to list worlds.
        /// Returns true if the API key is valid and the connection succeeds.
        /// </summary>
        public async Task<(bool success, string message)> TestConnection(CancellationToken ct = default)
        {
            try
            {
                var request = new ListWorldsRequest { page_size = 1 };
                var response = await ListWorlds(request, ct);
                return (true, "Connection successful.");
            }
            catch (WorldLabsApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                return (false, "Invalid API key. Please check your credentials.");
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a new world from the given request.
        /// Returns an operation ID for polling.
        /// </summary>
        public async Task<GenerateWorldResponse> GenerateWorld(WorldGenerateRequest request, CancellationToken ct = default)
        {
            string json = WorldLabsJsonHelper.SerializeGenerateRequest(request);
            string responseJson = await PostRequest("/marble/v1/worlds:generate", json, ct);
            return WorldLabsJsonHelper.DeserializeGenerateResponse(responseJson);
        }

        /// <summary>
        /// Lists worlds with optional filtering and pagination.
        /// </summary>
        public async Task<ListWorldsResponse> ListWorlds(ListWorldsRequest request, CancellationToken ct = default)
        {
            string json = WorldLabsJsonHelper.SerializeListWorldsRequest(request);
            string responseJson = await PostRequest("/marble/v1/worlds:list", json, ct);
            return WorldLabsJsonHelper.DeserializeListWorlds(responseJson);
        }

        /// <summary>
        /// Gets a specific world by ID with fresh signed asset URLs.
        /// </summary>
        public async Task<WorldData> GetWorld(string worldId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(worldId))
                throw new ArgumentException("World ID cannot be null or empty.", nameof(worldId));

            string responseJson = await GetRequest($"/marble/v1/worlds/{worldId}", ct);
            return WorldLabsJsonHelper.DeserializeWorld(responseJson);
        }

        /// <summary>
        /// Deletes a world permanently.
        /// </summary>
        public async Task<DeleteWorldResponse> DeleteWorld(string worldId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(worldId))
                throw new ArgumentException("World ID cannot be null or empty.", nameof(worldId));

            string responseJson = await DeleteRequest($"/marble/v1/worlds/{worldId}", ct);
            return WorldLabsJsonHelper.DeserializeDeleteResponse(responseJson);
        }

        /// <summary>
        /// Polls the status of a long-running operation (e.g., world generation).
        /// </summary>
        public async Task<OperationResponse> PollOperation(string operationId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(operationId))
                throw new ArgumentException("Operation ID cannot be null or empty.", nameof(operationId));

            string responseJson = await GetRequest($"/marble/v1/operations/{operationId}", ct);
            return WorldLabsJsonHelper.DeserializeOperation(responseJson);
        }

        /// <summary>
        /// Polls an operation until it completes or fails.
        /// Reports progress via the optional callback.
        /// </summary>
        public async Task<OperationResponse> WaitForOperation(
            string operationId,
            int pollIntervalSeconds = 5,
            Action<OperationResponse> onProgress = null,
            CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                var operation = await PollOperation(operationId, ct);
                onProgress?.Invoke(operation);

                if (operation.done)
                {
                    // JsonUtility always creates non-null nested objects, even when the JSON
                    // field is absent. The World Labs API (Google-style) may also include an
                    // empty "error": {} in successful responses. Check for actual error content.
                    bool hasError = operation.error != null &&
                                   (operation.error.code != 0 ||
                                    !string.IsNullOrEmpty(operation.error.message));
                    if (hasError)
                    {
                        throw new WorldLabsApiException(
                            operation.error.code,
                            $"Operation failed (code {operation.error.code}): {operation.error.message}");
                    }
                    return operation;
                }

                await Task.Delay(pollIntervalSeconds * 1000, ct);
            }

            throw new OperationCanceledException("Operation polling was cancelled.", ct);
        }

        /// <summary>
        /// Prepares a media asset upload and returns the signed upload URL.
        /// </summary>
        public async Task<MediaUploadResponse> PrepareMediaUpload(MediaUploadRequest request, CancellationToken ct = default)
        {
            string json = WorldLabsJsonHelper.SerializeMediaUploadRequest(request);
            string responseJson = await PostRequest("/marble/v1/media-assets:prepare_upload", json, ct);
            return WorldLabsJsonHelper.DeserializeMediaUpload(responseJson);
        }

        /// <summary>
        /// Uploads file data to the signed upload URL.
        /// </summary>
        public async Task UploadMediaData(string uploadUrl, byte[] fileData, string contentType, CancellationToken ct = default)
        {
            using var request = new UnityWebRequest(uploadUrl, "PUT");
            request.uploadHandler = new UploadHandlerRaw(fileData);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", contentType);
            request.timeout = _timeoutSeconds;

            await SendRequest(request, ct);

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new WorldLabsApiException(
                    (int)request.responseCode,
                    $"Media upload failed: {request.error}");
            }
        }

        /// <summary>
        /// Downloads raw data from a URL (for SPZ files, meshes, images).
        /// </summary>
        public async Task<byte[]> DownloadData(string url, int timeoutSeconds = 300, Action<float> onProgress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("Download URL cannot be null or empty.", nameof(url));

            using var request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                ct.ThrowIfCancellationRequested();
                onProgress?.Invoke(operation.progress);
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new WorldLabsApiException(
                    (int)request.responseCode,
                    $"Download failed: {request.error}");
            }

            onProgress?.Invoke(1f);
            return request.downloadHandler.data;
        }

        /// <summary>
        /// Downloads a texture from a URL.
        /// Uses UnityWebRequestTexture as primary method; falls back to raw download + LoadImage
        /// if the handler cast fails (e.g., signed CDN URLs with unexpected content-type headers).
        /// </summary>
        public async Task<Texture2D> DownloadTexture(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // Primary path: UnityWebRequestTexture
            try
            {
                using var request = UnityWebRequestTexture.GetTexture(url);
                request.timeout = _timeoutSeconds;
                await SendRequest(request, ct);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var handler = request.downloadHandler as DownloadHandlerTexture;
                    if (handler != null)
                    {
                        return handler.texture;
                    }

                    // Handler cast failed — fall through to raw download below
                    Debug.LogWarning("[WorldLabs] DownloadHandlerTexture cast failed, attempting raw image download.");
                }
                else
                {
                    Debug.LogWarning($"[WorldLabs] Texture request failed ({request.responseCode}): {request.error}. Attempting raw download.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldLabs] UnityWebRequestTexture threw: {ex.Message}. Attempting raw download.");
            }

            // Fallback path: download raw bytes and decode with Texture2D.LoadImage
            try
            {
                byte[] data = await DownloadData(url, _timeoutSeconds, null, ct);
                if (data != null && data.Length > 0)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(data))
                    {
                        return tex;
                    }
                    UnityEngine.Object.DestroyImmediate(tex);
                    Debug.LogWarning("[WorldLabs] LoadImage failed on raw thumbnail data.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldLabs] Fallback thumbnail download failed: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region Private HTTP Methods

        private async Task<string> GetRequest(string endpoint, CancellationToken ct)
        {
            return await ExecuteWithRetry(async () =>
            {
                string url = _baseUrl + endpoint;
                using var request = UnityWebRequest.Get(url);
                ConfigureRequest(request);

                await SendRequest(request, ct);
                return HandleResponse(request);
            }, ct);
        }

        private async Task<string> PostRequest(string endpoint, string jsonBody, CancellationToken ct)
        {
            return await ExecuteWithRetry(async () =>
            {
                string url = _baseUrl + endpoint;
                using var request = new UnityWebRequest(url, "POST");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                ConfigureRequest(request);
                request.SetRequestHeader("Content-Type", "application/json");

                await SendRequest(request, ct);
                return HandleResponse(request);
            }, ct);
        }

        private async Task<string> DeleteRequest(string endpoint, CancellationToken ct)
        {
            return await ExecuteWithRetry(async () =>
            {
                string url = _baseUrl + endpoint;
                using var request = UnityWebRequest.Delete(url);
                request.downloadHandler = new DownloadHandlerBuffer();
                ConfigureRequest(request);

                await SendRequest(request, ct);
                return HandleResponse(request);
            }, ct);
        }

        private void ConfigureRequest(UnityWebRequest request)
        {
            string apiKey = WorldLabsConfig.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new WorldLabsApiException(0, "API key is not configured. Set it in the World Labs settings.");
            }
            request.SetRequestHeader("WLT-Api-Key", apiKey);
            request.timeout = _timeoutSeconds;
        }

        private async Task SendRequest(UnityWebRequest request, CancellationToken ct)
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private string HandleResponse(UnityWebRequest request)
        {
            string responseBody = request.downloadHandler?.text ?? string.Empty;

            if (request.result == UnityWebRequest.Result.Success)
            {
                return responseBody;
            }

            int statusCode = (int)request.responseCode;
            string errorMessage = request.error;

            var apiError = WorldLabsJsonHelper.DeserializeError(responseBody);
            if (apiError?.error != null)
            {
                errorMessage = $"[{apiError.error.status}] {apiError.error.message}";
            }

            Debug.LogError($"[WorldLabs] API Error ({statusCode}): {errorMessage}");
            throw new WorldLabsApiException(statusCode, errorMessage);
        }

        private async Task<string> ExecuteWithRetry(Func<Task<string>> action, CancellationToken ct)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (WorldLabsApiException ex) when (IsRetryable(ex.StatusCode) && attempt < _maxRetries)
                {
                    attempt++;
                    int delayMs = (int)Math.Pow(2, attempt) * 1000;
                    Debug.LogWarning($"[WorldLabs] Request failed (attempt {attempt}/{_maxRetries}), retrying in {delayMs}ms: {ex.Message}");
                    await Task.Delay(delayMs, ct);
                }
            }
        }

        private static bool IsRetryable(int statusCode)
        {
            return statusCode == 429 || statusCode >= 500;
        }

        #endregion
    }

    #region Custom Exceptions

    public class WorldLabsApiException : Exception
    {
        public int StatusCode { get; }

        public WorldLabsApiException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    #endregion
}
