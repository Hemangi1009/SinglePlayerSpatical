using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class AnchorPlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private GameObject anchorVisualPrefab;

    [Header("Ray Settings")]
    [SerializeField] private float maxRayDistance = 10f;

    private XRControls controls;

    private readonly List<ARRaycastHit> raycastHits =
        new List<ARRaycastHit>();

    private readonly List<string> savedAnchorGuidJson =
        new List<string>();

    private string SaveFilePath =>
        Path.Combine(
            Application.persistentDataPath,
            "anchors.json"
        );

    [Serializable]
    private class AnchorGuidList
    {
        public List<string> guidJsonEntries =
            new List<string>();
    }

    private void Awake()
    {
        controls = new XRControls();
    }

    private void OnEnable()
    {
        controls.XRInteractions.PlaceAnchor.performed +=
            OnPlaceAnchorPressed;

        controls.Enable();
    }

    private void OnDisable()
    {
        controls.XRInteractions.PlaceAnchor.performed -=
            OnPlaceAnchorPressed;

        controls.Disable();
    }

    private async void Start()
    {
        if (anchorManager == null)
        {
            Debug.LogError(
                "AnchorPlacer: ARAnchorManager is not assigned."
            );

            return;
        }

        if (raycastManager == null)
        {
            Debug.LogError(
                "AnchorPlacer: ARRaycastManager is not assigned."
            );

            return;
        }

        // Give the AR session/subsystems time to start.
        await Task.Yield();

        await LoadSavedAnchorsAsync();
    }

    private void OnPlaceAnchorPressed(
        UnityEngine.InputSystem.InputAction.CallbackContext ctx)
    {
        TryPlaceAnchorAtRayHit();
    }

    private async void TryPlaceAnchorAtRayHit()
    {
        if (rayOrigin == null)
        {
            Debug.LogWarning(
                "AnchorPlacer: rayOrigin not assigned."
            );

            return;
        }

        if (raycastManager == null)
        {
            Debug.LogWarning(
                "AnchorPlacer: raycastManager not assigned."
            );

            return;
        }

        Ray ray = new Ray(
            rayOrigin.position,
            rayOrigin.forward
        );

        raycastHits.Clear();

        // AR Foundation raycast against the real-world
        // environment/trackables.
        bool hit = raycastManager.Raycast(
            ray,
            raycastHits,
            TrackableType.PlaneWithinPolygon |
            TrackableType.FeaturePoint
        );

        if (!hit || raycastHits.Count == 0)
        {
            Debug.Log(
                "AnchorPlacer: AR ray hit nothing. " +
                "Make sure the environment has been scanned."
            );

            return;
        }

        // Find the closest hit.
        ARRaycastHit closestHit = raycastHits[0];

        for (int i = 1; i < raycastHits.Count; i++)
        {
            if (raycastHits[i].distance <
                closestHit.distance)
            {
                closestHit = raycastHits[i];
            }
        }

        // Do not place beyond the configured ray distance.
        if (closestHit.distance > maxRayDistance)
        {
            Debug.Log(
                "AnchorPlacer: AR ray hit is beyond " +
                "maxRayDistance."
            );

            return;
        }

        Pose hitPose = closestHit.pose;

        Debug.Log(
            $"AnchorPlacer: AR hit at {hitPose.position}"
        );

        await CreateAndSaveAnchorAsync(hitPose);
    }

    private async Task CreateAndSaveAnchorAsync(Pose pose)
    {
        if (anchorManager == null)
        {
            Debug.LogError(
                "AnchorPlacer: ARAnchorManager is missing."
            );

            return;
        }

        /*
         * Create the ARAnchor GameObject manually.
         *
         * This uses AR Foundation's anchor subsystem.
         */
        GameObject anchorObject =
            new GameObject("Persistent AR Anchor");

        anchorObject.transform.SetPositionAndRotation(
            pose.position,
            pose.rotation
        );

        ARAnchor anchor =
            anchorObject.AddComponent<ARAnchor>();

        // Wait for the anchor to become tracked.
        await WaitForAnchorTrackingAsync(anchor);

        if (anchor == null)
        {
            Debug.LogWarning(
                "AnchorPlacer: Failed to create ARAnchor."
            );

            Destroy(anchorObject);

            return;
        }

        SpawnVisual(anchor.transform);

        /*
         * IMPORTANT:
         *
         * AR Foundation's ARAnchor itself does not guarantee
         * persistence between completely separate app sessions.
         *
         * The following code preserves the anchor's identifier
         * only if the underlying XR Anchor Subsystem provides
         * a persistent identifier.
         */
        string guidJson =
            JsonUtility.ToJson(anchor.sessionId);

        savedAnchorGuidJson.Add(guidJson);

        WriteGuidsToDisk();

        Debug.Log(
            $"AnchorPlacer: AR anchor created at " +
            $"{anchor.transform.position}"
        );
    }

    private async Task WaitForAnchorTrackingAsync(
        ARAnchor anchor)
    {
        /*
         * An ARAnchor can initially be pending.
         * AR Foundation may report the trackable on a
         * subsequent AR session update.
         */
        int frameCount = 0;

        while (
            anchor != null &&
            anchor.pending &&
            frameCount < 120)
        {
            frameCount++;

            await Task.Yield();
        }

        if (anchor != null && anchor.pending)
        {
            Debug.LogWarning(
                "AnchorPlacer: Anchor is still pending " +
                "after waiting."
            );
        }
    }

    private void SpawnVisual(Transform parent)
    {
        if (anchorVisualPrefab == null)
        {
            Debug.LogWarning(
                "AnchorPlacer: anchorVisualPrefab " +
                "is not assigned."
            );

            return;
        }

        Instantiate(
            anchorVisualPrefab,
            parent.position,
            parent.rotation,
            parent
        );
    }

    private async Task LoadSavedAnchorsAsync()
    {
        List<string> entries =
            ReadGuidsFromDisk();

        if (entries.Count == 0)
        {
            Debug.Log(
                "AnchorPlacer: no saved anchors to load."
            );

            return;
        }

        /*
         * IMPORTANT:
         *
         * AR Foundation does not provide a universal
         * "load persistent anchor from GUID" API.
         *
         * An ARAnchor's sessionId identifies the session
         * from which it originated, so it cannot simply be
         * used like a persistent spatial-anchor UUID.
         *
         * Therefore, this method cannot recreate anchors
         * across a new AR session using AR Foundation alone
         * unless the underlying XR provider exposes persistent
         * anchor functionality through XRAnchorSubsystem.
         */

        Debug.LogWarning(
            "AnchorPlacer: Saved anchor IDs were found, " +
            "but AR Foundation alone cannot universally " +
            "restore them across a new AR session."
        );

        await Task.CompletedTask;
    }

    private void WriteGuidsToDisk()
    {
        AnchorGuidList data =
            new AnchorGuidList
            {
                guidJsonEntries =
                    savedAnchorGuidJson
            };

        string json =
            JsonUtility.ToJson(data, true);

        File.WriteAllText(
            SaveFilePath,
            json
        );

        Debug.Log(
            $"AnchorPlacer: Saved anchor data to " +
            $"{SaveFilePath}"
        );
    }

    private List<string> ReadGuidsFromDisk()
    {
        if (!File.Exists(SaveFilePath))
        {
            return new List<string>();
        }

        string json =
            File.ReadAllText(SaveFilePath);

        AnchorGuidList data =
            JsonUtility.FromJson<AnchorGuidList>(
                json
            );

        return data?.guidJsonEntries ??
               new List<string>();
    }
}