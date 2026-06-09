using UnityEngine;
using UnityEngine.Android;
using Meta.XR.MRUtilityKit;

namespace MixedRealityProject
{
    /// <summary>
    /// Ensures the app has the Scene (USE_SCENE) permission at runtime and then loads
    /// the room model captured on the device. If the room has never been mapped,
    /// MRUK.LoadSceneFromDevice automatically launches the system Space Setup flow,
    /// so the very first launch guides the user through scanning the room.
    /// EffectMesh then turns floor/walls/ceiling into invisible colliders, so the
    /// physics ball stays contained by the real room instead of flying away.
    /// </summary>
    public class SceneAccessManager : MonoBehaviour
    {
        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => LoadRoom();
                callbacks.PermissionDenied += _ =>
                    Debug.LogWarning("[SceneAccessManager] USE_SCENE denied: the room model is unavailable.");
                Permission.RequestUserPermission(ScenePermission, callbacks);
                return;
            }
#endif
            LoadRoom();
        }

        async void LoadRoom()
        {
            if (MRUK.Instance == null)
            {
                Debug.LogError("[SceneAccessManager] No MRUK instance found in the scene.");
                return;
            }

            // requestSceneCaptureIfNoDataFound: true -> if the room has never been
            // captured on this device, MRUK opens the system Space Setup experience.
            var result = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true);
            Debug.Log($"[SceneAccessManager] Room load result: {result}");
        }
    }
}
