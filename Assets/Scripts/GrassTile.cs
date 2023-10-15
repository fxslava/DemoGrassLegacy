using UnityEngine;
using UnityEngine.Profiling;

public class GrassTile
{
    private const int NUM_LODS = 4;

    private Camera _mainCamera;
    private HiZBuffer _hiZBuffer;
    private Mesh _grassMesh;
    private Vector3 _tilePosition;
    private Vector2Int _count;
    private Vector2 _spacing;
    private Color _baseColor;
    private Color _tintColor;
    private float[] _lodDistance;
    private Material[] _material;
    private VisibilityManager _visibilityManager;

    private Bounds _bounds;
    private int _numInstances = 0;
    private ComputeShader _generateGrassInstancesCS;
    private ComputeBuffer _grassGeneratedInstancesBuffer;
    private ComputeBuffer[] _grassSortedLODsInstancesBuffer;
    private ComputeBuffer _grassInstancesBBoxesBuffer;
    private ComputeBuffer[] _argsBuffer;
    private ComputeBuffer _bayerMatrixBuffer;
    private int _generateGrassInstancesKernelId = -1;
    private VisibilityManager.VisibilityContext _visibilityContext = null;

    public GrassTile(VisibilityManager visManager, Camera mainCamera, HiZBuffer hiZBuffer, Mesh grassMesh, Vector3 tilePosition, Vector2Int count, Vector2 spacing, Color baseColor, Color tintColor, float[] lodDistance, Material[] material)
    {
        _tilePosition = tilePosition;
        _mainCamera = mainCamera;
        _hiZBuffer = hiZBuffer;
        _grassMesh = grassMesh;
        _count = count;
        _spacing = spacing;
        _baseColor = baseColor;
        _tintColor = tintColor;
        _lodDistance = lodDistance;
        _material = material;
        _visibilityManager = visManager;
    }

    int[] _bayerMatrix = {
            0, 48,12,60,3, 51,15,63,
            32,16,44,28,35,19,47,31,
            8, 56,4, 52,11,59,7, 55,
            40,24,36,20,43,27,39,23,
            2, 50,14,62,1, 49,13,61,
            34,18,46,30,33,17,45,29,
            10,58,6, 54,9, 57,5, 53,
            42,26,38,22,41,25,37,21 };

    private void InitializeBuffers()
    {
        _numInstances = _count.x * _count.y;

        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = _grassMesh.GetIndexCount(0);
        args[1] = (uint)_numInstances;
        args[2] = _grassMesh.GetIndexStart(0);
        args[3] = _grassMesh.GetBaseVertex(0);

        _argsBuffer = new ComputeBuffer[NUM_LODS];
        _grassSortedLODsInstancesBuffer = new ComputeBuffer[NUM_LODS];
        for (int i = 0; i < NUM_LODS; i++)
        {
            _argsBuffer[i] = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            _argsBuffer[i].SetData(args);

            _grassSortedLODsInstancesBuffer[i] = new ComputeBuffer(_numInstances, VisibilityManager.InstanceProperties.Size());
        }

        _grassGeneratedInstancesBuffer = new ComputeBuffer(_numInstances, VisibilityManager.InstanceProperties.Size());
        _grassInstancesBBoxesBuffer = new ComputeBuffer(_numInstances, VisibilityManager.InstanceBBox.Size());

        float[] bayerMat = new float[_bayerMatrix.Length];
        for (int i = 0; i < _bayerMatrix.Length; i++)
        {
            bayerMat[i] = _bayerMatrix[i] * (1.0f / 64.0f);
        }
        _bayerMatrixBuffer = new ComputeBuffer(_bayerMatrix.Length, sizeof(float));
        _bayerMatrixBuffer.SetData(bayerMat);

        _bounds = new Bounds(_tilePosition, new Vector3(_count.x * _spacing.x, 0, _spacing.y * _count.y) + _grassMesh.bounds.extents);
    }

    private void InitializeShaders()
    {
        _generateGrassInstancesCS = Resources.Load<ComputeShader>("Shaders/GrassGenerator");
        _generateGrassInstancesKernelId = _generateGrassInstancesCS.FindKernel("CSMain");
    }

