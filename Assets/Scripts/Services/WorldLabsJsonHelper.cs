// Purpose: Custom JSON serialization helpers for World Labs API.
// Functionality: Handles snake_case fields, numeric-prefixed keys, and nested objects
//                that JsonUtility cannot handle natively (e.g., "500k", "100k" keys).
// Dependencies: System.Text.RegularExpressions.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using WorldLabs.Unity.Models;

namespace WorldLabs.Unity.Services
{
    public static class WorldLabsJsonHelper
    {
        #region Request Serialization

        public static string SerializeGenerateRequest(WorldGenerateRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            sb.Append("\"world_prompt\":");
            sb.Append(SerializeWorldPrompt(request.world_prompt));

            if (!string.IsNullOrEmpty(request.display_name))
            {
                sb.Append(",\"display_name\":\"").Append(EscapeJson(request.display_name)).Append("\"");
            }

            if (!string.IsNullOrEmpty(request.model))
            {
                sb.Append(",\"model\":\"").Append(EscapeJson(request.model)).Append("\"");
            }

            if (request.seed >= 0)
            {
                sb.Append(",\"seed\":").Append(request.seed);
            }

            if (request.permission != null)
            {
                sb.Append(",\"permission\":{\"public\":").Append(request.permission.@public ? "true" : "false").Append("}");
            }

            if (request.tags != null && request.tags.Count > 0)
            {
                sb.Append(",\"tags\":[");
                for (int i = 0; i < request.tags.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(EscapeJson(request.tags[i])).Append("\"");
                }
                sb.Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        public static string SerializeListWorldsRequest(ListWorldsRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            bool first = true;

            if (request.page_size > 0)
            {
                AppendComma(sb, ref first);
                sb.Append("\"page_size\":").Append(request.page_size);
            }

            if (!string.IsNullOrEmpty(request.page_token))
            {
                AppendComma(sb, ref first);
                sb.Append("\"page_token\":\"").Append(EscapeJson(request.page_token)).Append("\"");
            }

            if (!string.IsNullOrEmpty(request.status))
            {
                AppendComma(sb, ref first);
                sb.Append("\"status\":\"").Append(EscapeJson(request.status)).Append("\"");
            }

            if (!string.IsNullOrEmpty(request.model))
            {
                AppendComma(sb, ref first);
                sb.Append("\"model\":\"").Append(EscapeJson(request.model)).Append("\"");
            }

            if (!string.IsNullOrEmpty(request.sort_by))
            {
                AppendComma(sb, ref first);
                sb.Append("\"sort_by\":\"").Append(EscapeJson(request.sort_by)).Append("\"");
            }

            if (!string.IsNullOrEmpty(request.created_after))
            {
                AppendComma(sb, ref first);
                sb.Append("\"created_after\":\"").Append(EscapeJson(request.created_after)).Append("\"");
            }

            if (!string.IsNullOrEmpty(request.created_before))
            {
                AppendComma(sb, ref first);
                sb.Append("\"created_before\":\"").Append(EscapeJson(request.created_before)).Append("\"");
            }

            sb.Append("}");
            return sb.ToString();
        }

        public static string SerializeMediaUploadRequest(MediaUploadRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"file_name\":\"").Append(EscapeJson(request.file_name)).Append("\"");
            sb.Append(",\"extension\":\"").Append(EscapeJson(request.extension)).Append("\"");
            sb.Append(",\"kind\":\"").Append(EscapeJson(request.kind)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        #endregion

        #region Response Deserialization

        public static WorldData DeserializeWorld(string json)
        {
            var world = JsonUtility.FromJson<WorldData>(json);
            PostProcessWorldAssets(ref world, json);
            PostProcessWorldStatus(ref world, json);
            return world;
        }

        public static ListWorldsResponse DeserializeListWorlds(string json)
        {
            Debug.Log($"[WorldLabs] [DEBUG] List worlds raw response: {json}");
            var response = JsonUtility.FromJson<ListWorldsResponse>(json);
            if (response?.worlds != null)
            {
                for (int i = 0; i < response.worlds.Count; i++)
                {
                    var world = response.worlds[i];
                    string worldJson = ExtractWorldJsonAtIndex(json, i);
                    if (!string.IsNullOrEmpty(worldJson))
                    {
                        PostProcessWorldAssets(ref world, worldJson);
                        PostProcessWorldStatus(ref world, worldJson);
                        response.worlds[i] = world;
                    }
                }
            }
            return response;
        }

        public static OperationResponse DeserializeOperation(string json)
        {
            Debug.Log($"[WorldLabs] [DEBUG] Operation raw response: {json}");
            var op = JsonUtility.FromJson<OperationResponse>(json);
            // Only post-process if the world actually has data (world_id is non-empty),
            // since JsonUtility creates non-null nested objects even for absent JSON fields.
            if (op?.response?.world != null && !string.IsNullOrEmpty(op.response.world.world_id))
            {
                string worldJson = ExtractNestedObject(json, "world");
                if (!string.IsNullOrEmpty(worldJson))
                {
                    var world = op.response.world;
                    PostProcessWorldAssets(ref world, worldJson);
                    PostProcessWorldStatus(ref world, worldJson);
                    op.response.world = world;
                }
            }
            return op;
        }

        public static GenerateWorldResponse DeserializeGenerateResponse(string json)
        {
            return JsonUtility.FromJson<GenerateWorldResponse>(json);
        }

        public static DeleteWorldResponse DeserializeDeleteResponse(string json)
        {
            return JsonUtility.FromJson<DeleteWorldResponse>(json);
        }

        public static MediaUploadResponse DeserializeMediaUpload(string json)
        {
            return JsonUtility.FromJson<MediaUploadResponse>(json);
        }

        public static ApiErrorResponse DeserializeError(string json)
        {
            try
            {
                return JsonUtility.FromJson<ApiErrorResponse>(json);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Private Helpers

        private static void PostProcessWorldAssets(ref WorldData world, string json)
        {
            if (world?.assets?.splats?.spz_urls == null) return;

            var match500k = Regex.Match(json, "\"500k\"\\s*:\\s*\"([^\"]+)\"");
            if (match500k.Success)
            {
                world.assets.splats.spz_urls.resolution_500k = match500k.Groups[1].Value;
            }

            var match100k = Regex.Match(json, "\"100k\"\\s*:\\s*\"([^\"]+)\"");
            if (match100k.Success)
            {
                world.assets.splats.spz_urls.resolution_100k = match100k.Groups[1].Value;
            }
        }

        /// <summary>
        /// Extracts the status field via regex as a fallback for cases where
        /// JsonUtility fails to parse it (e.g., proto enum numeric values or
        /// differently-named fields in the API response).
        /// </summary>
        private static void PostProcessWorldStatus(ref WorldData world, string json)
        {
            if (!string.IsNullOrEmpty(world.status)) return;

            // Try direct string match: "status": "SUCCEEDED"
            var statusMatch = Regex.Match(json, "\"status\"\\s*:\\s*\"([^\"]+)\"");
            if (statusMatch.Success)
            {
                world.status = statusMatch.Groups[1].Value;
                Debug.Log($"[WorldLabs] [DEBUG] Extracted status via regex: {world.status}");
                return;
            }

            // Try numeric proto enum match: "status": 3 → map to known values
            var numericMatch = Regex.Match(json, "\"status\"\\s*:\\s*(\\d+)");
            if (numericMatch.Success && int.TryParse(numericMatch.Groups[1].Value, out int statusInt))
            {
                world.status = statusInt switch
                {
                    0 => "UNKNOWN",
                    1 => "PENDING",
                    2 => "RUNNING",
                    3 => "SUCCEEDED",
                    4 => "FAILED",
                    _ => "UNKNOWN"
                };
                Debug.Log($"[WorldLabs] [DEBUG] Extracted numeric status {statusInt} → {world.status}");
            }
        }

        private static string ExtractWorldJsonAtIndex(string json, int index)
        {
            try
            {
                var matches = Regex.Matches(json, "\"world_id\"\\s*:");
                if (index < matches.Count)
                {
                    int start = FindObjectStart(json, matches[index].Index);
                    if (start >= 0)
                    {
                        int end = FindObjectEnd(json, start);
                        if (end >= 0)
                        {
                            return json.Substring(start, end - start + 1);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WorldLabs] Failed to extract world JSON at index {index}: {e.Message}");
            }
            return null;
        }

        private static string ExtractNestedObject(string json, string key)
        {
            try
            {
                var match = Regex.Match(json, $"\"{key}\"\\s*:");
                if (match.Success)
                {
                    int objStart = json.IndexOf('{', match.Index + match.Length);
                    if (objStart >= 0)
                    {
                        int end = FindObjectEnd(json, objStart);
                        if (end >= 0)
                        {
                            return json.Substring(objStart, end - objStart + 1);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WorldLabs] Failed to extract nested object '{key}': {e.Message}");
            }
            return null;
        }

        private static int FindObjectStart(string json, int fromIndex)
        {
            for (int i = fromIndex; i >= 0; i--)
            {
                if (json[i] == '{') return i;
            }
            return -1;
        }

        private static int FindObjectEnd(string json, int startIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escape = false;

            for (int i = startIndex; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string SerializeWorldPrompt(WorldPrompt prompt)
        {
            if (prompt == null) return "{}";

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"type\":\"").Append(EscapeJson(prompt.type)).Append("\"");

            if (!string.IsNullOrEmpty(prompt.text_prompt))
            {
                sb.Append(",\"text_prompt\":\"").Append(EscapeJson(prompt.text_prompt)).Append("\"");
            }

            if (prompt.disable_recaption)
            {
                sb.Append(",\"disable_recaption\":true");
            }

            switch (prompt.type)
            {
                case "image":
                    if (prompt.image_prompt != null)
                    {
                        sb.Append(",\"image_prompt\":").Append(SerializeImageSource(prompt.image_prompt));
                    }
                    if (prompt.is_pano)
                    {
                        sb.Append(",\"is_pano\":true");
                    }
                    break;

                case "multi-image":
                    if (prompt.multi_image_prompt != null)
                    {
                        sb.Append(",\"multi_image_prompt\":[");
                        for (int i = 0; i < prompt.multi_image_prompt.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            var item = prompt.multi_image_prompt[i];
                            sb.Append("{\"azimuth\":").Append(item.azimuth);
                            sb.Append(",\"content\":").Append(SerializeImageSource(item.content));
                            sb.Append("}");
                        }
                        sb.Append("]");
                    }
                    if (prompt.reconstruct_images)
                    {
                        sb.Append(",\"reconstruct_images\":true");
                    }
                    break;

                case "video":
                    if (prompt.video_prompt != null)
                    {
                        sb.Append(",\"video_prompt\":").Append(SerializeImageSource(prompt.video_prompt));
                    }
                    break;
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string SerializeImageSource(ImagePromptSource source)
        {
            if (source == null) return "{}";

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"source\":\"").Append(EscapeJson(source.source)).Append("\"");

            if (!string.IsNullOrEmpty(source.uri))
            {
                sb.Append(",\"uri\":\"").Append(EscapeJson(source.uri)).Append("\"");
            }
            if (!string.IsNullOrEmpty(source.media_asset_id))
            {
                sb.Append(",\"media_asset_id\":\"").Append(EscapeJson(source.media_asset_id)).Append("\"");
            }
            if (!string.IsNullOrEmpty(source.data_base64))
            {
                sb.Append(",\"data_base64\":\"").Append(source.data_base64).Append("\"");
                if (!string.IsNullOrEmpty(source.extension))
                {
                    sb.Append(",\"extension\":\"").Append(EscapeJson(source.extension)).Append("\"");
                }
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendComma(StringBuilder sb, ref bool first)
        {
            if (!first) sb.Append(",");
            first = false;
        }

        private static string EscapeJson(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        #endregion
    }
}
