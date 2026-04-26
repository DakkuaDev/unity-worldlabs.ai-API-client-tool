// Purpose: Data models for the World Labs API (https://api.worldlabs.ai).
// Functionality: Defines all request/response types matching the World Labs REST API schema.
// Dependencies: Uses Unity's JsonUtility-compatible serialization with snake_case field names.

using System;
using System.Collections.Generic;

namespace WorldLabs.Unity.Models
{
    #region Enums

    public enum WorldStatus
    {
        UNKNOWN,
        PENDING,
        RUNNING,
        SUCCEEDED,
        FAILED
    }

    public enum GenerationModel
    {
        Marble10Draft,
        Marble10,
        Marble11,
        Marble11Plus
    }

    public enum PromptType
    {
        Text,
        Image,
        MultiImage,
        Video
    }

    public enum ImageSourceType
    {
        Uri,
        MediaAsset,
        DataBase64
    }

    public enum MediaAssetKind
    {
        Image,
        Video
    }

    public enum SplatResolution
    {
        FullRes,
        Resolution500k,
        Resolution100k
    }

    #endregion

    #region Helper Utilities

    public static class WorldLabsModelUtils
    {
        public static string ModelToString(GenerationModel model)
        {
            return model switch
            {
                GenerationModel.Marble10Draft => "marble-1.0-draft",
                GenerationModel.Marble10 => "marble-1.0",
                GenerationModel.Marble11 => "marble-1.1",
                GenerationModel.Marble11Plus => "marble-1.1-plus",
                _ => "marble-1.1"
            };
        }

        public static GenerationModel StringToModel(string model)
        {
            return model switch
            {
                "marble-1.0-draft" => GenerationModel.Marble10Draft,
                "marble-1.0" => GenerationModel.Marble10,
                "marble-1.1" => GenerationModel.Marble11,
                "marble-1.1-plus" => GenerationModel.Marble11Plus,
                _ => GenerationModel.Marble11
            };
        }

        public static string StatusToString(WorldStatus status)
        {
            return status switch
            {
                WorldStatus.PENDING => "PENDING",
                WorldStatus.RUNNING => "RUNNING",
                WorldStatus.SUCCEEDED => "SUCCEEDED",
                WorldStatus.FAILED => "FAILED",
                _ => "UNKNOWN"
            };
        }

        public static WorldStatus StringToStatus(string status)
        {
            return status switch
            {
                "PENDING" => WorldStatus.PENDING,
                "RUNNING" => WorldStatus.RUNNING,
                "SUCCEEDED" => WorldStatus.SUCCEEDED,
                "FAILED" => WorldStatus.FAILED,
                _ => WorldStatus.UNKNOWN
            };
        }

        public static string ResolutionToKey(SplatResolution resolution)
        {
            return resolution switch
            {
                SplatResolution.FullRes => "full_res",
                SplatResolution.Resolution500k => "500k",
                SplatResolution.Resolution100k => "100k",
                _ => "full_res"
            };
        }
    }

    #endregion

    #region Request Models

    [Serializable]
    public class WorldPrompt
    {
        public string type;
        public string text_prompt;
        public bool disable_recaption;

        // Image prompt fields
        public ImagePromptSource image_prompt;
        public bool is_pano;

        // Multi-image prompt fields
        public List<SphericallyLocatedContent> multi_image_prompt;
        public bool reconstruct_images;

        // Video prompt fields
        public ImagePromptSource video_prompt;

        public static WorldPrompt CreateTextPrompt(string textPrompt, bool disableRecaption = false)
        {
            return new WorldPrompt
            {
                type = "text",
                text_prompt = textPrompt,
                disable_recaption = disableRecaption
            };
        }

        public static WorldPrompt CreateImagePrompt(string imageUri, string textPrompt = null, bool isPano = false)
        {
            return new WorldPrompt
            {
                type = "image",
                image_prompt = new ImagePromptSource { source = "uri", uri = imageUri },
                text_prompt = textPrompt,
                is_pano = isPano
            };
        }

        public static WorldPrompt CreateImagePromptFromMediaAsset(string mediaAssetId, string textPrompt = null)
        {
            return new WorldPrompt
            {
                type = "image",
                image_prompt = new ImagePromptSource { source = "media_asset", media_asset_id = mediaAssetId },
                text_prompt = textPrompt
            };
        }

        public static WorldPrompt CreateImagePromptFromBase64(string base64Data, string extension, string textPrompt = null)
        {
            return new WorldPrompt
            {
                type = "image",
                image_prompt = new ImagePromptSource { source = "data_base64", data_base64 = base64Data, extension = extension },
                text_prompt = textPrompt
            };
        }

        public static WorldPrompt CreateMultiImagePrompt(List<SphericallyLocatedContent> images, string textPrompt = null, bool reconstruct = false)
        {
            return new WorldPrompt
            {
                type = "multi-image",
                multi_image_prompt = images,
                text_prompt = textPrompt,
                reconstruct_images = reconstruct
            };
        }

