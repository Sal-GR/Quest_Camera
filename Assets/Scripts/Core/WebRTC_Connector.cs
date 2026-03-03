using System.Collections;
using Unity.WebRTC;
using UnityEngine;
using System;
using UnityEngine.Android;

public class WebRTCSender : MonoBehaviour
{
    [Header("Imported Modules")]
    [SerializeField] private VideoCompositor videoCompositor;
    [SerializeField] private SignalingClient signalingClient;
    [SerializeField] private PeerConnectionManager peerConnectionManager;

    private string callerName = "Meta Quest User";
    private string userId;
    private string room;

    void Awake()
    {
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
        userId = $"quest-{suffix}";
        room   = $"quest-{suffix}"; // private room = same as userId for clarity
    }
    void Start()
    {
        StartCoroutine(WebRTC.Update());
        // Subscribe to the compositor's event — we'll add the track once it's live
        videoCompositor.OnTrackReady += track => StartCoroutine(AddTrackWhenReady(track));

        // Signaling → PeerConnectionManager
        signalingClient.OnIceConfigReady += peerConnectionManager.SetupPeerConnection;
        signalingClient.OnCallAccepted += peerConnectionManager.SendOffer;
        signalingClient.OnAnswerReceived += peerConnectionManager.SetRemoteAnswer;
        signalingClient.OnIceCandidateReceived += peerConnectionManager.HandleIceCandidate;

        // PeerConnectionManager → Signaling
        peerConnectionManager.OnLocalIceCandidate += candidate => signalingClient.SendIceCandidate(room, candidate);
        peerConnectionManager.OnOfferReady        += sdp => signalingClient.SendWs(new {
            type     = "sendToGroup",
            group    = room,
            dataType = "json",
            data     = new { type = "offer", room, sdp }
        });
        peerConnectionManager.OnConnectionLost += () => signalingClient.SendCallEnded(room);

        StartCoroutine(RequestPermissionThenInit());
    }

    IEnumerator AddTrackWhenReady(VideoStreamTrack track)
    {
        float timeout = 10f;
        float elapsed = 0f;
        while (!peerConnectionManager.IsReady && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.2f);
            elapsed += 0.2f;
        }

        if (!peerConnectionManager.IsReady)
        {
            Debug.LogError("WebRTCSender: PeerConnectionManager never became ready, cannot add track.");
            yield break;
        }

        peerConnectionManager.AddVideoTrack(track);
    }   

    IEnumerator RequestPermissionThenInit()
    {
        string perm = "horizonos.permission.HEADSET_CAMERA";

        if (!Permission.HasUserAuthorizedPermission(perm))
        {
            bool decided = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => { decided = true; Debug.Log("WebRTC: Camera permission granted"); };
            callbacks.PermissionDenied += _ => { decided = true; Debug.LogError("WebRTC: Camera permission denied"); };
            Permission.RequestUserPermission(perm, callbacks);
            yield return new WaitUntil(() => decided);
        }

        if (!Permission.HasUserAuthorizedPermission(perm))
        {
            Debug.LogError("WebRTC: Cannot proceed without camera permission.");
            yield break;
        }

        videoCompositor.StartCompositor();
        signalingClient.StartSignaling(userId, room, callerName);
    }

    void OnDestroy()
    {
        signalingClient.SendCallEnded(room);
    }
}

[Serializable] public class NegotiateResponse { public string url; }
[Serializable] public class IceConfigResponse { public IceServerData[] iceServers; }
[Serializable] public class IceServerData { public string[] urls; public string username; public string credential; }