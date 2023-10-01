using UnityEngine;

public class GrassInstancedIndirect : MonoBehaviour
{
    private const int SCAN_GROUP_SIZE = 1024;

    [SerializeField] public Camera mainCamera;
    [SerializeField] public Material material;
    [SerializeField] public Mesh grassMesh;
    [SerializeField] public Vector2Int count = new Vector2Int(100, 100);
    [SerializeField] public Vector2 spacing = new Vector2(0.1f, 0.1f);
    [SerializeField] public Color baseColor = Color.green;
    [SerializeField] public Color tintColor = Color.black;
    [SerializeField] private ComputeShader _generateGrassInstancesCS;
    [SerializeField] private ComputeShader _grassInstancesVisibilityCS;
    [SerializeField] private ComputeShader _scanInstancesCS;
    [SerializeField] private ComputeShader _copyInstancesCS;

    private Bounds _bounds;
    private int _numInstances = 0;
    private int _numGroups = 0;
    private ComputeBuffer _grassGeneratedInstancesBuffer;
    private ComputeBuffer _grassSortedLODsInstancesBuffer;
    private ComputeBuffer _grassInstancesBBoxesBuffer;
    private ComputeBuffer _grassInstancesVisibilityBuffer;
    private ComputeBuffer _argsBuffer;
    private ComputeBuffer _scanIndicesBuffer;
    private ComputeBuffer _scanTempSumBuffer;
    private ComputeBuffer _scanOffsetsBuffer;
    private int _generateGrassInstancesKernelId = -1;
    private int _grassInstancesVisibilityKernelId = -1;
    private int _scanInstancesKernelId = -1;
    private int _copyInstancesKernelId = -1;

    private struct GrassInstanceProperties
    {
        public Matrix4x4 mat;
        public Vector4 color;

        public static int Size()
        {
            return
                sizeof(float) * 4 * 4 + // matrix;
                sizeof(float) * 4;      // color;
        }
    }

    private struct GrassInstanceBBox
    {
        public Vector3 center;
        public Vector3 extents;

        public static int Size()
        {
            return
                sizeof(float) * 3 +
                sizeof(float) * 3;
        }
    }

    private void InitializeBuffers()
    {
        _numInstances = count.x * count.y;
        _numGroups = (_numInstances + SCAN_GROUP_SIZE - 1) / SCAN_GROUP_SIZE;

        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = grassMesh.GetIndexCount(0);
        args[1] = (uint)_numInstances;
        args[2] = grassMesh.GetIndexStart(0);
        args[3] = grassMesh.GetBaseVertex(0);

        _argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(args);

        _grassGeneratedInstancesBuffer = new ComputeBuffer(_numInstances, GrassInstanceProperties.Size());
        _grassInstancesBBoxesBuffer = new ComputeBuffer(_numInstances, GrassInstanceBBox.Size());
        _grassSortedLODsInstancesBuffer = new ComputeBuffer(_numInstances, GrassInstanceProperties.Size());
        _grassInstancesVisibilityBuffer = new ComputeBuffer(_numInstances, sizeof(int));
        _scanIndicesBuffer = new ComputeBuffer(_numInstances, sizeof(int));
        _scanTempSumBuffer = new ComputeBuffer(_numGroups, sizeof(int));
        _scanOffsetsBuffer = new ComputeBuffer(_numGroups, sizeof(int));

        _bounds = new Bounds(transform.position, new Vector3(count.x * spacing.x, 0, spacing.y * count.y) + grassMesh.bounds.extents);
    }

    private void InitializeShaders()
    {
        _generateGrassInstancesKernelId = _generateGrassInstancesCS.FindKernel("CSMain");
        _grassInstancesVisibilityKernelId = _grassInstancesVisibilityCS.FindKernel("CSMain");
        _grassInstancesVisibilityCS.EnableKeyword("NAIVE_BBOX_CULL_MODE");
        _grassInstancesVisibilityCS.DisableKeyword("BBOX_CULL_MODE");
        _scanInstancesKernelId = _scanInstancesCS.FindKernel("CSMain");
        _copyInstancesKernelId = _copyInstancesCS.FindKernel("CSMain");
    }