        public static WorldPrompt CreateVideoPrompt(string videoUri, string textPrompt = null)
        {
            return new WorldPrompt
            {
                type = "video",
                video_prompt = new ImagePromptSource { source = "uri", uri = videoUri },
                text_prompt = textPrompt
            };
        }

        public static WorldPrompt CreateVideoPromptFromMediaAsset(string mediaAssetId, string textPrompt = null)
        {
            return new WorldPrompt
            {
                type = "video",
                video_prompt = new ImagePromptSource { source = "media_asset", media_asset_id = mediaAssetId },
                text_prompt = textPrompt
            };
        }
    }

    [Serializable]
    public class ImagePromptSource
    {
        public string source;
        public string uri;
        public string media_asset_id;
        public string data_base64;
        public string extension;
    }

    [Serializable]
    public class SphericallyLocatedContent
    {
        public ImagePromptSource content;
        public float azimuth;
    }

    [Serializable]
    public class WorldPermission
    {
        public bool @public;
    }

    [Serializable]
    public class WorldGenerateRequest
    {
        public WorldPrompt world_prompt;
        public string display_name;
        public string model;
        public WorldPermission permission;
        public int seed = -1;
        public List<string> tags;
    }

    [Serializable]
    public class ListWorldsRequest
    {
        public int page_size = 20;
        public string page_token;
        public string status;
        public string model;
        public List<string> tags;
        public string is_public;
        public string created_after;
        public string created_before;
        public string sort_by;
    }

    [Serializable]
    public class MediaUploadRequest
    {
        public string file_name;
        public string extension;
        public string kind;
    }

    #endregion

    #region Response Models

    [Serializable]
    public class GenerateWorldResponse
    {
        public string operation_id;
    }

    [Serializable]
    public class OperationResponse
    {
        public string operation_id;
        public bool done;
        public OperationMetadata metadata;
        public OperationResult response;
        public OperationError error;
    }

    [Serializable]
    public class OperationMetadata
    {
        public string progress;
        public string message;
    }

    [Serializable]
    public class OperationResult
    {
        public WorldData world;
    }

    [Serializable]
    public class OperationError
    {
        public int code;
        public string message;
    }

    [Serializable]
    public class ListWorldsResponse
    {
        public List<WorldData> worlds;
        public string next_page_token;
    }

    [Serializable]
    public class WorldData
    {
        public string world_id;
        public string display_name;
        public string status;
        public string world_marble_url;
        public string model;
        public string created_at;
        public string updated_at;
        public WorldAssets assets;
        public WorldPromptInfo world_prompt;
        public WorldPermission permission;
        public List<string> tags;

        public WorldStatus GetStatus()
        {
            return WorldLabsModelUtils.StringToStatus(status);
        }
    }

    [Serializable]
    public class WorldPromptInfo
    {
        public string type;
        public string text_prompt;
    }

    [Serializable]
    public class WorldAssets
    {
        public SplatAssets splats;
        public MeshAssets mesh;
        public ImageryAssets imagery;
        public string thumbnail_url;
        public string caption;
    }

    [Serializable]
    public class SplatAssets
    {
        public SpzUrls spz_urls;
    }

    [Serializable]
    public class SpzUrls
    {
        public string full_res;
        // JsonUtility doesn't support field names starting with digits,
        // so we use a custom JSON parser for these fields.
        [NonSerialized] public string resolution_500k;
        [NonSerialized] public string resolution_100k;
    }

    [Serializable]
    public class MeshAssets
    {
        public string collider_mesh_url;
    }

    [Serializable]
    public class ImageryAssets
    {
        public string pano_url;
    }

    [Serializable]
    public class DeleteWorldResponse
    {
        public string world_id;
        public bool deleted;
    }

    [Serializable]
    public class MediaUploadResponse
    {
        public MediaAssetInfo media_asset;
        public UploadInfo upload_info;
    }

    [Serializable]
    public class MediaAssetInfo
    {
        public string media_asset_id;
        public string file_name;
        public string extension;
        public string kind;
        public string created_at;
        public string updated_at;
    }

    [Serializable]
    public class UploadInfo
    {
        public string upload_url;
        public string curl_example;
    }

    [Serializable]
    public class ApiErrorResponse
    {
        public ApiErrorDetail error;
    }

    [Serializable]
    public class ApiErrorDetail
    {
        public int code;
        public string message;
        public string status;
    }

    #endregion

    #region Import Configuration

    [Serializable]
    public class WorldImportSettings
    {
        public string worldId;
        public string worldName;
        public SplatResolution splatResolution = SplatResolution.FullRes;
        public string importFolder = "Assets/GaussianAssets";
        public bool importColliderMesh = true;
        public bool importPanorama;
        public bool createSceneGameObject = true;
        public UnityEngine.Vector3 scenePosition = UnityEngine.Vector3.zero;
        public UnityEngine.Vector3 sceneScale = UnityEngine.Vector3.one;
    }

    #endregion
}
