using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;

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

    private void LoadTile(int x, int y, out float[] heightMap)
    {
        var resolution = tileResolution.value;

        heightMap = new float[resolution * resolution];

        var tileName = string.Format(filterText.value, y, x);
        var fileName = assetsPath.value + tileName + ".r32";

        Stream stream = new FileStream(fileName, FileMode.Open);
        BinaryReader br = new BinaryReader(stream);

        for (int j = 0; j < resolution; ++j) {
            for (int i = 0; i < resolution; ++i) {
                var height = br.ReadSingle();
                heightMap[i + j * resolution] = height;
            }
        }

        br.Close();
    }


    private void SaveExtendedTile(int x, int y, float[] heightMap)
    {
        var resolution = tileResolution.value;
        var tileName = string.Format(processEmplace.value ? filterText.value : outputText.value, y, x);
        var fileName = assetsPath.value + tileName + ".r32";

        Stream stream = new FileStream(fileName, FileMode.OpenOrCreate);
        BinaryWriter br = new BinaryWriter(stream);

        for (int j = 0; j <= resolution; ++j) {
            for (int i = 0; i <= resolution; ++i) {
                var height = heightMap[i + j * (resolution + 1)];
                br.Write(height);
            }
        }

        br.Close();
    }

    private void CalculateTileWithBorders(int x, int y, bool dX, bool dY)
    {
        var resolution = tileResolution.value;

        float[] baseHeight = new float[resolution * resolution];
        float[] heightdX = new float[resolution * resolution];
        float[] heightdY = new float[resolution * resolution];
        float[] heightdXdY = new float[resolution * resolution];

        LoadTile(x, y, out baseHeight);

        if (dX) {
            LoadTile(x + 1, y, out heightdX);
        }

        if (dY) {
            LoadTile(x, y + 1, out heightdY);
        }

        if (dX && dY) {
            LoadTile(x + 1, y + 1, out heightdXdY);
        }

        float[] heightMap = new float[(resolution + 1) * (resolution + 1)];

        for (int j = 0; j < resolution; ++j) {
            for (int i = 0; i < resolution; ++i) {
                heightMap[i + j * (resolution + 1)] = baseHeight[i + j * resolution];
            }
        }

        if (dX) {
            for (int i = 0; i < resolution; ++i) {
                heightMap[i * (resolution + 1) + resolution] = heightdX[i * resolution];
            }
        }

        if (dY) {
            for (int i = 0; i < resolution; ++i) {
                heightMap[i + resolution * (resolution + 1)] = heightdY[i];
            }
        }

        if (dX && dY) {
            heightMap[resolution * (resolution + 1) + resolution] = heightdXdY[0];
        }

        SaveExtendedTile(x, y, heightMap);
    }

    private void CalculateBorders(ClickEvent evt)
    {
        int numRows = rowNum.value;
        int numCols = colNum.value;

        for (int i = 0; i < numRows; i++)
        {
            for (int j = 0; j < numCols; j++)
            {
                CalculateTileWithBorders(i, j, i < (numRows - 1), j < (numCols - 1));

                Debug.Log(string.Format("Tile x{0:000}, y{1:000} processed.", i, j));
            }
        }
    }
}
