using UnityEngine;
using UnityEngine.Android;

public class RequestScenePermission : MonoBehaviour
{
    void Start()
    {
        RequestIfNeeded("com.oculus.permission.USE_SCENE");
        RequestIfNeeded("com.oculus.permission.USE_ANCHOR_API");
    }

    private void RequestIfNeeded(string permission)
    {
        if (!Permission.HasUserAuthorizedPermission(permission))
        {
            Permission.RequestUserPermission(permission);
        }
    }
}