    private void GenerateGrassInstances()
    {
        Vector3 origin = new Vector3(-0.5f * (spacing.x * count.x), 0.0f, -0.5f * (spacing.y * count.y));

        _generateGrassInstancesCS.SetBuffer(_generateGrassInstancesKernelId, "_grassInstances", _grassGeneratedInstancesBuffer);
        _generateGrassInstancesCS.SetBuffer(_generateGrassInstancesKernelId, "_bBoxes", _grassInstancesBBoxesBuffer);
        _generateGrassInstancesCS.SetVector("_baseColor", baseColor);
        _generateGrassInstancesCS.SetVector("_tintColor", tintColor);   
        _generateGrassInstancesCS.SetVector("_instanceBBoxCenter", grassMesh.bounds.center);
        _generateGrassInstancesCS.SetVector("_instanceBBoxExtents", grassMesh.bounds.extents);
        _generateGrassInstancesCS.SetVector("_origin", origin);
        _generateGrassInstancesCS.SetFloats("_spacing", spacing.x, spacing.y);
        _generateGrassInstancesCS.SetInts("_gridDimensions", count.x, count.y);

        uint _kernelGroupDimX = 0, _kernelGroupDimY = 0, _kernelGroupDimZ = 0;
        _generateGrassInstancesCS.GetKernelThreadGroupSizes(_generateGrassInstancesKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

        int _kernelGridDimX = (int)((count.x + (_kernelGroupDimX - 1)) / _kernelGroupDimX);
        int _kernelGridDimY = (int)((count.y + (_kernelGroupDimY - 1)) / _kernelGroupDimY);
        _generateGrassInstancesCS.Dispatch(_generateGrassInstancesKernelId, _kernelGridDimX, _kernelGridDimY, 1);
    }

    private void CalculateGrassVisibility()
    {
        // Global data
        Vector3 camPosition = mainCamera.transform.position;

        //Matrix4x4 m = mainCamera.transform.localToWorldMatrix;
        Matrix4x4 v = mainCamera.worldToCameraMatrix;
        Matrix4x4 p = mainCamera.projectionMatrix;
        Matrix4x4 MVP = p * v; // * m;

        _grassInstancesVisibilityCS.SetBuffer(_grassInstancesVisibilityKernelId, "bBoxes", _grassInstancesBBoxesBuffer);
        _grassInstancesVisibilityCS.SetBuffer(_grassInstancesVisibilityKernelId, "visibilityBuffer", _grassInstancesVisibilityBuffer);
        _grassInstancesVisibilityCS.SetMatrix("_UNITY_MATRIX_MVP", MVP);
        _grassInstancesVisibilityCS.SetInt("_numInstances", _numInstances);

        uint _kernelGroupDimX = 0, _kernelGroupDimY = 0, _kernelGroupDimZ = 0;
        _grassInstancesVisibilityCS.GetKernelThreadGroupSizes(_grassInstancesVisibilityKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

        int _kernelGridDimX = (int)((_numInstances + (_kernelGroupDimX - 1)) / _kernelGroupDimX);
        _grassInstancesVisibilityCS.Dispatch(_grassInstancesVisibilityKernelId, _kernelGridDimX, 1, 1);
    }

    private void CopyVisibleDistancesWithLOD(int lod = 1)
    {
        // scan
        _scanInstancesCS.EnableKeyword("LOD_PREFIXES_PASS");
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_lodBuffer", _grassInstancesVisibilityBuffer);
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_indicesBuffer", _scanIndicesBuffer);
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_groupSumBufferOut", _scanTempSumBuffer);
        _scanInstancesCS.SetInt("_lodId", lod);

        uint _kernelGroupDimX = 0, _kernelGroupDimY = 0, _kernelGroupDimZ = 0;
        _scanInstancesCS.GetKernelThreadGroupSizes(_scanInstancesKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

        int _kernelGridDimX = (_numInstances + (SCAN_GROUP_SIZE - 1)) / SCAN_GROUP_SIZE;
        _scanInstancesCS.Dispatch(_scanInstancesKernelId, _kernelGridDimX, 1, 1);

        // final scan
        _scanInstancesCS.DisableKeyword("LOD_PREFIXES_PASS");
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_groupSumBufferIn", _scanTempSumBuffer);
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_indicesBuffer", _scanOffsetsBuffer);
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_argsBuffer", _argsBuffer);

        _kernelGridDimX = (int)((_numGroups + (SCAN_GROUP_SIZE - 1)) / SCAN_GROUP_SIZE);
        _scanInstancesCS.Dispatch(_scanInstancesKernelId, _kernelGridDimX, 1, 1);

        //copy
        _copyInstancesCS.SetBuffer(_scanInstancesKernelId, "_visibilityBuffer", _grassInstancesVisibilityBuffer);
        _copyInstancesCS.SetBuffer(_scanInstancesKernelId, "_scanIndicesBuffer", _scanIndicesBuffer);
        _copyInstancesCS.SetBuffer(_scanInstancesKernelId, "_scanOffsetsBuffer", _scanOffsetsBuffer);
        _copyInstancesCS.SetBuffer(_scanInstancesKernelId, "_generatedGrassInstances", _grassGeneratedInstancesBuffer);
        _copyInstancesCS.SetBuffer(_scanInstancesKernelId, "_reducedGrassInstances", _grassSortedLODsInstancesBuffer);

        _copyInstancesCS.GetKernelThreadGroupSizes(_copyInstancesKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

        _kernelGridDimX = (int)((_numInstances + (_kernelGroupDimX - 1)) / _kernelGroupDimX);
        _copyInstancesCS.Dispatch(_copyInstancesKernelId, _kernelGridDimX, 1, 1);
    }


    private void Start()
    {
        InitializeBuffers();
        InitializeShaders();
        GenerateGrassInstances();
    }

    private bool firstUpdate = true;

    private void OnPreCull()
    {
    }

    private void OnRenderObject()
    {
        //CalculateGrassVisibility();
        //Graphics.DrawMeshInstancedIndirect(grassMesh, 0, material, _bounds, _argsBuffer);
    }

    private void Update()
    {
        CalculateGrassVisibility();

        if (false)
        {
            material.EnableKeyword("DEBUG");
            material.SetBuffer("_Properties", _grassGeneratedInstancesBuffer);
            material.SetBuffer("visibilityBuffer", _grassInstancesVisibilityBuffer);
        } else
        {
            CopyVisibleDistancesWithLOD();
            material.DisableKeyword("DEBUG");
            material.SetBuffer("_Properties", _grassSortedLODsInstancesBuffer);
        }

        material.SetVector("_bendMapOrigin", transform.position);
        material.SetVector("_bendMapInvExtents", new Vector3(1.0f / _bounds.extents.x, 1.0f / _bounds.extents.y, 1.0f / _bounds.extents.z));

        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, material, _bounds, _argsBuffer);
    }

    private void OnDestroy()
    {
        if (_argsBuffer != null) {
            _argsBuffer.Release();
            _argsBuffer = null;
        }

        if (_grassGeneratedInstancesBuffer != null) {
            _grassGeneratedInstancesBuffer.Release();
            _grassGeneratedInstancesBuffer = null;
        }

        if (_grassSortedLODsInstancesBuffer != null) {
            _grassSortedLODsInstancesBuffer.Release();
            _grassSortedLODsInstancesBuffer = null;
        }

        if (_grassInstancesBBoxesBuffer != null) {
            _grassInstancesBBoxesBuffer.Release();
            _grassInstancesBBoxesBuffer = null;
        }

        if (_grassInstancesVisibilityBuffer != null) {
            _grassInstancesVisibilityBuffer.Release();
            _grassInstancesVisibilityBuffer = null;
        }

        if (_scanIndicesBuffer != null) {
            _scanIndicesBuffer.Release();
            _scanIndicesBuffer = null;
        }

        if (_scanTempSumBuffer != null) {
            _scanTempSumBuffer.Release();
            _scanTempSumBuffer = null;
        }

        if (_scanOffsetsBuffer != null) {
            _scanOffsetsBuffer.Release();
            _scanOffsetsBuffer = null;
        }
    }
}
