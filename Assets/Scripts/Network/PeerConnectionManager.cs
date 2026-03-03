using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

public class PeerConnectionManager : MonoBehaviour
{
  // Fired when a local ICE candidate is ready to be sent via signaling
  public event Action<object> OnLocalIceCandidate;

  // Fired when the offer SDP is ready to be sent via signaling
  public event Action<string> OnOfferReady;
  public event Action OnConnectionLost;

  private RTCPeerConnection _peerConnection;
  private VideoStreamTrack _videoTrack;

  public bool IsReady => _peerConnection != null;

  public void SetupPeerConnection(IceConfigResponse iceConfig)
  {
      var iceServers = new List<RTCIceServer>();
      foreach (var s in iceConfig.iceServers)
          iceServers.Add(new RTCIceServer
          {
              urls       = s.urls,
              username   = s.username,
              credential = s.credential
          });

      var config = new RTCConfiguration { iceServers = iceServers.ToArray() };
      _peerConnection = new RTCPeerConnection(ref config);

      _peerConnection.OnIceCandidate = candidate =>
      {
          if (string.IsNullOrEmpty(candidate.Candidate)) return;
          OnLocalIceCandidate?.Invoke(new {
              candidate     = candidate.Candidate,
              sdpMid        = candidate.SdpMid,
              sdpMLineIndex = candidate.SdpMLineIndex ?? 0
          });
      };

      _peerConnection.OnIceConnectionChange   = state => Debug.Log($"WebRTC ICE: {state}");
      _peerConnection.OnConnectionStateChange = state =>
      {
          Debug.Log($"WebRTC Connection: {state}");
          if (state == RTCPeerConnectionState.Disconnected ||
              state == RTCPeerConnectionState.Failed)
              OnConnectionLost?.Invoke();
      };

      Debug.Log("PeerConnectionManager: Peer connection ready");
  }

  public void AddVideoTrack(VideoStreamTrack track)
  {
      if (_peerConnection == null)
      {
          Debug.LogError("PeerConnectionManager: Cannot add track, peer connection is null");
          return;
      }
      _videoTrack = track;
      _peerConnection.AddTrack(_videoTrack);
      Debug.Log("PeerConnectionManager: Video track added");
  }

  public void SendOffer()
  {
      StartCoroutine(SendOfferCoroutine());
  }

  private IEnumerator SendOfferCoroutine()
  {
      float timeout = 10f;
      float elapsed = 0f;
      while ((_videoTrack == null || _videoTrack.ReadyState != TrackState.Live) && elapsed < timeout)
      {
          yield return new WaitForSeconds(0.2f);
          elapsed += 0.2f;
      }

      if (_videoTrack == null || _videoTrack.ReadyState != TrackState.Live)
      {
          Debug.LogError("PeerConnectionManager: Video track never became live, cannot send offer");
          yield break;
      }

      yield return new WaitForSeconds(0.5f);

      var op       = _peerConnection.CreateOffer();
      yield return op;
      var offer    = op.Desc;
      var setLocal = _peerConnection.SetLocalDescription(ref offer);
      yield return setLocal;

      OnOfferReady?.Invoke(offer.sdp);
      Debug.Log("PeerConnectionManager: Offer sent");
  }

  public void SetRemoteAnswer(string sdp)
  {
      StartCoroutine(SetRemoteAnswerCoroutine(sdp));
  }

  private IEnumerator SetRemoteAnswerCoroutine(string sdp)
  {
      var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
      var op     = _peerConnection.SetRemoteDescription(ref answer);
      yield return op;
      Debug.Log("PeerConnectionManager: Remote answer set, WebRTC connected");
  }

  public void HandleIceCandidate(Dictionary<string, object> data)
  {
    if (!IceCandidateParser.TryParse(data, out var init)) return;
    _peerConnection.AddIceCandidate(new RTCIceCandidate(init));
  }
  void OnDestroy()
  {
      _peerConnection?.Close();
  }
}