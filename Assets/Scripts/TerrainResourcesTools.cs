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
    }

    struct CalculateBorderJob : IJob
    {
        public int    tileX;
        public int    tileY;
        public bool   rightNeighbor;
        public bool   bottomNeighbor;
        public NativeArray<byte> assetsPath;
        public NativeArray<byte> filterText;
        public NativeArray<byte> outputText;
        public int    colNum;
        public int    rowNum;
        public bool   processEmplace;
        public int    tileResolution;

        private void LoadTile(int x, int y, out float[] heightMap)
        {
            string _assetsPath = Encoding.ASCII.GetString(assetsPath.ToArray());
            string _filterText = Encoding.ASCII.GetString(filterText.ToArray());

            heightMap = new float[tileResolution * tileResolution];

            var tileName = string.Format(_filterText, y, x);
            var fileName = _assetsPath + tileName + ".r32";

            Stream stream = new FileStream(fileName, FileMode.Open);
            BinaryReader br = new BinaryReader(stream);

            for (int j = 0; j < tileResolution; ++j)
            {
                for (int i = 0; i < tileResolution; ++i)
                {
                    var height = br.ReadSingle();
                    heightMap[i + j * tileResolution] = height;
                }
            }

            br.Close();
        }


        private void SaveExtendedTile(int x, int y, in float[] heightMap)
        {
            string _assetsPath = Encoding.ASCII.GetString(assetsPath.ToArray());
            string _filterText = Encoding.ASCII.GetString(filterText.ToArray());
            string _outputText = Encoding.ASCII.GetString(outputText.ToArray());

            var tileName = string.Format(processEmplace ? _filterText : _outputText, y, x);
            var fileName = _assetsPath + tileName + ".r32";

            Stream stream = new FileStream(fileName, FileMode.OpenOrCreate);
            BinaryWriter br = new BinaryWriter(stream);

            for (int j = 0; j <= tileResolution; ++j)
            {
                for (int i = 0; i <= tileResolution; ++i)
                {
                    var height = heightMap[i + j * (tileResolution + 1)];
                    br.Write(height);
                }
            }

            br.Close();
        }

        private void CalculateTileWithBorders(int x, int y, bool dX, bool dY)
        {
            float[] baseHeight = new float[tileResolution * tileResolution];
            float[] heightdX = new float[tileResolution * tileResolution];
            float[] heightdY = new float[tileResolution * tileResolution];
            float[] heightdXdY = new float[tileResolution * tileResolution];

            LoadTile(x, y, out baseHeight);

            if (dX)
            {
                LoadTile(x + 1, y, out heightdX);
            }

            if (dY)
            {
                LoadTile(x, y + 1, out heightdY);
            }

            if (dX && dY)
            {
                LoadTile(x + 1, y + 1, out heightdXdY);
            }

            float[] heightMap = new float[(tileResolution + 1) * (tileResolution + 1)];

            for (int j = 0; j < tileResolution; ++j)
            {
                for (int i = 0; i < tileResolution; ++i)
                {
                    heightMap[i + j * (tileResolution + 1)] = baseHeight[i + j * tileResolution];
                }
            }

            if (dX)
            {
                for (int i = 0; i < tileResolution; ++i)
                {
                    heightMap[i * (tileResolution + 1) + tileResolution] = heightdX[i * tileResolution];
                }
            }

            if (dY)
            {
                for (int i = 0; i < tileResolution; ++i)
                {
                    heightMap[i + tileResolution * (tileResolution + 1)] = heightdY[i];
                }
            }

            if (dX && dY)
            {
                heightMap[tileResolution * (tileResolution + 1) + tileResolution] = heightdXdY[0];
            }

            SaveExtendedTile(x, y, heightMap);
        }

        public void Execute()
        {
            CalculateTileWithBorders(tileX, tileY, rightNeighbor, bottomNeighbor);
        }
    }

    private NativeArray<JobHandle> _calculateBordersJobs00;
    private NativeArray<JobHandle> _calculateBordersJobs01;
    private NativeArray<JobHandle> _calculateBordersJobs10;
    private NativeArray<JobHandle> _calculateBordersJobs11;
    private JobHandle[,] _allJobHandles;
    private bool[,] _isCompletedJob;
    private NativeArray<byte>[,] _assetsPath;
    private NativeArray<byte>[,] _filterText;
    private NativeArray<byte>[,] _outputText;
    private bool _isJobsStarted = false;

    private void CalculateBorders(ClickEvent evt)
    {
        int numRows = rowNum.value;
        int numCols = colNum.value;

        _assetsPath = new NativeArray<byte>[numRows, numCols];
        _filterText = new NativeArray<byte>[numRows, numCols];
        _outputText = new NativeArray<byte>[numRows, numCols];
        _isJobsStarted = true;

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

    private void Awake()
    {
        _isJobsStarted = false;
    }

    private void Update()
    {
        int numRows = rowNum.value;
        int numCols = colNum.value;

        if (_isJobsStarted)
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
                _isJobsStarted = false;
            }
        }
    }
}
