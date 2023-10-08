using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class HiZBuffer : MonoBehaviour
{
    [SerializeField] private Camera _camera;
    [SerializeField] private ComputeShader _HiZBufferShader;
    [SerializeField] private Material _HiZBufferFillMaterial;
    [SerializeField] private Material _HiZDebugMaterial;

    // Workaround for https://forum.unity.com/threads/multiple-instances-of-same-compute-shader-is-it-possible.506961/
    private ComputeShader[] _HiZBufferShaderArray;

    private CommandBuffer _commandBuffer;
    private RenderTexture _HiZBFullTexture;
    private RenderTexture _HiZBTexture;
    private CameraEvent _cameraEvent = CameraEvent.AfterForwardOpaque;
    private int _mipCount = 0;
    private int _hiZBkernelId = 0;
    private int _hiZBMipKernelId = 0;
    private const int HiZB_TILE = 16;
    private const int NUM_INSTANCES_4KEXP_MINUS_16EXP = 5;

    private void Start()
    {
        _camera = GetComponent<Camera>();
        _camera.depthTextureMode |= DepthTextureMode.Depth;

        _HiZBufferShaderArray = new ComputeShader[NUM_INSTANCES_4KEXP_MINUS_16EXP];
        for (int i = 0; i < _HiZBufferShaderArray.Length; i++) {
            _HiZBufferShaderArray[i] = Instantiate(_HiZBufferShader);
        }

        _hiZBkernelId = _HiZBufferShader.FindKernel("BaseDownsample");
        _hiZBMipKernelId = _HiZBufferShader.FindKernel("PyramidDownsample");
    }

    private Vector2Int CalculateHiZBBaseResolution(Vector2Int screenResolution)
    {
        int numTilesX = (screenResolution.x + (HiZB_TILE - 1)) / HiZB_TILE;
        int numTilesY = (screenResolution.y + (HiZB_TILE - 1)) / HiZB_TILE;
        return new Vector2Int(numTilesX, numTilesY);
    }

    private int CalculateMipMapNum(Vector2Int resolution)
    {
        int mipCount = Mathf.FloorToInt(Mathf.Log(Mathf.Max(resolution.x, resolution.y), 2.0f));
        return 1 + ((mipCount - 1) & (~1));
    }

    private void InitDepthTexture(Vector2Int size)
    {
        if (_HiZBFullTexture != null)
        {
            _HiZBFullTexture.Release();
            _HiZBFullTexture = null;
        }

        _HiZBFullTexture = new RenderTexture(new RenderTextureDescriptor(size.x, size.y, RenderTextureFormat.RFloat)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
            enableRandomWrite = true
        });
        _HiZBFullTexture.name = "_HiZBBaseTexture";

        Vector2Int hiZBSize = CalculateHiZBBaseResolution(size);
        _mipCount = CalculateMipMapNum(hiZBSize);
        var desc = new RenderTextureDescriptor(hiZBSize.x, hiZBSize.y, RenderTextureFormat.RFloat)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
            enableRandomWrite = true,
            useMipMap = true,
            mipCount = _mipCount,
            autoGenerateMips = false
        };
        _HiZBTexture = new RenderTexture(desc);
        _HiZBTexture.name = "_HiZBTexture";
    }

    private void HiZBDownsapleStep(RenderTexture HZBtex, Vector2Int resolution, int curStep)
    {
        uint _threadGroupX = 0, _threadGroupY = 0, threadGroupZ;
        _HiZBufferShaderArray[curStep].SetTexture(_hiZBMipKernelId, "_HiZBase", HZBtex, curStep * 2);
        _HiZBufferShaderArray[curStep].SetTexture(_hiZBMipKernelId, "_HiZmip1", HZBtex, curStep * 2 + 1);
        _HiZBufferShaderArray[curStep].SetTexture(_hiZBMipKernelId, "_HiZmip2", HZBtex, curStep * 2 + 2);
        _HiZBufferShaderArray[curStep].SetInts("_HiZBaseResolution", resolution.x, resolution.y);
        _HiZBufferShaderArray[curStep].GetKernelThreadGroupSizes(_hiZBMipKernelId, out _threadGroupX, out _threadGroupY, out threadGroupZ);
        int gridDimX = (resolution.x + (int)_threadGroupX - 1) / (int)_threadGroupX;
        int gridDimY = (resolution.y + (int)_threadGroupY - 1) / (int)_threadGroupY;
        _commandBuffer.DispatchCompute(_HiZBufferShaderArray[curStep], _hiZBMipKernelId, gridDimX, gridDimY, 1);
    }

    private void OnPreRender()
    {
        Vector2Int resolution = new Vector2Int(_camera.pixelWidth, _camera.pixelHeight);
        if ((_commandBuffer == null) || (_HiZBFullTexture == null) || (resolution.x != _HiZBFullTexture.width) || (resolution.y != _HiZBFullTexture.height))
        {
            InitDepthTexture(resolution);

            if (_commandBuffer != null) {
                _camera.RemoveCommandBuffer(_cameraEvent, _commandBuffer);
                _commandBuffer = null;
            }

            _commandBuffer = new CommandBuffer();
            _commandBuffer.name = "Hi-Z Buffer";

            RenderTargetIdentifier id = new RenderTargetIdentifier(_HiZBFullTexture);
            _commandBuffer.Blit(null, id, _HiZBufferFillMaterial);

            _HiZBufferShader.SetTexture(_hiZBkernelId, "_fullZ", _HiZBFullTexture);
            _HiZBufferShader.SetTexture(_hiZBkernelId, "_HiZBase", _HiZBTexture);
            _HiZBufferShader.SetInts("_DepthTexResolution", resolution.x, resolution.y);
            _HiZBufferShader.SetInts("_HiZBaseResolution", _HiZBTexture.width, _HiZBTexture.height);

            uint _threadGroupX = 0, _threadGroupY = 0, threadGroupZ;
            _HiZBufferShader.GetKernelThreadGroupSizes(_hiZBkernelId, out _threadGroupX, out _threadGroupY, out threadGroupZ);
            int gridDimX = (resolution.x + (int)_threadGroupX - 1) / (int)_threadGroupX;
            int gridDimY = (resolution.y + (int)_threadGroupY - 1) / (int)_threadGroupY;
            _commandBuffer.DispatchCompute(_HiZBufferShader, _hiZBkernelId, gridDimX, gridDimY, 1);

            resolution.x = _HiZBTexture.width;
            resolution.y = _HiZBTexture.height;

            for (int step = 0, processedMips = 0; processedMips < _mipCount - 1; step++, processedMips += 2) {
                HiZBDownsapleStep(_HiZBTexture, resolution, step);
                resolution.x /= 4;
                resolution.y /= 4;
            }

            _camera.AddCommandBuffer(_cameraEvent, _commandBuffer);
        }
    }

    private void DebugShowTecxture(RenderTexture tex, RenderTexture destination, int lod = 0)
    {
        _HiZDebugMaterial.SetTexture("_tex", tex);
        _HiZDebugMaterial.SetInt("_texResolutionX", tex.width >> lod);
        _HiZDebugMaterial.SetInt("_texResolutionY", tex.height >> lod);
        _HiZDebugMaterial.SetInt("_lodId", lod);

        Graphics.Blit(null, destination, _HiZDebugMaterial);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, destination);

        _camera.rect = new Rect(0.1f, 0.1f, 1.0f / 8.0f, 1.0f / 8.0f);
        DebugShowTecxture(_HiZBFullTexture, destination);

        _camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
    }

    private void OnDestroy()
    {
        
    }

    public bool IsHiZBufferAvailable()
    {
        return _HiZBTexture != null;
    }

    public int GetMipCount()
    {
        return _mipCount;
    }

    public Vector2Int GetHiZBufferDimensions()
    {
        return new Vector2Int(_HiZBTexture.width, _HiZBTexture.height);
    }

    public RenderTexture GetHiZBuffer()
    {
        return _HiZBTexture;
    }
}
