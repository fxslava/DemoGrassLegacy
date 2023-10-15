using UnityEngine;
using UnityEngine.Profiling;

public class VisibilityManager : MonoBehaviour
{
    private const int SCAN_GROUP_SIZE = 1024;

    private ComputeShader _instancesVisibilityCS;
    private ComputeShader _scanInstancesCS;
    private ComputeShader _copyInstancesCS;
    private int _instancesVisibilityKernelId = -1;
    private int _scanInstancesKernelId = -1;
    private int _copyInstancesKernelId = -1;

    public struct InstanceProperties
    {
        public Matrix4x4 mat;
        public Vector4 color;

        public static int Size()
        {
            return
                sizeof(float) * 4 * 4 + 
                sizeof(float) * 4;      
        }
    }

    public struct InstanceBBox
    {
        public Vector3 center;
        public float padding;
        public Vector3 extents;
        public float bayerThreashold;

        public static int Size()
        {
            return
                sizeof(float) * 4 +
                sizeof(float) * 4;
        }
    }

    public class VisibilityContext {

        private ComputeBuffer _instancesVisibilityBuffer;
        private ComputeBuffer _scanIndicesBuffer;
        private ComputeBuffer _scanTempSumBuffer;
        private ComputeBuffer _scanOffsetsBuffer;
        private VisibilityManager _owner;
        private int _numGroups;
        private int _numInstances;
        private float[] _lodDistance;

        public VisibilityContext(VisibilityManager owner, int numInstances, float[] lodDistance)
        {
            _owner = owner;
            _numInstances = numInstances;
            _numGroups = (_numInstances + SCAN_GROUP_SIZE - 1) / SCAN_GROUP_SIZE;
            _lodDistance = lodDistance;

            _instancesVisibilityBuffer = new ComputeBuffer(_numInstances, sizeof(int));
            _scanIndicesBuffer = new ComputeBuffer(_numInstances, sizeof(int));
            _scanTempSumBuffer = new ComputeBuffer(_numGroups, sizeof(int));
            _scanOffsetsBuffer = new ComputeBuffer(_numGroups, sizeof(int));
        }

