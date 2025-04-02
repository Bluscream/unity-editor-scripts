using UnityEngine;
using UnityEditor;

public class FlipGameObject : MonoBehaviour
{
    [MenuItem("GameObject/Transform/Flip %F")]
    public static void Flip()
    {
        foreach (var obj in Selection.gameObjects)
        {
            FlipObject(obj.transform);
        }
    }

    private static void FlipObject(Transform transform)
    {
        // Apply flip to the transform itself
        Vector3 scale = transform.localScale;
        scale.x *= -1; // Flip along the X-axis
        transform.localScale = scale;

        // Recursively apply flip to all children
        foreach (Transform child in transform)
        {
            FlipObject(child);
        }
    }
}
