using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TestGrassInstancesDraw
{
    private const int SCAN_GROUP_SIZE = 1024;
    private ComputeBuffer _visibilityBuffer;
    private ComputeBuffer _scanIndicesBuffer;
    private ComputeBuffer _scanOffsetsBuffer;
    private ComputeShader _scanInstancesCS;
    private int _scanInstancesKernelId = -1;
    int[] visibilityData = null;
    int[] indicesData = null;
    int[] offsetsData = null;

    [SerializeField] public int _numInstances = SCAN_GROUP_SIZE * 1024;

    private void InitHW()
    {
        _scanInstancesCS = Resources.Load<ComputeShader>("Shaders/ScanInstances");
        _scanInstancesCS.EnableKeyword("LOD_PREFIXES_PASS");
        _scanInstancesKernelId = _scanInstancesCS.FindKernel("CSMain");

        _visibilityBuffer = new ComputeBuffer(_numInstances, sizeof(int));
        _scanIndicesBuffer = new ComputeBuffer(_numInstances, sizeof(int));
        _scanOffsetsBuffer = new ComputeBuffer(_numInstances / SCAN_GROUP_SIZE, sizeof(int));
    }

    private void InitVisibilityBuffer()
    {
        for (int i = 0; i < _numInstances; i++) {
            visibilityData[i] = Random.value > 0.5f ? 1 : 0;
        }

        _visibilityBuffer.SetData(visibilityData);
    }

    private void ScanKernelDispatch(ComputeBuffer visibilityBuffer, ComputeBuffer scanIndices, ComputeBuffer scanGroupSums, int numInstances, int lodId = 1)
    {
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_lodBuffer", visibilityBuffer);
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_indicesBuffer", scanIndices);
        _scanInstancesCS.SetBuffer(_scanInstancesKernelId, "_groupSumBufferOut", scanGroupSums);
        _scanInstancesCS.SetInt("_numInstances", numInstances);
        _scanInstancesCS.SetInt("_lodId", lodId);

        uint _kernelGroupDimX = 0, _kernelGroupDimY = 0, _kernelGroupDimZ = 0;
        _scanInstancesCS.GetKernelThreadGroupSizes(_scanInstancesKernelId, out _kernelGroupDimX, out _kernelGroupDimY, out _kernelGroupDimZ);

        int _kernelGridDimX = (numInstances + (SCAN_GROUP_SIZE - 1)) / SCAN_GROUP_SIZE;
        _scanInstancesCS.Dispatch(_scanInstancesKernelId, _kernelGridDimX, 1, 1);
    }

    private void ScanKernelCPU()
    {
        int prefix = 0;
        for (int i = 0; i < _numInstances; i++) {
            indicesData[i] = prefix;
            prefix += visibilityData[i];

            if ((i % SCAN_GROUP_SIZE) == (SCAN_GROUP_SIZE - 1)) {
                offsetsData[i / SCAN_GROUP_SIZE] = prefix;
                prefix = 0;
            }
        }
    }

    private bool CheckBuffers()
    {
        int numOfGroups = _numInstances / SCAN_GROUP_SIZE;
        int[] indicesGPUData = new int[_numInstances];
        int[] offsetsGPUData = new int[numOfGroups];

        _scanIndicesBuffer.GetData(indicesGPUData);
        _scanOffsetsBuffer.GetData(offsetsGPUData);

        bool checkIndices = true;
        for (int i = 0; i < _numInstances; i++) {
            if (indicesGPUData[i] != indicesData[i]) {
                checkIndices = false;
            }
        }

        bool checkOffsets = true;
        for (int i = 0; i < numOfGroups; i++) {
            if (offsetsGPUData[i] != offsetsData[i]) {
                checkOffsets = false;
            }
        }

        return checkIndices && checkOffsets;
    }

    [SetUp]
    public void Setup()
    {
        visibilityData = new int[_numInstances];
        indicesData = new int[_numInstances];
        offsetsData = new int[_numInstances];

        InitHW();
        InitVisibilityBuffer();
    }

    [UnityTest]
    public IEnumerator TestGrassInstancesDrawWithEnumeratorPasses()
    {
        ScanKernelDispatch(_visibilityBuffer, _scanIndicesBuffer, _scanOffsetsBuffer, _numInstances);
        ScanKernelCPU();

        Assert.IsTrue(CheckBuffers());

        yield return null;
    }

    [TearDown]
    public void Teardown()
    {
        _visibilityBuffer.Release();
        _scanIndicesBuffer.Release();
        _scanOffsetsBuffer.Release();
    }
}
