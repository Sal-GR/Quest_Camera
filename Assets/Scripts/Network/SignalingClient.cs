using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Handles all signaling communication:
/// - HTTP fetches for ICE config and WebPubSub token
/// - WebSocket connection and message routing
/// Raises events for WebRTCSender to react to.
/// </summary>
public class SignalingClient : MonoBehaviour
{
  private string signalingServerUrl = "https://ar-signaling-server.azurewebsites.net";

  // Fired after ICE config and negotiate requests succeed
  public event Action<IceConfigResponse> OnIceConfigReady;

  // Signaling events for WebRTCSender to handle
  public event Action                            OnCallAccepted;
  public event Action                            OnCallDeclined;
  public event Action<string>                    OnAnswerReceived;
  public event Action<Dictionary<string, object>> OnIceCandidateReceived;

  private ClientWebSocket _ws;
  private CancellationTokenSource _cts;
  private readonly Queue<string> _messageQueue = new Queue<string>();

  private string _room;
  private string _callerName;

  public void StartSignaling(string userId, string room, string callerName)
  {
      _room       = room;
      _callerName = callerName;
      StartCoroutine(FetchConfigAndConnect(userId));
  }

  // -------------------------------------------------------------------------
  // HTTP
  // -------------------------------------------------------------------------

  private IEnumerator FetchConfigAndConnect(string userId)
  {
      // 1. ICE config
      using var iceReq = UnityWebRequest.Get($"{signalingServerUrl}/ice-config");
      yield return iceReq.SendWebRequest();

      if (iceReq.result != UnityWebRequest.Result.Success)
      {
          Debug.LogError("SignalingClient: ICE config fetch failed: " + iceReq.error);
          yield break;
      }

      var iceConfig = JsonUtility.FromJson<IceConfigResponse>(iceReq.downloadHandler.text);
      OnIceConfigReady?.Invoke(iceConfig);

      // 2. Negotiate WebPubSub token
      using var negReq = UnityWebRequest.Get(
          $"{signalingServerUrl}/negotiate?userId={userId}&room={_room}");
      yield return negReq.SendWebRequest();

      if (negReq.result != UnityWebRequest.Result.Success)
      {
          Debug.LogError("SignalingClient: Negotiate failed: " + negReq.error);
          yield break;
      }

      var negotiateResponse = JsonUtility.FromJson<NegotiateResponse>(negReq.downloadHandler.text);

      // 3. Open WebSocket
      _cts = new CancellationTokenSource();
      _ = ConnectWebSocket(negotiateResponse.url);
  }

  // -------------------------------------------------------------------------
  // WebSocket
  // -------------------------------------------------------------------------

  private async Task ConnectWebSocket(string url)
  {
      _ws = new ClientWebSocket();
      _ws.Options.AddSubProtocol("json.webpubsub.azure.v1");

      await _ws.ConnectAsync(new Uri(url), _cts.Token);
      Debug.Log("SignalingClient: Connected to Web PubSub");

      // Join our private room
      SendWs(new {
          type  = "joinGroup",
          group = _room
      });

      await Task.Delay(2000);

      // Announce to lobby
      SendWs(new {
          type     = "sendToGroup",
          group    = "lobby",
          dataType = "json",
          data     = new { type = "call-request", room = _room, callerName = _callerName }
      });
      Debug.Log($"SignalingClient: Call request sent to lobby for room={_room}");

      await ReceiveLoop();
  }

  private async Task ReceiveLoop()
  {
      var buffer = new byte[8192];
      while (_ws.State == WebSocketState.Open)
      {
          var sb = new StringBuilder();
          WebSocketReceiveResult result;
          do
          {
              result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
              sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
          } while (!result.EndOfMessage);

          lock (_messageQueue)
              _messageQueue.Enqueue(sb.ToString());
      }
  }

  // -------------------------------------------------------------------------
  // Send helpers (public so PeerConnectionManager can send ICE candidates)
  // -------------------------------------------------------------------------

  public void SendWs(object data)
  {
      var json  = Newtonsoft.Json.JsonConvert.SerializeObject(data);
      var bytes = Encoding.UTF8.GetBytes(json);
      _ = _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
  }

  public void SendIceCandidate(string room, object candidate)
  {
      SendWs(new {
          type     = "sendToGroup",
          group    = room,
          dataType = "json",
          data     = new { type = "ice-candidate", room, candidate }
      });
  }

  public void SendCallEnded(string room)
  {
      if (_ws != null && _ws.State == WebSocketState.Open)
      {
          SendWs(new {
              type     = "sendToGroup",
              group    = "lobby",
              dataType = "json",
              data     = new { type = "call-ended", room }
          });
      }
  }

  // -------------------------------------------------------------------------
  // Message dispatch (called from Update on main thread)
  // -------------------------------------------------------------------------

  void Update()
  {
      lock (_messageQueue)
      {
          while (_messageQueue.Count > 0)
              HandleMessage(_messageQueue.Dequeue());
      }
  }

  private void HandleMessage(string raw)
  {
      var msg = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(raw);
      if (!msg.ContainsKey("data")) return;

      var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(
          msg["data"].ToString());

      if (!data.ContainsKey("type")) return;
      var type = data["type"].ToString();

      // Ignore messages not meant for our room
      if (data.ContainsKey("room") && data["room"]?.ToString() != _room)
          return;

      switch (type)
      {
          case "call-accepted":
              Debug.Log("SignalingClient: Call accepted");
              OnCallAccepted?.Invoke();
              break;

          case "call-declined":
              Debug.Log("SignalingClient: Call declined");
              OnCallDeclined?.Invoke();
              break;

          case "answer":
              OnAnswerReceived?.Invoke(data["sdp"].ToString());
              break;

          case "ice-candidate":
              OnIceCandidateReceived?.Invoke(data);
              break;
      }
  }

  void OnDestroy()
  {
      _cts?.Cancel();
      _ws?.Dispose();
  }
}