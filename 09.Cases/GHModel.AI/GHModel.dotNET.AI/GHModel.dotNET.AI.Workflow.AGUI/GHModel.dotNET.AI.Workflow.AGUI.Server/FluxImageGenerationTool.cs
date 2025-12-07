using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace GHModel.dotNET.AI.Workflow.AGUI.Server;

/// <summary>
/// Tool for generating images using the Together.xyz FLUX API via HTTP.
/// This tool calls the web API directly rather than using a model provider.
/// </summary>
public class FluxImageGenerationTool
{
    private readonly HttpClient _httpClient;
    private readonly string _apiEndpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _outputDirectory;

    /// <summary>
    /// Initializes a new instance of the FluxImageGenerationTool.
    /// </summary>
    /// <param name="apiKey">Together.xyz API key (or set TOGETHER_API_KEY env var)</param>
    /// <param name="model">Model to use (default: black-forest-labs/FLUX.1-kontext-dev)</param>
    /// <param name="outputDirectory">Directory where generated images will be saved</param>
    public FluxImageGenerationTool(string? apiKey = null, string? model = null, string? outputDirectory = null)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // Image generation can take time
        
        _apiEndpoint = "https://api.together.xyz/v1/images/generations";
        
        _apiKey = apiKey 
            ?? Environment.GetEnvironmentVariable("TOGETHER_API_KEY") 
            ?? throw new InvalidOperationException("TOGETHER_API_KEY is required. Set it as an environment variable or pass it to the constructor.");
        
        // Use FLUX.1-schnell for text-to-image (kontext-dev requires a condition_image)
        _model = model 
            ?? Environment.GetEnvironmentVariable("TOGETHER_MODEL") 
            ?? "black-forest-labs/FLUX.1-schnell-Free";
        
        _outputDirectory = outputDirectory 
            ?? Environment.GetEnvironmentVariable("IMAGE_OUTPUT_DIR") 
            ?? Path.Combine(Path.GetTempPath(), "generated_images");
        
        // Ensure output directory exists
        Directory.CreateDirectory(_outputDirectory);
        
        // Set up authentication
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Gets the AIFunction that can be added to an agent as a tool.
    /// </summary>
    public AIFunction GetTool()
    {
        return AIFunctionFactory.Create(GenerateImageAsync, nameof(GenerateImageAsync),
            "Forge production-ready hex-based strategy game assets (terrain tiles, character sprites, built features, or UI overlays) using the Together.ai FLUX image API.");
    }

    /// <summary>
    /// Generates an image using the Together.xyz FLUX API tailored for hex-based games.
    /// </summary>
    [Description("Generate polished art assets for a hex-based strategy game (terrain, character sprites, features, or overlays) using the Together.ai FLUX endpoint. Returns the local PNG path.")]
    public async Task<ImageGenerationResult> GenerateImageAsync(
        [Description("Gameplay brief describing the desired asset (theme, faction, story beat, mood).")]
        string gameplayBrief,
        [Description("Pick the asset type so the tool can shape camera angle and readability (TerrainTile, CharacterSprite, BuiltFeature, SpellEffect, UiOverlay).")]
        HexAssetType assetType = HexAssetType.TerrainTile,
        [Description("Background treatment so the asset composites correctly (Transparent, FlatAlbedo, AtmosphericPlate).")]
        BackgroundTreatment background = BackgroundTreatment.Transparent,
        [Description("Desired pixel width (256-1024, multiples of 64 recommended). Defaults to 768.")]
        int width = 768,
        [Description("Desired pixel height (256-1024, multiples of 64 recommended). Defaults to 768.")]
        int height = 768,
        [Description("Optional art-direction or palette hints (comma separated). Example: 'ancient bronze, mossy patina, painterly'.")]
        string? styleGuide = null,
        [Description("Deterministic seed (0 = random).")]
        int seed = 0,
        [Description("Number of inference steps (1-4, default 4). Higher values produce better quality but take longer.")]
        int steps = 4,
        [Description("Number of variations to request (1-4, default 1)")]
        int numberOfImages = 1)
    {
        try
        {
            Console.WriteLine($"[FluxImageTool] Generating hex asset: {gameplayBrief}");

            steps = Math.Clamp(steps, 1, 4);
            numberOfImages = Math.Clamp(numberOfImages, 1, 4);
            width = NormalizeDimension(width);
            height = NormalizeDimension(height);

            var engineeredPrompt = BuildHexPrompt(gameplayBrief, assetType, background, styleGuide);

            var requestBody = new TogetherImageRequest
            {
                Model = _model,
                Prompt = engineeredPrompt,
                Steps = steps,
                N = numberOfImages,
                Width = width,
                Height = height,
                Seed = seed > 0 ? seed : null
            };

            var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            
            Console.WriteLine($"[FluxImageTool] Request: {jsonContent}");
            
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            // Make the POST request
            var response = await _httpClient.PostAsync(_apiEndpoint, content);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[FluxImageTool] Response status: {response.StatusCode}");
            Console.WriteLine($"[FluxImageTool] Response body: {responseBody}");
            
            if (!response.IsSuccessStatusCode)
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    Error = $"API request failed: {response.StatusCode} - {responseBody}",
                    Prompt = gameplayBrief,
                    AssetType = assetType,
                    Background = background,
                    Width = width,
                    Height = height
                };
            }