    private void GenerateGrassInstances()
    {
        Vector3 origin = new Vector3(-0.5f * (_spacing.x * _count.x), 0.0f, -0.5f * (_spacing.y * _count.y));

        _generateGrassInstancesCS.SetBuffer(_generateGrassInstancesKernelId, "_bayerMatrix", _bayerMatrixBuffer);
        _generateGrassInstancesCS.SetBuffer(_generateGrassInstancesKernelId, "_grassInstances", _grassGeneratedInstancesBuffer);
        _generateGrassInstancesCS.SetBuffer(_generateGrassInstancesKernelId, "_bBoxes", _grassInstancesBBoxesBuffer);
        _generateGrassInstancesCS.SetVector("_baseColor", _baseColor);
        _generateGrassInstancesCS.SetVector("_tintColor", _tintColor);
        _generateGrassInstancesCS.SetVector("_instanceBBoxCenter", _grassMesh.bounds.center);
        _generateGrassInstancesCS.SetVector("_instanceBBoxExtents", _grassMesh.bounds.extents);
        _generateGrassInstancesCS.SetVector("_origin", origin);
        _generateGrassInstancesCS.SetFloats("_spacing", _spacing.x, _spacing.y);
        _generateGrassInstancesCS.SetInts("_gridDimensions", _count.x, _count.y);

        uint _kernelGroupDimX = 0, _kernelGroupDimY = 0, _kernelGroupDimZ = 0;
        _generateGrassInstancesCS.GetKernelThreadGroupSizes(_generateGrassInstancesKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

        int _kernelGridDimX = (int)((_count.x + (_kernelGroupDimX - 1)) / _kernelGroupDimX);
        int _kernelGridDimY = (int)((_count.y + (_kernelGroupDimY - 1)) / _kernelGroupDimY);
        _generateGrassInstancesCS.Dispatch(_generateGrassInstancesKernelId, _kernelGridDimX, _kernelGridDimY, 1);
    }

    public void Init()
    {
        InitializeBuffers();
        InitializeShaders();
        GenerateGrassInstances();

        _visibilityContext = _visibilityManager.NewContext(_numInstances, _lodDistance);
    }

    public void Render(BendGrassManager bendManager)
    {
        _visibilityContext.CalculateVisibility(_mainCamera, _grassInstancesBBoxesBuffer, _hiZBuffer);

        for (int lodId = 0; lodId < NUM_LODS; lodId++)
        {
            _visibilityContext.CopyVisibleInstancesWithLOD(_grassSortedLODsInstancesBuffer[lodId], _grassGeneratedInstancesBuffer, _argsBuffer[lodId], lodId);

            _material[lodId].SetBuffer("_Properties", _grassSortedLODsInstancesBuffer[lodId]);

            if (bendManager != null)
            {
                _material[lodId].SetTexture("_BendGrassTex", bendManager.GetBendMap());
                _material[lodId].SetVector("_bendMapOrigin", bendManager.GetBendMapOrigin());
                _material[lodId].SetVector("_bendMapInvExtents", bendManager.GetBendMapInvExtents());
            }

            Graphics.DrawMeshInstancedIndirect(_grassMesh, 0, _material[lodId], _bounds, _argsBuffer[lodId]);
        }
    }

    public void Dispose()
    {
        if (_visibilityContext != null)
        {
            _visibilityContext.Dispose();
        }

        if (_argsBuffer != null)
        {
            for (int i = 0; i < NUM_LODS; i++)
            {
                _argsBuffer[i].Release();
                _argsBuffer[i] = null;
            }
        }

        if (_grassGeneratedInstancesBuffer != null)
        {
            _grassGeneratedInstancesBuffer.Release();
            _grassGeneratedInstancesBuffer = null;
        }

        if (_grassSortedLODsInstancesBuffer != null)
        {
            for (int i = 0; i < NUM_LODS; i++)
            {
                _grassSortedLODsInstancesBuffer[i].Release();
                _grassSortedLODsInstancesBuffer[i] = null;
            }
        }

        if (_grassInstancesBBoxesBuffer != null)
        {
            _grassInstancesBBoxesBuffer.Release();
            _grassInstancesBBoxesBuffer = null;
        }

        if (_bayerMatrixBuffer != null)
        {
            _bayerMatrixBuffer.Release();
            _bayerMatrixBuffer = null;
        }
    }
}
