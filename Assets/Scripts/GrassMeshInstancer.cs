using UnityEngine;
using UnityEngine.Rendering;

public class GrassMeshInstancer : MonoBehaviour
{
    [SerializeField] public Vector2Int _grassDensityMapResolution = new Vector2Int(128, 128);
    [SerializeField] public Material material;
    [SerializeField] public Color baseColor = Color.green;
    [SerializeField] public Color tintColor = Color.black;
    [SerializeField] public float GrassDensityScale = 1.0f;
    [SerializeField] public Texture2D densityMap;
    [SerializeField] public Mesh grassMesh;
    [SerializeField] private ComputeShader _grassInstancerComputeShader;


    private Bounds _bounds;
    private int _numberOfInstances = 0;
    private RenderTexture positionToUVsRT = null;
    private RenderTexture normalToUVsRT = null;
    private ComputeBuffer _argsBuffer = null;
    private ComputeBuffer _bayerMatrixBuffer = null;
    private ComputeBuffer _grassInstancesData = null;

#if UNITY_EDITOR
    Vector3[] instancePositions;
    Vector3[] instanceNormals;
#endif

    int[] _bayerMatrix = {
            0, 48,12,60,3, 51,15,63,
            32,16,44,28,35,19,47,31,
            8, 56,4, 52,11,59,7, 55,
            40,24,36,20,43,27,39,23,
            2, 50,14,62,1, 49,13,61,
            34,18,46,30,33,17,45,29,
            10,58,6, 54,9, 57,5, 53,
            42,26,38,22,41,25,37,21 };

    private struct GrassInstanceData
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

    private void CreateArgsBuffer()
    {
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = grassMesh.GetIndexCount(0);
        args[1] = 0;
        args[2] = grassMesh.GetIndexStart(0);
        args[3] = grassMesh.GetBaseVertex(0);

        _argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(args);
    }

    private void CreateBayerMatrixBuffer()
    {
        _bayerMatrixBuffer = new ComputeBuffer(_bayerMatrix.Length, sizeof(int));
        _bayerMatrixBuffer.SetData(_bayerMatrix);
    }

    private void Start()
    {
        GenerateGrass(false);

        _bounds = GetComponent<Renderer>().bounds;
        material.SetBuffer("_Properties", _grassInstancesData);
    }

    private void Update()
    {
        if (_numberOfInstances > 0)
        {
            Graphics.DrawMeshInstancedIndirect(grassMesh, 0, material, _bounds, _argsBuffer);
        }
    }


    private void CalculateGrassInstancesRTs()
    {
        Material posToUVsMat = new Material(Shader.Find("Unlit/posToUVs"));
        Material normToUVsMat = new Material(Shader.Find("Unlit/normToUVs"));

        MeshFilter meshFilter = GetComponent<MeshFilter>();

        CommandBuffer offlineCommandBuffer = new CommandBuffer();
        offlineCommandBuffer.name = "PrerenderBuffer";

        offlineCommandBuffer.SetRenderTarget(positionToUVsRT);
        offlineCommandBuffer.ClearRenderTarget(true, true, Color.clear);
        offlineCommandBuffer.DrawMesh(meshFilter.sharedMesh, transform.localToWorldMatrix, posToUVsMat, 0);

        offlineCommandBuffer.SetRenderTarget(normalToUVsRT);
        offlineCommandBuffer.ClearRenderTarget(true, true, Color.clear);
        offlineCommandBuffer.DrawMesh(meshFilter.sharedMesh, transform.localToWorldMatrix, normToUVsMat, 0);

        Graphics.ExecuteCommandBuffer(offlineCommandBuffer);

        offlineCommandBuffer.Release();
    }


