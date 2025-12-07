using DotNetEnv;
using GHModel.dotNET.AI.Workflow.AGUI.Server;

/// <summary>
/// Simple test class for the FluxImageGenerationTool
/// Run with: dotnet run --project . -- --test-image
/// </summary>
public static class TestImageTool
{
    public static async Task RunTest()
    {
        Console.WriteLine("=== Testing FluxImageGenerationTool ===\n");
        
        // Load environment variables
        Env.Load(".env");
        
        var apiKey = Environment.GetEnvironmentVariable("TOGETHER_API_KEY");
        if (string.IsNullOrEmpty(apiKey) || apiKey == "your_api_key_here")
        {
            Console.WriteLine("❌ ERROR: Please set your TOGETHER_API_KEY in the .env file");
            Console.WriteLine("   Get your API key from: https://api.together.xyz/");
            return;
        }
        
        Console.WriteLine("✓ TOGETHER_API_KEY is set");
        Console.WriteLine($"✓ Model: {Environment.GetEnvironmentVariable("TOGETHER_MODEL") ?? "black-forest-labs/FLUX.1-kontext-dev"}");
        Console.WriteLine($"✓ Output Dir: {Environment.GetEnvironmentVariable("IMAGE_OUTPUT_DIR") ?? Path.Combine(Path.GetTempPath(), "generated_images")}");
        Console.WriteLine();
        
        try
        {
            // Create the tool
            var imageTool = new FluxImageGenerationTool();
            Console.WriteLine("✓ FluxImageGenerationTool created successfully\n");
            
            // Test image generation
            string testPrompt = "A beautiful sunset over a tropical beach with palm trees, vibrant orange and pink sky, peaceful waves";
            Console.WriteLine($"📝 Test Prompt: {testPrompt}\n");
            Console.WriteLine("⏳ Generating image (this may take 10-30 seconds)...\n");
            
            var result = await imageTool.GenerateImageAsync(
                prompt: testPrompt,
                steps: 10,
                numberOfImages: 1
            );
            
            Console.WriteLine("\n=== Result ===");
            if (result.Success)
            {
                Console.WriteLine($"✅ SUCCESS!");
                Console.WriteLine($"   File Path: {result.FilePath}");
                Console.WriteLine($"   Model: {result.Model}");
                Console.WriteLine($"   Images Generated: {result.ImagesGenerated}");
                
                if (!string.IsNullOrEmpty(result.RevisedPrompt))
                {
                    Console.WriteLine($"   Revised Prompt: {result.RevisedPrompt}");
                }
                
                // Check if file exists
                if (File.Exists(result.FilePath))
                {
                    var fileInfo = new FileInfo(result.FilePath!);
                    Console.WriteLine($"   File Size: {fileInfo.Length / 1024.0:F1} KB");
                    Console.WriteLine($"\n🖼️  Open the image at: {result.FilePath}");
                }
            }
            else
            {
                Console.WriteLine($"❌ FAILED: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex.Message}");
            Console.WriteLine($"   Stack: {ex.StackTrace}");
        }
    }
}
