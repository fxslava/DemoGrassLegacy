using UnityEngine;
using System.IO;

class TerrainTile
{
    public Bounds _bounds;

    private Vector3 _terrainOrigin;
    private Vector3 _tileOrigin;
    private string _tileNameFormat;
    private string _diffuseNameFormat;
    private int _tileResolution;
    private float _tileSize;
    private Material _material;
    private float _patchSize;
    private Texture2D _heightMapTex = null;
    private Texture2D _diffuseMapTex = null;
    private Vector2Int _tileIndex = new Vector2Int(-1, -1);
    private bool _isLoaded = false;
    private float _heightScale = 1.0f;

    public TerrainTile(Vector3 terrainOrigin, string tileNameFormat, string diffuseNameFormat, int tileResolution, float tileSize, float heightScale, Material material)
    {
        _terrainOrigin = terrainOrigin;
        _tileNameFormat = tileNameFormat;
        _diffuseNameFormat = diffuseNameFormat;
        _tileResolution = tileResolution;
        _tileSize = tileSize;
        _heightScale = heightScale;
        _material = material;
        _patchSize = _tileSize / _tileResolution;
    }

    public void LoadTile(int x, int y)
    {
        _tileIndex.x = x;
        _tileIndex.y = y;

        var tileName = string.Format(_tileNameFormat, y, x);
        var fileName = "assets/resources/terrain/" + tileName + ".r32";

        Stream stream = new FileStream(fileName, FileMode.Open);
        BinaryReader br = new BinaryReader(stream);

        float[] heightMap = new float[(_tileResolution + 1) * (_tileResolution + 1)];

        float minHeight = 100.0f, maxHeight = -100.0f;

        for (int j = 0; j <= _tileResolution; ++j)
        {
            for (int i = 0; i <= _tileResolution; ++i)
            {
                var height = br.ReadSingle();
                heightMap[i + j * (_tileResolution + 1)] = height;

                if (height < minHeight)
                {
                    minHeight = height;
                }
                if (height > maxHeight)
                {
                    maxHeight = height;
                }
            }
        }

        _tileOrigin = _terrainOrigin + new Vector3(x * _tileSize, 0, y * _tileSize);
        _bounds.center = _terrainOrigin + new Vector3((x + 0.5f) * _tileSize, (minHeight + maxHeight) * 0.5f * _heightScale, (y + 0.5f) * _tileSize);
        _bounds.extents = new Vector3(_tileSize * 0.5f, (maxHeight - minHeight) * 0.5f * _heightScale, _tileSize * 0.5f);

        if (_heightMapTex == null)
        {
            _heightMapTex = new Texture2D(_tileResolution + 1, _tileResolution + 1, TextureFormat.RFloat, false);
        }

        _heightMapTex.SetPixelData(heightMap, 0);
        _heightMapTex.Apply();
        _heightMapTex.name = tileName;

        stream.Close();

        var diffuseName = string.Format(_diffuseNameFormat, y, x);
        var diffuseFileName = "assets/resources/terrain/" + diffuseName;

        if (_diffuseMapTex == null)
        {
            _diffuseMapTex = new Texture2D(_tileResolution + 1, _tileResolution + 1);
        }

        var rawData = System.IO.File.ReadAllBytes(diffuseFileName);
        ImageConversion.LoadImage(_diffuseMapTex, rawData);
        _diffuseMapTex.name = diffuseName;

        _isLoaded = true;
    }

    public void Render()
    {
        if (_isLoaded)
        {
            RenderParams rp = new RenderParams(_material);
            rp.worldBounds = _bounds;
            rp.matProps = new MaterialPropertyBlock();
            rp.matProps.SetTexture("_HeightMap", _heightMapTex);
            rp.matProps.SetTexture("_DiffuseMap", _diffuseMapTex);
            rp.matProps.SetFloat("_patchSize", _patchSize);
            rp.matProps.SetInt("_gridDimensions", _tileResolution);
            rp.matProps.SetVector("_tileOrigin", _tileOrigin);
            rp.matProps.SetFloat("_heightScale", _heightScale);
            Graphics.RenderPrimitives(rp, MeshTopology.Quads, 4, _tileResolution * _tileResolution);
        }
    }

    public Vector2 GetTilePositionXZ()
    {
        return new Vector2(_bounds.center.x, _bounds.center.z);
    }

    public Vector2Int GetTileIndex()
    {
        return _tileIndex;
    }

    public bool IsLoaded { get { return _isLoaded; } }
}
