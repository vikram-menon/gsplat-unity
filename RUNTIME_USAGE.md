# Gsplat Unity Runtime Loading

This package provides runtime loading capabilities for PLY files containing 3D Gaussian Splatting data.

## Quick Start

### Method 1: Using GsplatRuntimeManager (Recommended)

1. Add `GsplatRuntimeManager` component to a GameObject
2. Use one of the loading methods:

```csharp
using Gsplat;

// Load from StreamingAssets folder
manager.LoadPlyFromStreamingAssets("your_file.ply");

// Load from file path
manager.LoadPlyFile("/path/to/your/file.ply");

// Load from Resources folder
manager.LoadPlyFromResources("your_file"); // no .ply extension needed

// Load from byte array
byte[] plyData = // ... get your data
manager.LoadPlyFromBytes(plyData);
```

### Method 2: Using Static Quick Methods

```csharp
using Gsplat;

// Quick load from StreamingAssets - creates new GameObject automatically
GsplatRuntimeManager.QuickLoadFromStreamingAssets("example.ply");

// Quick load from file path - creates new GameObject automatically  
GsplatRuntimeManager.QuickLoadFromFile("/path/to/file.ply");
```

### Method 3: Direct PLY Loading

```csharp
using Gsplat;

// Load PLY data directly
GsplatAsset asset = GsplatPlyLoader.LoadFromFile("/path/to/file.ply");

// Create renderer manually
GameObject go = new GameObject("My Gsplat");
GsplatRenderer renderer = go.AddComponent<GsplatRenderer>();
renderer.GsplatAsset = asset;
```

## Events and Callbacks

```csharp
var manager = GetComponent<GsplatRuntimeManager>();

// Listen for successful loading
manager.OnGsplatLoaded.AddListener(asset => {
    Debug.Log($"Loaded {asset.SplatCount} splats!");
});

// Listen for loading progress
manager.OnLoadProgress.AddListener(progress => {
    Debug.Log($"Loading: {progress * 100}%");
});

// Listen for errors
manager.OnLoadError.AddListener(error => {
    Debug.LogError($"Load failed: {error}");
});
```

## File Placement Options

### StreamingAssets (Recommended for Runtime Loading)
Place PLY files in `Assets/StreamingAssets/` folder. These files will be included in builds and accessible at runtime.

```csharp
manager.LoadPlyFromStreamingAssets("my_splat.ply");
```

### Resources Folder
Place PLY files in any `Assets/Resources/` folder (without .ply extension in code).

```csharp
manager.LoadPlyFromResources("my_splat"); // loads my_splat.ply from Resources
```

### File System
Load from any accessible file path:

```csharp
manager.LoadPlyFile(Application.persistentDataPath + "/downloaded_splat.ply");
```

## Configuration

The `GsplatRuntimeManager` provides these settings:

- **Auto Create Renderer**: Automatically adds GsplatRenderer component
- **SH Degree**: Spherical harmonics degree (0-3) for rendering quality
- **Gamma To Linear**: Color space conversion
- **Async Upload**: Enable for better performance with large files
- **Upload Batch Size**: Number of splats uploaded per frame

## Example Scripts

### Simple Loader
```csharp
using Gsplat.Examples;

// Add SimpleGsplatLoader component to GameObject
// Configure plyFileName in inspector
// Will load automatically on Start
```

### UI Example
```csharp
using Gsplat.Examples;

// Add GsplatRuntimeExample component
// Connect UI buttons in inspector
// Provides load/clear buttons with progress display
```

## Performance Tips

1. **Use AsyncUpload** for large PLY files (>100k splats)
2. **Adjust UploadBatchSize** based on target framerate
3. **Place files in StreamingAssets** for fastest loading
4. **Monitor memory usage** - each splat uses ~100 bytes

## Troubleshooting

### File Not Found
- Check file path spelling and case sensitivity
- Ensure PLY files are in correct folder (StreamingAssets/Resources)
- Use forward slashes in paths

### Loading Fails
- Verify PLY file format is compatible with 3D Gaussian Splatting
- Check Unity console for specific error messages
- Ensure file is not corrupted

### Poor Performance
- Enable async upload for large files
- Reduce upload batch size if framerate drops
- Consider loading smaller PLY files or culling distant splats

## API Reference

### GsplatPlyLoader (Static)
- `LoadFromFile(string filePath)`: Load from file path
- `LoadFromBytes(byte[] data)`: Load from byte array
- `LoadFromStream(Stream stream)`: Load from stream
- `LoadFromFileAsync(string filePath, Action<float> progressCallback)`: Async loading

### GsplatRuntimeManager
- `LoadPlyFile(string filePath)`: Load PLY file
- `LoadPlyFromStreamingAssets(string fileName)`: Load from StreamingAssets
- `LoadPlyFromResources(string resourcePath)`: Load from Resources
- `LoadPlyFromBytes(byte[] data)`: Load from byte array
- `SetGsplatAsset(GsplatAsset asset)`: Set pre-loaded asset
- `ClearAsset()`: Clear current asset
- `IsLoading`: Check if currently loading
- `CurrentAsset`: Get current loaded asset
