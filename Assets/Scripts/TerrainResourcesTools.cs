using System.IO;
using System.Text;
using Unity.Jobs;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class TerrainToolCalculateBorders : EditorWindow
{
    [MenuItem("Window/Terrain Tools/Calculate HeightMap Borders")]
    public static void ShowExample()
    {
        TerrainToolCalculateBorders wnd = GetWindow<TerrainToolCalculateBorders>();
        wnd.titleContent = new GUIContent("Terrain Tiles Calculate HeightMap Borders");
    }

    TextField assetsPath = null;
    TextField filterText = null;
    TextField outputText = null;
    IntegerField colNum = null;
    IntegerField rowNum = null;
    IntegerField numLODs = null;
    Toggle processEmplace = null;
    IntegerField tileResolution = null;

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;

        assetsPath = new TextField();
        assetsPath.label = "Assets path";
        assetsPath.value = "Assets/Resources/Terrain/";
        root.Add(assetsPath);

        filterText = new TextField();
        filterText.label = "Terrain tile filter";
        filterText.value = "Wizard_y{0:000}_x{1:000}";
        root.Add(filterText);

        outputText = new TextField();
        outputText.label = "Terrain tile output";
        outputText.value = "Output_y{0:000}_x{1:000}";
        root.Add(outputText);

        colNum = new IntegerField();
        colNum.label = "Num Cols";
        colNum.value = 16;
        root.Add(colNum);

        rowNum = new IntegerField();
        rowNum.label = "Num Rows";
        rowNum.value = 16;
        root.Add(rowNum);

        tileResolution = new IntegerField();
        tileResolution.label = "Tile resolution";
        tileResolution.value = 256;
        root.Add(tileResolution);

        processEmplace = new Toggle();
        processEmplace.label = "Process tiles emplace";
        processEmplace.value = false;
        root.Add(processEmplace);

        Button button = new Button();
        button.name = "AppendBordersButton";
        button.text = "Append Borders";
        root.Add(button);

        button.RegisterCallback<ClickEvent>(CalculateBorders);

        numLODs = new IntegerField();
        numLODs.label = "Num LODs";
        numLODs.value = 3;
        root.Add(numLODs);

        Button button1 = new Button();
        button1.name = "CalculateLODs";
        button1.text = "Calculate LODs";
        root.Add(button1);

        button1.RegisterCallback<ClickEvent>(CalculateLODs);
    }

    private enum TaskType
    {
        ProcessBorders,
        ProcessLODs,
        Idle
    }

    private TaskType currentTask = TaskType.Idle;
    private NativeArray<JobHandle> _calculateBordersJobs00;
    private NativeArray<JobHandle> _calculateBordersJobs01;
    private NativeArray<JobHandle> _calculateBordersJobs10;
    private NativeArray<JobHandle> _calculateBordersJobs11;
    private NativeArray<byte>[,] _assetsPath;
    private NativeArray<byte>[,] _filterText;
    private NativeArray<byte>[,] _outputText;
    private JobHandle[,] _allJobHandles;
    private bool[,] _isCompletedJob;

    private void CalculateBorders(ClickEvent evt)
    {
        if (currentTask != TaskType.Idle)
        {
            return;
        }

        currentTask = TaskType.ProcessBorders;

        int numRows = rowNum.value;
        int numCols = colNum.value;

        _assetsPath = new NativeArray<byte>[numRows, numCols];
        _filterText = new NativeArray<byte>[numRows, numCols];
        _outputText = new NativeArray<byte>[numRows, numCols];

        _isCompletedJob = new bool[numRows, numCols];
        _allJobHandles = new JobHandle[numRows, numCols];
        _calculateBordersJobs00 = new NativeArray<JobHandle>(numRows * numCols / 4, Allocator.TempJob);
        _calculateBordersJobs01 = new NativeArray<JobHandle>(numRows * numCols / 4, Allocator.TempJob);
        _calculateBordersJobs10 = new NativeArray<JobHandle>(numRows * numCols / 4, Allocator.TempJob);
        _calculateBordersJobs11 = new NativeArray<JobHandle>(numRows * numCols / 4, Allocator.TempJob);

        CalculateBorderJob[,] allJobs = new CalculateBorderJob[numRows, numCols];

        for (int i = 0; i < numRows; i++)
        {
            for (int j = 0; j < numCols; j++)
            {
                _assetsPath[i,j] = new NativeArray<byte>(Encoding.ASCII.GetBytes(assetsPath.value), Allocator.TempJob);
                _filterText[i,j] = new NativeArray<byte>(Encoding.ASCII.GetBytes(filterText.value), Allocator.TempJob);
                _outputText[i,j] = new NativeArray<byte>(Encoding.ASCII.GetBytes(outputText.value), Allocator.TempJob);
                _isCompletedJob[i, j] = false;

                allJobs[i,j] = new CalculateBorderJob()
                {
                    tileX = i,
                    tileY = j,
                    rightNeighbor = i < (numRows - 1),
                    bottomNeighbor = j < (numCols - 1),
                    assetsPath = _assetsPath[i,j],
                    filterText = _filterText[i,j],
                    outputText = _outputText[i,j],
                    colNum = colNum.value,
                    rowNum = rowNum.value,
                    processEmplace = processEmplace.value,
                    tileResolution = tileResolution.value
                };
            }
        }

        int k = 0;
        for (int i = 0; i < numRows; i += 2) {
            for (int j = 0; j < numCols; j += 2) {
                _allJobHandles[i,j] = _calculateBordersJobs00[k++] = allJobs[i, j].Schedule();
            }
        }

        JobHandle jobs00 = JobHandle.CombineDependencies(_calculateBordersJobs00);

        k = 0;
        for (int i = 1; i < numRows; i += 2) {
            for (int j = 0; j < numCols; j += 2) {
                _allJobHandles[i, j] = _calculateBordersJobs01[k++] = allJobs[i, j].Schedule(jobs00);
            }
        }

        JobHandle jobs01 = JobHandle.CombineDependencies(_calculateBordersJobs01);

        k = 0;
        for (int i = 0; i < numRows; i += 2) {
            for (int j = 1; j < numCols; j += 2) {
                _allJobHandles[i, j] = _calculateBordersJobs10[k++] = allJobs[i, j].Schedule(jobs01);
            }
        }

        JobHandle jobs10 = JobHandle.CombineDependencies(_calculateBordersJobs10);

        k = 0;
        for (int i = 1; i < numRows; i += 2) {
            for (int j = 1; j < numCols; j += 2) {
                _allJobHandles[i, j] = _calculateBordersJobs11[k++] = allJobs[i, j].Schedule(jobs10);
            }
        }
    }

    private void CalculateLODs(ClickEvent evt)
    {
        if (currentTask != TaskType.Idle)
        {
            return;
        }

        currentTask = TaskType.ProcessLODs;

        int numRows = rowNum.value;
        int numCols = colNum.value;

        _assetsPath = new NativeArray<byte>[numRows, numCols];
        _filterText = new NativeArray<byte>[numRows, numCols];
        _allJobHandles = new JobHandle[numRows, numCols];
        _isCompletedJob = new bool[numRows, numCols];

        for (int i = 0; i < numRows; i++)
        {
            for (int j = 0; j < numCols; j++)
            {
                _assetsPath[i, j] = new NativeArray<byte>(Encoding.ASCII.GetBytes(assetsPath.value), Allocator.TempJob);
                _filterText[i, j] = new NativeArray<byte>(Encoding.ASCII.GetBytes(filterText.value), Allocator.TempJob);
                _isCompletedJob[i, j] = false;

                var _job = new CalculateLODsJob()
                {
                    tileX = i,
                    tileY = j,
                    assetsPath = _assetsPath[i, j],
                    filterText = _filterText[i, j],
                    colNum = colNum.value,
                    rowNum = rowNum.value,
                    numLODs = numLODs.value,
                    tileResolution = tileResolution.value
                };

                _allJobHandles[i,j] = _job.Schedule();
            }
        }
    }

    private void Awake()
    {
        currentTask = TaskType.Idle;
    }

    private void Update()
    {
        int numRows = rowNum.value;
        int numCols = colNum.value;

        if (currentTask == TaskType.ProcessBorders)
        {
            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    if (!_isCompletedJob[i, j] && _allJobHandles[i, j].IsCompleted)
                    {
                        _allJobHandles[i, j].Complete();
                        _isCompletedJob[i, j] = true;
                        Debug.Log(string.Format("Tile x{0:000}, y{1:000} processed.", i, j));
                    }
                }
            }

            bool allCompleted = true;
            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    allCompleted = allCompleted && _isCompletedJob[i, j];
                }
            }


            if (allCompleted)
            {
                _calculateBordersJobs00.Dispose();
                _calculateBordersJobs01.Dispose();
                _calculateBordersJobs10.Dispose();
                _calculateBordersJobs11.Dispose();

                for (int i = 0; i < numRows; i++)
                {
                    for (int j = 0; j < numCols; j++)
                    {
                        _assetsPath[i, j].Dispose();
                        _filterText[i, j].Dispose();
                        _outputText[i, j].Dispose();
                    }
                }
                currentTask = TaskType.Idle;
            }
        }

        if (currentTask == TaskType.ProcessLODs)
        {
            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    if (!_isCompletedJob[i, j] && _allJobHandles[i, j].IsCompleted)
                    {
                        _allJobHandles[i, j].Complete();
                        _isCompletedJob[i, j] = true;
                        Debug.Log(string.Format("Tile x{0:000}, y{1:000} processed.", i, j));
                    }
                }
            }

            bool allCompleted = true;
            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    allCompleted = allCompleted && _isCompletedJob[i, j];
                }
            }


            if (allCompleted)
            {
                for (int i = 0; i < numRows; i++)
                {
                    for (int j = 0; j < numCols; j++)
                    {
                        _assetsPath[i, j].Dispose();
                        _filterText[i, j].Dispose();
                    }
                }
                currentTask = TaskType.Idle;
            }
        }
    }
}