        public void CalculateVisibility(Camera camera, ComputeBuffer instancesBBoxesBuffer, HiZBuffer hiZBuffer = null)
        {
            Profiler.BeginSample("CalculateGrassVisibility()");

            // Global data
            Vector3 camPosition = camera.transform.position;

            //Matrix4x4 m = mainCamera.transform.localToWorldMatrix;
            Matrix4x4 v = camera.worldToCameraMatrix;
            Matrix4x4 p = camera.projectionMatrix;
            Matrix4x4 MVP = p * v; // * m;

            _owner._instancesVisibilityCS.SetVector("_cameraPos", camPosition);
            _owner._instancesVisibilityCS.SetFloat("_DistanceLOD0", _lodDistance[0]);
            _owner._instancesVisibilityCS.SetFloat("_DistanceLOD1", _lodDistance[1]);
            _owner._instancesVisibilityCS.SetFloat("_DistanceLOD2", _lodDistance[2]);
            _owner._instancesVisibilityCS.SetFloat("_DistanceLOD3", _lodDistance[3]);
            _owner._instancesVisibilityCS.SetBuffer(_owner._instancesVisibilityKernelId, "bBoxes", instancesBBoxesBuffer);
            _owner._instancesVisibilityCS.SetBuffer(_owner._instancesVisibilityKernelId, "visibilityBuffer", _instancesVisibilityBuffer);
            _owner._instancesVisibilityCS.SetMatrix("_UNITY_MATRIX_MVP", MVP);
            _owner._instancesVisibilityCS.SetInt("_numInstances", _numInstances);

            if (hiZBuffer != null && hiZBuffer.IsHiZBufferAvailable())
            {
                _owner._instancesVisibilityCS.EnableKeyword("USE_HiZB");
                Vector2Int hiZBufferDim = hiZBuffer.GetHiZBufferDimensions();
                _owner._instancesVisibilityCS.SetTexture(_owner._instancesVisibilityKernelId, "_HiZMap", hiZBuffer.GetHiZBuffer());
                _owner._instancesVisibilityCS.SetInts("_HiZTextureSize", hiZBufferDim.x, hiZBufferDim.y);
                _owner._instancesVisibilityCS.SetInt("_HiZMaxMip", hiZBuffer.GetMipCount() - 1);
            }
            else
            {
                _owner._instancesVisibilityCS.DisableKeyword("USE_HiZB");
            }

            uint _kernelGroupDimX = 0, _kernelGroupDimY = 0, _kernelGroupDimZ = 0;
            _owner._instancesVisibilityCS.GetKernelThreadGroupSizes(_owner._instancesVisibilityKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

            int _kernelGridDimX = (int)((_numInstances + (_kernelGroupDimX - 1)) / _kernelGroupDimX);
            _owner._instancesVisibilityCS.Dispatch(_owner._instancesVisibilityKernelId, _kernelGridDimX, 1, 1);

            Profiler.EndSample();
        }

        public void CopyVisibleInstancesWithLOD(ComputeBuffer dstInstances, ComputeBuffer srcInstances, ComputeBuffer argsBuffer, int lod = 0)
        {
            Profiler.BeginSample("CopyVisibleDistancesWithLOD()");

            // scan
            _owner._scanInstancesCS.EnableKeyword("LOD_PREFIXES_PASS");
            _owner._scanInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_lodBuffer", _instancesVisibilityBuffer);
            _owner._scanInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_indicesBuffer", _scanIndicesBuffer);
            _owner._scanInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_groupSumBufferOut", _scanTempSumBuffer);
            _owner._scanInstancesCS.SetInt("_lodId", lod);

            uint _kernelGroupDimX = 0, _kernelGroupDimY = 0, _kernelGroupDimZ = 0;
            _owner._scanInstancesCS.GetKernelThreadGroupSizes(_owner._scanInstancesKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

            int _kernelGridDimX = (_numInstances + (SCAN_GROUP_SIZE - 1)) / SCAN_GROUP_SIZE;
            _owner._scanInstancesCS.Dispatch(_owner._scanInstancesKernelId, _kernelGridDimX, 1, 1);

            // final scan
            _owner._scanInstancesCS.DisableKeyword("LOD_PREFIXES_PASS");
            _owner._scanInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_groupSumBufferIn", _scanTempSumBuffer);
            _owner._scanInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_indicesBuffer", _scanOffsetsBuffer);
            _owner._scanInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_argsBuffer", argsBuffer);

            _kernelGridDimX = (int)((_numGroups + (SCAN_GROUP_SIZE - 1)) / SCAN_GROUP_SIZE);
            _owner._scanInstancesCS.Dispatch(_owner._scanInstancesKernelId, _kernelGridDimX, 1, 1);

            //copy
            _owner._copyInstancesCS.SetInt("_lodId", lod);
            _owner._copyInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_visibilityBuffer", _instancesVisibilityBuffer);
            _owner._copyInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_scanIndicesBuffer", _scanIndicesBuffer);
            _owner._copyInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_scanOffsetsBuffer", _scanOffsetsBuffer);
            _owner._copyInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_generatedGrassInstances", srcInstances);
            _owner._copyInstancesCS.SetBuffer(_owner._scanInstancesKernelId, "_reducedGrassInstances", dstInstances);

            _owner._copyInstancesCS.GetKernelThreadGroupSizes(_owner._copyInstancesKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

            _kernelGridDimX = (int)((_numInstances + (_kernelGroupDimX - 1)) / _kernelGroupDimX);
            _owner._copyInstancesCS.Dispatch(_owner._copyInstancesKernelId, _kernelGridDimX, 1, 1);

            Profiler.EndSample();
        }

        public void Dispose()
        {
            if (_instancesVisibilityBuffer != null)
            {
                _instancesVisibilityBuffer.Release();
                _instancesVisibilityBuffer = null;
            }
            if (_scanIndicesBuffer != null)
            {
                _scanIndicesBuffer.Release();
                _scanIndicesBuffer = null;
            }
            if (_scanTempSumBuffer != null)
            {
                _scanTempSumBuffer.Release();
                _scanTempSumBuffer = null;
            }
            if (_scanOffsetsBuffer != null)
            {
                _scanOffsetsBuffer.Release();
                _scanOffsetsBuffer = null;
            }
        }
    }

    private void InitializeShaders()
    {
        _instancesVisibilityCS = Resources.Load<ComputeShader>("Shaders/GrassInstancesVisibility");
        _copyInstancesCS = Resources.Load<ComputeShader>("Shaders/GrassInstancesCopy");
        _scanInstancesCS = Resources.Load<ComputeShader>("Shaders/ScanInstances");

        _instancesVisibilityKernelId = _instancesVisibilityCS.FindKernel("CSMain");
        _instancesVisibilityCS.EnableKeyword("NAIVE_BBOX_CULL_MODE");
        _instancesVisibilityCS.DisableKeyword("BBOX_CULL_MODE");
        _scanInstancesKernelId = _scanInstancesCS.FindKernel("CSMain");
        _copyInstancesKernelId = _copyInstancesCS.FindKernel("CSMain");
    }

    public VisibilityContext NewContext(int numInstances, float[] lodDistance)
    {
        return new VisibilityContext(this, numInstances, lodDistance);
    }

    void Awake()
    {
        InitializeShaders();
    }
}
