//using UnityEngine;
//using UnityEngine.XR.Interaction.Toolkit;
//using UnityEngine.XR.ARFoundation;
//using UnityEngine.XR.Interaction.Toolkit.Interactors;
//using System.Threading.Tasks;

//public class SpwanAnchorByRay : MonoBehaviour
//{
//    public XRRayInteractor rayInteractor;
//    public ARAnchorManager anchorManager;

//    void Start()
//    {
//        rayInteractor.selectEntered.AddListener(SpawnAnchor);
//    }

//    public async void SpawnAnchor(BaseInteractionEventArgs args)
//    {
//        rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit);

//        Pose hitPose = new Pose(hit.point, Quaternion.LookRotation(-hit.normal));

//        var result = await anchorManager.TryAddAnchorAsync(hitPose);

//        if (result.status.IsSuccess())
//        {
//            ARAnchor anchor = result.value;
//        }

//    }
//}

using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Threading.Tasks;

public class SpwanAnchorByRay : MonoBehaviour
{
    public XRRayInteractor rayInteractor;
    public ARAnchorManager anchorManager;

    void Start()
    {
        rayInteractor.selectEntered.AddListener(SpawnAnchor);
    }

    public async void SpawnAnchor(BaseInteractionEventArgs args)
    {
        if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            Pose hitPose = new Pose(
                hit.point,
                Quaternion.LookRotation(-hit.normal)
            );

            var result = await anchorManager.TryAddAnchorAsync(hitPose);

            if (result.status.IsSuccess())
            {
                ARAnchor anchor = result.value;

                Debug.Log("Anchor created successfully: " + anchor.name);
            }
            else
            {
                Debug.LogError("Failed to create anchor. Status: " + result.status);
            }
        }
    }
}