using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GrassMeshInstancer))]
public class GenerateGrassForMeshButton : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GrassMeshInstancer grassMeshInstancer = (GrassMeshInstancer)target;
        if (GUILayout.Button("Generate grass for Mesh"))
        {
            grassMeshInstancer.GenerateGrass(true);
        }
    }
}
