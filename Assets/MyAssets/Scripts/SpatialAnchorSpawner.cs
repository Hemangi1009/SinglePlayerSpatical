using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.XR.CoreUtils.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Spawns a persistent spatial anchor at the end of a controller ray when the
/// trigger is pressed. Anchors survive recenter (handled automatically by the
/// OpenXR runtime) and survive app restart (handled by saving GUIDs to disk).
/// </summary>
public class SpatialAnchorSpawner : MonoBehaviour
{
    [Header("AR Foundation")]
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARSession arSession;

    [Header("Input")]
    [Tooltip("Bind this to the controller's trigger / activate action.")]
    [SerializeField] private InputActionReference triggerAction;

    [Header("Ray")]
    [Tooltip("Your XR Ray Interactor — the spawn point is taken directly from its current raycast hit.")]
    [SerializeField] private XRRayInteractor rayInteractor;

    [Header("Content")]
    [Tooltip("Prefab instantiated as a child of each anchor.")]
    [SerializeField] private GameObject anchorContentPrefab;

    private const string SaveFileName = "saved_anchor_guids.json";
    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    [Serializable]
    private class GuidListWrapper { public List<string> guids = new List<string>(); }

    private void OnEnable()
    {
        triggerAction.action.performed += OnTriggerPressed;
        triggerAction.action.Enable();
    }

    private void OnDisable()
    {
        triggerAction.action.performed -= OnTriggerPressed;
    }

    private async void Start()
    {
        await WaitForSessionReadyAsync();
        await LoadSavedAnchorsAsync();
    }

    private async Task WaitForSessionReadyAsync()
    {
        if (arSession == null)
        {
            Debug.LogWarning("ARSession not assigned — attempting to load anchors immediately, this may fail if the session isn't ready yet.");
            return;
        }

        while (ARSession.state != ARSessionState.SessionTracking &&
               ARSession.state != ARSessionState.SessionInitializing)
        {
            await Task.Yield();
        }

        // Give it a couple more frames once initializing starts.
        while (ARSession.state != ARSessionState.SessionTracking)
        {
            await Task.Yield();
        }
    }

    private void OnTriggerPressed(InputAction.CallbackContext ctx)
    {
        if (anchorManager == null) return;

        Pose pose;

        if (rayInteractor != null && rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit interactorHit))
        {
            Debug.Log($"[AnchorSpawn] Using RAY INTERACTOR hit at {interactorHit.point}, normal {interactorHit.normal}");
            // Exactly matches what the user sees, since this is the same hit
            // XRIT used to draw the ray/line visual.
            pose = new Pose(interactorHit.point, Quaternion.LookRotation(interactorHit.normal));
        }
        else
        {
            // Ray isn't over a detected plane right now — don't spawn.
            Debug.Log("[AnchorSpawn] No ray interactor hit — ray is not currently over a plane. Anchor not spawned.");
            return;
        }

        _ = SpawnAndSaveAnchorAsync(pose);
    }

    private async Task SpawnAndSaveAnchorAsync(Pose pose)
    {
        var addResult = await anchorManager.TryAddAnchorAsync(pose);
        if (addResult.status.IsError())
        {
            Debug.LogError($"Failed to add anchor: {addResult.status}");
            return;
        }

        ARAnchor anchor = addResult.value;
        SpawnContent(anchor);

        var saveResult = await anchorManager.TrySaveAnchorAsync(anchor);
        if (saveResult.status.IsError())
        {
            Debug.LogError($"Failed to save anchor: {saveResult.status}");
            return;
        }

        Debug.Log($"Anchor saved successfully with GUID: {saveResult.value}");
        AppendGuidToDisk(saveResult.value);
    }

    private async Task LoadSavedAnchorsAsync()
    {
        List<SerializableGuid> guids = ReadGuidsFromDisk();
        Debug.Log($"Found {guids.Count} saved anchor GUID(s) on disk.");
        if (guids.Count == 0) return;

        var results = new List<ARSaveOrLoadAnchorResult>();
        await anchorManager.TryLoadAnchorsAsync(guids, results, OnIncrementalLoadResult);

        foreach (var result in results)
        {
            if (result.resultStatus.IsError())
                Debug.LogWarning($"Failed to load anchor {result.savedAnchorGuid}: {result.resultStatus}");
            else
                Debug.Log($"Loaded anchor {result.savedAnchorGuid} successfully.");
        }
    }

    private void OnIncrementalLoadResult(ReadOnlyListSpan<ARSaveOrLoadAnchorResult> batch)
    {
        foreach (var result in batch)
        {
            if (result.resultStatus.IsSuccess() && result.anchor != null)
                SpawnContent(result.anchor);
        }
    }

    private void SpawnContent(ARAnchor anchor)
    {
        if (anchorContentPrefab != null)
            Instantiate(anchorContentPrefab, anchor.transform);
    }

    // --- Disk persistence for GUIDs (Unity's own anchor store does NOT remember these for you) ---

    private void AppendGuidToDisk(SerializableGuid guid)
    {
        var wrapper = File.Exists(SavePath)
            ? JsonUtility.FromJson<GuidListWrapper>(File.ReadAllText(SavePath))
            : new GuidListWrapper();

        wrapper.guids.Add(guid.ToString());
        File.WriteAllText(SavePath, JsonUtility.ToJson(wrapper));
    }

    private List<SerializableGuid> ReadGuidsFromDisk()
    {
        var list = new List<SerializableGuid>();
        if (!File.Exists(SavePath)) return list;

        var wrapper = JsonUtility.FromJson<GuidListWrapper>(File.ReadAllText(SavePath));
        foreach (var s in wrapper.guids)
        {
            if (Guid.TryParse(s, out Guid g))
                list.Add(new SerializableGuid(g));
        }
        return list;
    }
}