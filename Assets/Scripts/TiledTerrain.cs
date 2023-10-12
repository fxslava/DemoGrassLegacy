using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

public class TiledTerrain : MonoBehaviour
{
    [SerializeField] public Camera TerrainCamera;
    [SerializeField] public string tileNameFormat = "Wizard_y{0:000}_x{1:000}";
    [SerializeField] public string diffuseNameFormat = "SatMaps_y{0:000}_x{1:000}.png";
    [SerializeField] public int tileResolution = 256;
    [SerializeField] public float tileSize = 100.0f;
    [SerializeField] public Vector3 terrainOrigin = new Vector3(0.0f, 0.0f, 0.0f);
    [SerializeField] public Vector2Int grid = new Vector2Int(16, 16);
    [SerializeField] public float heightScale = 1.0f;
    [SerializeField] public int residentTilesNum = 25;
    [SerializeField] public int tilesCacheSize = 50;
    [SerializeField] public Shader terrainTessShader = null;

    private Material _material = null;

    private const int INVALID_TILE_ALLOCATION_INDEX = -1;

    private bool needPreWarmCache = true;
    private int[,] _tilesAllocationMap = null;
    private TerrainTile[] _tilesCache = null;

    private class TerrainTile
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

            for (int j = 0; j <= _tileResolution; ++j) {
                for (int i = 0; i <= _tileResolution; ++i) {
                    var height = br.ReadSingle();
                    heightMap[i + j * (_tileResolution + 1)] = height;

                    if (height < minHeight) {
                        minHeight = height;
                    }
                    if (height > maxHeight) {
                        maxHeight = height;
                    }
                }
            }

            _tileOrigin = _terrainOrigin + new Vector3(x * _tileSize, 0, y * _tileSize);
            _bounds.center = _terrainOrigin + new Vector3((x + 0.5f) * _tileSize, (minHeight + maxHeight) * 0.5f * _heightScale, (y + 0.5f) * _tileSize);
            _bounds.extents = new Vector3(_tileSize * 0.5f, (maxHeight - minHeight) * 0.5f * _heightScale, _tileSize * 0.5f);

            if (_heightMapTex == null) {
                _heightMapTex = new Texture2D(_tileResolution + 1, _tileResolution + 1, TextureFormat.RFloat, false);
            }

            _heightMapTex.SetPixelData(heightMap, 0);
            _heightMapTex.Apply();
            _heightMapTex.name = tileName;

            stream.Close();

            var diffuseName = string.Format(_diffuseNameFormat, y, x);
            var diffuseFileName = "assets/resources/terrain/" + diffuseName;

            if (_diffuseMapTex == null) {
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



    private void Start()
    {
        _material = new Material(terrainTessShader);
        _tilesCache = new TerrainTile[tilesCacheSize];
        _tilesAllocationMap = new int[grid.x, grid.y];

        for (int i = 0; i < tilesCacheSize; ++i) {
            _tilesCache[i] = new TerrainTile(terrainOrigin, tileNameFormat, diffuseNameFormat, tileResolution, tileSize, heightScale, _material);
        }

        for (int y = 0; y < grid.y; ++y) {
            for (int x = 0; x < grid.x; ++x) {
                _tilesAllocationMap[x, y] = INVALID_TILE_ALLOCATION_INDEX;
            }
        }

        needPreWarmCache = true;
    }

    private Vector2 CalcTilePosition(Vector2Int tileIndex)
    {
        return new Vector2(terrainOrigin.x + (tileIndex.x + 0.5f) * tileSize, terrainOrigin.z + (tileIndex.y + 0.5f) * tileSize);
    }

    private void Update()
    {
        Vector3 camPos = TerrainCamera.transform.position;

        Vector2Int[] nearestTiles = new Vector2Int[grid.x * grid.y];
        float[] distances = new float[grid.x * grid.y];

        for (int y = 0; y < grid.y; ++y) {
            for (int x = 0; x < grid.x; ++x)
            {
                float cameraDistance = Vector2.Distance(new Vector2(camPos.x, camPos.z), CalcTilePosition(new Vector2Int(x, y)));
                distances[x + y * grid.x] = cameraDistance;
                nearestTiles[x + y * grid.x] = new Vector2Int(x, y);
            }
        }

        Array.Sort(distances, nearestTiles);

        LinkedList<Vector2Int> tilesToAllocateList = new LinkedList<Vector2Int>();
        LinkedList<Vector2Int> tilesToFreeList = new LinkedList<Vector2Int>();

        if (needPreWarmCache) {
            for (int i = 0; i < tilesCacheSize; ++i) {
                var tileIndex = nearestTiles[i];
                _tilesCache[i].LoadTile(tileIndex.x, tileIndex.y);
                _tilesAllocationMap[tileIndex.x, tileIndex.y] = i;
            }

            needPreWarmCache = false;
        }
        else
        {
            for (int i = 0; i < residentTilesNum; ++i) {
                var tileIndex = nearestTiles[i];
                if (_tilesAllocationMap[tileIndex.x, tileIndex.y] == INVALID_TILE_ALLOCATION_INDEX) {
                    tilesToAllocateList.AddLast(tileIndex);
                }
            }

            for (int i = residentTilesNum; i < nearestTiles.Length; ++i) {
                var tileIndex = nearestTiles[i];
                if (_tilesAllocationMap[tileIndex.x, tileIndex.y] != INVALID_TILE_ALLOCATION_INDEX) {
                    tilesToFreeList.AddFirst(tileIndex);
                }
            }

            LinkedListNode<Vector2Int> freeTile = tilesToFreeList.First;

            for (LinkedListNode<Vector2Int> allocTile = tilesToAllocateList.First; allocTile != null; allocTile = allocTile.Next) {

                Vector2Int freeTileIndex = freeTile.Value;
                Vector2Int allocTileIndex = allocTile.Value;

                int cacheIndex = _tilesAllocationMap[freeTileIndex.x, freeTileIndex.y];
                _tilesCache[cacheIndex].LoadTile(allocTileIndex.x, allocTileIndex.y);

                _tilesAllocationMap[freeTileIndex.x, freeTileIndex.y] = INVALID_TILE_ALLOCATION_INDEX;
                _tilesAllocationMap[allocTileIndex.x, allocTileIndex.y] = cacheIndex;

                freeTile = freeTile.Next;
            }
        }

        for (int i = 0; i < tilesCacheSize; ++i)
        {
            _tilesCache[i].Render();
        }
    }
}
