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
            "Generate a beautiful postcard-style image based on a text prompt using FLUX. Returns the local file path of the generated image.");
    }

    /// <summary>
    /// Generates an image using the Together.xyz FLUX API.
    /// </summary>
    /// <param name="prompt">Detailed description of the image to generate. Be specific about the scene, style, lighting, and atmosphere.</param>
    /// <param name="steps">Number of inference steps (1-4, default 4). Higher values may produce better quality but take longer.</param>
    /// <param name="numberOfImages">Number of images to generate (1-4, default 1)</param>
    /// <returns>Result containing the file path of the generated image or an error message</returns>
    [Description("Generate a beautiful postcard-style image based on a text prompt. Returns the local file path of the generated image.")]
    public async Task<ImageGenerationResult> GenerateImageAsync(
        [Description("Detailed description of the image to generate. Be specific about the scene, style, lighting, and atmosphere.")]
        string prompt,
        [Description("Number of inference steps (1-4, default 4). Higher values produce better quality but take longer.")]
        int steps = 4,
        [Description("Number of images to generate (1-4, default 1)")]
        int numberOfImages = 1)
    {
        try
        {
            Console.WriteLine($"[FluxImageTool] Generating image for prompt: {prompt}");
            
            // Validate parameters (FLUX.1-schnell only supports 1-4 steps)
            steps = Math.Clamp(steps, 1, 4);
            numberOfImages = Math.Clamp(numberOfImages, 1, 4);

            // Build the request body matching the Together.xyz API
            var requestBody = new TogetherImageRequest
            {
                Model = _model,
                Prompt = prompt,
                Steps = steps,
                N = numberOfImages
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
                    Prompt = prompt
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
                    Prompt = prompt
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
                    Prompt = prompt
                };
            }

            Console.WriteLine($"[FluxImageTool] Image saved to: {filePath}");

            return new ImageGenerationResult
            {
                Success = true,
                FilePath = filePath,
                Prompt = prompt,
                RevisedPrompt = firstImage.RevisedPrompt,
                Model = _model,
                ImagesGenerated = togetherResponse.Data.Count
            };
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[FluxImageTool] HTTP Error: {ex.Message}");
            return new ImageGenerationResult
            {
                Success = false,
                Error = $"HTTP request failed: {ex.Message}",
                Prompt = prompt
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FluxImageTool] Error: {ex.Message}");
            return new ImageGenerationResult
            {
                Success = false,
                Error = $"Image generation failed: {ex.Message}",
                Prompt = prompt
            };
        }
    }
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
    public int Steps { get; set; } = 10;
    
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
    
    public override string ToString()
    {
        if (Success)
        {
            return $"Image generated successfully and saved to: {FilePath}";
        }
        return $"Image generation failed: {Error}";
    }
}
