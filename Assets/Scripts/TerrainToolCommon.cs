using System.IO;
using System.Text;
using Unity.Jobs;
using Unity.Collections;

struct TileLoader {

    public void LoadTile(int x, int y, out float[] heightMap, int resolution, string fileName)
    {
        heightMap = new float[resolution * resolution];

        Stream stream = new FileStream(fileName, FileMode.Open);
        BinaryReader br = new BinaryReader(stream);

        for (int j = 0; j < resolution; ++j)
        {
            for (int i = 0; i < resolution; ++i)
            {
                var height = br.ReadSingle();
                heightMap[i + j * resolution] = height;
            }
        }

        br.Close();
    }
}

struct TileSaver
{
    public void SaveTile(int x, int y, in float[] heightMap, int resolution, string fileName)
    {
        Stream stream = new FileStream(fileName, FileMode.OpenOrCreate);
        BinaryWriter br = new BinaryWriter(stream);

        for (int j = 0; j < resolution; ++j)
        {
            for (int i = 0; i < resolution; ++i)
            {
                var height = heightMap[i + j * resolution];
                br.Write(height);
            }
        }

        br.Close();
    }
}


struct CalculateBorderJob : IJob
{
    public int tileX;
    public int tileY;
    public int colNum;
    public int rowNum;
    public int tileResolution;
    public bool rightNeighbor;
    public bool bottomNeighbor;
    public bool processEmplace;
    public NativeArray<byte> assetsPath;
    public NativeArray<byte> filterText;
    public NativeArray<byte> outputText;

    private void LoadTile(int x, int y, out float[] heightMap)
    {
        string _assetsPath = Encoding.ASCII.GetString(assetsPath.ToArray());
        string _filterText = Encoding.ASCII.GetString(filterText.ToArray());

        var tileName = string.Format(_filterText, y, x);
        var fileName = _assetsPath + tileName + ".r32";

        TileLoader tileLoader;
        tileLoader.LoadTile(x, y, out heightMap, tileResolution, fileName);
    }

    private void SaveTile(int x, int y, in float[] heightMap)
    {
        string _assetsPath = Encoding.ASCII.GetString(assetsPath.ToArray());
        string _filterText = processEmplace ? Encoding.ASCII.GetString(filterText.ToArray()) : Encoding.ASCII.GetString(outputText.ToArray());

        var tileName = string.Format(_filterText, y, x);
        var fileName = _assetsPath + tileName + ".r32";

        TileSaver tileSaver;
        tileSaver.SaveTile(x, y, heightMap, tileResolution + 1, fileName);
    }

    private void CalculateTileWithBorders(int x, int y, bool dX, bool dY)
    {
        float[] baseHeight = null;
        float[] heightdX = null;
        float[] heightdY = null;
        float[] heightdXdY = null;

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

        SaveTile(x, y, heightMap);
    }

    public void Execute()
    {
        CalculateTileWithBorders(tileX, tileY, rightNeighbor, bottomNeighbor);
    }
}

struct CalculateLODsJob : IJob
{
    public int tileX;
    public int tileY;
    public int colNum;
    public int rowNum;
    public int numLODs;
    public int tileResolution;
    public NativeArray<byte> assetsPath;
    public NativeArray<byte> filterText;

    private void Downsample(in float[] src, out float[] dst, int currentResolution)
    {
        int downsampledResolution = (currentResolution / 2);
        dst = new float[downsampledResolution * downsampledResolution];

        for (int j = 0; j < downsampledResolution; ++j) {
            for (int i = 0; i < downsampledResolution; ++i) {
                float sum = src[i * 2 + j * 2 * currentResolution];
                sum += src[i * 2 + (j * 2 + 1) * currentResolution];
                sum += src[(i * 2 + 1) + j * 2 * currentResolution];
                sum += src[(i * 2 + 1) + (j * 2 + 1) * currentResolution];
                dst[i + j * downsampledResolution] = (sum + 2) / 4;
            }
        }
    }

    private void LoadTile(int x, int y, out float[] heightMap)
    {
        string _assetsPath = Encoding.ASCII.GetString(assetsPath.ToArray());
        string _filterText = Encoding.ASCII.GetString(filterText.ToArray());

        var tileName = string.Format(_filterText, y, x);
        var fileName = _assetsPath + tileName + ".r32";

        TileLoader tileLoader;
        tileLoader.LoadTile(x, y, out heightMap, tileResolution, fileName);
    }

    private void SaveTile(int x, int y, int lod, in float[] heightMap, int resolution)
    {
        string _assetsPath = Encoding.ASCII.GetString(assetsPath.ToArray());
        string _filterText = Encoding.ASCII.GetString(filterText.ToArray());

        var tileName = string.Format(_filterText, y, x);
        var fileName = _assetsPath + tileName + string.Format("_lod{0}.r32", lod);

        TileSaver tileSaver;
        tileSaver.SaveTile(x, y, heightMap, resolution, fileName);
    }

    private void CalculateLods(int x, int y)
    {
        float[] lastHeightMapLOD = null;
        LoadTile(x, y, out lastHeightMapLOD);

        int currentResolution = tileResolution;
        for (int lod = 0; lod < numLODs; ++lod)
        {
            float[] currentHeightMapLOD = null;
            Downsample(lastHeightMapLOD, out currentHeightMapLOD, currentResolution);

            currentResolution /= 2;

            SaveTile(x, y, lod + 1, currentHeightMapLOD, currentResolution);

            lastHeightMapLOD = currentHeightMapLOD;
        }
    }

    public void Execute()
    {
        CalculateLods(tileX, tileY);
    }
}