            // Parse the response
            var togetherResponse = JsonSerializer.Deserialize<TogetherImageResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (togetherResponse?.Data == null || togetherResponse.Data.Count == 0)
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    Error = "No images were returned from the API",
                    Prompt = gameplayBrief,
                    AssetType = assetType,
                    Background = background,
                    Width = width,
                    Height = height
                };
            }

            // Process the first image (or all if needed)
            var firstImage = togetherResponse.Data[0];
            var fileName = $"flux_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png";
            var filePath = Path.Combine(_outputDirectory, fileName);

            // Check if we got a URL or base64 data
            if (!string.IsNullOrEmpty(firstImage.Url))
            {
                // Download the image from URL
                Console.WriteLine($"[FluxImageTool] Downloading image from URL: {firstImage.Url}");
                var imageBytes = await _httpClient.GetByteArrayAsync(firstImage.Url);
                await File.WriteAllBytesAsync(filePath, imageBytes);
            }
            else if (!string.IsNullOrEmpty(firstImage.B64Json))
            {
                // Decode base64 image
                Console.WriteLine($"[FluxImageTool] Decoding base64 image");
                var imageBytes = Convert.FromBase64String(firstImage.B64Json);
                await File.WriteAllBytesAsync(filePath, imageBytes);
            }
            else
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    Error = "Image data not found in response (no URL or base64 data)",
                    Prompt = gameplayBrief,
                    AssetType = assetType,
                    Background = background,
                    Width = width,
                    Height = height
                };
            }

            Console.WriteLine($"[FluxImageTool] Image saved to: {filePath}");

            return new ImageGenerationResult
            {
                Success = true,
                FilePath = filePath,
                Prompt = gameplayBrief,
                RevisedPrompt = firstImage.RevisedPrompt ?? engineeredPrompt,
                Model = _model,
                ImagesGenerated = togetherResponse.Data.Count,
                AssetType = assetType,
                Background = background,
                Width = width,
                Height = height,
                AppliedStyleGuide = styleGuide,
                Seed = requestBody.Seed,
                GeneratedPrompt = engineeredPrompt
            };
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[FluxImageTool] HTTP Error: {ex.Message}");
            return new ImageGenerationResult
            {
                Success = false,
                Error = $"HTTP request failed: {ex.Message}",
                Prompt = gameplayBrief,
                AssetType = assetType,
                Background = background,
                Width = width,
                Height = height
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FluxImageTool] Error: {ex.Message}");
            return new ImageGenerationResult
            {
                Success = false,
                Error = $"Image generation failed: {ex.Message}",
                Prompt = gameplayBrief,
                AssetType = assetType,
                Background = background,
                Width = width,
                Height = height
            };
        }
    }

    private static int NormalizeDimension(int value)
    {
        var clamped = Math.Clamp(value, 256, 1024);
        var remainder = clamped % 64;
        if (remainder == 0)
        {
            return clamped;
        }

        var roundedUp = clamped + (64 - remainder);
        return roundedUp <= 1024 ? roundedUp : clamped - remainder;
    }

    private static string BuildHexPrompt(string gameplayBrief, HexAssetType assetType, BackgroundTreatment background, string? styleGuide)
    {
        var sb = new StringBuilder();
        sb.Append("Create a production-ready illustration for a premium hex-based strategy game. ");
        sb.Append(GetAssetTypeGuidance(assetType));
        sb.Append(' ');
        sb.Append(GetBackgroundGuidance(background));
        sb.Append(" Maintain crisp readability when scaled to small hex tiles. Use board-game friendly lighting, subtle rim lights, and clearly defined silhouettes. ");

        if (!string.IsNullOrWhiteSpace(styleGuide))
        {
            sb.Append($"Style guide: {styleGuide}. ");
        }

        sb.Append($"Player request: {gameplayBrief}. ");
        sb.Append("Render as a high-quality PNG asset.");

        return sb.ToString();
    }

    private static string GetAssetTypeGuidance(HexAssetType assetType) => assetType switch
    {
        HexAssetType.TerrainTile => "Depict a seamless top-down terrain tile with isometric lighting and edge falloff that fits perfectly inside a hex.",
        HexAssetType.CharacterSprite => "Depict a character or creature hero token viewed slightly from above (15 degree tilt) with heroic pose and readable faction markings.",
        HexAssetType.BuiltFeature => "Depict a constructed feature (city, fortress, wonder, outpost) centered inside the hex with clear architectural silhouette and depth cues.",
        HexAssetType.SpellEffect => "Depict an energetic spell or status effect hovering over a hex, with layered glows and directional motion.",
        HexAssetType.UiOverlay => "Design a flat illustrative overlay icon that can sit on top of a hex tile, using transparent background and minimal shading.",
        _ => "Depict a reusable asset for a hex-based map with clear silhouette.",
    };

    private static string GetBackgroundGuidance(BackgroundTreatment background) => background switch
    {
        BackgroundTreatment.Transparent => "Use a perfectly transparent background (alpha channel) so the asset composites seamlessly.",
        BackgroundTreatment.FlatAlbedo => "Use a flat neutral background (#0F1116) with soft vignette for easy keying.",
        BackgroundTreatment.AtmosphericPlate => "Use a subtle atmospheric backdrop that reinforces depth but keeps edges readable.",
        _ => string.Empty
    };
}

