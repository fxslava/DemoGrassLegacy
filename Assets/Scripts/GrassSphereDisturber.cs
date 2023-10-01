using UnityEngine;

public class GrassSphereDisturber : MonoBehaviour
{
    [SerializeField] public float Radius = 1.0f;
    [SerializeField] public BendGrassManager bendManager;

    // Update is called once per frame
    void Update()
    {
        bendManager.BendSphere(transform.position, Radius);
    }

    void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, Radius);
    }
}
