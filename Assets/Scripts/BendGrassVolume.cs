using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BendGrassVolume : MonoBehaviour
{
    public Bounds GetBounds()
    {
        return new Bounds(transform.position, transform.localScale);
    }

    void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }
}
