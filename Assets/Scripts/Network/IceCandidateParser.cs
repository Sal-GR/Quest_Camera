using System;
using System.Collections.Generic;
using NUnit.Framework.Interfaces;
using Unity.WebRTC;
using UnityEngine;

/// <summary>
/// Pure static utility for parsing inbound ICE candidate signaling messages
/// into RTCIceCandidateInit structs.
/// No MonoBehaviour, no Unity lifecycle — just parsing logic.
/// </summary>
public static class IceCandidateParser
{
  /// <summary>
  /// Attempts to parse a raw signaling data dictionary into an RTCIceCandidateInit.
  /// Returns null if the message is invalid, empty, or end-of-candidates.
  /// </summary>
  /// 
  /// removed ? in RTCIceCandidateInit?
  public static bool TryParse(Dictionary<string, object> data, out RTCIceCandidateInit result)
  {
    result = default;
    try
    {
      if (!data.ContainsKey("candidate")) return false;

      var candidateRaw = data["candidate"];
      if (candidateRaw == null) return false;

      var candidateObj = Newtonsoft.Json.JsonConvert
          .DeserializeObject<Dictionary<string, object>>(candidateRaw.ToString());

      if (candidateObj == null) return false;

      var candidateStr = candidateObj.ContainsKey("candidate")
          ? candidateObj["candidate"]?.ToString()
          : "";

      // Empty candidate = end-of-candidates signal, safe to ignore
      if (string.IsNullOrEmpty(candidateStr)) return false;

      var sdpMid = candidateObj.ContainsKey("sdpMid")
          ? candidateObj["sdpMid"]?.ToString()
          : "0";

      var sdpMLineIndex = candidateObj.ContainsKey("sdpMLineIndex")
          ? int.Parse(candidateObj["sdpMLineIndex"].ToString())
          : 0;

      result = new RTCIceCandidateInit
      {
          candidate     = candidateStr,
          sdpMid        = sdpMid ?? "0",
          sdpMLineIndex = sdpMLineIndex
      };
      return true;
    }
    catch (Exception e)
    {
        Debug.LogError("IceCandidateParser: Failed to parse candidate: " + e.Message);
        return false;
    }
  }
}