    private int CalculateInstancesPlacements(ComputeBuffer indirectBuffer)
    {
        int grassInstanceCounterKernelIndex = _grassInstancerComputeShader.FindKernel("GrassInstanceCounter");
        int grassInstancerKernelIndex = _grassInstancerComputeShader.FindKernel("GrassInstancer");

        uint threadX, threadY, threadZ;
        _grassInstancerComputeShader.GetKernelThreadGroupSizes(grassInstanceCounterKernelIndex, out threadX, out threadY, out threadZ);
        int threadGroupX = (int)(_grassDensityMapResolution.x / threadX);
        int threadGroupY = (int)(_grassDensityMapResolution.y / threadY);
        int threadGroupZ = 1;

        _grassInstancerComputeShader.SetTexture(grassInstanceCounterKernelIndex, "_PositionMap", positionToUVsRT);
        _grassInstancerComputeShader.SetTexture(grassInstanceCounterKernelIndex, "_DensityMap", densityMap);
        _grassInstancerComputeShader.SetBuffer(grassInstanceCounterKernelIndex, "_bayerMatrix", _bayerMatrixBuffer);
        _grassInstancerComputeShader.SetBuffer(grassInstanceCounterKernelIndex, "_argsBuffer", _argsBuffer);
        _grassInstancerComputeShader.SetBuffer(grassInstanceCounterKernelIndex, "_indirectBuffer", indirectBuffer);
        _grassInstancerComputeShader.Dispatch(grassInstanceCounterKernelIndex, threadGroupX, threadGroupY, threadGroupZ);

        _grassInstancerComputeShader.SetVector("_baseColor", baseColor);
        _grassInstancerComputeShader.SetVector("_tintColor", tintColor);
        _grassInstancerComputeShader.SetFloat("_DensityScale", GrassDensityScale);

        uint[] outputArray = new uint[5];
        _argsBuffer.GetData(outputArray);

        _numberOfInstances = (int)outputArray[1];

        if (_numberOfInstances > 0)
        {
            _grassInstancesData = new ComputeBuffer((int)_numberOfInstances, GrassInstanceData.Size());

            _grassInstancerComputeShader.GetKernelThreadGroupSizes(grassInstancerKernelIndex, out threadX, out threadY, out threadZ);
            threadGroupX = (int)(_numberOfInstances / threadX);
            threadGroupY = 1;
            threadGroupZ = 1;

            _grassInstancerComputeShader.SetTexture(grassInstancerKernelIndex, "_PositionMap", positionToUVsRT);
            _grassInstancerComputeShader.SetTexture(grassInstancerKernelIndex, "_NormalMap", normalToUVsRT);
            _grassInstancerComputeShader.SetBuffer(grassInstancerKernelIndex, "_indirectBuffer", indirectBuffer);
            _grassInstancerComputeShader.SetBuffer(grassInstancerKernelIndex, "_grassInstances", _grassInstancesData);
            _grassInstancerComputeShader.Dispatch(grassInstancerKernelIndex, threadGroupX, threadGroupY, threadGroupZ);
        }

        return 0;
    }

    private void ReadDebugData(ComputeBuffer indirectBuffer)
    {
        var positionsBuffer = new ComputeBuffer(_numberOfInstances, sizeof(float) * 3);
        var normalsBuffer = new ComputeBuffer(_numberOfInstances, sizeof(float) * 3);

        int debugInstancesKernelIndex = _grassInstancerComputeShader.FindKernel("DebugGrassInstancer");

        uint threadX, threadY, threadZ;
        _grassInstancerComputeShader.GetKernelThreadGroupSizes(debugInstancesKernelIndex, out threadX, out threadY, out threadZ);
        int threadGroupX = (int)(_numberOfInstances / threadX);
        int threadGroupY = 1;
        int threadGroupZ = 1;

        _grassInstancerComputeShader.SetTexture(debugInstancesKernelIndex, "_PositionMap", positionToUVsRT);
        _grassInstancerComputeShader.SetTexture(debugInstancesKernelIndex, "_NormalMap", normalToUVsRT);
        _grassInstancerComputeShader.SetBuffer(debugInstancesKernelIndex, "_indirectBuffer", indirectBuffer);
        _grassInstancerComputeShader.SetBuffer(debugInstancesKernelIndex, "_positionsBuffer", positionsBuffer);
        _grassInstancerComputeShader.SetBuffer(debugInstancesKernelIndex, "_normalsBuffer", normalsBuffer);
        _grassInstancerComputeShader.Dispatch(debugInstancesKernelIndex, threadGroupX, threadGroupY, threadGroupZ);

        instancePositions = null;
        instanceNormals = null;

        instancePositions = new Vector3[_numberOfInstances];
        instanceNormals = new Vector3[_numberOfInstances];

        positionsBuffer.GetData(instancePositions);
        normalsBuffer.GetData(instanceNormals);

        positionsBuffer.Release();
        normalsBuffer.Release();
    }

    public void GenerateGrass(bool genDebugData)
    {
        positionToUVsRT = new RenderTexture(_grassDensityMapResolution.x, _grassDensityMapResolution.y, 24, RenderTextureFormat.ARGBFloat);
        normalToUVsRT = new RenderTexture(_grassDensityMapResolution.x, _grassDensityMapResolution.y, 24, RenderTextureFormat.ARGBFloat);

        CreateArgsBuffer();
        CreateBayerMatrixBuffer();

        CalculateGrassInstancesRTs();

        var indirectBuffer = new ComputeBuffer(_grassDensityMapResolution.x * _grassDensityMapResolution.y, sizeof(int) * 2);

        CalculateInstancesPlacements(indirectBuffer);
        if (genDebugData)
        {
            ReadDebugData(indirectBuffer);
        }

        indirectBuffer.Release();

        positionToUVsRT.Release();
        positionToUVsRT = null;

        normalToUVsRT.Release();
        normalToUVsRT = null;
    }

    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (instancePositions != null && instancePositions != null && instancePositions.Length > 0 && instanceNormals.Length > 0)
        {
            for (int i = 0; i < instancePositions.Length; i++) 
            {
                Gizmos.DrawLine(instancePositions[i], instancePositions[i] + instanceNormals[i] * 0.1f);
            }
        }
#endif
    }

    private void OnDestroy()
    {
        if (_argsBuffer != null)
        {
            _argsBuffer.Release();
            _argsBuffer = null;
        }
        if (_bayerMatrixBuffer != null)
        {
            _bayerMatrixBuffer.Release();
            _bayerMatrixBuffer = null;
        }
        if (_grassInstancesData != null)
        {
            _grassInstancesData.Release();
            _grassInstancesData = null;
        }
    }
}
