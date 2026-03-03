using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Handles all HTTP communication with the signaling server.
/// Fetches the ICE config and negotiates a WebPubSub token,
/// then fires events so SignalingClient can open the WebSocket.
/// </summary>
public class SignalingHttpClient : MonoBehaviour
{
  private string signalingServerUrl = "https://ar-signaling-server.azurewebsites.net";

  // Fired once the ICE config is successfully fetched
  public event Action<IceConfigResponse> OnIceConfigReady;

  // Fired once the WebPubSub token is successfully negotiated
  public event Action<string> OnNegotiateComplete;

  public void FetchAndNegotiate(string userId, string room)
  {
    StartCoroutine(FetchConfigAndNegotiate(userId, room));
  }

  private IEnumerator FetchConfigAndNegotiate(string userId, string room)
  {
    // 1. ICE config
    using var iceReq = UnityWebRequest.Get($"{signalingServerUrl}/ice-config");
    yield return iceReq.SendWebRequest();

    if (iceReq.result != UnityWebRequest.Result.Success)
    {
      Debug.LogError("SignalingHttpClient: ICE config fetch failed: " + iceReq.error);
      yield break;
    }

    var iceConfig = JsonUtility.FromJson<IceConfigResponse>(iceReq.downloadHandler.text);
    OnIceConfigReady?.Invoke(iceConfig);
    Debug.Log("SignalingHttpClient: ICE config ready");

    // 2. Negotiate WebPubSub token
    using var negReq = UnityWebRequest.Get(
        $"{signalingServerUrl}/negotiate?userId={userId}&room={room}");
    yield return negReq.SendWebRequest();

    if (negReq.result != UnityWebRequest.Result.Success)
    {
      Debug.LogError("SignalingHttpClient: Negotiate failed: " + negReq.error);
      yield break;
    }

    var negotiateResponse = JsonUtility.FromJson<NegotiateResponse>(negReq.downloadHandler.text);
    OnNegotiateComplete?.Invoke(negotiateResponse.url);
    Debug.Log("SignalingHttpClient: Negotiate complete");
  }
}