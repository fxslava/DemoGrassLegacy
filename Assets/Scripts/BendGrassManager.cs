using UnityEngine;

public class BendGrassManager : MonoBehaviour
{
    [SerializeField] public Vector2Int bentGrassMapResolution = new Vector2Int(256, 256);

    [SerializeField] private BendGrassVolume _grassVolume;
    [SerializeField] private ComputeShader _bendGrassComputeShader;

    private RenderTexture _bendGrassTex2D = null;
    private int _bentGrassKernelIndex;
    private int _resetGrassKernelIndex;
    private int _threadGroupX;
    private int _threadGroupY;
    private int _threadGroupZ;
    private bool _firstUpdate;
    private Bounds _bounds;

    private void InitializeShaders()
    {
        var desc = new RenderTextureDescriptor(bentGrassMapResolution.x, bentGrassMapResolution.y, RenderTextureFormat.RG16)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
            enableRandomWrite = true
        };

        _bendGrassTex2D = new RenderTexture(desc);
        _bendGrassTex2D.Create();

        _bentGrassKernelIndex = _bendGrassComputeShader.FindKernel("BendGrass");
        _resetGrassKernelIndex = _bendGrassComputeShader.FindKernel("ResetGrass");

        uint threadX, threadY, threadZ;
        _bendGrassComputeShader.GetKernelThreadGroupSizes(_bentGrassKernelIndex, out threadX, out threadY, out threadZ);
        _threadGroupX = (int)(bentGrassMapResolution.x / threadX);
        _threadGroupY = (int)(bentGrassMapResolution.y / threadY);
        _threadGroupZ = 1;
    }

    // Start is called before the first frame update
    void Start()
    {
        _firstUpdate = true;
        InitializeShaders();
    }

    // Update is called once per frame
    void Update()
    {
        _bounds = _grassVolume.GetBounds();
        _bendGrassComputeShader.SetTexture(_resetGrassKernelIndex, "_BendMap", _bendGrassTex2D);

        if (_firstUpdate)
        {
            _bendGrassComputeShader.Dispatch(_resetGrassKernelIndex, _threadGroupX, _threadGroupY, _threadGroupZ);
            _firstUpdate = false;
        }
    }

    public void BendSphere(Vector3 position, float radius)
    {
        _bendGrassComputeShader.SetFloat("_DisturberRadius", radius);
        _bendGrassComputeShader.SetVector("_LocalDisturberPosition", _bounds.center - position);
        _bendGrassComputeShader.SetVector("_Extents", _bounds.extents);
        _bendGrassComputeShader.SetVector("_InvDimensions", new Vector2(1.0f / (float)bentGrassMapResolution.x, 1.0f / (float)bentGrassMapResolution.y));
        _bendGrassComputeShader.SetTexture(_bentGrassKernelIndex, "_BendMap", _bendGrassTex2D);

        _bendGrassComputeShader.Dispatch(_bentGrassKernelIndex, _threadGroupX, _threadGroupY, _threadGroupZ);
    }

    public RenderTexture GetBendMap()
    {
        return _bendGrassTex2D;
    }

    public Vector3 GetBendMapOrigin()
    {
        return _bounds.center;
    }

    public Vector3 GetBendMapInvExtents()
    {
        return new Vector3(1.0f / _bounds.extents.x, 1.0f / _bounds.extents.y, 1.0f / _bounds.extents.z);
    }
}
