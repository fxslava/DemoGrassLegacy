using UnityEngine;
using System;
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
