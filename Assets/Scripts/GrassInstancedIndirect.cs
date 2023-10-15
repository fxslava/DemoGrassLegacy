using UnityEngine;


//[ExecuteInEditMode]
public class GrassInstancedIndirect : MonoBehaviour
{
    [SerializeField] public Camera mainCamera;
    [SerializeField] public Mesh grassMesh;
    [SerializeField] public Vector2Int count = new Vector2Int(100, 100);
    [SerializeField] public Vector2 spacing = new Vector2(0.1f, 0.1f);
    [SerializeField] public Color baseColor = Color.green;
    [SerializeField] public Color tintColor = Color.black;
    [SerializeField] public float[] lodDistance;
    [SerializeField] public Material[] material;

    private BendGrassManager _bendManager = null;
    private GrassTile _grassTile = null;
    private HiZBuffer hiZBuffer = null;

    private void Start()
    {
        _bendManager = GetComponent<BendGrassManager>();
        hiZBuffer = mainCamera.GetComponent<HiZBuffer>();
        _grassTile = new GrassTile(mainCamera, hiZBuffer, grassMesh, transform.position, count, spacing, baseColor, tintColor, lodDistance, material);
        _grassTile.Init();
    }

    private void Update()
    {
        _grassTile.Render(_bendManager);
    }

    private void OnDestroy()
    {
        _grassTile.Dispose();
    }
}