#region Together.xyz API Request/Response Models

/// <summary>
/// Request format for Together.xyz Image Generation API
/// </summary>
public class TogetherImageRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "black-forest-labs/FLUX.1-schnell-Free";
    
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;
    
    [JsonPropertyName("steps")]
    public int Steps { get; set; } = 4;
    
    [JsonPropertyName("n")]
    public int N { get; set; } = 1;
    
    // Optional parameters you can add later
    [JsonPropertyName("width")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Width { get; set; }
    
    [JsonPropertyName("height")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Height { get; set; }
    
    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; set; }
}

/// <summary>
/// Response from Together.xyz Image Generation API
/// </summary>
public class TogetherImageResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    
    [JsonPropertyName("object")]
    public string? Object { get; set; }
    
    [JsonPropertyName("data")]
    public List<TogetherImageData> Data { get; set; } = new();
}

/// <summary>
/// Individual image data from Together.xyz response
/// </summary>
public class TogetherImageData
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    
    [JsonPropertyName("b64_json")]
    public string? B64Json { get; set; }
    
    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; set; }
    
    [JsonPropertyName("index")]
    public int Index { get; set; }
}

#endregion

/// <summary>
/// Result of an image generation operation
/// </summary>
public class ImageGenerationResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }
    
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }
    
    [JsonPropertyName("revisedPrompt")]
    public string? RevisedPrompt { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    
    [JsonPropertyName("imagesGenerated")]
    public int ImagesGenerated { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    [JsonPropertyName("assetType")]
    public HexAssetType AssetType { get; set; } = HexAssetType.TerrainTile;
    
    [JsonPropertyName("background")]
    public BackgroundTreatment Background { get; set; } = BackgroundTreatment.Transparent;
    
    [JsonPropertyName("width")]
    public int Width { get; set; }
    
    [JsonPropertyName("height")]
    public int Height { get; set; }
    
    [JsonPropertyName("styleGuide")]
    public string? AppliedStyleGuide { get; set; }
    
    [JsonPropertyName("seed")]
    public int? Seed { get; set; }
    
    [JsonPropertyName("engineeredPrompt")]
    public string? GeneratedPrompt { get; set; }
    
    public override string ToString()
    {
        if (Success)
        {
            return $"Hex asset ({AssetType}) generated at {Width}x{Height}px and saved to: {FilePath}";
        }
        return $"Image generation failed: {Error}";
    }
}

public enum HexAssetType
{
    TerrainTile,
    CharacterSprite,
    BuiltFeature,
    SpellEffect,
    UiOverlay
}

public enum BackgroundTreatment
{
    Transparent,
    FlatAlbedo,
    AtmosphericPlate
}
